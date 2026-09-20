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
