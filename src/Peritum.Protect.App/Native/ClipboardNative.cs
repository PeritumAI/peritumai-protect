using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Peritum.Protect.App.Native;

public sealed class ClipboardNative : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private HwndSource? _source;

    public event Action? Updated;

    public void Start()
    {
        var parameters = new HwndSourceParameters("PeritumProtectClipboard")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(Hook);
        if (!AddClipboardFormatListener(_source.Handle))
            throw new InvalidOperationException("Could not listen for clipboard changes.");
    }

    public void Dispose()
    {
        if (_source is null)
            return;
        try
        {
            RemoveClipboardFormatListener(_source.Handle);
        }
        catch
        {
            // The window is going away with the process.
        }
        _source.RemoveHook(Hook);
        _source.Dispose();
        _source = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            Updated?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
