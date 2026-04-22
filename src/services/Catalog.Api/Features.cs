using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Api;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("catalog");

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("products");
            entity.HasKey(product => product.Id);
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Sku).HasMaxLength(64).IsRequired();
            entity.Property(product => product.Name).HasMaxLength(128).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(2048);
            entity.Property(product => product.Currency).HasMaxLength(3).IsRequired();
            entity.Property(product => product.Price).HasPrecision(18, 2);
        });
    }
}

public sealed class CatalogDatabaseInitializer(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<CatalogDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        dbContext.Products.AddRange(
            new Product
            {
                Id = Guid.NewGuid(),
                Sku = "SKU-CHAIR-001",
                Name = "Nordic Chair",
                Description = "Chair used as baseline item for ecommerce validations.",
                Price = 349.90m,
                Currency = "BRL",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            new Product
            {
                Id = Guid.NewGuid(),
                Sku = "SKU-DESK-001",
                Name = "Standing Desk",
                Description = "Desk used for order, stock and payment end-to-end validations.",
                Price = 1899.00m,
                Currency = "BRL",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Catalog seeded with baseline products.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class Product
{
    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public required string Currency { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record CreateProductRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Sku,
    [property: Required, StringLength(128, MinimumLength = 3)] string Name,
    [property: StringLength(2048)] string? Description,
    [property: Range(typeof(decimal), "0.01", "999999999")] decimal Price,
    [property: Required, StringLength(3, MinimumLength = 3)] string Currency);

public sealed record UpdateProductPriceRequest(
    [property: Range(typeof(decimal), "0.01", "999999999")] decimal Price);

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    bool IsActive,
    DateTime UpdatedAtUtc)
{
    public static ProductResponse FromEntity(Product product) =>
        new(
            product.Id,
            product.Sku,
            product.Name,
            product.Description,
            product.Price,
            product.Currency,
            product.IsActive,
            product.UpdatedAtUtc);
}
