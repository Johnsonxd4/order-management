using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace Orders.Api;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ordering");

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(order => order.Id);
            entity.Property(order => order.CustomerId).HasMaxLength(64).IsRequired();
            entity.Property(order => order.Currency).HasMaxLength(3).IsRequired();
            entity.Property(order => order.Status).HasMaxLength(64).IsRequired();
            entity.Property(order => order.TotalAmount).HasPrecision(18, 2);
            entity.Property(order => order.PaymentTransactionId).HasMaxLength(128);
            entity.Property(order => order.FailureReason).HasMaxLength(512);

            entity.OwnsMany(order => order.Lines, line =>
            {
                line.ToTable("order_lines");
                line.WithOwner().HasForeignKey("OrderId");
                line.Property<int>("Id");
                line.HasKey("Id");
                line.Property(item => item.Sku).HasMaxLength(64).IsRequired();
                line.Property(item => item.ProductName).HasMaxLength(128).IsRequired();
                line.Property(item => item.Currency).HasMaxLength(3).IsRequired();
                line.Property(item => item.UnitPrice).HasPrecision(18, 2);
                line.Property(item => item.LineTotal).HasPrecision(18, 2);
            });
        });
    }
}

public sealed class OrdersDatabaseInitializer(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ServiceEndpointsOptions
{
    [Required]
    [Url]
    public string CatalogBaseUrl { get; init; } = string.Empty;

    [Required]
    [Url]
    public string InventoryBaseUrl { get; init; } = string.Empty;

    [Required]
    [Url]
    public string PaymentsBaseUrl { get; init; } = string.Empty;
}

public sealed class Order
{
    public Guid Id { get; set; }

    public required string CustomerId { get; set; }

    public required string Currency { get; set; }

    public required string Status { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? PaymentTransactionId { get; set; }

    public string? FailureReason { get; set; }

    public List<OrderLine> Lines { get; set; } = [];
}

public sealed class OrderLine
{
    public required string Sku { get; set; }

    public required string ProductName { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public required string Currency { get; set; }
}

public static class OrderStatuses
{
    public const string PendingValidation = "pending-validation";
    public const string InventoryReserved = "inventory-reserved";
    public const string Authorized = "authorized";
    public const string Rejected = "rejected";
    public const string PaymentFailed = "payment-failed";
}

public sealed record CreateOrderRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string CustomerId,
    [property: Required, StringLength(3, MinimumLength = 3)] string Currency,
    [property: Required, MinLength(1)] IReadOnlyCollection<CreateOrderLineRequest> Lines,
    [property: Required, StringLength(128, MinimumLength = 8)] string PaymentMethodToken);

public sealed record CreateOrderLineRequest(
    [property: Required, StringLength(64, MinimumLength = 3)] string Sku,
    [property: Range(1, 1000)] int Quantity);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Currency,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    string? PaymentTransactionId,
    string? FailureReason,
    IReadOnlyCollection<OrderLineResponse> Lines)
{
    public static OrderResponse FromEntity(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.Currency,
            order.Status,
            order.TotalAmount,
            order.CreatedAtUtc,
            order.PaymentTransactionId,
            order.FailureReason,
            order.Lines.Select(line => new OrderLineResponse(line.Sku, line.ProductName, line.Quantity, line.UnitPrice, line.LineTotal, line.Currency)).ToArray());
}

public sealed record OrderLineResponse(
    string Sku,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    string Currency);

public sealed record CatalogProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    bool IsActive,
    DateTime UpdatedAtUtc);

public sealed record ReserveInventoryRequest(string Sku, int Quantity);
public sealed record ReleaseInventoryRequest(string Sku, int Quantity);
public sealed record ReservationResponse(bool Success, string Sku, int Quantity, int RemainingAvailableQuantity, string? Reason);

public sealed record PaymentAuthorizationRequest(Guid OrderId, string CustomerId, decimal Amount, string Currency, string PaymentMethodToken);
public sealed record PaymentAuthorizationResponse(Guid PaymentId, Guid OrderId, string TransactionId, bool Approved, string? Reason, decimal Amount, string Currency);

public sealed class BearerTokenForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorizationHeader = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorizationHeader)
            && request.Headers.Authorization is null
            && AuthenticationHeaderValue.TryParse(authorizationHeader, out var parsedHeader))
        {
            request.Headers.Authorization = parsedHeader;
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class CatalogServiceClient(HttpClient httpClient)
{
    public async Task<CatalogProductResponse?> GetProductAsync(string sku, CancellationToken cancellationToken)
    {
        return await httpClient.GetFromJsonAsync<CatalogProductResponse>($"/api/products/{sku}", cancellationToken);
    }
}

public sealed class InventoryServiceClient(HttpClient httpClient)
{
    public async Task<ReservationResponse?> ReserveAsync(ReserveInventoryRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/stocks/reservations", request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new ReservationResponse(false, request.Sku, request.Quantity, 0, "SKU not found in inventory.");
        }

        return await response.Content.ReadFromJsonAsync<ReservationResponse>(cancellationToken: cancellationToken);
    }

    public async Task ReleaseAsync(ReleaseInventoryRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/stocks/releases", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class PaymentServiceClient(HttpClient httpClient)
{
    public async Task<PaymentAuthorizationResponse> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/payments/authorize", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentAuthorizationResponse>(cancellationToken: cancellationToken))!;
    }
}
