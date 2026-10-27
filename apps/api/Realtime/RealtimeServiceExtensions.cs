using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace RestaurantOrder.Api.Realtime;

public static class RealtimeServiceExtensions
{
    public static IServiceCollection AddRealtimeEvents(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<IRealtimeConnectionTracker, RealtimeConnectionTracker>();
        services.AddSingleton<IRealtimeConnectionAuthorizer, RealtimeConnectionAuthorizer>();

        var signalR = services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = environment.IsDevelopment();
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        });

        var redisUrl = configuration["REDIS_URL"] ?? configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisUrl))
        {
            signalR.AddStackExchangeRedis(redisUrl, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("restaurant-order:signalr");
            });
        }

        services.AddHostedService<RedisEventSubscriberService>();

        return services;
    }

    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<EventsHub>("/hubs/events", options =>
        {
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                                 Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
        });

        app.MapEventSyncEndpoints();
        app.MapDeviceRegistrationEndpoints();

        return app;
    }
}
