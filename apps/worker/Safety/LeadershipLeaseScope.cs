namespace RestaurantOrder.Worker.Safety;

public sealed class LeadershipLeaseScope : IAsyncDisposable
{
    private readonly IWorkerActivationGuard _guard;
    private readonly IWorkerLeaseManager _leases;
    private readonly string _key;
    private readonly TimeSpan _duration;
    private readonly CancellationTokenSource _lifetime;
    private readonly SemaphoreSlim _renewLock = new(1, 1);
    private readonly Task _monitor;
    public CancellationToken Token => _lifetime.Token;

    public LeadershipLeaseScope(IWorkerActivationGuard guard, IWorkerLeaseManager leases, string key,
        TimeSpan duration, TimeSpan interval, CancellationToken ct)
    {
        _guard = guard; _leases = leases; _key = key; _duration = duration;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _monitor = MonitorAsync(TimeSpan.FromMilliseconds(Math.Max(10, Math.Min(interval.TotalMilliseconds, duration.TotalMilliseconds / 3))));
    }

    public async Task<bool> ValidateAsync(CancellationToken ct)
    {
        if (_lifetime.IsCancellationRequested) return false;
        await _renewLock.WaitAsync(ct);
        try
        {
            if (await _guard.IsActiveSlotAsync(ct) && await _leases.RenewLeaseAsync(_key, _duration, ct)) return true;
            await _lifetime.CancelAsync();
            return false;
        }
        catch
        {
            await _lifetime.CancelAsync();
            return false;
        }
        finally { _renewLock.Release(); }
    }

    private async Task MonitorAsync(TimeSpan interval)
    {
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                await Task.Delay(interval, _lifetime.Token);
                if (!await ValidateAsync(_lifetime.Token)) break;
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        await _monitor;
        _lifetime.Dispose();
        _renewLock.Dispose();
    }
}
