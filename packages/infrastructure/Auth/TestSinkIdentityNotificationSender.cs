using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RestaurantOrder.Application.Auth;

namespace RestaurantOrder.Infrastructure.Auth;

public sealed record SentInvitationNotification(
    string Email,
    string InvitationToken,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset SentAtUtc);

public sealed record SentPasswordResetNotification(
    string Email,
    string ResetToken,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset SentAtUtc);

/// <summary>
/// Explicit test and development sink for identity notifications.
/// Captures sent tokens in-memory for automated verification.
/// Strictly prevented from being used as a production provider.
/// </summary>
public sealed class TestSinkIdentityNotificationSender : IIdentityNotificationSender
{
    private readonly ILogger<TestSinkIdentityNotificationSender> _logger;
    private readonly ConcurrentBag<SentInvitationNotification> _invitations = new();
    private readonly ConcurrentBag<SentPasswordResetNotification> _passwordResets = new();

    public TestSinkIdentityNotificationSender(ILogger<TestSinkIdentityNotificationSender> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyCollection<SentInvitationNotification> Invitations => _invitations.ToArray();
    public IReadOnlyCollection<SentPasswordResetNotification> PasswordResets => _passwordResets.ToArray();

    public string? LastInvitationToken => _invitations.OrderByDescending(i => i.SentAtUtc).FirstOrDefault()?.InvitationToken;
    public string? LastResetToken => _passwordResets.OrderByDescending(r => r.SentAtUtc).FirstOrDefault()?.ResetToken;

    public Task SendInvitationAsync(
        string email,
        string invitationToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default)
    {
        var record = new SentInvitationNotification(
            Email: email,
            InvitationToken: invitationToken,
            ExpiresAtUtc: expiresAtUtc,
            SentAtUtc: DateTimeOffset.UtcNow);

        _invitations.Add(record);
        _logger.LogInformation("[TEST SINK] Staff invitation dispatched to '{Email}' (expires at {ExpiresAtUtc}). Raw token captured in test sink.",
            email, expiresAtUtc);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string email,
        string resetToken,
        DateTimeOffset expiresAtUtc,
        CancellationToken ct = default)
    {
        var record = new SentPasswordResetNotification(
            Email: email,
            ResetToken: resetToken,
            ExpiresAtUtc: expiresAtUtc,
            SentAtUtc: DateTimeOffset.UtcNow);

        _passwordResets.Add(record);
        _logger.LogInformation("[TEST SINK] Password reset dispatched to '{Email}' (expires at {ExpiresAtUtc}). Raw token captured in test sink.",
            email, expiresAtUtc);

        return Task.CompletedTask;
    }

    public void Clear()
    {
        _invitations.Clear();
        _passwordResets.Clear();
    }
}
