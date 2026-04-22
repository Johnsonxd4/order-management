using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api;
using SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformServices();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddOpenApi("v1");
builder.Services.AddValidatedOptions<ServiceEndpointsOptions>(builder.Configuration.GetSection("DownstreamServices"));
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

builder.Services.AddDbContext<OrdersDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<OrdersDatabaseInitializer>();
builder.Services.AddHealthChecks().AddPostgresDbHealthCheck<OrdersDbContext>("orders-postgres");
builder.Services.AddTransient<BearerTokenForwardingHandler>();

builder.Services.AddHttpClient<CatalogServiceClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.CatalogBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<BearerTokenForwardingHandler>();

builder.Services.AddHttpClient<InventoryServiceClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.InventoryBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<BearerTokenForwardingHandler>();

builder.Services.AddHttpClient<PaymentServiceClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.PaymentsBaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(10);
})
.AddHttpMessageHandler<BearerTokenForwardingHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapSwaggerUi("Orders API");

app.MapGet("/", () => Results.Ok(new { service = "orders-api", status = "ok" }));

var orders = app.MapGroup("/api/orders");

orders.MapGet("/", async (OrdersDbContext dbContext, CancellationToken cancellationToken) =>
{
    var result = await dbContext.Orders
        .AsNoTracking()
        .Include(entry => entry.Lines)
        .OrderByDescending(entry => entry.CreatedAtUtc)
        .Select(entry => OrderResponse.FromEntity(entry))
        .ToListAsync(cancellationToken);

    return Results.Ok(result);
})
.RequireAuthorization(PlatformPolicies.OrderRead);

orders.MapGet("/{id:guid}", async (Guid id, OrdersDbContext dbContext, CancellationToken cancellationToken) =>
{
    var order = await dbContext.Orders
        .AsNoTracking()
        .Include(entry => entry.Lines)
        .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    return order is null ? Results.NotFound() : Results.Ok(OrderResponse.FromEntity(order));
})
.RequireAuthorization(PlatformPolicies.OrderRead);

orders.MapPost("/", async (
    CreateOrderRequest request,
    OrdersDbContext dbContext,
    CatalogServiceClient catalogClient,
    InventoryServiceClient inventoryClient,
    PaymentServiceClient paymentClient,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    if (request.Lines.Count == 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["Lines"] = ["At least one line is required."]
        });
    }

    var nestedValidationErrors = new Dictionary<string, string[]>();

    for (var index = 0; index < request.Lines.Count; index++)
    {
        var line = request.Lines.ElementAt(index);
        if (string.IsNullOrWhiteSpace(line.Sku))
        {
            nestedValidationErrors[$"Lines[{index}].Sku"] = ["SKU is required."];
        }

        if (line.Quantity <= 0)
        {
            nestedValidationErrors[$"Lines[{index}].Quantity"] = ["Quantity must be greater than zero."];
        }
    }

    if (nestedValidationErrors.Count > 0)
    {
        return Results.ValidationProblem(nestedValidationErrors);
    }

    var now = timeProvider.GetUtcNow().UtcDateTime;
    var order = new Order
    {
        Id = Guid.NewGuid(),
        CustomerId = request.CustomerId.Trim(),
        Currency = request.Currency.Trim().ToUpperInvariant(),
        Status = OrderStatuses.PendingValidation,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(cancellationToken);

    var reservedItems = new List<ReleaseInventoryRequest>();

    try
    {
        foreach (var requestedLine in request.Lines)
        {
            var normalizedSku = requestedLine.Sku.Trim().ToUpperInvariant();
            var product = await catalogClient.GetProductAsync(normalizedSku, cancellationToken);

            if (product is null || !product.IsActive)
            {
                order.Status = OrderStatuses.Rejected;
                order.FailureReason = $"SKU '{normalizedSku}' was not found or is inactive.";
                order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(cancellationToken);
                await ReleaseReservationsAsync(reservedItems, inventoryClient, cancellationToken);

                return Results.BadRequest(OrderResponse.FromEntity(order));
            }

            if (!string.Equals(product.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
            {
                order.Status = OrderStatuses.Rejected;
                order.FailureReason = $"Currency mismatch for SKU '{normalizedSku}'.";
                order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(cancellationToken);
                await ReleaseReservationsAsync(reservedItems, inventoryClient, cancellationToken);

                return Results.BadRequest(OrderResponse.FromEntity(order));
            }

            var reservation = await inventoryClient.ReserveAsync(new ReserveInventoryRequest(normalizedSku, requestedLine.Quantity), cancellationToken);

            if (reservation is null || !reservation.Success)
            {
                order.Status = OrderStatuses.Rejected;
                order.FailureReason = reservation?.Reason ?? $"Failed to reserve stock for SKU '{normalizedSku}'.";
                order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await dbContext.SaveChangesAsync(cancellationToken);
                await ReleaseReservationsAsync(reservedItems, inventoryClient, cancellationToken);

                return Results.BadRequest(OrderResponse.FromEntity(order));
            }

            reservedItems.Add(new ReleaseInventoryRequest(normalizedSku, requestedLine.Quantity));

            order.Lines.Add(new OrderLine
            {
                Sku = product.Sku,
                ProductName = product.Name,
                Quantity = requestedLine.Quantity,
                UnitPrice = product.Price,
                Currency = product.Currency,
                LineTotal = product.Price * requestedLine.Quantity
            });
        }

        order.TotalAmount = order.Lines.Sum(line => line.LineTotal);
        order.Status = OrderStatuses.InventoryReserved;
        order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);

        var payment = await paymentClient.AuthorizeAsync(
            new PaymentAuthorizationRequest(order.Id, order.CustomerId, order.TotalAmount, order.Currency, request.PaymentMethodToken.Trim()),
            cancellationToken);

        if (!payment.Approved)
        {
            order.Status = OrderStatuses.PaymentFailed;
            order.FailureReason = payment.Reason;
            order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);

            await ReleaseReservationsAsync(reservedItems, inventoryClient, cancellationToken);

            return Results.BadRequest(OrderResponse.FromEntity(order));
        }

        order.Status = OrderStatuses.Authorized;
        order.PaymentTransactionId = payment.TransactionId;
        order.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/orders/{order.Id}", OrderResponse.FromEntity(order));
    }
    catch
    {
        await ReleaseReservationsAsync(reservedItems, inventoryClient, cancellationToken);
        throw;
    }
})
.AddEndpointFilter<ValidationEndpointFilter<CreateOrderRequest>>()
.RequireAuthorization(PlatformPolicies.OrderWrite);

app.MapPlatformEndpoints();
app.Run();

static async Task ReleaseReservationsAsync(
    IEnumerable<ReleaseInventoryRequest> reservations,
    InventoryServiceClient inventoryClient,
    CancellationToken cancellationToken)
{
    foreach (var reservation in reservations)
    {
        await inventoryClient.ReleaseAsync(reservation, cancellationToken);
    }
}
