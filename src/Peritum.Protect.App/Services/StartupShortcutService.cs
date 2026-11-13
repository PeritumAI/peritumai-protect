using Peritum.Protect.App.Native;

namespace Peritum.Protect.App.Services;

public static class StartupShortcutService
{
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "PeritumAI Protect.lnk");

    // Builds before the PeritumAI rename created this shortcut name.
    public static string LegacyShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "Peritum Protect.lnk");

    public static void Apply(bool enabled)
    {
        Remove(LegacyShortcutPath);
        if (!enabled)
        {
            Remove(ShortcutPath);
            return;
        }

        var exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0)
            return;
        Create(ShortcutPath, exe, AppContext.BaseDirectory);
    }

    public static void Remove(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
            File.Delete(shortcutPath);
    }

    public static void Create(string shortcutPath, string exePath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
            return;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.WindowStyle = 7;
            shortcut.Description = "PeritumAI Protect";
            shortcut.IconLocation = exePath + ",0";
            shortcut.Save();
        }
        finally
        {
            if (shell is IDisposable disposable)
                disposable.Dispose();
        }

        if (File.Exists(shortcutPath))
            ShellShortcut.SetIconAndAppId(shortcutPath, exePath, AppUserModel.Id);
    }

    public static string IconLocation(string shortcutPath) => ShellShortcut.ReadIconPath(shortcutPath);

    public static string? ShortcutAppUserModelId(string shortcutPath) => ShellShortcut.ReadAppId(shortcutPath);
}
