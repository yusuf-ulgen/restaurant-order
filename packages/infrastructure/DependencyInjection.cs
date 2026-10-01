using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RestaurantOrder.Infrastructure.Persistence;

namespace RestaurantOrder.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers persistence services, including RestaurantOrderDbContext with PostgreSQL/Npgsql.
    /// Follows fail-fast configuration rules: in Staging/Production, missing connection strings throw immediately.
    /// In Development, allows local fallback or connection string from DATABASE_URL / ConnectionStrings:Database.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var rawConn = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Database");

        if (string.IsNullOrWhiteSpace(rawConn))
        {
            if (environment.IsProduction() || environment.IsEnvironment("Staging"))
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: DATABASE_URL (or ConnectionStrings:Database) is required in '{environment.EnvironmentName}' environment.");
            }

            // In development without configured connection string, provide a default local connection string
            rawConn = "Host=localhost;Port=5432;Database=restaurant_order_dev;Username=postgres;Password=postgres";
        }

        var connectionString = NormalizeConnectionString(rawConn);

        services.AddDbContext<RestaurantOrderDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<ITenantDatabaseSession, TenantDatabaseSession>();

        // IAM Security & Hashing Services
        services.AddSingleton<RestaurantOrder.Application.Auth.IPasswordHasher, RestaurantOrder.Infrastructure.Auth.AspNetCorePasswordHasher>();

        services.Configure<RestaurantOrder.Application.Auth.PinHasherOptions>(options =>
        {
            var section = configuration.GetSection(RestaurantOrder.Application.Auth.PinHasherOptions.DefaultSectionName);
            section.Bind(options);

            options.Environment = environment.EnvironmentName;
            var envPepper = configuration["PIN_PEPPER_SECRET"];
            if (!string.IsNullOrWhiteSpace(envPepper))
            {
                options.PepperValue = envPepper;
            }
        });
        services.AddSingleton<RestaurantOrder.Application.Auth.IPinHasher, RestaurantOrder.Infrastructure.Auth.PepperedPinHasher>();
        services.AddScoped<RestaurantOrder.Application.Auth.IIamUserLookupGateway, RestaurantOrder.Infrastructure.Auth.PostgreSqlIamUserLookupGateway>();
        services.AddScoped<RestaurantOrder.Application.Auth.IIamBootstrapGateway, RestaurantOrder.Infrastructure.Auth.PostgreSqlIamBootstrapGateway>();

        // JWT & Authentication Services
        services.Configure<RestaurantOrder.Application.Auth.JwtSettings>(options =>
        {
            var section = configuration.GetSection(RestaurantOrder.Application.Auth.JwtSettings.SectionName);
            section.Bind(options);

            var envSecret = configuration["JWT_SECRET"];
            if (!string.IsNullOrWhiteSpace(envSecret))
            {
                options.Secret = envSecret;
            }
            else if (string.IsNullOrWhiteSpace(options.Secret) && environment.IsDevelopment())
            {
                options.Secret = "dev-only-insecure-jwt-secret-min-32-chars-long!";
            }
        });

        services.Configure<RestaurantOrder.Application.Auth.AuthSettings>(configuration.GetSection(RestaurantOrder.Application.Auth.AuthSettings.SectionName));

        // Distributed Redis Infrastructure
        services.AddSingleton<RestaurantOrder.Infrastructure.Redis.IRedisDatabaseProvider, RestaurantOrder.Infrastructure.Redis.StackExchangeRedisDatabaseProvider>();

        services.AddSingleton<RestaurantOrder.Infrastructure.Auth.JwtTokenService>();
        services.AddSingleton<RestaurantOrder.Application.Auth.IJwtTokenGenerator>(sp => sp.GetRequiredService<RestaurantOrder.Infrastructure.Auth.JwtTokenService>());
        services.AddSingleton<RestaurantOrder.Application.Auth.IRefreshTokenService, RestaurantOrder.Infrastructure.Auth.RefreshTokenService>();
        services.AddScoped<RestaurantOrder.Application.Auth.IPlatformSessionStore, RestaurantOrder.Infrastructure.Auth.PostgreSqlPlatformSessionStore>();
        services.AddScoped<RestaurantOrder.Application.Auth.ILoginRateLimiter, RestaurantOrder.Infrastructure.Auth.RedisLoginRateLimiter>();
        services.AddScoped<RestaurantOrder.Application.Auth.ITokenRevocationValidator, RestaurantOrder.Infrastructure.Auth.DistributedTokenRevocationValidator>();
        services.AddScoped<RestaurantOrder.Infrastructure.Auth.IAuthSessionManager, RestaurantOrder.Infrastructure.Auth.AuthSessionManager>();
        services.AddScoped<RestaurantOrder.Application.Auth.IAuthService, RestaurantOrder.Infrastructure.Auth.AuthService>();

        // Central Authorization & Capability Registry
        services.AddSingleton<RestaurantOrder.Application.Auth.IJwtClaimPrincipalParser, RestaurantOrder.Application.Auth.JwtClaimPrincipalParser>();
        services.AddSingleton<RestaurantOrder.Application.Auth.IResourceOwnershipRequirement, RestaurantOrder.Application.Auth.DefaultResourceOwnershipRequirement>();
        services.AddSingleton<RestaurantOrder.Application.Auth.IPermissionRegistry, RestaurantOrder.Application.Auth.PermissionRegistry>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RestaurantOrder.Infrastructure.Auth.PermissionAuthorizationHandler>();

        // Trusted Terminal & Staff PIN Authentication
        services.AddScoped<RestaurantOrder.Application.Auth.ITerminalEnrollmentStore, RestaurantOrder.Infrastructure.Auth.RedisTerminalEnrollmentStore>();
        services.AddScoped<RestaurantOrder.Application.Auth.ITerminalPinRateLimiter, RestaurantOrder.Infrastructure.Auth.RedisTerminalPinRateLimiter>();
        services.AddScoped<RestaurantOrder.Application.Auth.ITrustedTerminalService, RestaurantOrder.Infrastructure.Auth.TrustedTerminalService>();
        services.AddScoped<RestaurantOrder.Application.Auth.IStaffPinAuthService, RestaurantOrder.Infrastructure.Auth.StaffPinAuthService>();
        services.AddScoped<RestaurantOrder.Application.Auth.IStaffIdentityService, RestaurantOrder.Infrastructure.Auth.StaffIdentityService>();

        services.AddSingleton<RestaurantOrder.Infrastructure.Auth.TestSinkIdentityNotificationSender>();
        services.AddSingleton<RestaurantOrder.Application.Auth.IIdentityNotificationSender>(sp =>
            sp.GetRequiredService<RestaurantOrder.Infrastructure.Auth.TestSinkIdentityNotificationSender>());

        if (environment.IsDevelopment())
        {
            services.AddScoped<RestaurantOrder.Infrastructure.Persistence.Seed.IDevDataSeeder, RestaurantOrder.Infrastructure.Persistence.Seed.DevDataSeeder>();
        }

        return services;
    }

    private static string NormalizeConnectionString(string raw)
    {
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':');
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.TrimStart('/'),
                Username = userInfo.Length > 0 ? userInfo[0] : "postgres",
                Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
                Timeout = 15,
                CommandTimeout = 30
            };
            return builder.ConnectionString;
        }

        return raw;
    }
}
