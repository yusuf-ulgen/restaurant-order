using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Security;

public class WebhookIdentityNotificationTransportTests
{
    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var mock = new Mock<IHostEnvironment>();
        mock.Setup(e => e.EnvironmentName).Returns(environmentName);
        return mock.Object;
    }

    private static string TestSecret(string name) => $"test_{name}_{new string('w', 32)}";

    [Fact]
    public async Task SendAsync_DeliversSignedPayloadWithHeaders_Successfully()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var secret = TestSecret("webhook");
        var webhookUrl = "https://notifications.internal/webhook";

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedRequest = req;
                capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        var httpClient = new HttpClient(handlerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEBHOOK_NOTIFICATION_URL"] = webhookUrl,
                ["WEBHOOK_NOTIFICATION_SECRET"] = secret
            })
            .Build();

        var transport = new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance);

        var idempotencyKey = Guid.NewGuid().ToString("N");
        await transport.SendAsync(
            "UserInvitation",
            "chef@restaurant.com",
            "{\"token\":\"invite-token-abc\"}",
            idempotencyKey);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest.Method);
        Assert.Equal(webhookUrl, capturedRequest.RequestUri?.ToString());

        // Check idempotency header
        Assert.True(capturedRequest.Headers.TryGetValues("X-Idempotency-Key", out var idempValues));
        Assert.Equal(idempotencyKey, idempValues.First());

        // Check HMAC signature
        Assert.True(capturedRequest.Headers.TryGetValues("X-Webhook-Signature", out var sigValues));
        var sigHeader = sigValues.First();
        Assert.StartsWith("sha256=", sigHeader);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedSig = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(capturedBody!))).ToLowerInvariant();
        Assert.Equal(expectedSig, sigHeader);
    }

    [Fact]
    public async Task SendAsync_WhenRemoteReturnsError_ThrowsHttpRequestExceptionWithoutLeakingBody()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Server busy and secret reflected body", Encoding.UTF8, "text/plain")
            });

        var httpClient = new HttpClient(handlerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEBHOOK_NOTIFICATION_URL"] = "https://notifications.internal/webhook",
                ["WEBHOOK_NOTIFICATION_SECRET"] = TestSecret("webhook")
            })
            .Build();

        var transport = new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            transport.SendAsync("UserInvitation", "chef@restaurant.com", "{}", "idem-1"));

        Assert.Contains("500", ex.Message);
        Assert.Contains("WebhookDeliveryFailed", ex.Message);
        Assert.DoesNotContain("Server busy", ex.Message);
        Assert.DoesNotContain("secret reflected body", ex.Message);
    }

    [Fact]
    public async Task SendAsync_WhenRemoteErrorContainsToken_DoesNotLeakTokenInException()
    {
        var sensitiveToken = "raw-sensitive-invitation-token-123456789";
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"{{\"error\":\"invalid token: {sensitiveToken}\"}}", Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEBHOOK_NOTIFICATION_URL"] = "https://notifications.internal/webhook",
                ["WEBHOOK_NOTIFICATION_SECRET"] = TestSecret("webhook")
            })
            .Build();

        var transport = new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            transport.SendAsync("UserInvitation", "chef@restaurant.com", "{}", "idem-test-token"));

        Assert.Contains("400", ex.Message);
        Assert.Contains("WebhookDeliveryFailed", ex.Message);
        Assert.DoesNotContain(sensitiveToken, ex.Message);
    }

    [Fact]
    public void Constructor_HttpUrlInProduction_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEBHOOK_NOTIFICATION_URL"] = "http://insecure.internal/webhook",
                ["WEBHOOK_NOTIFICATION_SECRET"] = TestSecret("webhook")
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance));

        Assert.Contains("must be a valid absolute HTTPS URL", ex.Message);
    }

    [Fact]
    public void Constructor_ShortSecretInProduction_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEBHOOK_NOTIFICATION_URL"] = "https://secure.internal/webhook",
                ["WEBHOOK_NOTIFICATION_SECRET"] = "too-short"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance));

        Assert.Contains("at least 32 characters long", ex.Message);
    }

    [Fact]
    public void Constructor_MissingUrlInProduction_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Production"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance));

        Assert.Contains("WEBHOOK_NOTIFICATION_URL is required", ex.Message);
    }

    [Fact]
    public void Constructor_InDevelopment_AllowsDefaults()
    {
        var httpClient = new HttpClient();
        var config = new ConfigurationBuilder().Build();

        var transport = new WebhookIdentityNotificationTransport(
            httpClient,
            config,
            CreateEnvironment("Development"),
            NullLogger<WebhookIdentityNotificationTransport>.Instance);

        Assert.NotNull(transport);
    }
}
