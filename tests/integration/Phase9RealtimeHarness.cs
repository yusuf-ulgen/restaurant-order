using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Brands;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Worker.Safety;
using StackExchange.Redis;

namespace RestaurantOrder.IntegrationTests;

internal sealed class Phase9RealtimeHarness(TestcontainersFixture fixture) : IAsyncDisposable
{
    public WebApplicationFactory<RestaurantOrder.Api.Program> A { get; private set; } = null!;
    public WebApplicationFactory<RestaurantOrder.Api.Program> B { get; private set; } = null!;
    private IHost? _worker;
    public record BranchScope(TenantId Tenant, BranchId Branch, DiningAreaId Area);
    public record Staff(UserId User, Guid Session, string Token);
    public RestaurantOrderDbContext Admin() => new(new DbContextOptionsBuilder<RestaurantOrderDbContext>()
        .UseNpgsql(fixture.DatabaseConnectionString).Options);

    public async Task StartAsync()
    {
        var apiConnection = await fixture.ProvisionTemporaryRuntimeRoleAsync();
        A = Factory(apiConnection); B = Factory(apiConnection);
        _ = A.Server; _ = B.Server;
        using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint);
        await redis.GetDatabase().StringSetAsync(RedisWorkerActivationGuard.DefaultActiveSlotKey, "blue");
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["DATABASE_URL"] = await fixture.ProvisionTemporaryWorkerRoleAsync(), ["REDIS_URL"] = fixture.RedisEndpoint,
            ["DEPLOYMENT_COLOR"] = "blue", ["WORKER_DISPATCH_POLL_INTERVAL_MS"] = "50",
            ["PUSH_PROVIDER"] = "Disabled", ["Notification:Provider"] = "TestSink"
        });
        builder.Logging.ClearProviders();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<IWorkerActivationGuard, RedisWorkerActivationGuard>();
        builder.Services.AddSingleton<IWorkerLeaseManager, RedisWorkerLeaseManager>();
        builder.Services.AddHostedService<RestaurantOrder.Worker.Worker>();
        _worker = builder.Build();
        await _worker.StartAsync();
    }

    private WebApplicationFactory<RestaurantOrder.Api.Program> Factory(string connection) =>
        new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(b => {
            b.UseEnvironment("Development");
            b.UseSetting("DATABASE_URL", connection); b.UseSetting("REDIS_URL", fixture.RedisEndpoint);
            b.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            b.UseSetting("PIN_PEPPER_SECRET", RestaurantConfigTestHelpers.SharedPinPepper);
            b.UseSetting("Auth:PinHasher:PepperKeyId", RestaurantConfigTestHelpers.SharedTestPinPepperKeyId);
            b.UseSetting("Auth:PinHasher:PepperValue", RestaurantConfigTestHelpers.SharedPinPepper);
            b.UseSetting("QrSecurity:CurrentKeyId", "k1");
            b.UseSetting("QrSecurity:Keys:k1", RestaurantConfigTestHelpers.SharedTestQrKeyBase64);
            b.UseSetting("PUSH_PROVIDER", "Disabled");
            b.ConfigureLogging(l => l.ClearProviders());
        });

    public async Task RestartBAsync()
    {
        await B.DisposeAsync();
        B = Factory(await fixture.ProvisionTemporaryRuntimeRoleAsync());
        _ = B.Server;
    }

    public async Task<BranchScope> SeedBranchAsync(TenantId? existingTenant = null)
    {
        await using var db = Admin();
        var tenant = existingTenant ?? TenantId.New();
        if (!existingTenant.HasValue) db.Tenants.Add(Tenant.Create("Synthetic tenant", $"t-{Guid.NewGuid():N}"[..16], tenant));
        var brand = Brand.Create(tenant, "Synthetic brand", $"b-{Guid.NewGuid():N}"[..16]);
        var branch = Branch.Create(tenant, brand.Id, "Synthetic branch", $"br-{Guid.NewGuid():N}"[..16]);
        var area = DiningArea.Create(tenant, branch.Id, "Synthetic area", $"a-{Guid.NewGuid():N}"[..16], DiningAreaType.Indoor);
        db.AddRange(brand, branch, area);
        await db.SaveChangesAsync();
        return new(tenant, branch.Id, area.Id);
    }

    public async Task<Staff> SeedStaffAsync(BranchScope scope, AuthRole role = AuthRole.Waiter, UserId? existingUser = null)
    {
        await using var db = Admin();
        var now = DateTimeOffset.UtcNow;
        var user = existingUser ?? UserId.New();
        if (!existingUser.HasValue) db.Users.Add(User.Create($"synthetic-{Guid.NewGuid():N}@example.invalid", "synthetic-hash", now, user));
        var membership = UserMembership.Create(scope.Tenant, user, role, scope.Branch, now);
        var session = AuthSession.Create(scope.Tenant, user, membership.Id, scope.Branch, AuthenticationMethod.Password, TimeSpan.FromHours(1), now);
        db.AddRange(membership, session);
        await db.SaveChangesAsync();
        return new(user, session.Id, RestaurantConfigTestHelpers.GenerateToken(user.Value, session.Id, scope.Tenant.Value, scope.Branch.Value, role.ToString()));
    }

    public async Task<Guid> SeedCustomerAsync(BranchScope scope)
    {
        await using var db = Admin();
        var table = RestaurantTable.Create(scope.Tenant, scope.Branch, scope.Area, Guid.NewGuid().ToString("N")[..8], "Synthetic table", 4);
        var session = DiningSession.Open(scope.Tenant, scope.Branch, table, 2);
        session.Activate();
        db.AddRange(table, session);
        await db.SaveChangesAsync();
        return session.Id.Value;
    }

    public static HttpClient Client(WebApplicationFactory<RestaurantOrder.Api.Program> factory, string token)
    {
        var client = factory.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<Guid> MutateAsync(BranchScope scope, Guid? customer = null)
    {
        var session = customer ?? await SeedCustomerAsync(scope);
        using var client = Client(A, RestaurantConfigTestHelpers.GenerateCustomerToken(scope.Tenant.Value, scope.Branch.Value, session));
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var response = await client.PostAsJsonAsync("/api/v1/customer/service-requests", new { requestType = "Waiter" });
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP mutation failed: {response.StatusCode} {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<HubConnection> ConnectAsync(WebApplicationFactory<RestaurantOrder.Api.Program> factory, string token)
    {
        var hub = new HubConnectionBuilder().WithUrl(new Uri(factory.Server.BaseAddress, "hubs/events"), options => {
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
        }).Build();
        await hub.StartAsync();
        return hub;
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker != null) { await _worker.StopAsync(); _worker.Dispose(); }
        if (A != null) await A.DisposeAsync();
        if (B != null) await B.DisposeAsync();
    }
}
