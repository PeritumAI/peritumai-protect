using Microsoft.Win32;
using Peritum.Protect.Core.Files;

namespace Peritum.Protect.App.Services;

public static class ExplorerMenu
{
    public static void Register(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
            return;
        foreach (var extension in SupportedFileTypes.Extensions)
        {
            var root = $@"Software\Classes\SystemFileAssociations\{extension}\shell";
            Write(root + @"\PeritumProtect.Scan", "Scan with PeritumAI Protect", $"\"{exe}\" --scan \"%1\"");
            Write(root + @"\PeritumProtect.Copy", "Make protected copy", $"\"{exe}\" --protect \"%1\"");
        }

        Write(@"Software\Classes\Directory\shell\PeritumProtect", "Check with PeritumAI Protect", $"\"{exe}\" --scan \"%1\"");
    }

    public static void Unregister()
    {
        foreach (var extension in SupportedFileTypes.Extensions)
        {
            var root = $@"Software\Classes\SystemFileAssociations\{extension}\shell";
            Delete(root + @"\PeritumProtect.Scan");
            Delete(root + @"\PeritumProtect.Copy");
        }

        Delete(@"Software\Classes\Directory\shell\PeritumProtect");
    }

    private static void Write(string keyPath, string label, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        key?.SetValue("", label);
        using var commandKey = key?.CreateSubKey("command");
        commandKey?.SetValue("", command);
    }

    private static void Delete(string keyPath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // The menu was already gone.
        }
    }
}
