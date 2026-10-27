using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Notifications;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Worker.Safety;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class WorkerProcessCrashRecoveryTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Fact]
    public async Task KilledWorker_AfterStagingBeforeCommit_RecoversOneDeliveryWithNewProcess()
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var h = new Phase9RealtimeHarness(fixture);
        var branch = await h.SeedBranchAsync();
        var staff = await h.SeedStaffAsync(branch);
        var now = DateTimeOffset.UtcNow;
        var device = DeviceRegistration.CreateForStaff(branch.Tenant, branch.Branch, staff.User, "synthetic_process_crash", "web", now, TimeSpan.FromDays(1));
        var message = OperationalOutboxMessage.Create(branch.Tenant, branch.Branch, "order", "order.created", "{}", Guid.NewGuid().ToString(), now, now);
        await using var admin = h.Admin();
        admin.AddRange(device, message); await admin.SaveChangesAsync();
        var workerConnection = await fixture.ProvisionTemporaryWorkerRoleAsync();
        var workerUser = new NpgsqlConnectionStringBuilder(workerConnection).Username;
        const long gate = 794291027;
        await using var blocker = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await blocker.OpenAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_lock({gate})", blocker)) await command.ExecuteNonQueryAsync();
        // Yalnızca bu geçici test veritabanında: tamamlanma UPDATE'ini staging
        // yazısından sonra durdur. Üretim koduna test hook'u eklenmez.
        await admin.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION tenancy.test_crash_commit_gate() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.status = 3 THEN
                    PERFORM pg_advisory_xact_lock(794291027);
                END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER test_crash_commit_gate BEFORE UPDATE ON tenancy.operational_outbox
                FOR EACH ROW EXECUTE FUNCTION tenancy.test_crash_commit_gate();
            """);
        using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisEndpoint);
        await redis.GetDatabase().StringSetAsync(RedisWorkerActivationGuard.DefaultActiveSlotKey, "blue");
        Process? first = null; Process? second = null;
        try
        {
            first = StartWorker(workerConnection, fixture.RedisEndpoint);
            await EventuallyAsync(async () => {
                await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
                await connection.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE l.locktype='advisory' AND NOT l.granted AND a.usename=@worker)", connection);
                query.Parameters.AddWithValue("worker", workerUser!);
                return await query.ExecuteScalarAsync() is true;
            });
            Assert.Empty(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).AsNoTracking().ToListAsync());
            first.Kill(entireProcessTree: true);
            await first.WaitForExitAsync();
            // PostgreSQL kilit beklerken soketi okumaz. Süreç öldükten sonra
            // kapıyı aç; sunucu EOF'u görüp açık transaction'ı geri alabilsin.
            await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({gate})", blocker)) await release.ExecuteNonQueryAsync();
            await EventuallyAsync(async () => !await admin.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND NOT granted) AS \"Value\"").SingleAsync());
            Assert.Empty(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).AsNoTracking().ToListAsync());
            Assert.Equal(OperationalOutboxStatus.Processing, (await admin.OperationalOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == message.Id)).Status);
            await admin.Database.ExecuteSqlRawAsync("DROP TRIGGER test_crash_commit_gate ON tenancy.operational_outbox; DROP FUNCTION tenancy.test_crash_commit_gate();");
            await admin.Database.ExecuteSqlInterpolatedAsync($"UPDATE tenancy.operational_outbox SET lock_expires_utc=clock_timestamp()-interval '1 second' WHERE id={message.Id}");
            await redis.GetDatabase().KeyDeleteAsync("restaurant-order:lease:worker-leadership");
            second = StartWorker(workerConnection, fixture.RedisEndpoint);
            await EventuallyAsync(async () => await admin.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(d => d.EventId == message.Id && d.Status == NotificationDeliveryStatus.ProviderAccepted));
            Assert.Single(await admin.NotificationDeliveries.IgnoreQueryFilters().Where(d => d.EventId == message.Id).AsNoTracking().ToListAsync());
            Assert.Equal(OperationalOutboxStatus.Dispatched, (await admin.OperationalOutbox.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == message.Id)).Status);
        }
        finally
        {
            foreach (var process in new[] { first, second })
                if (process != null) { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } process.Dispose(); }
        }
    }

    private static Process StartWorker(string database, string redis)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "RestaurantOrder.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var assembly = Path.Combine(root.FullName, "apps", "worker", "bin", "Release", "net10.0", "RestaurantOrder.Worker.dll");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly);
        start.Environment["WORKER_DATABASE_URL"] = database;
        start.Environment["REDIS_URL"] = redis;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        start.Environment["DEPLOYMENT_COLOR"] = "blue";
        start.Environment["ACTIVE_DEPLOYMENT_SLOT"] = "blue";
        start.Environment["PUSH_PROVIDER"] = "Disabled";
        start.Environment["WORKER_DISPATCH_POLL_INTERVAL_MS"] = "50";
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        var process = Process.Start(start)!;
        // Pipe tamponları child sürecini bloke etmesin; kimlik bilgileri yazdırılmaz.
        process.OutputDataReceived += (_, _) => { }; process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        return process;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> check)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline) { if (await check()) return; await Task.Delay(50); }
        Assert.Fail("Worker process crash/recovery did not reach the expected database boundary.");
    }
}
