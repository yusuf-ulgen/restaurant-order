using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class WorkerConfigurationValidatorTests
{
    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Worker";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
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

    [Fact]
    public void WorkerValidate_WhenAllKeysValidInProduction_Passes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "DEPLOYMENT_COLOR", "blue" },
                { "ACTIVE_DEPLOYMENT_SLOT", "green" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var exception = Record.Exception(() => WorkerConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void WorkerValidate_WithConnectionStringsFallback_Passes()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:Database", "Host=localhost;Database=test" },
                { "ConnectionStrings:Redis", "localhost:6379" },
                { "DEPLOYMENT_COLOR", "green" },
                { "ACTIVE_DEPLOYMENT_SLOT", "blue" }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = "Staging" };
        var exception = Record.Exception(() => WorkerConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void WorkerValidate_InDevelopment_SkipsValidation()
    {
        var config = new ConfigurationBuilder().Build();
        var env = new TestHostEnvironment { EnvironmentName = Environments.Development };

        var exception = Record.Exception(() => WorkerConfigurationValidator.Validate(config, env));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("yellow", "blue")]
    [InlineData("blue", "invalid")]
    public void WorkerValidate_WhenInvalidSlotColor_Throws(string slot, string activeSlot)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DATABASE_URL", "Host=localhost;Database=test" },
                { "REDIS_URL", "localhost:6379" },
                { "DEPLOYMENT_COLOR", slot },
                { "ACTIVE_DEPLOYMENT_SLOT", activeSlot }
            })
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        Assert.Throws<InvalidOperationException>(() => WorkerConfigurationValidator.Validate(config, env));
    }
}
