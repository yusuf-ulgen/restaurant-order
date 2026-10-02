using RestaurantOrder.Infrastructure.Auth;

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

            // 5. PIN Pepper Secret Validation (canonical PIN_PEPPER_SECRET strictly enforced)
            var pinPepper = configuration["PIN_PEPPER_SECRET"];
            if (string.IsNullOrWhiteSpace(pinPepper))
            {
                missingKeys.Add("PIN_PEPPER_SECRET");
            }
            else
            {
                ValidatePinPepperSecret(pinPepper);
            }

            // 6. CORS Allowed Origins Validation (HTTPS strictly enforced)
            var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? configuration["CORS_ALLOWED_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (corsOrigins == null || corsOrigins.Length == 0)
            {
                missingKeys.Add("Cors:AllowedOrigins (or CORS_ALLOWED_ORIGINS)");
            }
            else
            {
                ValidateCorsAllowedOrigins(corsOrigins);
            }

            // 7. Forwarded Headers Validation (explicit proxies or CIDR networks required if enabled)
            var forwardedHeadersEnabled = configuration.GetValue<bool>("ForwardedHeaders:Enabled")
                || string.Equals(configuration["FORWARDED_HEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);

            if (forwardedHeadersEnabled)
            {
                var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()
                    ?? configuration["FORWARDED_HEADERS_KNOWN_PROXIES"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
                    ?? configuration["FORWARDED_HEADERS_KNOWN_NETWORKS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                ValidateForwardedHeaders(proxies, networks);
            }

            // 8. Notification Provider Validation (prevent silent test sink in production)
            var notificationProvider = configuration["Notification:Provider"] ?? configuration["NOTIFICATION_PROVIDER"];
            if (string.IsNullOrWhiteSpace(notificationProvider))
            {
                missingKeys.Add("Notification:Provider (or NOTIFICATION_PROVIDER)");
            }
            else
            {
                ValidateNotificationProvider(notificationProvider);

                var encryptionKey = configuration["NOTIFICATION_ENCRYPTION_KEY"] ?? configuration["Notification:EncryptionKey"];
                if (string.IsNullOrWhiteSpace(encryptionKey))
                {
                    missingKeys.Add("NOTIFICATION_ENCRYPTION_KEY");
                }
                else
                {
                    ValidateNotificationEncryptionKey(encryptionKey);
                }

                var webhookUrl = configuration["WEBHOOK_NOTIFICATION_URL"] ?? configuration["Notification:Webhook:Url"];
                if (string.IsNullOrWhiteSpace(webhookUrl))
                {
                    missingKeys.Add("WEBHOOK_NOTIFICATION_URL");
                }
                else
                {
                    ValidateWebhookUrl(webhookUrl);
                }

                var webhookSecret = configuration["WEBHOOK_NOTIFICATION_SECRET"] ?? configuration["Notification:Webhook:Secret"];
                if (string.IsNullOrWhiteSpace(webhookSecret))
                {
                    missingKeys.Add("WEBHOOK_NOTIFICATION_SECRET");
                }
                else
                {
                    ValidateWebhookSecret(webhookSecret);
                }
            }

            if (missingKeys.Count > 0)
            {
                var formattedKeys = string.Join(", ", missingKeys);
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: Missing required configuration keys for environment '{environment.EnvironmentName}': [{formattedKeys}]. Process terminating fail-fast.");
            }
        }
    }

    public static void ValidateForwardedHeaders(string[]? proxies, string[]? networks)
    {
        var hasProxies = proxies != null && proxies.Length > 0;
        var hasNetworks = networks != null && networks.Length > 0;

        if (!hasProxies && !hasNetworks)
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: ForwardedHeaders is enabled, but neither KnownProxies nor KnownNetworks contains any entries. At least one explicit trusted proxy or CIDR network is required in Staging/Production.");
        }

        if (hasProxies)
        {
            foreach (var proxy in proxies!)
            {
                if (!System.Net.IPAddress.TryParse(proxy.Trim(), out _))
                {
                    throw new InvalidOperationException(
                        $"FATAL CONFIGURATION ERROR: ForwardedHeaders:KnownProxies contains invalid IP address: '{proxy}'.");
                }
            }
        }

        if (hasNetworks)
        {
            foreach (var net in networks!)
            {
                if (!TryParseCidr(net.Trim(), out _, out _))
                {
                    throw new InvalidOperationException(
                        $"FATAL CONFIGURATION ERROR: ForwardedHeaders:KnownNetworks contains invalid CIDR notation: '{net}'. Expected format like '10.0.1.0/24'.");
                }
            }
        }
    }

    public static bool TryParseCidr(string cidr, out System.Net.IPAddress ip, out int prefixLength)
    {
        ip = System.Net.IPAddress.None;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(cidr)) return false;
        var parts = cidr.Split('/');
        if (parts.Length != 2) return false;

        if (!System.Net.IPAddress.TryParse(parts[0].Trim(), out ip!)) return false;
        if (!int.TryParse(parts[1].Trim(), out prefixLength)) return false;

        var maxPrefix = ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        return prefixLength >= 0 && prefixLength <= maxPrefix;
    }

    private static readonly string[] AllowedNotificationProviders = ["TransactionalOutbox"];

    private static void ValidateNotificationProvider(string provider)
    {
        if (!AllowedNotificationProviders.Any(p => p.Equals(provider.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"FATAL CONFIGURATION ERROR: Unsupported Notification:Provider '{provider}'. Must be one of: [{string.Join(", ", AllowedNotificationProviders)}]. Development test sink is strictly prohibited in Staging/Production.");
        }
    }

    private static void ValidateNotificationEncryptionKey(string key)
    {
        foreach (var keyword in InsecureKeywords)
        {
            if (key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: NOTIFICATION_ENCRYPTION_KEY contains insecure placeholder pattern. A high-entropy secret from a secret manager is required.");
            }
        }

        if (!AesKeyValidator.TryParseKey(key, out _, out var errorMessage))
        {
            throw new InvalidOperationException(
                $"FATAL CONFIGURATION ERROR: {errorMessage}");
        }
    }

    private static void ValidateWebhookUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_URL '{url}' is not a valid absolute https URI. Production and Staging strictly require HTTPS endpoints.");
        }
    }

    private static void ValidateWebhookSecret(string secret)
    {
        if (secret.Length < 32)
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_SECRET must be at least 32 characters long for cryptographic security in Staging/Production.");
        }

        foreach (var keyword in InsecureKeywords)
        {
            if (secret.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_SECRET contains insecure placeholder pattern. A high-entropy secret from a secret manager is required.");
            }
        }
    }

    private static void ValidatePinPepperSecret(string pepper)
    {
        if (pepper.Length < 32)
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: PIN_PEPPER_SECRET must be at least 32 characters long for cryptographic security in Staging/Production.");
        }

        foreach (var keyword in InsecureKeywords)
        {
            if (pepper.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: PIN_PEPPER_SECRET contains insecure placeholder pattern. A high-entropy secret from a secret manager is required.");
            }
        }
    }

    private static void ValidateCorsAllowedOrigins(string[] origins)
    {
        foreach (var origin in origins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: Cors:AllowedOrigins contains empty or whitespace entry.");
            }

            if (origin.Contains('*'))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: Wildcard '*' is strictly prohibited in Cors:AllowedOrigins in Staging/Production.");
            }

            if (origin.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "FATAL CONFIGURATION ERROR: Localhost origins are prohibited in Cors:AllowedOrigins in Staging/Production.");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: Cors:AllowedOrigins origin '{origin}' is not a valid absolute https URI. Production and Staging strictly require HTTPS origins.");
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
