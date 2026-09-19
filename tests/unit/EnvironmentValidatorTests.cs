using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Api;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class EnvironmentValidatorTests
{
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
                { "JWT_SECRET", "super-secure-production-key-at-least-32-chars-long!" },
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
                { "JWT_SECRET", "super-secure-production-key-at-least-32-chars-long!" },
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
                { "JWT_SECRET", "super-secure-production-key-at-least-32-chars-long!" },
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
                { "JWT_SECRET", "super-secure-production-key-at-least-32-chars-long!" },
                { "DEPLOYMENT_COLOR", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var exception = Record.Exception(() => ConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void WorkerValidate_WhenInProductionAndMissingSlot_ThrowsInvalidOperationException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            WorkerConfigurationValidator.Validate(config, env));

        Assert.Contains("DEPLOYMENT_COLOR", ex.Message);
        Assert.Contains("ACTIVE_DEPLOYMENT_SLOT", ex.Message);
    }
}
