using System.Text.Json;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Infrastructure.Redis;

namespace RestaurantOrder.Infrastructure.Auth;

/// <summary>
/// Distributed Redis store for terminal enrollment tickets.
/// Enforces single-use atomic consumption (GETDEL) and automatic TTL expiration.
/// </summary>
public sealed class RedisTerminalEnrollmentStore : ITerminalEnrollmentStore
{
    private readonly IRedisDatabaseProvider _redisProvider;
    private readonly IHostEnvironment _environment;

    public RedisTerminalEnrollmentStore(
        IRedisDatabaseProvider redisProvider,
        IHostEnvironment environment)
    {
        _redisProvider = redisProvider ?? throw new ArgumentNullException(nameof(redisProvider));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task StoreEnrollmentTicketAsync(
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

        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var key = AuthRedisKeyBuilder.TerminalEnrollment(_environment.EnvironmentName, codeHash);
        var json = JsonSerializer.Serialize(ticket);

        await db.StringSetAsync(key, json, ttl);
    }

    public async Task<TerminalEnrollmentTicket?> ConsumeEnrollmentTicketAsync(
        string codeHash,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return null;
        }

        var db = await _redisProvider.GetRequiredDatabaseAsync(ct);
        var key = AuthRedisKeyBuilder.TerminalEnrollment(_environment.EnvironmentName, codeHash);

        // Atomic GETDEL guarantees single-use across multiple instances
        var val = await db.StringGetDeleteAsync(key);
        if (val.IsNullOrEmpty)
        {
            return null;
        }

        try
        {
            var ticket = JsonSerializer.Deserialize<TerminalEnrollmentTicket>(val.ToString());
            if (ticket != null && ticket.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return ticket;
            }
        }
        catch
        {
            // Invalid JSON or corrupted payload returns null
            return null;
        }

        return null;
    }
}
