namespace RestaurantOrder.Worker.Safety;

/// <summary>
/// Validates worker startup configuration and enforces fail-fast behavior in Staging and Production.
/// </summary>
public static class WorkerConfigurationValidator
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        // Staging and Production strict fail-fast validations
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

            var slotColor = configuration["DEPLOYMENT_COLOR"];
            if (string.IsNullOrWhiteSpace(slotColor) ||
                (!slotColor.Equals("blue", StringComparison.OrdinalIgnoreCase) &&
                 !slotColor.Equals("green", StringComparison.OrdinalIgnoreCase)))
            {
                missingKeys.Add("DEPLOYMENT_COLOR (must be 'blue' or 'green')");
            }

            var activeSlot = configuration["ACTIVE_DEPLOYMENT_SLOT"];
            if (string.IsNullOrWhiteSpace(activeSlot) ||
                (!activeSlot.Equals("blue", StringComparison.OrdinalIgnoreCase) &&
                 !activeSlot.Equals("green", StringComparison.OrdinalIgnoreCase)))
            {
                missingKeys.Add("ACTIVE_DEPLOYMENT_SLOT (must be 'blue' or 'green')");
            }

            if (missingKeys.Count > 0)
            {
                var formattedKeys = string.Join(", ", missingKeys);
                throw new InvalidOperationException(
                    $"FATAL WORKER CONFIGURATION ERROR: Missing required configuration keys for environment '{environment.EnvironmentName}': [{formattedKeys}]. Worker host terminating fail-fast.");
            }
        }
    }
}
