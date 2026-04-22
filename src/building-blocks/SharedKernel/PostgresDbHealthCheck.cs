using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel;

public sealed class PostgresDbHealthCheck<TDbContext>(IServiceScopeFactory scopeFactory) : IHealthCheck
    where TDbContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

        return canConnect
            ? HealthCheckResult.Healthy("PostgreSQL connection is healthy.")
            : HealthCheckResult.Unhealthy("PostgreSQL connection failed.");
    }
}
