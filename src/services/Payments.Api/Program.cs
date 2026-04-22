using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Api;
using SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformServices();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddOpenApi("v1");
builder.Services.AddValidatedOptions<PaymentGatewayOptions>(builder.Configuration.GetSection("PaymentGateway"));
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Connection string 'Postgres' was not configured.");

builder.Services.AddDbContext<PaymentsDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<PaymentsDatabaseInitializer>();
builder.Services.AddHealthChecks().AddPostgresDbHealthCheck<PaymentsDbContext>("payments-postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapSwaggerUi("Payments API");

app.MapGet("/", () => Results.Ok(new { service = "payments-api", status = "ok" }));

var payments = app.MapGroup("/api/payments");

payments.MapGet("/", async (PaymentsDbContext dbContext, CancellationToken cancellationToken) =>
{
    var result = await dbContext.Payments
        .AsNoTracking()
        .OrderByDescending(entry => entry.ProcessedAtUtc)
        .Select(payment => PaymentAuthorizationResponse.FromEntity(payment))
        .ToListAsync(cancellationToken);

    return Results.Ok(result);
})
.RequireAuthorization(PlatformPolicies.PaymentRead);

payments.MapGet("/{orderId:guid}", async (Guid orderId, PaymentsDbContext dbContext, CancellationToken cancellationToken) =>
{
    var payment = await dbContext.Payments.AsNoTracking().FirstOrDefaultAsync(entry => entry.OrderId == orderId, cancellationToken);
    return payment is null ? Results.NotFound() : Results.Ok(PaymentAuthorizationResponse.FromEntity(payment));
})
.RequireAuthorization(PlatformPolicies.PaymentRead);

payments.MapPost("/authorize", async (
    PaymentAuthorizationRequest request,
    PaymentsDbContext dbContext,
    IOptions<PaymentGatewayOptions> gatewayOptions,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var existingPayment = await dbContext.Payments.FirstOrDefaultAsync(entry => entry.OrderId == request.OrderId, cancellationToken);

    if (existingPayment is not null)
    {
        return Results.Ok(PaymentAuthorizationResponse.FromEntity(existingPayment));
    }

    var options = gatewayOptions.Value;
    var normalizedCurrency = request.Currency.Trim().ToUpperInvariant();
    var normalizedToken = request.PaymentMethodToken.Trim();

    var blockedToken = options.BlockedTokens.Any(token => string.Equals(token, normalizedToken, StringComparison.OrdinalIgnoreCase));
    var approved = !blockedToken
                   && !normalizedToken.EndsWith("0000", StringComparison.Ordinal)
                   && request.Amount <= options.AutoApproveLimit;

    var payment = new PaymentRecord
    {
        Id = Guid.NewGuid(),
        OrderId = request.OrderId,
        CustomerId = request.CustomerId.Trim(),
        Amount = request.Amount,
        Currency = normalizedCurrency,
        TransactionId = $"PAY-{Guid.NewGuid():N}",
        Status = approved ? "approved" : "declined",
        Reason = approved ? null : "Payment policy rejected the authorization request.",
        ProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime
    };

    dbContext.Payments.Add(payment);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(PaymentAuthorizationResponse.FromEntity(payment));
})
.AddEndpointFilter<ValidationEndpointFilter<PaymentAuthorizationRequest>>()
.RequireAuthorization(PlatformPolicies.PaymentProcess);

app.MapPlatformEndpoints();
app.Run();
