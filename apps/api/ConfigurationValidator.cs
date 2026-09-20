namespace RestaurantOrder.Api;

/// <summary>
/// Validates critical configuration at startup.
/// Enforces fail-fast behavior in Staging and Production environments.
/// Ensures secrets meet length and entropy constraints without leaking values into log streams.
/// </summary>
public static class ConfigurationValidator
{
    public const string InsecureDevJwtSecret = "dev-only-insecure-jwt-secret-min-32-chars-long!";

    private static readonly string[] InsecureKeywords =
    [
        "placeholder",
        "changeme",
        "example",
        "dummy",
        "staging_secure_pass",
        "password123",
        "default"
    ];

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // Fail-fast strictly enforced in Staging and Production
        if (environment.IsProduction() || environment.IsEnvironment("Staging"))
        {
            var missingKeys = new List<string>();

            // 1. Database Connection Validation
            var dbUrl = configuration["DATABASE_URL"] ?? configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(dbUrl))
            {
                missingKeys.Add("DATABASE_URL (or ConnectionStrings:Database)");
            }
            else
            {
                ValidateDatabaseUrlFormat(dbUrl);
            }

            // 2. Redis Connection Validation
            var redisUrl = configuration["REDIS_URL"] ?? configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisUrl))
            {
                missingKeys.Add("REDIS_URL (or ConnectionStrings:Redis)");
            }
            else
            {
                ValidateRedisUrlFormat(redisUrl);
            }

            // 3. JWT Secret Validation
            var jwtSecret = configuration["JWT_SECRET"] ?? configuration["Jwt:Secret"];
            if (string.IsNullOrWhiteSpace(jwtSecret))
            {
                missingKeys.Add("JWT_SECRET");
            }
            else
            {
                ValidateJwtSecret(jwtSecret);
            }

            // 4. Deployment Color Validation
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

    private static void ValidateJwtSecret(string secret)
    {
        if (secret.Length < 32)
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: JWT_SECRET must be at least 32 characters long for cryptographic security in Staging/Production.");
        }

        if (secret.Equals(InsecureDevJwtSecret, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: The default insecure development JWT secret cannot be used in Staging or Production.");
        }

        foreach (var keyword in InsecureKeywords)
        {
            if (secret.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: JWT_SECRET contains insecure placeholder or example pattern. A high-entropy secret from a secret manager is required.");
            }
        }
    }

    private static void ValidateDatabaseUrlFormat(string dbUrl)
    {
        foreach (var keyword in InsecureKeywords)
        {
            if (dbUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: DATABASE_URL contains insecure placeholder password or configuration values.");
            }
        }

        var isUri = dbUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                    dbUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

        if (isUri)
        {
            if (!Uri.TryCreate(dbUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: DATABASE_URL URI format is invalid.");
            }
        }
        else
        {
            var hasHost = dbUrl.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                          dbUrl.Contains("Server=", StringComparison.OrdinalIgnoreCase);
            var hasDb = dbUrl.Contains("Database=", StringComparison.OrdinalIgnoreCase) ||
                        dbUrl.Contains("Db=", StringComparison.OrdinalIgnoreCase);

            if (!hasHost || !hasDb)
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: DATABASE_URL ADO.NET connection string is invalid. Must specify Host and Database.");
            }
        }
    }

    private static void ValidateRedisUrlFormat(string redisUrl)
    {
        foreach (var keyword in InsecureKeywords)
        {
            if (redisUrl.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: REDIS_URL contains insecure placeholder configuration values.");
            }
        }

        if (redisUrl.StartsWith("redis://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(redisUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: REDIS_URL URI format is invalid.");
            }
        }
        else
        {
            var parts = redisUrl.Split(':');
            if (parts.Length > 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: REDIS_URL host/port format is invalid.");
            }
        }
    }
}
