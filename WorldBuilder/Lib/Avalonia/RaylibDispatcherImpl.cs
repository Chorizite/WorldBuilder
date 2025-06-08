using Avalonia.Threading;
using WorldBuilder;

internal sealed class RaylibDispatcherImpl : IDispatcherImpl {
    private readonly Thread _mainThread;
    private readonly System.Threading.Timer _timer;
    private readonly SendOrPostCallback _invokeSignaled;
    private readonly SendOrPostCallback _invokeTimer;
    private static readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(50); // Minimum interval for timer updates

    public long Now => DateTime.Now.Ticks;

    public bool CurrentThreadIsLoopThread => _mainThread == Thread.CurrentThread;

    public event Action? Signaled;
    public event Action? Timer;

    public RaylibDispatcherImpl(Thread mainThread) {
        _mainThread = mainThread;
        _invokeSignaled = InvokeSignaled;
        _invokeTimer = InvokeTimer;
        _timer = new System.Threading.Timer(OnTimerTick, this, Timeout.Infinite, Timeout.Infinite);
    }

    public void UpdateTimer(long? dueTimeInMs) {
        var interval = dueTimeInMs is { } value
            ? Math.Max((value - Now) / TimeSpan.TicksPerMillisecond, _minInterval.TotalMilliseconds)
            : Timeout.Infinite;

        _timer.Change(TimeSpan.FromMilliseconds(interval), TimeSpan.FromMilliseconds(Timeout.Infinite));
    }

    private void OnTimerTick(object? state) {
        Program.Invoke(() => _invokeTimer(state));
    }

    public void Signal() {
        Program.Invoke(() => _invokeSignaled(this));
    }

    private void InvokeSignaled(object? state) => Signaled?.Invoke();
    private void InvokeTimer(object? state) => Timer?.Invoke();
}