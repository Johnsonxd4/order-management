using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> Items => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("items");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Sku).IsUnique();
            entity.Property(item => item.Sku).HasMaxLength(64).IsRequired();
        });
    }
}

public sealed class InventoryDatabaseInitializer(
    IServiceProvider serviceProvider,
    TimeProvider timeProvider,
    ILogger<InventoryDatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.Items.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        dbContext.Items.AddRange(
            new InventoryItem
            {
                Id = Guid.NewGuid(),
                Sku = "SKU-CHAIR-001",
                AvailableQuantity = 25,
                ReservedQuantity = 0,
                UpdatedAtUtc = now
            },
            new InventoryItem
            {
                Id = Guid.NewGuid(),
                Sku = "SKU-DESK-001",
                AvailableQuantity = 10,
                ReservedQuantity = 0,
                UpdatedAtUtc = now
            });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Inventory seeded with baseline stock.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class InventoryItem
{
    public Guid Id { get; set; }

    public required string Sku { get; set; }

    public int AvailableQuantity { get; set; }

    public int ReservedQuantity { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record SeedInventoryRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Sku,
    [property: Range(0, 100000)] int AvailableQuantity);

public sealed record ReserveInventoryRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Sku,
    [property: Range(1, 10000)] int Quantity);

public sealed record ReleaseInventoryRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Sku,
    [property: Range(1, 10000)] int Quantity);

public sealed record InventoryResponse(
    string Sku,
    int AvailableQuantity,
    int ReservedQuantity,
    DateTime UpdatedAtUtc)
{
    public static InventoryResponse FromEntity(InventoryItem item) =>
        new(item.Sku, item.AvailableQuantity, item.ReservedQuantity, item.UpdatedAtUtc);
}

public sealed record ReservationResponse(
    bool Success,
    string Sku,
    int Quantity,
    int RemainingAvailableQuantity,
    string? Reason);
