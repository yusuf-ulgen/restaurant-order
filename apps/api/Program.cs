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

var forwardedHeadersEnabled = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled")
    || string.Equals(builder.Configuration["FORWARDED_HEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);

if (forwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        options.RequireHeaderSymmetry = true;
        options.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit")
            ?? (int.TryParse(builder.Configuration["FORWARDED_HEADERS_FORWARD_LIMIT"], out var fl) ? fl : 2);

        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();

        var knownProxiesConfig = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>()
            ?? builder.Configuration["FORWARDED_HEADERS_KNOWN_PROXIES"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (knownProxiesConfig != null)
        {
            foreach (var proxy in knownProxiesConfig)
            {
                if (System.Net.IPAddress.TryParse(proxy.Trim(), out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }
        }

        var knownNetworksConfig = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>()
            ?? builder.Configuration["FORWARDED_HEADERS_KNOWN_NETWORKS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (knownNetworksConfig != null)
        {
            foreach (var net in knownNetworksConfig)
            {
                if (RestaurantOrder.Api.ConfigurationValidator.TryParseCidr(net.Trim(), out var ip, out var prefix))
                {
                    options.KnownIPNetworks.Add(new System.Net.IPNetwork(ip, prefix));
                }
            }
        }

        if (builder.Environment.IsDevelopment())
        {
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Loopback, 8));
            options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.IPv6Loopback, 128));
        }
    });
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        var rawOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? builder.Configuration["CORS_ALLOWED_ORIGINS"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string[] allowedOrigins;
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
        {
            allowedOrigins = (rawOrigins != null && rawOrigins.Length > 0)
                ? rawOrigins
                : ["http://localhost:3000", "http://localhost:3001", "http://localhost:3002"];
        }
        else
        {
            allowedOrigins = rawOrigins ?? [];
        }

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
            OnTokenValidated = async context =>
            {
                var validator = context.HttpContext.RequestServices.GetRequiredService<RestaurantOrder.Application.Auth.ITokenRevocationValidator>();

                var sidClaim = context.Principal?.FindFirst(RestaurantOrder.Application.Auth.JwtClaimNames.SessionId)?.Value
                    ?? context.Principal?.FindFirst("sid")?.Value;
                var subClaim = context.Principal?.FindFirst(RestaurantOrder.Application.Auth.JwtClaimNames.Subject)?.Value
                    ?? context.Principal?.FindFirst("sub")?.Value
                    ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var secVerClaim = context.Principal?.FindFirst(RestaurantOrder.Application.Auth.JwtClaimNames.SecurityVersion)?.Value
                    ?? context.Principal?.FindFirst("security_version")?.Value
                    ?? context.Principal?.FindFirst("sec_ver")?.Value;

                if (Guid.TryParse(sidClaim, out var sessionId) &&
                    Guid.TryParse(subClaim, out var userId) &&
                    int.TryParse(secVerClaim, out var securityVersion))
                {
                    try
                    {
                        var isValid = await validator.ValidateTokenActiveAsync(sessionId, userId, securityVersion, context.HttpContext.RequestAborted);
                        if (!isValid)
                        {
                            context.Fail("Token session has been revoked or security version has expired.");
                        }
                    }
                    catch (RestaurantOrder.Application.Auth.DistributedSecurityStateUnavailableException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        context.Fail($"Token validation failed closed: {ex.Message}");
                    }
                }
                else
                {
                    context.Fail("Token is missing required session or security claims.");
                }
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

if (forwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (RestaurantOrder.Application.Auth.DistributedSecurityStateUnavailableException ex)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.ContentType = "application/problem+json";
        var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
        if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("D");
        var problemJson = JsonSerializer.Serialize(new
        {
            type = "https://httpstatuses.com/503",
            title = "Security State Unavailable",
            status = StatusCodes.Status503ServiceUnavailable,
            detail = ex.Message,
            instance = context.Request.Path.Value,
            correlationId
        });
        await context.Response.WriteAsync(problemJson);
    }
    catch (Exception ex)
    {
        var correlationId = context.Response.Headers["X-Correlation-Id"].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = context.TraceIdentifier;
                if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("D");
            }
            context.Response.Headers["X-Correlation-Id"] = correlationId;
        }

        var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("RestaurantOrder.Api.Global");
        logger?.LogError(ex, "Unhandled server error. CorrelationId: {CorrelationId}", correlationId);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        var problemJson = JsonSerializer.Serialize(new
        {
            type = "https://httpstatuses.com/500",
            title = "Internal Server Error",
            status = StatusCodes.Status500InternalServerError,
            detail = "An unexpected error occurred while processing your request.",
            instance = context.Request.Path.Value,
            correlationId
        });
        await context.Response.WriteAsync(problemJson);
    }
});

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

// Internal testing endpoint requiring authenticated tenant context (only available in Development or Testing)
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapGet("/api/v1/test/tenant-scope", (ITenantContext context) => Results.Ok(new
    {
        TenantId = context.TenantId,
        BranchId = context.BranchId,
        IsAuthenticated = context.IsAuthenticated
    }))
    .WithMetadata(new RequireTenantAttribute())
    .WithName("TestTenantScope")
    .WithSummary("Test endpoint enforcing tenant presence verification");
}

RestaurantOrder.Api.Auth.AuthEndpoints.MapAuthEndpoints(app);
RestaurantOrder.Api.Auth.TerminalEndpoints.MapTerminalEndpoints(app);
RestaurantOrder.Api.Auth.PinAuthEndpoints.MapPinAuthEndpoints(app);
RestaurantOrder.Api.Auth.StaffEndpoints.MapStaffEndpoints(app);
RestaurantOrder.Api.RestaurantConfig.RestaurantConfigEndpoints.MapRestaurantConfigEndpoints(app);

app.Run();

namespace RestaurantOrder.Api
{
    public partial class Program
    {
        public static readonly Type ApplicationLayer = typeof(RestaurantOrder.Application.AssemblyReference);
    }
}
