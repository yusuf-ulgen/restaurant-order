using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Starter Health Endpoints
app.MapGet("/health/live", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow.ToString("O"),
    service = "restaurant-order-api",
    version = "0.1.0"
}))
.WithName("HealthLive")
.WithSummary("Liveness probe indicating process is alive")
.WithTags("Health");

app.MapGet("/health/ready", () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTime.UtcNow.ToString("O"),
    service = "restaurant-order-api",
    version = "0.1.0"
}))
.WithName("HealthReady")
.WithSummary("Readiness probe indicating service is ready to accept traffic")
.WithTags("Health");

app.Run();

namespace RestaurantOrder.Api
{
    public partial class Program { }
}
