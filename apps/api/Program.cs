using System.Text.Json;
using RestaurantOrder.Api;
using RestaurantOrder.Api.Domain.Pricing;
using RestaurantOrder.Api.Health;
using RestaurantOrder.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration validation
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IDatabaseHealthCheck, NpgsqlDatabaseHealthCheck>();
builder.Services.AddSingleton<IRedisConnectionProvider, StackExchangeRedisConnectionProvider>();
builder.Services.AddSingleton<IRedisHealthCheck, StackExchangeRedisHealthCheck>();
builder.Services.AddSingleton<IPricingService, PricingService>();
builder.Services.AddOpenApi();

var app = builder.Build();

var deploymentColor = builder.Configuration["DEPLOYMENT_COLOR"] ?? "unknown";
var color = deploymentColor;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness probe indicating process is alive (never checks external dependencies)
app.MapGet("/health/live", () => Results.Ok(new HealthLiveResponse(
    Status: "Healthy",
    Timestamp: DateTime.UtcNow.ToString("O"),
    Service: "restaurant-order-api",
    Version: "0.1.0",
    Color: deploymentColor
)))
.WithName("HealthLive")
.WithSummary("Liveness probe indicating process is alive")
.WithTags("Health");

// Readiness probe verifying real PostgreSQL and Redis accessibility
app.MapGet("/health/ready", async (
    IDatabaseHealthCheck dbCheck,
    IRedisHealthCheck redisCheck,
    CancellationToken ct) =>
{
    var dbTask = dbCheck.IsHealthyAsync(ct);
    var redisTask = redisCheck.IsHealthyAsync(ct);

    await Task.WhenAll(dbTask, redisTask);

    var dbHealthy = await dbTask;
    var redisHealthy = await redisTask;
    var allHealthy = dbHealthy && redisHealthy;

    var response = new HealthReadyResponse(
        Status: allHealthy ? "Healthy" : "Unhealthy",
        Timestamp: DateTime.UtcNow.ToString("O"),
        Service: "restaurant-order-api",
        Version: "0.1.0",
        Color: deploymentColor,
        Checks: new HealthChecks(
            Database: dbHealthy ? "Healthy" : "Unhealthy",
            Redis: redisHealthy ? "Healthy" : "Unhealthy"
        )
    );

    return allHealthy
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("HealthReady")
.WithSummary("Readiness probe verifying database and redis accessibility")
.WithTags("Health");

app.Run();

namespace RestaurantOrder.Api
{
    public partial class Program
    {
        public static readonly Type ApplicationLayer = typeof(RestaurantOrder.Application.AssemblyReference);
    }
}
