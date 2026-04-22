using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace SharedKernel;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });

        services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddHealthChecks();
        services.AddSingleton(TimeProvider.System);

        return services;
    }

    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfigurationSection section)
        where TOptions : class
    {
        return services
            .AddOptions<TOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    public static IHealthChecksBuilder AddPostgresDbHealthCheck<TDbContext>(
        this IHealthChecksBuilder healthChecksBuilder,
        string name)
        where TDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        return healthChecksBuilder.AddCheck<PostgresDbHealthCheck<TDbContext>>(name, HealthStatus.Unhealthy);
    }

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        endpoints.MapGet("/health/ready", async (HealthCheckService healthCheckService, CancellationToken cancellationToken) =>
        {
            var report = await healthCheckService.CheckHealthAsync(cancellationToken);

            return report.Status == HealthStatus.Healthy
                ? Results.Ok(new { status = "ready" })
                : Results.Problem(
                    title: "Dependency health check failed",
                    detail: "One or more dependencies are unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return endpoints;
    }

    public static Dictionary<string, string[]> ToValidationDictionary(IEnumerable<ValidationResult> validationResults)
    {
        return validationResults
            .SelectMany(result =>
            {
                var members = result.MemberNames.Any() ? result.MemberNames : ["request"];
                return members.Select(member => new { Member = member, Error = result.ErrorMessage ?? "Validation error." });
            })
            .GroupBy(item => item.Member)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Error).Distinct().ToArray());
    }
}
