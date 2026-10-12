using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Application.Notifications;

namespace RestaurantOrder.Infrastructure.Notifications;

public static class PushServiceCollectionExtensions
{
    public static IServiceCollection AddOperationalPush(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["PUSH_PROVIDER"] ?? "Disabled";
        if (string.Equals(provider, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPushNotificationTransport, DisabledPushNotificationTransport>();
            return services;
        }

        if (!string.Equals(provider, "Fcm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PUSH_PROVIDER yalnızca Disabled veya Fcm olabilir.");
        }

        var projectId = configuration["FCM_PROJECT_ID"];
        if (string.IsNullOrWhiteSpace(projectId) || projectId.Length is < 6 or > 30 ||
            !char.IsAsciiLetterLower(projectId[0]) || projectId.EndsWith('-') ||
            projectId.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-'))
        {
            throw new InvalidOperationException("FCM_PROJECT_ID geçerli bir Firebase proje kimliği olmalıdır.");
        }

        // Kimlik bilgisi yalnızca taşıma ilk çözüldüğünde ADC üzerinden alınır.
        // Hiçbir JSON anahtarı veya alternatif kimlik sağlayıcısı uygulama ayarına gömülmez.
        services.AddSingleton<IPushNotificationTransport>(_ => new FirebasePushNotificationTransport(
            new AppOptions { ProjectId = projectId, Credential = GoogleCredential.GetApplicationDefault() },
            TimeProvider.System));
        return services;
    }
}
