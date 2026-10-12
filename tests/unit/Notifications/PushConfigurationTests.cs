using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Infrastructure.Notifications;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public sealed class PushConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Disabled")]
    [InlineData("disabled")]
    public async Task DefaultOrDisabled_DoesNotRequireFirebaseCredentials(string? provider)
    {
        using var services = new ServiceCollection().AddOperationalPush(Config(provider)).BuildServiceProvider();
        var transport = services.GetRequiredService<IPushNotificationTransport>();
        Assert.IsType<DisabledPushNotificationTransport>(transport);
        Assert.Equal(PushSendStatus.Disabled, (await transport.SendAsync(FirebasePushTestHarness.Request())).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Firebase")]
    [InlineData("TransactionalOutbox")]
    public void UnknownProvider_FailsWithoutEchoingSetting(string provider)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOperationalPush(Config(provider)));
        Assert.Equal("PUSH_PROVIDER yalnızca Disabled veya Fcm olabilir.", error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("project/path")]
    [InlineData("https://example.invalid")]
    [InlineData("UpperCase")]
    [InlineData("short")]
    [InlineData("1project")]
    [InlineData("project-")]
    public void FcmWithInvalidProject_FailsAtRegistration(string? project)
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOperationalPush(Config("Fcm", project)));
    }

    [Fact]
    public void FcmWithOversizedProject_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOperationalPush(Config("Fcm", new string('a', 31))));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(30)]
    public void FcmProjectLengthBoundaries_AreAccepted(int length)
    {
        Assert.Single(new ServiceCollection().AddOperationalPush(Config("Fcm", new string('a', length))));
    }

    [Theory]
    [InlineData("Fcm")]
    [InlineData("fcm")]
    public void FcmRegistration_IsLazyAndIndependentOfIdentityProvider(string provider)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PUSH_PROVIDER"] = provider, ["FCM_PROJECT_ID"] = "restaurant-order-test", ["NOTIFICATION_PROVIDER"] = "TransactionalOutbox"
        }).Build();
        var services = new ServiceCollection().AddOperationalPush(config);
        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IPushNotificationTransport), descriptor.ServiceType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal("TransactionalOutbox", config["NOTIFICATION_PROVIDER"]);
    }

    [Fact]
    public async Task DisabledTransport_RespectsCancellationAndNullValidation()
    {
        var transport = new DisabledPushNotificationTransport();
        await Assert.ThrowsAsync<ArgumentNullException>(() => transport.SendAsync(null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendAsync(FirebasePushTestHarness.Request(), cancellation.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("fid/unsafe")]
    [InlineData("fid\nunsafe")]
    public void Request_RejectsInvalidInstallationId(string? id)
    {
        var error = Assert.Throws<ArgumentException>(() => new PushNotificationRequest(id!, Guid.NewGuid(), FirebasePushTestHarness.Now));
        Assert.DoesNotContain("fid/unsafe", error.Message);
    }

    [Fact]
    public void Request_RejectsOversizedIdAndEmptyEvent_AndRedactsId()
    {
        Assert.Throws<ArgumentException>(() => new PushNotificationRequest(new string('a', 257), Guid.NewGuid(), FirebasePushTestHarness.Now));
        Assert.Throws<ArgumentException>(() => new PushNotificationRequest("synthetic-id", Guid.Empty, FirebasePushTestHarness.Now));
        Assert.DoesNotContain(FirebasePushTestHarness.InstallationId, FirebasePushTestHarness.Request().ToString());
    }

    private static IConfiguration Config(string? provider, string? project = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PUSH_PROVIDER"] = provider, ["FCM_PROJECT_ID"] = project
        }).Build();
}
