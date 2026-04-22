using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Payments.Api;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(payment => payment.Id);
            entity.HasIndex(payment => payment.OrderId).IsUnique();
            entity.Property(payment => payment.CustomerId).HasMaxLength(64).IsRequired();
            entity.Property(payment => payment.TransactionId).HasMaxLength(128).IsRequired();
            entity.Property(payment => payment.Status).HasMaxLength(32).IsRequired();
            entity.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.Reason).HasMaxLength(256);
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
        });
    }
}

public sealed class PaymentsDatabaseInitializer(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class PaymentGatewayOptions
{
    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal AutoApproveLimit { get; init; } = 5000m;

    public string[] BlockedTokens { get; init; } = [];
}

public sealed class PaymentRecord
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public required string CustomerId { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required string TransactionId { get; set; }

    public required string Status { get; set; }

    public string? Reason { get; set; }

    public DateTime ProcessedAtUtc { get; set; }
}

public sealed record PaymentAuthorizationRequest(
    Guid OrderId,
    [property: Required, StringLength(64, MinimumLength = 3)] string CustomerId,
    [property: Range(typeof(decimal), "0.01", "999999999")] decimal Amount,
    [property: Required, StringLength(3, MinimumLength = 3)] string Currency,
    [property: Required, StringLength(128, MinimumLength = 8)] string PaymentMethodToken);

public sealed record PaymentAuthorizationResponse(
    Guid PaymentId,
    Guid OrderId,
    string TransactionId,
    bool Approved,
    string? Reason,
    decimal Amount,
    string Currency)
{
    public static PaymentAuthorizationResponse FromEntity(PaymentRecord payment) =>
        new(
            payment.Id,
            payment.OrderId,
            payment.TransactionId,
            string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase),
            payment.Reason,
            payment.Amount,
            payment.Currency);
}
