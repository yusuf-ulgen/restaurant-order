using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Persistence.Seed;
using Xunit;

namespace RestaurantOrder.UnitTests.Persistence;

public class DevDataSeederUnitTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("production")]
    [InlineData("staging")]
    public async Task DevDataSeeder_Throws_WhenEnvironmentIsNotDevelopment(string environmentName)
    {
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(environmentName);

        var mockLogger = new Mock<ILogger<DevDataSeeder>>();

        // DbContext is null because environment check happens before any DB operation
        var seeder = new DevDataSeeder(null!, mockEnv.Object, mockLogger.Object);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("FATAL SECURITY VIOLATION", ex.Message);
        Assert.Contains(environmentName, ex.Message);
    }

    [Fact]
    public void SyntheticSeed_Guids_AreDeterministicAndNonZero()
    {
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticTenant1Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticTenant2Id.Value);
        Assert.NotEqual(DevDataSeeder.SyntheticTenant1Id, DevDataSeeder.SyntheticTenant2Id);

        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBrand1Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBrand2Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBrand3Id.Value);

        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBranch1Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBranch2Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBranch3Id.Value);
        Assert.NotEqual(Guid.Empty, DevDataSeeder.SyntheticBranch4Id.Value);
    }
}
