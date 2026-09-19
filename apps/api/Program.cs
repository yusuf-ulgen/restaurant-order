using System.Text.Json;
using RestaurantOrder.Api;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration validation
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddOpenApi();

var app = builder.Build();

var deploymentColor = builder.Configuration["DEPLOYMENT_COLOR"] ?? "unknown";

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Starter Health Endpoints with deployment color visibility
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

app.MapGet("/health/ready", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow.ToString("O"),
    service = "restaurant-order-api",
    version = "0.1.0",
    color = deploymentColor
}))
.WithName("HealthReady")
.WithSummary("Readiness probe indicating service is ready to accept traffic")
.WithTags("Health");

app.Run();

namespace RestaurantOrder.Api
{
    public partial class Program { }
}
