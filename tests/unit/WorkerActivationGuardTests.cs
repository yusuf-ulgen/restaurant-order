using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class WorkerActivationGuardTests
{
    private class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "RestaurantOrder.Worker";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task IsActiveSlotAsync_WhenColorMatchesActiveSlot_ReturnsTrueAndActiveStatus()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "DEPLOYMENT_COLOR", "blue" },
            { "ACTIVE_DEPLOYMENT_SLOT", "blue" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<ConfigurationWorkerActivationGuard>.Instance;

        var guard = new ConfigurationWorkerActivationGuard(configuration, env, logger);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.True(isActive);
        Assert.Equal(WorkerActivationStatus.Active, guard.Status);
        Assert.Equal("blue", guard.SlotColor);
        Assert.Equal("blue", guard.ActiveSlot);
    }

    [Fact]
    public async Task IsActiveSlotAsync_WhenColorDoesNotMatchActiveSlot_ReturnsFalseAndStandbyStatus()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "DEPLOYMENT_COLOR", "green" },
            { "ACTIVE_DEPLOYMENT_SLOT", "blue" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<ConfigurationWorkerActivationGuard>.Instance;

        var guard = new ConfigurationWorkerActivationGuard(configuration, env, logger);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Standby, guard.Status);
        Assert.Equal("green", guard.SlotColor);
        Assert.Equal("blue", guard.ActiveSlot);
    }

    [Fact]
    public async Task IsActiveSlotAsync_WhenInProductionAndConfigMissing_FailsClosedWithErrorStatus()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            // Missing DEPLOYMENT_COLOR and ACTIVE_DEPLOYMENT_SLOT
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<ConfigurationWorkerActivationGuard>.Instance;

        var guard = new ConfigurationWorkerActivationGuard(configuration, env, logger);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Error, guard.Status);
    }

    [Fact]
    public async Task IsActiveSlotAsync_WhenWorkerDisabled_ReturnsFalseAndDisabledStatus()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "WORKER_ENABLED", "false" },
            { "DEPLOYMENT_COLOR", "blue" },
            { "ACTIVE_DEPLOYMENT_SLOT", "blue" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var env = new TestHostEnvironment { EnvironmentName = Environments.Production };
        var logger = NullLogger<ConfigurationWorkerActivationGuard>.Instance;

        var guard = new ConfigurationWorkerActivationGuard(configuration, env, logger);

        var isActive = await guard.IsActiveSlotAsync();

        Assert.False(isActive);
        Assert.Equal(WorkerActivationStatus.Disabled, guard.Status);
    }
}
