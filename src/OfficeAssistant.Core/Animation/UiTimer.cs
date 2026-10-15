namespace OfficeAssistant.Core.Animation;

/// <summary>
/// One-shot timer that runs its callback on the thread that created it (through that
/// thread's SynchronizationContext). Restarting cancels the pending callback.
/// </summary>
public sealed class UiTimer : IDisposable
{
    private readonly SynchronizationContext _context;
    private readonly Timer _timer;
    private Action? _callback;
    private int _generation;

    public UiTimer()
    {
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("UiTimer must be created on a UI thread.");
        _timer = new Timer(OnElapsed);
    }

    public void Start(int milliseconds, Action callback)
    {
        _callback = callback;
        Interlocked.Increment(ref _generation);
        _timer.Change(Math.Max(0, milliseconds), Timeout.Infinite);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        _callback = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }

    private void OnElapsed(object? state)
    {
        var generation = Volatile.Read(ref _generation);
        _context.Post(
            _ =>
            {
                // Ignore ticks that were superseded by Start/Stop after they fired.
                if (generation == _generation && _callback is { } callback)
                {
                    _callback = null;
                    callback();
                }
            },
            null);
    }
}
