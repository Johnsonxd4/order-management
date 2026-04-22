using Inventory.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.OpenApi;
using SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformServices();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddOpenApi("v1");
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

builder.Services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<InventoryDatabaseInitializer>();
builder.Services.AddHealthChecks().AddPostgresDbHealthCheck<InventoryDbContext>("inventory-postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapSwaggerUi("Inventory API");

app.MapGet("/", () => Results.Ok(new { service = "inventory-api", status = "ok" }));

var stock = app.MapGroup("/api/stocks");

stock.MapGet("/", async (InventoryDbContext dbContext, CancellationToken cancellationToken) =>
{
    var items = await dbContext.Items
        .AsNoTracking()
        .OrderBy(item => item.Sku)
        .Select(item => InventoryResponse.FromEntity(item))
        .ToListAsync(cancellationToken);

    return Results.Ok(items);
})
.RequireAuthorization(PlatformPolicies.InventoryRead);

stock.MapGet("/{sku}", async (string sku, InventoryDbContext dbContext, CancellationToken cancellationToken) =>
{
    var item = await dbContext.Items.AsNoTracking().FirstOrDefaultAsync(entry => entry.Sku == sku, cancellationToken);
    return item is null ? Results.NotFound() : Results.Ok(InventoryResponse.FromEntity(item));
})
.RequireAuthorization(PlatformPolicies.InventoryRead);

stock.MapPost("/seed", async (SeedInventoryRequest request, InventoryDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var now = timeProvider.GetUtcNow().UtcDateTime;
    var normalizedSku = request.Sku.Trim().ToUpperInvariant();

    var existing = await dbContext.Items.FirstOrDefaultAsync(item => item.Sku == normalizedSku, cancellationToken);

    if (existing is null)
    {
        existing = new InventoryItem
        {
            Id = Guid.NewGuid(),
            Sku = normalizedSku,
            AvailableQuantity = request.AvailableQuantity,
            ReservedQuantity = 0,
            UpdatedAtUtc = now
        };

        dbContext.Items.Add(existing);
    }
    else
    {
        existing.AvailableQuantity = request.AvailableQuantity;
        existing.UpdatedAtUtc = now;
    }

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(InventoryResponse.FromEntity(existing));
})
.AddEndpointFilter<ValidationEndpointFilter<SeedInventoryRequest>>()
.RequireAuthorization(PlatformPolicies.InventoryWrite);

stock.MapPost("/reservations", async (ReserveInventoryRequest request, InventoryDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var normalizedSku = request.Sku.Trim().ToUpperInvariant();
    var item = await dbContext.Items.FirstOrDefaultAsync(entry => entry.Sku == normalizedSku, cancellationToken);

    if (item is null)
    {
        return Results.NotFound(new ReservationResponse(false, normalizedSku, request.Quantity, 0, "SKU not found in inventory."));
    }

    if (item.AvailableQuantity < request.Quantity)
    {
        return Results.BadRequest(new ReservationResponse(false, normalizedSku, request.Quantity, item.AvailableQuantity, "Insufficient stock."));
    }

    item.AvailableQuantity -= request.Quantity;
    item.ReservedQuantity += request.Quantity;
    item.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new ReservationResponse(true, normalizedSku, request.Quantity, item.AvailableQuantity, null));
})
.AddEndpointFilter<ValidationEndpointFilter<ReserveInventoryRequest>>()
.RequireAuthorization(PlatformPolicies.InventoryReservation);

stock.MapPost("/releases", async (ReleaseInventoryRequest request, InventoryDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var normalizedSku = request.Sku.Trim().ToUpperInvariant();
    var item = await dbContext.Items.FirstOrDefaultAsync(entry => entry.Sku == normalizedSku, cancellationToken);

    if (item is null)
    {
        return Results.NotFound();
    }

    if (item.ReservedQuantity < request.Quantity)
    {
        return Results.BadRequest(new { message = $"Cannot release {request.Quantity} items because only {item.ReservedQuantity} are reserved." });
    }

    item.ReservedQuantity -= request.Quantity;
    item.AvailableQuantity += request.Quantity;
    item.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(InventoryResponse.FromEntity(item));
})
.AddEndpointFilter<ValidationEndpointFilter<ReleaseInventoryRequest>>()
.RequireAuthorization(PlatformPolicies.InventoryReservation);

app.MapPlatformEndpoints();
app.Run();
