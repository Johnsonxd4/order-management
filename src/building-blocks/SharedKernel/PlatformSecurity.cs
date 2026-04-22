using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace SharedKernel;

public static class PlatformRoles
{
    public const string Customer = "customer";
    public const string CatalogManager = "catalog-manager";
    public const string InventoryManager = "inventory-manager";
    public const string OrderManager = "order-manager";
    public const string FinanceAnalyst = "finance-analyst";
    public const string PlatformAdmin = "platform-admin";
}

public static class PlatformPolicies
{
    public const string AuthenticatedUser = "authenticated-user";
    public const string CatalogWrite = "catalog-write";
    public const string InventoryRead = "inventory-read";
    public const string InventoryWrite = "inventory-write";
    public const string InventoryReservation = "inventory-reservation";
    public const string OrderRead = "order-read";
    public const string OrderWrite = "order-write";
    public const string PaymentRead = "payment-read";
    public const string PaymentProcess = "payment-process";
}

public sealed class AuthenticationOptions
{
    [Required]
    [Url]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    [Url]
    public string MetadataAddress { get; init; } = string.Empty;

    public bool RequireHttpsMetadata { get; init; }
}

public static class PlatformSecurityServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<AuthenticationOptions>(configuration.GetSection("Authentication"));
        services.AddHttpContextAccessor();

        var options = configuration.GetSection("Authentication").Get<AuthenticationOptions>()
            ?? throw new InvalidOperationException("Authentication options were not configured.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.MapInboundClaims = false;
                jwtOptions.MetadataAddress = options.MetadataAddress;
                jwtOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = false,
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };
                jwtOptions.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is not ClaimsIdentity identity)
                        {
                            return Task.CompletedTask;
                        }

                        var realmAccess = context.Principal.FindFirst("realm_access")?.Value;
                        if (string.IsNullOrWhiteSpace(realmAccess))
                        {
                            return Task.CompletedTask;
                        }

                        using var document = JsonDocument.Parse(realmAccess);
                        if (!document.RootElement.TryGetProperty("roles", out var rolesElement) || rolesElement.ValueKind != JsonValueKind.Array)
                        {
                            return Task.CompletedTask;
                        }

                        foreach (var roleElement in rolesElement.EnumerateArray())
                        {
                            var role = roleElement.GetString();
                            if (string.IsNullOrWhiteSpace(role))
                            {
                                continue;
                            }

                            if (!identity.HasClaim(ClaimTypes.Role, role))
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PlatformPolicies.AuthenticatedUser, policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(PlatformPolicies.CatalogWrite, policy => policy.RequireRole(PlatformRoles.CatalogManager, PlatformRoles.PlatformAdmin));
            options.AddPolicy(
                PlatformPolicies.InventoryRead,
                policy => policy.RequireRole(
                    PlatformRoles.CatalogManager,
                    PlatformRoles.InventoryManager,
                    PlatformRoles.OrderManager,
                    PlatformRoles.PlatformAdmin));
            options.AddPolicy(PlatformPolicies.InventoryWrite, policy => policy.RequireRole(PlatformRoles.InventoryManager, PlatformRoles.PlatformAdmin));
            options.AddPolicy(PlatformPolicies.InventoryReservation, policy => policy.RequireRole(PlatformRoles.Customer, PlatformRoles.OrderManager, PlatformRoles.PlatformAdmin));
            options.AddPolicy(
                PlatformPolicies.OrderRead,
                policy => policy.RequireRole(
                    PlatformRoles.CatalogManager,
                    PlatformRoles.InventoryManager,
                    PlatformRoles.OrderManager,
                    PlatformRoles.FinanceAnalyst,
                    PlatformRoles.PlatformAdmin));
            options.AddPolicy(PlatformPolicies.OrderWrite, policy => policy.RequireRole(PlatformRoles.Customer, PlatformRoles.OrderManager, PlatformRoles.PlatformAdmin));
            options.AddPolicy(PlatformPolicies.PaymentRead, policy => policy.RequireRole(PlatformRoles.FinanceAnalyst, PlatformRoles.PlatformAdmin));
            options.AddPolicy(PlatformPolicies.PaymentProcess, policy => policy.RequireRole(PlatformRoles.Customer, PlatformRoles.OrderManager, PlatformRoles.PlatformAdmin));
        });

        return services;
    }
}
