using System.Runtime.InteropServices;
using Peritum.Protect.App.Native;

namespace Peritum.Protect.App.Services;

/// <summary>
/// Fires when another process becomes the foreground window. Reused by later clipboard checks.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private IntPtr _hook;
    private WinEventDelegate? _callback;

    public event Action<string?>? Changed;

    public void Start()
    {
        if (_hook != IntPtr.Zero)
            return;
        _callback = OnEvent;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _callback,
            0,
            0,
            WinEventOutOfContext);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
        _callback = null;
    }

    private void OnEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != EventSystemForeground || hwnd == IntPtr.Zero)
            return;
        Changed?.Invoke(WindowNative.ProcessName(hwnd));
    }

    private const uint EventSystemForeground = 0x0003;
    private const uint WinEventOutOfContext = 0x0000;

    private delegate void WinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr module,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
