using RestaurantOrder.Infrastructure.Auth;
using Xunit;

namespace RestaurantOrder.UnitTests.Auth;

public class TerminalPinRateLimiterTests
{
    private readonly TerminalPinRateLimiter _limiter = new();

    [Fact]
    public async Task CheckAttemptAllowed_InitialCall_IsAllowedWithoutDelay()
    {
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var result = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);

        Assert.True(result.IsAllowed);
        Assert.False(result.IsLockedOut);
        Assert.Null(result.BackoffDelay);
    }

    [Fact]
    public async Task FailedAttempts_BelowThreshold_AllowsWithoutDelay()
    {
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _limiter.RecordFailedAttemptAsync(terminalId, userId);
        await _limiter.RecordFailedAttemptAsync(terminalId, userId);

        var result = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);

        Assert.True(result.IsAllowed);
        Assert.False(result.IsLockedOut);
        Assert.Null(result.BackoffDelay);
    }

    [Fact]
    public async Task FailedAttempts_ReachingBackoffThreshold_RequiresDelay()
    {
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _limiter.RecordFailedAttemptAsync(terminalId, userId);
        await _limiter.RecordFailedAttemptAsync(terminalId, userId);
        await _limiter.RecordFailedAttemptAsync(terminalId, userId); // 3rd failure

        var result = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);

        Assert.True(result.IsAllowed);
        Assert.False(result.IsLockedOut);
        Assert.NotNull(result.BackoffDelay);
        Assert.True(result.BackoffDelay.Value > TimeSpan.Zero);
    }

    [Fact]
    public async Task FailedAttempts_ReachingLockoutThreshold_LocksOut()
    {
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            await _limiter.RecordFailedAttemptAsync(terminalId, userId);
        }

        var result = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);

        Assert.False(result.IsAllowed);
        Assert.True(result.IsLockedOut);
        Assert.True(result.RetryAfterSeconds > 0);
    }

    [Fact]
    public async Task ResetAttempts_AfterFailures_RestoresAllowedState()
    {
        var terminalId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            await _limiter.RecordFailedAttemptAsync(terminalId, userId);
        }

        var lockedResult = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);
        Assert.True(lockedResult.IsLockedOut);

        await _limiter.ResetAttemptsAsync(terminalId, userId);

        var restoredResult = await _limiter.CheckAttemptAllowedAsync(terminalId, userId);
        Assert.True(restoredResult.IsAllowed);
        Assert.False(restoredResult.IsLockedOut);
    }
}
