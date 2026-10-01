using System.Text.Json;
using RestaurantOrder.Api;
using RestaurantOrder.Api.Domain.Pricing;
using RestaurantOrder.Api.Health;
using RestaurantOrder.Api.Tenancy;
using RestaurantOrder.Application.Tenancy;
using RestaurantOrder.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast configuration validation
ConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ITenantContextAccessor, AsyncLocalTenantContextAccessor>();

// Register tenant context resolver strategy
builder.Services.AddScoped<ITenantContextResolver>(sp =>
{
    var env = sp.GetRequiredService<IHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();
    if (env.IsDevelopment() && config.GetValue<bool>("Tenancy:AllowDevHeaderOverride", false))
    {
        return ActivatorUtilities.CreateInstance<DevelopmentHeaderTenantContextResolver>(sp);
    }
    return ActivatorUtilities.CreateInstance<DefaultTenantContextResolver>(sp);
});

builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>().TenantContext);

builder.Services.AddSingleton<RestaurantOrder.Api.Auth.IAuthCookieService, RestaurantOrder.Api.Auth.AuthCookieService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:3000", "http://localhost:3001", "http://localhost:3002"];
        policy.WithOrigins(allowedOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .Configure<RestaurantOrder.Infrastructure.Auth.JwtTokenService>((options, jwtService) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwtService.GetTokenValidationParameters();
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token))
                {
                    context.Token = context.Request.Cookies[RestaurantOrder.Api.Auth.AuthCookieService.AccessTokenCookieName];
                }
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
                if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("D");
                var problemJson = JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.com/401",
                    title = "Unauthorized",
                    status = 401,
                    detail = "Authentication required or invalid credentials.",
                    instance = context.Request.Path.Value,
                    correlationId
                });
                await context.Response.WriteAsync(problemJson);
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
                if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("D");
                var problemJson = JsonSerializer.Serialize(new
                {
                    type = "https://httpstatuses.com/403",
                    title = "Forbidden",
                    status = 403,
                    detail = "You do not have permission to perform this action.",
                    instance = context.Request.Path.Value,
                    correlationId
                });
                await context.Response.WriteAsync(problemJson);
            }
        };
    });

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, RestaurantOrder.Api.Auth.PermissionPolicyProvider>();
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IDatabaseHealthCheck, NpgsqlDatabaseHealthCheck>();
builder.Services.AddSingleton<IRedisConnectionProvider, StackExchangeRedisConnectionProvider>();
builder.Services.AddSingleton<IRedisHealthCheck, StackExchangeRedisHealthCheck>();
builder.Services.AddSingleton<IPricingService, PricingService>();
builder.Services.AddOpenApi();

var app = builder.Build();

var deploymentColor = builder.Configuration["DEPLOYMENT_COLOR"] ?? "unknown";

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    app.MapPost("/api/v1/dev/seed", async (
        RestaurantOrder.Infrastructure.Persistence.Seed.IDevDataSeeder seeder,
        CancellationToken ct) =>
    {
        var result = await seeder.SeedAsync(ct);
        return Results.Ok(result);
    })
    .WithName("DevSeed")
    .WithSummary("Idempotent synthetic seed for local development only")
    .WithTags("Development");
}

app.UseCors("DefaultCorsPolicy");
app.UseMiddleware<RestaurantOrder.Api.Auth.CsrfValidationMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();
app.UseMiddleware<RestaurantOrder.Api.Tenancy.TenantTransactionMiddleware>();

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

// Internal testing endpoint requiring authenticated tenant context (used for middleware verification)
app.MapGet("/api/v1/test/tenant-scope", (ITenantContext context) => Results.Ok(new
{
    TenantId = context.TenantId,
    BranchId = context.BranchId,
    IsAuthenticated = context.IsAuthenticated
}))
.WithMetadata(new RequireTenantAttribute())
.WithName("TestTenantScope")
.WithSummary("Test endpoint enforcing tenant presence verification");

RestaurantOrder.Api.Auth.AuthEndpoints.MapAuthEndpoints(app);
RestaurantOrder.Api.Auth.TerminalEndpoints.MapTerminalEndpoints(app);
RestaurantOrder.Api.Auth.PinAuthEndpoints.MapPinAuthEndpoints(app);
RestaurantOrder.Api.Auth.StaffEndpoints.MapStaffEndpoints(app);

app.Run();

namespace RestaurantOrder.Api
{
    public partial class Program
    {
        public static readonly Type ApplicationLayer = typeof(RestaurantOrder.Application.AssemblyReference);
    }
}
