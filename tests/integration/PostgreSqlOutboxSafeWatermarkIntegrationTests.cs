using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class PostgreSqlOutboxSafeWatermarkIntegrationTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealHttpSync_ReverseXidSequenceOrder_WaitsForEarlierSequence(bool commit)
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        await h.StartAsync();
        var scope = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(scope);
        await using var older = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await using var newer = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await older.OpenAsync(); await newer.OpenAsync();
        await using var olderTx = await older.BeginTransactionAsync();
        await using var xid = new NpgsqlCommand("SELECT pg_current_xact_id()", older, olderTx);
        await xid.ExecuteScalarAsync();
        await using var newerTx = await newer.BeginTransactionAsync();
        async Task<long> Insert(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            await using var cmd = new NpgsqlCommand(@"INSERT INTO tenancy.operational_outbox
                (id,tenant_id,branch_id,aggregate_id,event_type,payload,schema_version,idempotency_key,
                 created_at,occurred_at,status,attempt_count,max_attempts)
                VALUES (@id,@tenant,@branch,'aggregate','order.created','{}',1,@key,clock_timestamp(),clock_timestamp(),1,0,5)
                RETURNING sequence_number", conn, tx);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("tenant", scope.Tenant.Value);
            cmd.Parameters.AddWithValue("branch", scope.Branch.Value);
            cmd.Parameters.AddWithValue("key", Guid.NewGuid().ToString());
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        var low = await Insert(newer, newerTx);
        var high = await Insert(older, olderTx);
        Assert.True(high > low);
        await olderTx.CommitAsync();
        using var client = Phase9RealtimeHarness.Client(h.B, staff.Token);
        var scan = client.GetAsync($"/api/v1/events/sync?since_sequence={low - 1}&limit=100");
        await Task.Delay(150);
        Assert.False(scan.IsCompleted, "HTTP cursor must not pass an uncommitted lower sequence, even when the higher sequence has an older XID.");
        if (commit) await newerTx.CommitAsync(); else await newerTx.RollbackAsync();
        using var response = await scan.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var sequences = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("sequence_number").GetInt64()).ToArray();
        Assert.Equal(commit ? new[] {low, high} : new[] { high }, sequences);
        Assert.Equal(high, json.RootElement.GetProperty("latest_sequence_number").GetInt64());
    }
}
