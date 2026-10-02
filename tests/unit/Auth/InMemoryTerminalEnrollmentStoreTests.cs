using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class InMemoryTerminalEnrollmentStoreTests
{
    private readonly InMemoryTerminalEnrollmentStore _store = new();

    [Fact]
    public async Task StoreAndConsume_ValidTicket_SucceedsOnce()
    {
        var codeHash = "test_hash_123";
        var ticket = new TerminalEnrollmentTicket(
            CodeHash: codeHash,
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            TerminalName: "POS 1",
            DeviceIdentifier: "dev-01",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(15));

        await _store.StoreEnrollmentTicketAsync(codeHash, ticket, TimeSpan.FromMinutes(15));

        var consumed = await _store.ConsumeEnrollmentTicketAsync(codeHash);
        Assert.NotNull(consumed);
        Assert.Equal(ticket.TerminalName, consumed.TerminalName);

        // Single-use: second consume must be null
        var secondConsume = await _store.ConsumeEnrollmentTicketAsync(codeHash);
        Assert.Null(secondConsume);
    }

    [Fact]
    public async Task Consume_ExpiredTicket_ReturnsNull()
    {
        var codeHash = "expired_hash";
        var ticket = new TerminalEnrollmentTicket(
            CodeHash: codeHash,
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            TerminalName: "POS 2",
            DeviceIdentifier: "dev-02",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-20),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)); // Expired in past

        await _store.StoreEnrollmentTicketAsync(codeHash, ticket, TimeSpan.FromMinutes(15));

        var consumed = await _store.ConsumeEnrollmentTicketAsync(codeHash);
        Assert.Null(consumed);
    }

    [Fact]
    public async Task Consume_UnknownCode_ReturnsNull()
    {
        var consumed = await _store.ConsumeEnrollmentTicketAsync("nonexistent_hash");
        Assert.Null(consumed);
    }
}
