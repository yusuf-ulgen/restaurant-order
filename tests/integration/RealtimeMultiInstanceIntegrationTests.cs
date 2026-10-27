using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantOrder.Application.Events;
using RestaurantOrder.Infrastructure.Events;
using RestaurantOrder.Infrastructure.Redis;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Verifies low-level Redis pub/sub transport layer communication, publisher channels,
/// and session/user eviction payload formats.
/// End-to-end multi-host delivery and p95 latency are verified by TwoApiInstanceRealtimeIntegrationTests.
/// </summary>
public class RealtimeMultiInstanceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public RealtimeMultiInstanceIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class TestRedisProvider : IRedisDatabaseProvider
    {
        private readonly ConnectionMultiplexer _multiplexer;

        public TestRedisProvider(ConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
        }

        public Task<IDatabase?> GetDatabaseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDatabase?>(_multiplexer.GetDatabase());

        public Task<IDatabase> GetRequiredDatabaseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_multiplexer.GetDatabase());

        public Task<ISubscriber?> GetSubscriberAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ISubscriber?>(_multiplexer.GetSubscriber());

        public Task<ISubscriber> GetRequiredSubscriberAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_multiplexer.GetSubscriber());
    }

    [Fact]
    public async Task MultiInstance_EventPublishedOnInstanceA_ReceivedOnInstanceB()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redisA = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisB = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);

        var providerA = new TestRedisProvider(redisA);
        var publisherA = new RedisRealtimeEventPublisher(providerA, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var subscriberB = redisB.GetSubscriber();
        var receivedMessages = new ConcurrentBag<string>();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await subscriberB.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.BroadcastChannel),
            (channel, msg) =>
            {
                var str = msg.ToString();
                receivedMessages.Add(str);
                tcs.TrySetResult(str);
            });

        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var envelope = new
        {
            event_id = eventId,
            event_type = RealtimeEventTypes.OrderCreated,
            schema_version = 1,
            occurred_at_utc = DateTimeOffset.UtcNow,
            tenant_id = tenantId,
            branch_id = branchId,
            aggregate_id = Guid.NewGuid().ToString(),
            sequence_number = 1001L,
            data = new { order_id = Guid.NewGuid(), item_count = 2 }
        };

        var json = JsonSerializer.Serialize(envelope);

        // Instance A publishes
        await publisherA.PublishBroadcastAsync(json);

        // Instance B receives within 5000ms
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);

        var receivedJson = await tcs.Task;
        using var doc = JsonDocument.Parse(receivedJson);
        Assert.Equal(eventId.ToString(), doc.RootElement.GetProperty("event_id").GetString());
        Assert.Equal(RealtimeEventTypes.OrderCreated, doc.RootElement.GetProperty("event_type").GetString());
    }

    [Fact]
    public async Task LatencyBenchmark_CrossInstancePropagation_MeetsUnder500msP95Budget()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redisA = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisB = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);

        var providerA = new TestRedisProvider(redisA);
        var publisherA = new RedisRealtimeEventPublisher(providerA, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var subscriberB = redisB.GetSubscriber();
        var latenciesMs = new List<double>();
        var sampleCount = 50;
        var testChannel = $"test-benchmark:{Guid.NewGuid():N}";

        for (var i = 0; i < sampleCount; i++)
        {
            var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sw = Stopwatch.StartNew();

            await subscriberB.SubscribeAsync(RedisChannel.Literal(testChannel), (ch, msg) =>
            {
                sw.Stop();
                tcs.TrySetResult(sw.ElapsedMilliseconds);
            });

            await publisherA.PublishBroadcastAsync(JsonSerializer.Serialize(new { ping = i }));
            var subscriberChannel = redisA.GetSubscriber();
            await subscriberChannel.PublishAsync(RedisChannel.Literal(testChannel), "ping");

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
            Assert.Equal(tcs.Task, completed);

            var elapsed = await tcs.Task;
            latenciesMs.Add(elapsed);

            await subscriberB.UnsubscribeAsync(RedisChannel.Literal(testChannel));
        }

        latenciesMs.Sort();
        var p50 = latenciesMs[(int)(sampleCount * 0.50)];
        var p95 = latenciesMs[(int)(sampleCount * 0.95)];

        // Assert strictly against roadmap < 500ms requirement
        Assert.True(p95 < 500, $"p95 latency was {p95}ms, which exceeds the 500ms budget.");
        Assert.True(p50 < 100, $"p50 latency was {p50}ms.");
    }

    [Fact]
    public async Task MultiInstance_SessionEviction_BroadcastsAcrossInstances()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redisA = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisB = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);

        var providerA = new TestRedisProvider(redisA);
        var publisherA = new RedisRealtimeEventPublisher(providerA, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var subscriberB = redisB.GetSubscriber();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await subscriberB.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.SessionEvictionChannel),
            (ch, msg) => tcs.TrySetResult(msg.ToString()));

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await publisherA.PublishSessionInvalidatedAsync(tenantId, branchId, sessionId);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);

        var evictionPayload = await tcs.Task;
        using var doc = JsonDocument.Parse(evictionPayload);
        Assert.Equal(tenantId.ToString(), doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal(sessionId.ToString(), doc.RootElement.GetProperty("session_id").GetString());
    }

    [Fact]
    public async Task MultiInstance_UserEviction_BroadcastsAcrossInstances()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var redisA = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisB = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);

        var providerA = new TestRedisProvider(redisA);
        var publisherA = new RedisRealtimeEventPublisher(providerA, NullLogger<RedisRealtimeEventPublisher>.Instance);

        var subscriberB = redisB.GetSubscriber();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await subscriberB.SubscribeAsync(
            RedisChannel.Literal(RedisRealtimeEventPublisher.UserEvictionChannel),
            (ch, msg) => tcs.TrySetResult(msg.ToString()));

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await publisherA.PublishUserInvalidatedAsync(tenantId, userId, sessionId, "revoked");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
        Assert.Equal(tcs.Task, completed);

        var evictionPayload = await tcs.Task;
        using var doc = JsonDocument.Parse(evictionPayload);
        Assert.Equal(tenantId.ToString(), doc.RootElement.GetProperty("tenant_id").GetString());
        Assert.Equal(userId.ToString(), doc.RootElement.GetProperty("user_id").GetString());
        Assert.Equal(sessionId.ToString(), doc.RootElement.GetProperty("session_id").GetString());
        Assert.Equal("revoked", doc.RootElement.GetProperty("reason").GetString());
    }
}
