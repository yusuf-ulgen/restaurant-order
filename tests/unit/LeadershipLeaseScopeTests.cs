using Moq;
using RestaurantOrder.Worker.Safety;
using Xunit;

namespace RestaurantOrder.UnitTests;

public class LeadershipLeaseScopeTests
{
    [Fact]
    public async Task SlowWork_IsCancelled_WhenRedisOwnershipIsLost()
    {
        var guard = new Mock<IWorkerActivationGuard>();
        guard.Setup(g => g.IsActiveSlotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var leases = new Mock<IWorkerLeaseManager>();
        leases.Setup(l => l.RenewLeaseAsync("leadership", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await using var leadership = new LeadershipLeaseScope(guard.Object, leases.Object, "leadership",
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10), CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(TimeSpan.FromSeconds(2), leadership.Token));
        Assert.False(await leadership.ValidateAsync(CancellationToken.None));
        leases.Verify(l => l.RenewLeaseAsync("leadership", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
