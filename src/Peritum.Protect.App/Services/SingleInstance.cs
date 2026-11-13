namespace Peritum.Protect.App.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _show;
    private RegisteredWaitHandle? _wait;

    public SingleInstance()
    {
        _mutex = new Mutex(false, @"Local\Peritum.Protect.SingleInstance");
        try
        {
            IsFirst = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsFirst = true;
        }
        _show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Peritum.Protect.Show");
    }

    public bool IsFirst { get; }

    public void SignalShow() => _show.Set();

    public void Listen(Action onShow)
    {
        _wait = ThreadPool.RegisterWaitForSingleObject(_show, (_, _) => onShow(), null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _wait?.Unregister(null);
        _show.Dispose();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }

    private bool _disposed;
}
