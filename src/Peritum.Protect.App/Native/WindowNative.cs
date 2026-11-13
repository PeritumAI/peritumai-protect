using System.Runtime.InteropServices;
using System.Text;

namespace Peritum.Protect.App.Native;

public static class WindowNative
{
    public static string? ForegroundProcessName() => ProcessName(GetForegroundWindow());

    public static string? ProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    public static string? ForegroundTitle()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return null;
        var buffer = new StringBuilder(512);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.Length == 0 ? null : buffer.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
