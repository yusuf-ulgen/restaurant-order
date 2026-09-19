namespace RestaurantOrder.Api;

/// <summary>
/// Validates critical configuration at startup.
/// Enforces fail-fast behavior in Staging and Production environments.
/// </summary>
public static class ConfigurationValidator
{
    public const string InsecureDevJwtSecret = "dev-only-insecure-jwt-secret-min-32-chars-long!";

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // Fail-fast strictly enforced in Staging and Production
        if (environment.IsProduction() || environment.IsEnvironment("Staging"))
        {
            var missingKeys = new List<string>();

            // Check Database Connection
            var dbUrl = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(dbUrl))
            {
                missingKeys.Add("DATABASE_URL (or ConnectionStrings:Database)");
            }

            // Check Redis Connection
            var redisUrl = configuration["REDIS_URL"] ?? configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisUrl))
            {
                missingKeys.Add("REDIS_URL (or ConnectionStrings:Redis)");
            }

            var jwtSecret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret))
            {
                missingKeys.Add("JWT_SECRET");
            }
            else if (jwtSecret.Equals(InsecureDevJwtSecret, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: The default insecure development JWT secret cannot be used in Staging or Production.");
            }

            var color = configuration["DEPLOYMENT_COLOR"];
            if (string.IsNullOrWhiteSpace(color) ||
                (!color.Equals("blue", StringComparison.OrdinalIgnoreCase) &&
                 !color.Equals("green", StringComparison.OrdinalIgnoreCase)))
            {
                missingKeys.Add("DEPLOYMENT_COLOR (must be 'blue' or 'green')");
            }

            if (missingKeys.Count > 0)
            {
                var formattedKeys = string.Join(", ", missingKeys);
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: Missing required configuration keys for environment '{environment.EnvironmentName}': [{formattedKeys}]. Process terminating fail-fast.");
            }
        }
    }
}
