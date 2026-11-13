using System.Runtime.InteropServices;

namespace Peritum.Protect.App.Native;

public static class AppUserModel
{
    public const string Id = "Peritum.Protect";

    public static void SetCurrentProcess()
    {
        _ = SetCurrentProcessExplicitAppUserModelID(Id);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
