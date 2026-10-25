using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Events;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Proves the PostgreSQL safe commit watermark design under real concurrency.
/// Prevents the out-of-order sequence race where a fast higher-sequence transaction commits
/// while a slower lower-sequence transaction is still in-flight.
/// </summary>
public class PostgreSqlOutboxSafeWatermarkIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public PostgreSqlOutboxSafeWatermarkIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task SafeWatermark_WhenLowerSequenceTransactionIsInFlight_PreventsSkippingUncommittedEvent()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var tenantId = TenantId.New();
        var branchId = BranchId.New();

        // Seed tenant, brand and branch
        await using (var db = new RestaurantOrderDbContext(
            new DbContextOptionsBuilder<RestaurantOrderDbContext>().UseNpgsql(_fixture.DatabaseConnectionString).Options))
        {
            var tenant = Tenant.Create("Watermark Tenant", $"wt-{Guid.NewGuid():N}"[..10], tenantId);
            var brand = RestaurantOrder.Domain.Brands.Brand.Create(tenantId, "Watermark Brand", $"wb-{Guid.NewGuid():N}"[..10]);
            var branch = Branch.Create(tenantId, brand.Id, "Watermark Branch", $"wbr-{Guid.NewGuid():N}"[..10], id: branchId);
            db.Tenants.Add(tenant);
            db.Brands.Add(brand);
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
        }

        var connStr = _fixture.DatabaseConnectionString;

        await using var connA = new NpgsqlConnection(connStr);
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // Transaction A: inserts outbox row (obtains sequence N via DB sequence default), but does NOT commit yet
        long seqA;
        await using (var cmdA = connA.CreateCommand())
        {
            cmdA.Transaction = txA;
            cmdA.CommandText = @"
                INSERT INTO tenancy.operational_outbox
                (id, tenant_id, branch_id, aggregate_id, event_type, schema_version, payload, idempotency_key, created_at, occurred_at, status, attempt_count, max_attempts)
                VALUES
                (@p_eid, @p_tid, @p_bid, @p_aid, 'order.created', 1, '{}'::jsonb, @p_ikey, NOW(), NOW(), 0, 0, 5)
                RETURNING sequence_number;";

            cmdA.Parameters.AddWithValue("@p_eid", Guid.NewGuid());
            cmdA.Parameters.AddWithValue("@p_tid", tenantId.Value);
            cmdA.Parameters.AddWithValue("@p_bid", branchId.Value);
            cmdA.Parameters.AddWithValue("@p_aid", Guid.NewGuid().ToString());
            cmdA.Parameters.AddWithValue("@p_ikey", $"outbox-txA-{Guid.NewGuid():N}");

            seqA = (long)(await cmdA.ExecuteScalarAsync())!;
        }

        // Transaction B: inserts outbox row on a separate connection and commits immediately
        long seqB;
        await using (var connB = new NpgsqlConnection(connStr))
        {
            await connB.OpenAsync();
            await using var txB = await connB.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await using var cmdB = connB.CreateCommand();
            cmdB.Transaction = txB;
            cmdB.CommandText = @"
                INSERT INTO tenancy.operational_outbox
                (id, tenant_id, branch_id, aggregate_id, event_type, schema_version, payload, idempotency_key, created_at, occurred_at, status, attempt_count, max_attempts)
                VALUES
                (@p_eid, @p_tid, @p_bid, @p_aid, 'order.created', 1, '{}'::jsonb, @p_ikey, NOW(), NOW(), 0, 0, 5)
                RETURNING sequence_number;";

            cmdB.Parameters.AddWithValue("@p_eid", Guid.NewGuid());
            cmdB.Parameters.AddWithValue("@p_tid", tenantId.Value);
            cmdB.Parameters.AddWithValue("@p_bid", branchId.Value);
            cmdB.Parameters.AddWithValue("@p_aid", Guid.NewGuid().ToString());
            cmdB.Parameters.AddWithValue("@p_ikey", $"outbox-txB-{Guid.NewGuid():N}");

            seqB = (long)(await cmdB.ExecuteScalarAsync())!;
            await txB.CommitAsync();
        }

        // Verify that B has a higher sequence number than A
        Assert.True(seqB > seqA, $"Expected seqB ({seqB}) > seqA ({seqA})");

        // Now, on a third connection, run the watermark query while TxA is still in-flight
        await using (var connQuery = new NpgsqlConnection(connStr))
        {
            await connQuery.OpenAsync();
            await using var cmdQuery = connQuery.CreateCommand();
            cmdQuery.CommandText = @"
                SELECT COALESCE(
                    (SELECT MIN(sequence_number) - 1
                     FROM tenancy.operational_outbox
                     WHERE sequence_number > @p_since
                       AND tenant_id = @p_tenant
                       AND branch_id = @p_branch
                       AND (xmin::text)::bigint >= (pg_snapshot_xmin(pg_current_snapshot())::text)::bigint),
                    (SELECT MAX(sequence_number)
                     FROM (
                         SELECT sequence_number
                         FROM tenancy.operational_outbox
                         WHERE sequence_number > @p_since
                           AND tenant_id = @p_tenant
                           AND branch_id = @p_branch
                         ORDER BY sequence_number ASC
                         LIMIT @p_limit
                     ) sub)
                );";

            cmdQuery.Parameters.AddWithValue("@p_since", seqA - 1);
            cmdQuery.Parameters.AddWithValue("@p_tenant", tenantId.Value);
            cmdQuery.Parameters.AddWithValue("@p_branch", branchId.Value);
            cmdQuery.Parameters.AddWithValue("@p_limit", 50);

            var wmResult = await cmdQuery.ExecuteScalarAsync();
            var watermarkBeforeCommit = (long)Convert.ToInt64(wmResult ?? 0L);

            // Watermark MUST NOT advance past seqA while TxA is still in flight!
            // Candidate rows >= pg_snapshot_xmin cannot be returned as fully safe cursor.
            Assert.True(watermarkBeforeCommit < seqB,
                $"Safe watermark {watermarkBeforeCommit} advanced prematurely past in-flight transaction seqA ({seqA}) to seqB ({seqB}).");
        }

        // Now commit Transaction A
        await txA.CommitAsync();

        // Query again: now both TxA and TxB are fully committed with no preceding active transaction
        await using (var connQueryAfter = new NpgsqlConnection(connStr))
        {
            await connQueryAfter.OpenAsync();
            await using var cmdQueryAfter = connQueryAfter.CreateCommand();
            cmdQueryAfter.CommandText = @"
                SELECT COALESCE(
                    (SELECT MIN(sequence_number) - 1
                     FROM tenancy.operational_outbox
                     WHERE sequence_number > @p_since
                       AND tenant_id = @p_tenant
                       AND branch_id = @p_branch
                       AND (xmin::text)::bigint >= (pg_snapshot_xmin(pg_current_snapshot())::text)::bigint),
                    (SELECT MAX(sequence_number)
                     FROM (
                         SELECT sequence_number
                         FROM tenancy.operational_outbox
                         WHERE sequence_number > @p_since
                           AND tenant_id = @p_tenant
                           AND branch_id = @p_branch
                         ORDER BY sequence_number ASC
                         LIMIT @p_limit
                     ) sub)
                );";

            cmdQueryAfter.Parameters.AddWithValue("@p_since", seqA - 1);
            cmdQueryAfter.Parameters.AddWithValue("@p_tenant", tenantId.Value);
            cmdQueryAfter.Parameters.AddWithValue("@p_branch", branchId.Value);
            cmdQueryAfter.Parameters.AddWithValue("@p_limit", 50);

            var wmResultAfter = await cmdQueryAfter.ExecuteScalarAsync();
            var watermarkAfterCommit = (long)Convert.ToInt64(wmResultAfter ?? 0L);

            // Now the watermark can safely advance to seqB (both events captured in order)
            Assert.Equal(seqB, watermarkAfterCommit);
        }
    }
}
