using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2;
using RestaurantOrder.Application.Notifications;
using Xunit;

namespace RestaurantOrder.UnitTests.Notifications;

public sealed class FirebasePushNotificationTransportTests
{
    [Theory]
    [InlineData(30.9, 30)]
    [InlineData(3600, 60)]
    [InlineData(1, 1)]
    public async Task Send_UsesFidAndBoundedLifetime_WithoutPrivateContent(double lifetime, int expectedTtl)
    {
        using var harness = new FirebasePushTestHarness();
        var request = FirebasePushTestHarness.Request(lifetime);
        var result = await harness.Transport.SendAsync(request);

        Assert.Equal(PushSendStatus.ProviderAccepted, result.Status);
        Assert.Null(result.RetryAfter);
        Assert.Equal("https://fcm.googleapis.com/v1/projects/restaurant-order-test/messages:send", harness.RequestUri!.ToString());
        using var json = JsonDocument.Parse(Assert.Single(harness.Bodies));
        var message = json.RootElement.GetProperty("message");
        Assert.Equal(FirebasePushTestHarness.InstallationId, message.GetProperty("fid").GetString());
        Assert.False(message.TryGetProperty("token", out _));
        Assert.False(message.TryGetProperty("topic", out _));
        Assert.False(message.TryGetProperty("condition", out _));
        var data = message.GetProperty("data");
        Assert.Equal(2, data.EnumerateObject().Count());
        Assert.Equal(request.EventId.ToString("N"), data.GetProperty("event_id").GetString());
        var expiry = FirebasePushTestHarness.Now.AddSeconds(expectedTtl).ToUnixTimeSeconds().ToString();
        Assert.Equal(expiry, data.GetProperty("expires_at").GetString());
        Assert.Equal("Restoran bildirimi", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.Equal("Yeni bir güncelleme var. Ayrıntılar için uygulamayı açın.", message.GetProperty("notification").GetProperty("body").GetString());
        var webpush = message.GetProperty("webpush");
        Assert.Equal(expectedTtl.ToString(), webpush.GetProperty("headers").GetProperty("TTL").GetString());
        Assert.False(webpush.TryGetProperty("fcm_options", out _));
        Assert.Equal($"{expectedTtl}s", message.GetProperty("android").GetProperty("ttl").GetString());
        Assert.Equal(expiry, message.GetProperty("apns").GetProperty("headers").GetProperty("apns-expiration").GetString());
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(0.9)]
    public async Task ExpiredOrSubsecondEvent_DoesNotContactProvider(double lifetime)
    {
        using var harness = new FirebasePushTestHarness();
        Assert.Equal(PushSendStatus.Expired, (await harness.Transport.SendAsync(FirebasePushTestHarness.Request(lifetime))).Status);
        Assert.Empty(harness.Bodies);
    }

    [Theory]
    [InlineData("UNREGISTERED", 404, "NOT_FOUND", PushSendStatus.InvalidRecipient)]
    [InlineData("SENDER_ID_MISMATCH", 403, "PERMISSION_DENIED", PushSendStatus.ConfigurationFailure)]
    [InlineData("THIRD_PARTY_AUTH_ERROR", 401, "UNAUTHENTICATED", PushSendStatus.ConfigurationFailure)]
    [InlineData("INVALID_ARGUMENT", 400, "INVALID_ARGUMENT", PushSendStatus.PermanentFailure)]
    [InlineData("QUOTA_EXCEEDED", 429, "RESOURCE_EXHAUSTED", PushSendStatus.RetryableFailure)]
    [InlineData("UNAVAILABLE", 503, "UNAVAILABLE", PushSendStatus.RetryableFailure)]
    [InlineData("INTERNAL", 500, "INTERNAL", PushSendStatus.RetryableFailure)]
    [InlineData(null, 403, "PERMISSION_DENIED", PushSendStatus.ConfigurationFailure)]
    [InlineData(null, 401, "UNAUTHENTICATED", PushSendStatus.ConfigurationFailure)]
    [InlineData(null, 404, "NOT_FOUND", PushSendStatus.ConfigurationFailure)]
    [InlineData(null, 400, "INVALID_ARGUMENT", PushSendStatus.PermanentFailure)]
    [InlineData(null, 429, "RESOURCE_EXHAUSTED", PushSendStatus.RetryableFailure)]
    [InlineData(null, 418, "UNKNOWN", PushSendStatus.RetryableFailure)]
    public async Task ProviderErrors_AreClassifiedWithoutReturningSensitiveErrorBody(
        string? messagingCode, int httpStatus, string status, PushSendStatus expected)
    {
        using var harness = new FirebasePushTestHarness(_ =>
        {
            var json = JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = httpStatus, status, message = "synthetic-private-provider-detail",
                    details = messagingCode is null ? [] : new[] { new Dictionary<string, string>
                    {
                        ["@type"] = "type.googleapis.com/google.firebase.fcm.v1.FcmError", ["errorCode"] = messagingCode
                    } }
                }
            });
            return Task.FromResult(FirebasePushTestHarness.Json((HttpStatusCode)httpStatus, json));
        });
        var result = await harness.Transport.SendAsync(FirebasePushTestHarness.Request());
        Assert.Equal(expected, result.Status);
        Assert.DoesNotContain("synthetic-private-provider-detail", result.ToString());
        Assert.DoesNotContain(FirebasePushTestHarness.InstallationId, result.ToString());
        if (httpStatus == 429) Assert.Equal(TimeSpan.FromMinutes(1), result.RetryAfter);
    }

    [Theory]
    [InlineData(120, false, 120)]
    [InlineData(-30, false, 60)]
    [InlineData(90, true, 90)]
    [InlineData(-30, true, 60)]
    public async Task RetryAfter_IsPreservedWithQuotaMinimum(int seconds, bool asDate, int expectedSeconds)
    {
        using var harness = new FirebasePushTestHarness(_ =>
        {
            var response = FirebasePushTestHarness.Json(HttpStatusCode.TooManyRequests,
                "{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"QUOTA_EXCEEDED\"}]}}");
            response.Headers.RetryAfter = asDate
                ? new RetryConditionHeaderValue(FirebasePushTestHarness.Now.AddSeconds(seconds))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(Math.Max(0, seconds)));
            return Task.FromResult(response);
        });
        var result = await harness.Transport.SendAsync(FirebasePushTestHarness.Request());
        Assert.Equal(PushSendStatus.RetryableFailure, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), result.RetryAfter);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndDoesNotContactProvider()
    {
        using var harness = new FirebasePushTestHarness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Transport.SendAsync(FirebasePushTestHarness.Request(), cancellation.Token));
        Assert.Empty(harness.Bodies);
    }

    [Fact]
    public async Task InflightCallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var harness = new FirebasePushTestHarness(async ct =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Transport.SendAsync(FirebasePushTestHarness.Request(), cancellation.Token));
    }

    [Fact]
    public async Task SendDeadline_BoundsSlowProvider()
    {
        using var harness = new FirebasePushTestHarness(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null!;
        });
        var result = await harness.Transport.SendAsync(FirebasePushTestHarness.Request(1.1));
        Assert.Equal(PushSendStatus.RetryableFailure, result.Status);
    }

    [Fact]
    public async Task CredentialTokenFailure_IsSanitized()
    {
        using var harness = new FirebasePushTestHarness(credential:
            GoogleCredential.FromAccessToken("synthetic-test-credential", new FailingAccessMethod()));
        var result = await harness.Transport.SendAsync(FirebasePushTestHarness.Request());
        Assert.Equal(PushSendStatus.ConfigurationFailure, result.Status);
        Assert.DoesNotContain("synthetic-private-credential-detail", result.ToString());
        Assert.Empty(harness.Bodies);
    }

    [Fact]
    public async Task NullRequest_IsRejected()
    {
        using var harness = new FirebasePushTestHarness();
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Transport.SendAsync(null!));
        Assert.Empty(harness.Bodies);
    }

    private sealed class FailingAccessMethod : IAccessMethod
    {
        public void Intercept(HttpRequestMessage request, string token) => throw new TokenResponseException(new TokenErrorResponse
        {
            Error = "invalid_grant", ErrorDescription = "synthetic-private-credential-detail"
        });

        public string GetAccessToken(HttpRequestMessage request) => "synthetic-test-credential";
    }
}
