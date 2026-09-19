using System.Text.Json;
using RestaurantOrder.Api;
using RestaurantOrder.Api.Health;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration validation
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddSingleton<IDatabaseHealthCheck, NpgsqlDatabaseHealthCheck>();
builder.Services.AddSingleton<IRedisHealthCheck, StackExchangeRedisHealthCheck>();
builder.Services.AddOpenApi();

var app = builder.Build();

var deploymentColor = builder.Configuration["DEPLOYMENT_COLOR"] ?? "unknown";

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness probe indicating process is alive (never checks external dependencies)
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow.ToString("O"),
    service = "restaurant-order-api",
    version = "0.1.0",
    color = deploymentColor
}))
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

    var response = new
    {
        status = allHealthy ? "Healthy" : "Unhealthy",
        timestamp = DateTime.UtcNow.ToString("O"),
        service = "restaurant-order-api",
        version = "0.1.0",
        color = deploymentColor,
        checks = new
        {
            database = dbHealthy ? "Healthy" : "Unhealthy",
            redis = redisHealthy ? "Healthy" : "Unhealthy"
        }
    };

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
    public partial class Program { }
}
