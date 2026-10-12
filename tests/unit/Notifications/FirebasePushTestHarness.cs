using System.Net;
using System.Text;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using RestaurantOrder.Application.Notifications;
using RestaurantOrder.Infrastructure.Notifications;

namespace RestaurantOrder.UnitTests.Notifications;

internal sealed class FirebasePushTestHarness : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    public const string InstallationId = "cSyntheticInstallation";
    public List<string> Bodies { get; } = [];
    public Uri? RequestUri { get; private set; }
    public FirebasePushNotificationTransport Transport { get; }

    public FirebasePushTestHarness(Func<CancellationToken, Task<HttpResponseMessage>>? respond = null, GoogleCredential? credential = null)
    {
        respond ??= _ => Task.FromResult(Json(HttpStatusCode.OK, "{\"name\":\"projects/restaurant-order-test/messages/synthetic\"}"));
        Transport = new FirebasePushNotificationTransport(new AppOptions
        {
            ProjectId = "restaurant-order-test",
            Credential = credential ?? GoogleCredential.FromAccessToken("synthetic-test-credential"),
            HttpClientFactory = new TestClientFactory(async (request, ct) =>
            {
                RequestUri = request.RequestUri;
                Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
                return await respond(ct);
            })
        }, new FixedTimeProvider());
    }

    public static PushNotificationRequest Request(double lifetimeSeconds = 30) =>
        new(InstallationId, Guid.Parse("10000000-0000-0000-0000-000000000001"), Now.AddSeconds(lifetimeSeconds));

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Dispose() => Transport.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestClientFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : Google.Apis.Http.HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => new TestHandler(respond);
    }

    private sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }
}
