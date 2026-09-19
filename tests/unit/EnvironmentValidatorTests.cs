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
    public void ApiValidate_WhenInProductionAndUsingInsecureJwtSecret_ThrowsInvalidOperationException()
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

    [Fact]
    public void ApiValidate_WhenInProductionAndValidConfig_PassesWithoutException()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
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
