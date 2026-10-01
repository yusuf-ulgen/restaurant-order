using System.Collections.Concurrent;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// In-memory implementation of ITerminalEnrollmentStore with single-use consumption and automatic TTL pruning.
/// </summary>
public sealed class InMemoryTerminalEnrollmentStore : ITerminalEnrollmentStore
{
    private readonly ConcurrentDictionary<string, TerminalEnrollmentTicket> _tickets = new(StringComparer.Ordinal);

    public Task StoreEnrollmentTicketAsync(
        string codeHash,
        TerminalEnrollmentTicket ticket,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            throw new ArgumentException("Code hash cannot be empty.", nameof(codeHash));
        }

        ArgumentNullException.ThrowIfNull(ticket);

        // Prune expired tickets opportunistically
        PruneExpired();

        _tickets[codeHash] = ticket;
        return Task.CompletedTask;
    }

    public Task<TerminalEnrollmentTicket?> ConsumeEnrollmentTicketAsync(
        string codeHash,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return Task.FromResult<TerminalEnrollmentTicket?>(null);
        }

        if (_tickets.TryRemove(codeHash, out var ticket))
        {
            if (ticket.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return Task.FromResult<TerminalEnrollmentTicket?>(ticket);
            }
        }

        return Task.FromResult<TerminalEnrollmentTicket?>(null);
    }

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _tickets)
        {
            if (kvp.Value.ExpiresAtUtc <= now)
            {
                _tickets.TryRemove(kvp.Key, out _);
            }
        }
    }
}
