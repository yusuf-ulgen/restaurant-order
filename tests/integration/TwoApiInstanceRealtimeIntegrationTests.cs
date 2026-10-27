using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Genuine multi-instance integration tests running two distinct API hosts (API A and API B)
/// backed by the same real PostgreSQL and Redis instances.
/// Verifies end-to-end event propagation (A -> Outbox -> Redis -> B -> Client),
/// scope isolation, deduplication across replicas, session revocation, and p95 latency &lt; 500ms.
/// </summary>
public class TwoApiInstanceRealtimeIntegrationTests : IClassFixture<TestcontainersFixture>, IAsyncDisposable
{
    private readonly TestcontainersFixture _fixture;
    private WebApplicationFactory<RestaurantOrder.Api.Program>? _factoryA;
    private WebApplicationFactory<RestaurantOrder.Api.Program>? _factoryB;

    public TwoApiInstanceRealtimeIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factoryA != null) await _factoryA.DisposeAsync();
        if (_factoryB != null) await _factoryB.DisposeAsync();
    }

    private WebApplicationFactory<RestaurantOrder.Api.Program> CreateApiFactory(string instanceName)
    {
        return new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", _fixture.DatabaseConnectionString);
            builder.UseSetting("DATABASE_URL", _fixture.DatabaseConnectionString);
            builder.UseSetting("REDIS_URL", _fixture.RedisEndpoint);
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            builder.UseSetting("PIN_PEPPER_SECRET", RestaurantConfigTestHelpers.SharedPinPepper);
            builder.UseSetting("Auth:PinHasher:PepperKeyId", RestaurantConfigTestHelpers.SharedTestPinPepperKeyId);
            builder.UseSetting("Auth:PinHasher:PepperValue", RestaurantConfigTestHelpers.SharedPinPepper);
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("QrSecurity:CurrentKeyId", "k1");
            builder.UseSetting("QrSecurity:Keys:k1", RestaurantConfigTestHelpers.SharedTestQrKeyBase64);
            builder.UseSetting("InstanceName", instanceName);
            builder.ConfigureServices(services =>
            {
                var mockValidator = new Mock<ITokenRevocationValidator>();
                mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                services.AddScoped(_ => mockValidator.Object);
            });
        });
    }

    private static async Task<HubConnection> ConnectHubAsync(
        WebApplicationFactory<RestaurantOrder.Api.Program> factory,
        string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/events"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();
        return connection;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task TwoApiInstances_MutationOnInstanceA_DeliveredToClientOnInstanceB_WithScopeIsolation()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        _factoryA = CreateApiFactory("api-a");
        _factoryB = CreateApiFactory("api-b");

        var tenantId = Guid.NewGuid();
        var branchAId = Guid.NewGuid();
        var branchBId = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "TwoInstance Tenant", $"ti-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchAId, "Branch A");
        await SeedBranchAsync(tenantId, branchBId, "Branch B");

        var staffTokenA = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchAId, role: "Waiter");
        var staffTokenWrongBranch = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchBId, role: "Waiter");

        // Client on B listening to Branch A floor
        await using var hubClientB = await ConnectHubAsync(_factoryB, staffTokenA);
        // Client on B listening to Branch B (wrong branch)
        await using var hubClientWrongScope = await ConnectHubAsync(_factoryB, staffTokenWrongBranch);

        var receivedA = new ConcurrentBag<string>();
        var tcsA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hubClientB.On<string>("ReceiveEvent", msg =>
        {
            receivedA.Add(msg);
            tcsA.TrySetResult(msg);
        });

        var receivedWrongScope = new ConcurrentBag<string>();
        hubClientWrongScope.On<string>("ReceiveEvent", msg => receivedWrongScope.Add(msg));

        // Create an event on Instance A for Branch A
        var eventId = Guid.NewGuid();
        var outboxMessage = OperationalOutboxMessage.Create(
            tenantId: new TenantId(tenantId),
            branchId: new RestaurantOrder.Domain.Branches.BranchId(branchAId),
            aggregateId: $"table-{Guid.NewGuid():N}",
            eventType: RealtimeEventTypes.TableServiceRequested,
            payload: JsonSerializer.Serialize(new
            {
                service_request_id = Guid.NewGuid(),
                table_id = Guid.NewGuid(),
                request_type = "WaiterCall"
            }),
            schemaVersion: 1,
            idempotencyKey: $"sr-{eventId:N}",
            occurredAtUtc: DateTimeOffset.UtcNow,
            createdAtUtc: DateTimeOffset.UtcNow);

        // Inject event into PostgreSQL outbox via DbContext on Instance A
        using (var scopeA = _factoryA.Services.CreateScope())
        {
            var dbContextA = scopeA.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
            dbContextA.OperationalOutbox.Add(outboxMessage);
            await dbContextA.SaveChangesAsync();

            // Dispatch outbox batch from instance A dispatcher
            var dispatcherA = scopeA.ServiceProvider.GetRequiredService<IOperationalOutboxDispatcher>();
            var count = await dispatcherA.DispatchPendingBatchAsync(10, tenantId: tenantId);
            Assert.True(count > 0, "Operational outbox should have dispatched at least 1 message.");
        }

        // Verify Client on B receives the event within 5000ms
        var completed = await Task.WhenAny(tcsA.Task, Task.Delay(5000));
        Assert.Equal(tcsA.Task, completed);

        var payload = await tcsA.Task;
        using var doc = JsonDocument.Parse(payload);
        Assert.Equal(RealtimeEventTypes.TableServiceRequested, doc.RootElement.GetProperty("event_type").GetString());
        Assert.Equal(tenantId.ToString(), doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal(branchAId.ToString(), doc.RootElement.GetProperty("branch_id").GetString());

        // Wait a short window and assert wrong branch client never received anything
        await Task.Delay(500);
        Assert.Empty(receivedWrongScope);
    }

    [Fact]
    public async Task TwoApiInstances_Deduplication_OnlySingleBridgeMessageAcrossInstances()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        _factoryA = CreateApiFactory("api-a");
        _factoryB = CreateApiFactory("api-b");

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Dedup Tenant", $"dt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Dedup Branch");

        var staffToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        await using var hubClient = await ConnectHubAsync(_factoryB, staffToken);
        var received = new ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubClient.On<string>("ReceiveEvent", msg =>
        {
            received.Add(msg);
            tcs.TrySetResult(msg);
        });

        // Insert event
        var outboxMessage = OperationalOutboxMessage.Create(
            tenantId: new TenantId(tenantId),
            branchId: new RestaurantOrder.Domain.Branches.BranchId(branchId),
            aggregateId: $"order-{Guid.NewGuid():N}",
            eventType: RealtimeEventTypes.OrderCreated,
            payload: JsonSerializer.Serialize(new { order_id = Guid.NewGuid(), item_count = 1 }),
            schemaVersion: 1,
            idempotencyKey: $"dedup-{Guid.NewGuid():N}",
            occurredAtUtc: DateTimeOffset.UtcNow,
            createdAtUtc: DateTimeOffset.UtcNow);

        using (var scope = _factoryA.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
            dbContext.OperationalOutbox.Add(outboxMessage);
            await dbContext.SaveChangesAsync();

            var dispatcher = scope.ServiceProvider.GetRequiredService<IOperationalOutboxDispatcher>();
            await dispatcher.DispatchPendingBatchAsync(10, tenantId: tenantId);
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);

        // Wait to verify no second duplicate message arrives
        await Task.Delay(500);
        Assert.Single(received);
    }

    [Fact]
    public async Task TwoApiInstances_EndToEndLatency_MeetsUnder500msP95Budget()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        _factoryA = CreateApiFactory("api-a");
        _factoryB = CreateApiFactory("api-b");

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Perf Tenant", $"pt-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Perf Branch");

        var staffToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        await using var hubClientB = await ConnectHubAsync(_factoryB, staffToken);

        var latenciesMs = new List<double>();
        var sampleCount = 20;

        for (var i = 0; i < sampleCount; i++)
        {
            var targetEventId = Guid.Empty;
            var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sw = new Stopwatch();

            IDisposable? handler = null;
            handler = hubClientB.On<string>("ReceiveEvent", msg =>
            {
                using var doc = JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("event_id", out var evIdProp) &&
                    evIdProp.TryGetGuid(out var parsedId) &&
                    parsedId == targetEventId)
                {
                    sw.Stop();
                    tcs.TrySetResult(sw.ElapsedMilliseconds);
                }
            });

            var outboxMessage = OperationalOutboxMessage.Create(
                tenantId: new TenantId(tenantId),
                branchId: new RestaurantOrder.Domain.Branches.BranchId(branchId),
                aggregateId: $"bench-{i}",
                eventType: RealtimeEventTypes.OrderCreated,
                payload: JsonSerializer.Serialize(new { ping_index = i }),
                schemaVersion: 1,
                idempotencyKey: $"bench-{Guid.NewGuid():N}",
                occurredAtUtc: DateTimeOffset.UtcNow,
            createdAtUtc: DateTimeOffset.UtcNow);

            targetEventId = outboxMessage.Id;

            sw.Start();

            using (var scope = _factoryA.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
                dbContext.OperationalOutbox.Add(outboxMessage);
                await dbContext.SaveChangesAsync();

                var dispatcher = scope.ServiceProvider.GetRequiredService<IOperationalOutboxDispatcher>();
                await dispatcher.DispatchPendingBatchAsync(10, tenantId: tenantId);
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.Equal(tcs.Task, completed);

            var elapsed = await tcs.Task;
            latenciesMs.Add(elapsed);

            handler.Dispose();
        }

        latenciesMs.Sort();
        var p50 = latenciesMs[(int)(sampleCount * 0.50)];
        var p95 = latenciesMs[(int)(sampleCount * 0.95)];

        Assert.True(p95 < 500, $"p95 latency was {p95}ms, which exceeds the 500ms budget.");
        Assert.True(p50 < 200, $"p50 latency was {p50}ms.");
    }

    [Fact]
    public async Task TwoApiInstances_SessionRevocation_EvictsClientOnInstanceB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        _factoryA = CreateApiFactory("api-a");
        _factoryB = CreateApiFactory("api-b");

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Evict Tenant", $"et-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Evict Branch");
        await SeedActiveSessionAsync(tenantId, branchId, sessionId);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        await using var hubClientB = await ConnectHubAsync(_factoryB, customerToken);
        var tcsClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubClientB.On<object>("SessionClosed", _ => tcsClosed.TrySetResult(true));
        hubClientB.Closed += _ =>
        {
            tcsClosed.TrySetResult(true);
            return Task.CompletedTask;
        };

        // Publish session invalidation from Instance A publisher
        using (var scopeA = _factoryA.Services.CreateScope())
        {
            var publisherA = scopeA.ServiceProvider.GetRequiredService<IRealtimeEventPublisher>();
            await publisherA.PublishSessionInvalidatedAsync(tenantId, branchId, sessionId);
        }

        // Verify Client on Instance B receives eviction / termination within 5000ms
        var completed = await Task.WhenAny(tcsClosed.Task, Task.Delay(5000));
        Assert.Equal(tcsClosed.Task, completed);
    }

    [Fact]
    public async Task TwoApiInstances_InstanceRestart_RecoveryMaintainsEventDelivery()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        _factoryA = CreateApiFactory("api-a");
        _factoryB = CreateApiFactory("api-b");

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Restart Tenant", $"rst-{Guid.NewGuid():N}");
        await SeedBranchAsync(tenantId, branchId, "Restart Branch");

        var staffToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, branchId, role: "Waiter");

        await using var hubClientB = await ConnectHubAsync(_factoryB, staffToken);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        hubClientB.On<string>("ReceiveEvent", msg => tcs.TrySetResult(msg));

        // Instance A crashes / restarts
        await _factoryA.DisposeAsync();
        _factoryA = CreateApiFactory("api-a-restarted");

        var outboxMessage = OperationalOutboxMessage.Create(
            tenantId: new TenantId(tenantId),
            branchId: new RestaurantOrder.Domain.Branches.BranchId(branchId),
            aggregateId: "order-after-restart",
            eventType: RealtimeEventTypes.OrderCreated,
            payload: JsonSerializer.Serialize(new { order_id = Guid.NewGuid() }),
            schemaVersion: 1,
            idempotencyKey: $"restart-{Guid.NewGuid():N}",
            occurredAtUtc: DateTimeOffset.UtcNow,
            createdAtUtc: DateTimeOffset.UtcNow);

        using (var scopeA = _factoryA.Services.CreateScope())
        {
            var dbContextA = scopeA.ServiceProvider.GetRequiredService<RestaurantOrderDbContext>();
            dbContextA.OperationalOutbox.Add(outboxMessage);
            await dbContextA.SaveChangesAsync();

            var dispatcherA = scopeA.ServiceProvider.GetRequiredService<IOperationalOutboxDispatcher>();
            await dispatcherA.DispatchPendingBatchAsync(10, tenantId: tenantId);
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);
    }

    private async Task SeedBranchAsync(Guid tenantId, Guid branchId, string name)
    {
        var brandId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@brandId, @tenantId, 'TwoInstance Brand', @brandSlug, 'Active', NOW(), gen_random_uuid())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@branchId, @tenantId, @brandId, @name, @branchSlug, 'Europe/Istanbul', 'TRY', 'Active', NOW(), gen_random_uuid())
            ON CONFLICT (id) DO NOTHING;";

        cmd.Parameters.AddWithValue("brandId", brandId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("brandSlug", $"brnd-{Guid.NewGuid():N}"[..12]);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("branchSlug", $"brn-{Guid.NewGuid():N}"[..12]);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SeedActiveSessionAsync(Guid tenantId, Guid branchId, Guid sessionId)
    {
        var areaId = Guid.NewGuid();
        var tableId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@areaId, @tenantId, @branchId, 'Test Area', @areaCode, 1, 0, true, NOW(), gen_random_uuid())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO tenancy.restaurant_tables (id, tenant_id, branch_id, dining_area_id, table_number, name, public_code, capacity, position_x, position_y, width, height, rotation_degrees, shape, is_active, qr_version, concurrency_token, created_at)
            VALUES (@tableId, @tenantId, @branchId, @areaId, 'T1', 'Table 1', @publicCode, 4, 0, 0, 100, 100, 0, 1, true, 1, gen_random_uuid(), NOW())
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO tenancy.dining_sessions (id, tenant_id, branch_id, table_id, status, guest_count, opened_at_utc, activated_at_utc, concurrency_token, created_at_utc)
            VALUES (@sessionId, @tenantId, @branchId, @tableId, 2, 2, NOW(), NOW(), gen_random_uuid(), NOW())
            ON CONFLICT (id) DO NOTHING;";

        cmd.Parameters.AddWithValue("areaId", areaId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("branchId", branchId);
        cmd.Parameters.AddWithValue("areaCode", $"da-{Guid.NewGuid():N}"[..12]);
        cmd.Parameters.AddWithValue("tableId", tableId);
        cmd.Parameters.AddWithValue("publicCode", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("sessionId", sessionId);

        await cmd.ExecuteNonQueryAsync();
    }
}
