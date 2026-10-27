using Npgsql;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class WorkerGatewayPrivilegeTests(TestcontainersFixture fixture) : IClassFixture<TestcontainersFixture>
{
    [Theory]
    [InlineData("claim_pending_operational_outbox")]
    [InlineData("claim_pending_notification_deliveries")]
    public async Task ApiRole_CannotClaimGlobalQueue(string function)
    {
        if (!TestcontainersGuard.ShouldRun(fixture)) return;
        await using var conn = new NpgsqlConnection(await fixture.ProvisionTemporaryRuntimeRoleAsync());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT * FROM tenancy.{function}(1, clock_timestamp(), 30, 'api-forbidden', NULL)", conn);
        var error = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
