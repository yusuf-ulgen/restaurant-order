using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RestaurantOrder.Domain.Auth;
using Xunit;
using Xunit.Abstractions;

namespace RestaurantOrder.IntegrationTests;

public class TwoApiInstanceRealtimeIntegrationTests(TestcontainersFixture fixture, ITestOutputHelper output)
    : IClassFixture<TestcontainersFixture>
{
    [Fact]
    public async Task HttpMutation_RealWorkerPolling_ReachesB_WithoutCrossTenantOrBranchLeakage()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var wrongBranch = await h.SeedBranchAsync(branch.Tenant);
        var otherTenant = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var wrongStaff = await h.SeedStaffAsync(wrongBranch);
        var otherStaff = await h.SeedStaffAsync(otherTenant);
        await using var hub = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        await using var wrongHub = await Phase9RealtimeHarness.ConnectAsync(h.B, wrongStaff.Token);
        await using var otherHub = await Phase9RealtimeHarness.ConnectAsync(h.A, otherStaff.Token);
        var received = new ConcurrentBag<string>();
        var forbidden = new ConcurrentBag<string>();
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string>("ReceiveEvent", value => { received.Add(value); ready.TrySetResult(value); });
        wrongHub.On<string>("ReceiveEvent", value => forbidden.Add(value));
        otherHub.On<string>("ReceiveEvent", value => forbidden.Add(value));
        var request = await h.MutateAsync(branch);
        var envelope = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var admin = h.Admin();
        var row = await admin.OperationalOutbox.IgnoreQueryFilters().SingleAsync(o => o.AggregateId == request.ToString());
        using var json = JsonDocument.Parse(envelope);
        Assert.Equal(row.Id, json.RootElement.GetProperty("event_id").GetGuid());
        await Task.Delay(200);
        Assert.Single(received);
        Assert.Empty(forbidden);
    }

    [Fact]
    public async Task RealWorkerLatency_HttpRequestStartToBReceive_ReportsTwentySamples()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        await using var hub = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        var samples = new List<double>();
        for (var i = 0; i < 21; i++)
        {
            var customer = await h.SeedCustomerAsync(branch);
            var start = new Stopwatch();
            var received = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = hub.On<string>("ReceiveEvent", _ => received.TrySetResult(start.Elapsed.TotalMilliseconds));
            start.Start();
            await h.MutateAsync(branch, customer);
            var elapsed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (i > 0) samples.Add(elapsed);
        }
        samples.Sort();
        var p50 = samples[9]; var p95 = samples[18];
        output.WriteLine($"HTTP request start -> API B ReceiveEvent; warm-up=1; n={samples.Count}; p50={p50:F2}ms; p95={p95:F2}ms; max={samples[^1]:F2}ms");
        Assert.True(p95 < 500, $"Actual worker p95={p95:F2}ms");
    }

    [Fact]
    public async Task StaffSuspensionEndpoint_ClosesBothInstances_PreservesOtherTenantSession()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var other = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var manager = await h.SeedStaffAsync(branch, AuthRole.BranchManager);
        var otherStaff = await h.SeedStaffAsync(other, existingUser: staff.User);
        await using var first = await Phase9RealtimeHarness.ConnectAsync(h.A, staff.Token);
        await using var second = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        await using var unaffected = await Phase9RealtimeHarness.ConnectAsync(h.B, otherStaff.Token);
        var closedA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forbidden = new ConcurrentBag<string>();
        first.Closed += _ => { closedA.TrySetResult(); return Task.CompletedTask; };
        second.Closed += _ => { closedB.TrySetResult(); return Task.CompletedTask; };
        second.On<string>("ReceiveEvent", value => forbidden.Add(value));
        using var client = Phase9RealtimeHarness.Client(h.A, manager.Token);
        using var response = await client.PostAsJsonAsync($"/api/v1/staff/{staff.User.Value}/status", new { status = "Suspended", branchId = branch.Branch.Value });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await Task.WhenAll(closedA.Task, closedB.Task).WaitAsync(TimeSpan.FromSeconds(10));
        await h.MutateAsync(branch);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        unaffected.On<string>("ReceiveEvent", _ => received.TrySetResult());
        await h.MutateAsync(other);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(forbidden);
    }

    [Fact]
    public async Task SessionRevokeEndpoint_AbortsTransportBeforeFurtherPrivateEvents()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        await using var hub = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        var received = new ConcurrentBag<string>();
        hub.On<string>("ReceiveEvent", value => received.Add(value));
        using var client = Phase9RealtimeHarness.Client(h.A, staff.Token);
        using var response = await client.DeleteAsync($"/api/v1/auth/sessions/{staff.Session}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.MutateAsync(branch);
        Assert.Empty(received);
    }

    [Fact]
    public async Task DisconnectedB_HttpCatchUpAfterRestart_RecoversWorkerDispatchedEvent()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var request = await h.MutateAsync(branch);
        await h.RestartBAsync();
        using var client = Phase9RealtimeHarness.Client(h.B, staff.Token);
        using var response = await client.GetAsync("/api/v1/events/sync?since_sequence=0&limit=100");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains(json.RootElement.GetProperty("events").EnumerateArray(), e => e.GetProperty("aggregate_id").GetString() == request.ToString());
        await using var hub = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string>("ReceiveEvent", _ => delivered.TrySetResult());
        await h.MutateAsync(branch);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task MissedInvalidation_BRevalidatesMembershipBeforeNextPrivateDelivery()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var manager = await h.SeedStaffAsync(branch, AuthRole.BranchManager);
        await using var hub = await Phase9RealtimeHarness.ConnectAsync(h.B, staff.Token);
        var provider = h.B.Services.GetRequiredService<RestaurantOrder.Infrastructure.Redis.IRedisDatabaseProvider>();
        var subscriber = await provider.GetRequiredSubscriberAsync();
        await subscriber.UnsubscribeAsync(StackExchange.Redis.RedisChannel.Literal("restaurant-order:events:auth-invalidated"));
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaked = new ConcurrentBag<string>();
        hub.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        hub.On<string>("ReceiveEvent", value => leaked.Add(value));
        using var client = Phase9RealtimeHarness.Client(h.A, manager.Token);
        using var response = await client.PostAsJsonAsync($"/api/v1/staff/{staff.User.Value}/status", new { status = "Disabled", branchId = branch.Branch.Value });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await h.MutateAsync(branch);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(leaked);
    }
}
