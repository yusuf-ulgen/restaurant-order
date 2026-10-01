using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Production webhook transport for identity notifications.
/// Delivers encrypted outbox payloads signed with HMAC-SHA256 to configured webhook endpoints.
/// Enforces HTTPS endpoints and strict secret management in Staging and Production.
/// </summary>
public sealed class WebhookIdentityNotificationTransport : IIdentityNotificationTransport
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly byte[]? _secretBytes;
    private readonly ILogger<WebhookIdentityNotificationTransport> _logger;

    public WebhookIdentityNotificationTransport(
        HttpClient httpClient,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<WebhookIdentityNotificationTransport> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var url = configuration["WEBHOOK_NOTIFICATION_URL"]
            ?? configuration["Notification:Webhook:Url"];

        var secret = configuration["WEBHOOK_NOTIFICATION_SECRET"]
            ?? configuration["Notification:Webhook:Secret"];

        var isProductionOrStaging = environment.IsProduction() || environment.IsEnvironment("Staging");

        if (string.IsNullOrWhiteSpace(url))
        {
            if (isProductionOrStaging)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_URL is required in '{environment.EnvironmentName}' environment.");
            }

            url = "http://localhost:5000/webhook/notifications";
        }

        if (isProductionOrStaging)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUri) || parsedUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_URL '{url}' must be a valid absolute HTTPS URL in '{environment.EnvironmentName}'.");
            }

            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            {
                throw new InvalidOperationException(
                    $"FATAL CONFIGURATION ERROR: WEBHOOK_NOTIFICATION_SECRET must be at least 32 characters long in '{environment.EnvironmentName}'.");
            }
        }

        _webhookUrl = url;
        _secretBytes = !string.IsNullOrWhiteSpace(secret) ? Encoding.UTF8.GetBytes(secret) : null;
    }

    public async Task SendAsync(
        string notificationType,
        string recipientEmail,
        string plaintextPayloadJson,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        object? parsedPayload;
        try
        {
            parsedPayload = JsonSerializer.Deserialize<JsonElement>(plaintextPayloadJson);
        }
        catch
        {
            parsedPayload = plaintextPayloadJson;
        }

        var envelope = new
        {
            notificationType,
            recipientEmail,
            payload = parsedPayload,
            idempotencyKey,
            timestamp = DateTimeOffset.UtcNow
        };

        var bodyJson = JsonSerializer.Serialize(envelope);
        var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, _webhookUrl)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("X-Idempotency-Key", idempotencyKey);

        if (_secretBytes != null && _secretBytes.Length > 0)
        {
            using var hmac = new HMACSHA256(_secretBytes);
            var hash = hmac.ComputeHash(bodyBytes);
            var signature = Convert.ToHexString(hash).ToLowerInvariant();
            request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        }

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Security: Never read or attach the remote response body into the exception or logs,
            // as downstream error sinks or databases could leak reflected tokens or payload data.
            throw new HttpRequestException(
                $"Webhook delivery failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Category: WebhookDeliveryFailed, IdempotencyKey: {idempotencyKey}");
        }

        _logger.LogInformation(
            "Webhook notification of type '{NotificationType}' successfully delivered to {RecipientEmail} (Status: {StatusCode}).",
            notificationType, MaskEmail(recipientEmail), (int)response.StatusCode);
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***" + (atIndex >= 0 ? email[atIndex..] : "");
        return $"{email[0]}***{email[(atIndex - 1)..]}";
    }
}
