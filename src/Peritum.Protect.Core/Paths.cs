namespace Peritum.Protect.Core;

/// <summary>
/// User data lives under %LOCALAPPDATA%\Peritum\Protect\.
/// PERITUM_PROTECT_DATA overrides that folder. The capture script sets it so screenshots do not touch the real log.
/// </summary>
public static class Paths
{
    public const string DataEnvironmentVariable = "PERITUM_PROTECT_DATA";

    public static string Root
    {
        get
        {
            var over = Environment.GetEnvironmentVariable(DataEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(over))
                return over.Trim();
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Peritum",
                "Protect");
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string EventsFile => Path.Combine(Root, "events.jsonl");
    public static string AllowListFile => Path.Combine(Root, "allowlist.json");
    public static string KeyFile => Path.Combine(Root, "machine.key");
    public static string ErrorLogFile => Path.Combine(LogsDir, "errors.log");
    public static string RulesDir => Path.Combine(Root, "rules");
    public static string UserRulesFile => Path.Combine(RulesDir, "user.json");
    public static string ImportsDir => Path.Combine(RulesDir, "imports");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string MapsDir => Path.Combine(Root, "maps");
    public static string CommandFile => Path.Combine(Root, "pending-command.txt");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(RulesDir);
        Directory.CreateDirectory(ImportsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(MapsDir);
    }
}
