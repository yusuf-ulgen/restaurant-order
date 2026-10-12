using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Direct SQL integration tests verifying PostgreSQL check constraints for:
/// 1. Table shape (1, 2, 3) and canvas boundary coordinates.
/// 2. Dining session lifecycle status timestamps and chronological ordering.
/// </summary>
public class FloorAndSessionIntegrityConstraintIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;

    public FloorAndSessionIntegrityConstraintIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
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

    private static string ValidPublicCode() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static async Task<(Guid tenantId, Guid branchId, Guid diningAreaId)> SeedAreaAsync(NpgsqlConnection conn)
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var diningAreaId = Guid.NewGuid();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.tenants (id, name, slug, status, created_at, concurrency_token)
            VALUES (@tId, 'Test Tenant', @tIdent, 'Active', clock_timestamp(), gen_random_uuid());
            INSERT INTO tenancy.brands (id, tenant_id, name, slug, status, created_at, concurrency_token)
            VALUES (@bndId, @tId, 'Test Brand', @bndIdent, 'Active', clock_timestamp(), gen_random_uuid());
            INSERT INTO tenancy.branches (id, tenant_id, brand_id, name, slug, timezone, currency, status, created_at, concurrency_token)
            VALUES (@brId, @tId, @bndId, 'Test Branch', @brIdent, 'Europe/Istanbul', 'TRY', 'Active', clock_timestamp(), gen_random_uuid());
            INSERT INTO tenancy.dining_areas (id, tenant_id, branch_id, name, code, area_type, sort_order, is_active, created_at, concurrency_token)
            VALUES (@daId, @tId, @brId, 'Salon', @daCode, 1, 1, true, clock_timestamp(), gen_random_uuid());";
        cmd.Parameters.AddWithValue("tId", tenantId);
        cmd.Parameters.AddWithValue("tIdent", $"t-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("bndId", brandId);
        cmd.Parameters.AddWithValue("bndIdent", $"bnd-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("brId", branchId);
        cmd.Parameters.AddWithValue("brIdent", $"br-{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("daId", diningAreaId);
        cmd.Parameters.AddWithValue("daCode", $"da-{Guid.NewGuid():N}");
        await cmd.ExecuteNonQueryAsync();

        return (tenantId, branchId, diningAreaId);
    }

    private static async Task<int> InsertTableSqlAsync(
        NpgsqlConnection conn, Guid id, Guid tid, Guid bid, Guid aid,
        string tnum, int x, int y, int w, int h, int shape, string? pcode = null)
    {
        pcode ??= ValidPublicCode();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.restaurant_tables (
                id, tenant_id, branch_id, dining_area_id, table_number, name,
                capacity, position_x, position_y, width, height, rotation_degrees,
                shape, is_active, qr_version, public_code, concurrency_token, created_at
            ) VALUES (
                @id, @tid, @bid, @aid, @tnum, 'Test Table',
                4, @x, @y, @w, @h, 0, @shp, true, 1, @pcode, @token, clock_timestamp()
            );";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tid", tid);
        cmd.Parameters.AddWithValue("bid", bid);
        cmd.Parameters.AddWithValue("aid", aid);
        cmd.Parameters.AddWithValue("tnum", tnum);
        cmd.Parameters.AddWithValue("x", x);
        cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("w", w);
        cmd.Parameters.AddWithValue("h", h);
        cmd.Parameters.AddWithValue("shp", shape);
        cmd.Parameters.AddWithValue("pcode", pcode);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> InsertSessionSqlAsync(
        NpgsqlConnection conn, Guid id, Guid tid, Guid bid, Guid tbid, int status,
        DateTimeOffset opened, DateTimeOffset? act, DateTimeOffset? bill, DateTimeOffset? closed,
        Guid? mergedInto = null)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenancy.dining_sessions (
                id, tenant_id, branch_id, table_id, status, guest_count,
                opened_at_utc, activated_at_utc, bill_requested_at_utc, closed_at_utc,
                close_reason, merged_into_session_id, concurrency_token, created_at_utc
            ) VALUES (
                @id, @tid, @bid, @tbid, @status, 2,
                @open, @act, @bill, @close, 'Closed', @merged, @token, @open
            );";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tid", tid);
        cmd.Parameters.AddWithValue("bid", bid);
        cmd.Parameters.AddWithValue("tbid", tbid);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("open", opened);
        cmd.Parameters.AddWithValue("act", (object?)act ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bill", (object?)bill ?? DBNull.Value);
        cmd.Parameters.AddWithValue("close", (object?)closed ?? DBNull.Value);
        cmd.Parameters.AddWithValue("merged", (object?)mergedInto ?? DBNull.Value);
        cmd.Parameters.AddWithValue("token", Guid.NewGuid());
        return await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Table_ShapeConstraint_RejectsInvalidShape_AcceptsValid()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var (tenantId, branchId, diningAreaId) = await SeedAreaAsync(conn);

        // 1. Invalid shape 0 must fail
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTableSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, diningAreaId, "SHP-0", 0, 0, 100, 100, 0));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Contains("ck_restaurant_tables_shape", ex.ConstraintName);

        // 2. Valid shape 1, 2, 3 must succeed
        foreach (var shp in new[] { 1, 2, 3 })
        {
            var rows = await InsertTableSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, diningAreaId, $"SHP-{shp}", 0, 0, 100, 100, shp);
            Assert.Equal(1, rows);
        }
    }

    [Fact]
    public async Task Table_CanvasBoundsConstraint_RejectsExceedingCoordinates()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var (tenantId, branchId, diningAreaId) = await SeedAreaAsync(conn);

        // 1. position_x + width = 9950 + 100 = 10050 > 10000 must fail
        var exX = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTableSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, diningAreaId, "BND-X", 9950, 0, 100, 100, 1));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exX.SqlState);
        Assert.Contains("ck_restaurant_tables_canvas_bounds", exX.ConstraintName);

        // 2. position_y + height = 9950 + 100 = 10050 > 10000 must fail
        var exY = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertTableSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, diningAreaId, "BND-Y", 0, 9950, 100, 100, 1));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exY.SqlState);
        Assert.Contains("ck_restaurant_tables_canvas_bounds", exY.ConstraintName);

        // 3. Exactly on border (9900 + 100 = 10000) must succeed
        var rows = await InsertTableSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, diningAreaId, "BND-OK", 9900, 9900, 100, 100, 1);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task DiningSession_LifecycleTimestampsConstraint_EnforcedByDatabase()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var (tenantId, branchId, diningAreaId) = await SeedAreaAsync(conn);

        var tableId = Guid.NewGuid();
        await InsertTableSqlAsync(conn, tableId, tenantId, branchId, diningAreaId, "SES-01", 0, 0, 100, 100, 1);

        var now = DateTimeOffset.UtcNow;

        // 1. Status 1 (Open) with non-null activated_at_utc must fail
        var ex1 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 1, now, now.AddMinutes(1), null, null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex1.SqlState);
        Assert.Contains("ck_dining_sessions_status_timestamps", ex1.ConstraintName);

        // 2. Status 2 (Active) with null activated_at_utc must fail
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 2, now, null, null, null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex2.SqlState);
        Assert.Contains("ck_dining_sessions_status_timestamps", ex2.ConstraintName);

        // 3. Status 3 (BillRequested) with null bill_requested_at_utc must fail
        var ex3 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 3, now, now.AddMinutes(1), null, null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex3.SqlState);
        Assert.Contains("ck_dining_sessions_status_timestamps", ex3.ConstraintName);

        // 4. Status 4 (Closed unmerged) with null closed_at_utc must fail status_timestamps constraint (while satisfying chronology)
        var ex4 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 4, now, now.AddMinutes(1), now.AddMinutes(2), null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex4.SqlState);
        Assert.Contains("ck_dining_sessions_status_timestamps", ex4.ConstraintName);

        // 5. Valid linear progression for Closed session must succeed
        var rows = await InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 4,
            now, now.AddMinutes(1), now.AddMinutes(2), now.AddMinutes(3));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task DiningSession_ChronologyConstraint_RejectsOutOfOrderTimestamps()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        var (tenantId, branchId, diningAreaId) = await SeedAreaAsync(conn);

        var tableId = Guid.NewGuid();
        await InsertTableSqlAsync(conn, tableId, tenantId, branchId, diningAreaId, "SES-02", 0, 0, 100, 100, 1);

        var now = DateTimeOffset.UtcNow;

        // 1. opened_at_utc > activated_at_utc must fail chronology constraint
        var ex1 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 2, now, now.AddMinutes(-5), null, null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex1.SqlState);
        Assert.Contains("ck_dining_sessions_chronology", ex1.ConstraintName);

        // 2. activated_at_utc > bill_requested_at_utc must fail chronology constraint
        var ex2 = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSessionSqlAsync(conn, Guid.NewGuid(), tenantId, branchId, tableId, 3, now, now.AddMinutes(5), now.AddMinutes(2), null));
        Assert.Equal(PostgresErrorCodes.CheckViolation, ex2.SqlState);
        Assert.Contains("ck_dining_sessions_chronology", ex2.ConstraintName);
    }
}
