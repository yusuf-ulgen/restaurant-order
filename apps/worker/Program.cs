using RestaurantOrder.Infrastructure;
using RestaurantOrder.Worker;
using RestaurantOrder.Worker.Safety;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration validation for worker host
WorkerConfigurationValidator.Validate(builder.Configuration, builder.Environment);

// Register infrastructure services (DbContext, Redis, Operational Outbox)
builder.Services.AddHttpContextAccessor();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Register distributed worker safety services
builder.Services.AddSingleton<IWorkerActivationGuard, RedisWorkerActivationGuard>();
builder.Services.AddSingleton<IWorkerLeaseManager, RedisWorkerLeaseManager>();
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IWorkerHealthIndicator, WorkerHealthIndicator>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

app.MapGet("/health/live", (IWorkerHealthIndicator indicator) => Results.Ok(new
{
    status = "Healthy",
    service = "restaurant-order-worker",
    slot = indicator.SlotColor,
    activeSlot = indicator.ActiveSlot,
    activationStatus = indicator.CurrentStatus.ToString(),
    timestamp = DateTime.UtcNow.ToString("O")
}));

app.Run();
