using Catalog.Api;
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

builder.Services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<CatalogDatabaseInitializer>();
builder.Services.AddHealthChecks().AddPostgresDbHealthCheck<CatalogDbContext>("catalog-postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapSwaggerUi("Catalog API");

app.MapGet("/", () => Results.Ok(new { service = "catalog-api", status = "ok" }));

var products = app.MapGroup("/api/products");
products.RequireAuthorization(PlatformPolicies.AuthenticatedUser);

products.MapGet("/", async (CatalogDbContext dbContext, CancellationToken cancellationToken) =>
{
    var result = await dbContext.Products
        .AsNoTracking()
        .OrderBy(product => product.Name)
        .Select(product => ProductResponse.FromEntity(product))
        .ToListAsync(cancellationToken);

    return Results.Ok(result);
});

products.MapGet("/{sku}", async (string sku, CatalogDbContext dbContext, CancellationToken cancellationToken) =>
{
    var product = await dbContext.Products
        .AsNoTracking()
        .FirstOrDefaultAsync(item => item.Sku == sku, cancellationToken);

    return product is null
        ? Results.NotFound()
        : Results.Ok(ProductResponse.FromEntity(product));
});

products.MapPost("/", async (CreateProductRequest request, CatalogDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var existingProduct = await dbContext.Products.AnyAsync(product => product.Sku == request.Sku, cancellationToken);

    if (existingProduct)
    {
        return Results.Conflict(new { message = $"Product with SKU '{request.Sku}' already exists." });
    }

    var now = timeProvider.GetUtcNow().UtcDateTime;
    var product = new Product
    {
        Id = Guid.NewGuid(),
        Sku = request.Sku.Trim().ToUpperInvariant(),
        Name = request.Name.Trim(),
        Description = request.Description?.Trim(),
        Price = request.Price,
        Currency = request.Currency.Trim().ToUpperInvariant(),
        IsActive = true,
        CreatedAtUtc = now,
        UpdatedAtUtc = now
    };

    dbContext.Products.Add(product);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/products/{product.Sku}", ProductResponse.FromEntity(product));
})
.AddEndpointFilter<ValidationEndpointFilter<CreateProductRequest>>()
.RequireAuthorization(PlatformPolicies.CatalogWrite);

products.MapPut("/{sku}/price", async (string sku, UpdateProductPriceRequest request, CatalogDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken) =>
{
    var product = await dbContext.Products.FirstOrDefaultAsync(item => item.Sku == sku, cancellationToken);

    if (product is null)
    {
        return Results.NotFound();
    }

    product.Price = request.Price;
    product.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(ProductResponse.FromEntity(product));
})
.AddEndpointFilter<ValidationEndpointFilter<UpdateProductPriceRequest>>()
.RequireAuthorization(PlatformPolicies.CatalogWrite);

app.MapPlatformEndpoints();
app.Run();
