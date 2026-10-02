using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Api;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class EnvironmentValidatorTests
{
    private static string TestHexKey() => Convert.ToHexString(new byte[32]);
    private static string TestSecret(string name) => $"test_{name}_{new string('x', 32)}";
    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndMissingDatabaseUrl_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("DATABASE_URL", ex.Message);
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndJwtSecretTooShort_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", "too-short-jwt-secret" }, // < 32 chars
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("at least 32 characters long", ex.Message);
        Assert.DoesNotContain("too-short-jwt-secret", ex.Message); // Never leak secret
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndUsingInsecureDevJwtSecret_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", ConfigurationValidator.InsecureDevJwtSecret },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("insecure development JWT secret", ex.Message);
    }

    [Theory]
    [InlineData("staging-jwt-secret-placeholder-32-chars!")]
    [InlineData("my-dummy-jwt-secret-that-is-at-least-32-chars!")]
    [InlineData("example-super-long-secret-key-32-characters-long")]
    [InlineData("changeme-super-long-secret-key-32-characters!")]
    public void ApiValidate_WhenInProductionAndJwtSecretContainsPlaceholder_ThrowsInvalidOperationException(string placeholderSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", placeholderSecret },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("insecure placeholder", ex.Message);
        Assert.DoesNotContain(placeholderSecret, ex.Message); // Never leak secret
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndDatabaseUrlContainsPlaceholder_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=postgres;Port=5432;Database=restaurant_order_staging;Username=postgres;Password=staging_secure_pass_placeholder" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("insecure placeholder", ex.Message);
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndDatabaseUrlInvalidFormat_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "just-an-invalid-string" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidator.Validate(config, env));

        Assert.Contains("DATABASE_URL ADO.NET connection string is invalid", ex.Message);
    }

    [Fact]
    public void ApiValidate_WhenInProductionAndValidConfig_PassesWithoutException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Port=5432;Database=restaurant_order_prod;Username=app;Password=real_prod_entropy_991823" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" },
                { "PIN_PEPPER_SECRET", TestSecret("pin_pepper") },
                { "Cors:AllowedOrigins:0", "https://admin.restaurantorder.app" },
                { "NOTIFICATION_PROVIDER", "TransactionalOutbox" },
                { "NOTIFICATION_ENCRYPTION_KEY", TestHexKey() },
                { "WEBHOOK_NOTIFICATION_URL", "https://notifications.internal/webhook" },
                { "WEBHOOK_NOTIFICATION_SECRET", TestSecret("webhook") }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() => ConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void ApiValidate_WhenFallbackConnectionStringsUsed_ValidatesSuccessfully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Database", "Host=localhost;Database=test" },
                { "ConnectionStrings:Redis", "localhost:6379" },
                { "Jwt:Secret", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "green" },
                { "PIN_PEPPER_SECRET", TestSecret("pin_pepper") },
                { "Cors:AllowedOrigins:0", "https://admin.restaurantorder.app" },
                { "NOTIFICATION_PROVIDER", "TransactionalOutbox" },
                { "NOTIFICATION_ENCRYPTION_KEY", TestHexKey() },
                { "WEBHOOK_NOTIFICATION_URL", "https://notifications.internal/webhook" },
                { "WEBHOOK_NOTIFICATION_SECRET", TestSecret("webhook") }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var exception = Record.Exception(() => ConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("postgres://app_user:strong_password@localhost:5432/restaurant_db")]
    [InlineData("postgresql://app_user:strong_password@localhost:5432/restaurant_db")]
    public void ApiValidate_WhenPostgresUriFormat_ValidatesSuccessfully(string dbUri)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", dbUri },
                { "REDIS_URL", "redis://redis_user:secret_auth_token@localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" },
                { "PIN_PEPPER_SECRET", TestSecret("pin_pepper") },
                { "Cors:AllowedOrigins:0", "https://admin.restaurantorder.app" },
                { "NOTIFICATION_PROVIDER", "TransactionalOutbox" },
                { "NOTIFICATION_ENCRYPTION_KEY", TestHexKey() },
                { "WEBHOOK_NOTIFICATION_URL", "https://notifications.internal/webhook" },
                { "WEBHOOK_NOTIFICATION_SECRET", TestSecret("webhook") }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var exception = Record.Exception(() => ConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void ApiValidate_WhenRedisUriValid_ValidatesSuccessfully()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "redis://redis_user:secret_auth_token@localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" },
                { "PIN_PEPPER_SECRET", TestSecret("pin_pepper") },
                { "Cors:AllowedOrigins:0", "https://admin.restaurantorder.app" },
                { "NOTIFICATION_PROVIDER", "TransactionalOutbox" },
                { "NOTIFICATION_ENCRYPTION_KEY", TestHexKey() },
                { "WEBHOOK_NOTIFICATION_URL", "https://notifications.internal/webhook" },
                { "WEBHOOK_NOTIFICATION_SECRET", TestSecret("webhook") }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var exception = Record.Exception(() => ConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("postgres:///missing_host")]
    [InlineData("postgresql://:5432/missing_host")]
    public void ApiValidate_WhenPostgresUriInvalidHost_Throws(string invalidUri)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", invalidUri },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }

    [Theory]
    [InlineData("Host=localhost")] // Missing Database
    [InlineData("Database=test")]  // Missing Host
    [InlineData("Port=5432")]      // Missing both
    public void ApiValidate_WhenAdoNetDatabaseUrlMissingHostOrDb_Throws(string invalidConnStr)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", invalidConnStr },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }

    [Theory]
    [InlineData("redis:///missing_host")]
    [InlineData("redis://:6379/")]
    public void ApiValidate_WhenRedisUriInvalidHost_Throws(string invalidRedisUri)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", invalidRedisUri },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }

    [Theory]
    [InlineData(":6379")]           // Empty host
    [InlineData("host:6379:extra")]  // Too many colons
    public void ApiValidate_WhenRedisHostPortFormatInvalid_Throws(string invalidHostPort)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", invalidHostPort },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }

    [Theory]
    [InlineData("yellow")]
    [InlineData("")]
    [InlineData(null)]
    public void ApiValidate_WhenDeploymentColorInvalid_Throws(string? invalidColor)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", invalidColor }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }

    [Theory]
    [InlineData("Host=localhost;Database=test;Password=changeme", "localhost:6379")]
    [InlineData("Host=localhost;Database=test", "localhost:6379,password=default")]
    public void ApiValidate_WhenInsecureKeywordsInUrls_Throws(string dbUrl, string redisUrl)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", dbUrl },
                { "REDIS_URL", redisUrl },
                { "JWT_SECRET", TestSecret("jwt") },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => ConfigurationValidator.Validate(config, env));
    }
}

