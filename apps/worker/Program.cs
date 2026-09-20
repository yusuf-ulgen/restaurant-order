using RestaurantOrder.Worker;
using RestaurantOrder.Worker.Safety;

var builder = Host.CreateApplicationBuilder(args);

// Fail-fast configuration validation for worker host
WorkerConfigurationValidator.Validate(builder.Configuration, builder.Environment);

// Register distributed worker safety services
builder.Services.AddSingleton<IWorkerActivationGuard, RedisWorkerActivationGuard>();
builder.Services.AddSingleton<IWorkerLeaseManager, RedisWorkerLeaseManager>();
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
builder.Services.AddSingleton<IWorkerHealthIndicator, WorkerHealthIndicator>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
