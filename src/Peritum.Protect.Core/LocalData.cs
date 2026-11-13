namespace Peritum.Protect.Core;

public static class LocalData
{
    public static IReadOnlyList<string> ActivityFiles(string root)
    {
        if (!Directory.Exists(root))
            return [];
        return Directory.GetFiles(root, "events*.jsonl")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void DeleteActivity(string root)
    {
        foreach (var file in ActivityFiles(root))
            File.Delete(file);
    }

    public static void DeleteAllowList(string root)
    {
        DeleteIfExists(Path.Combine(root, "allowlist.json"));
        DeleteIfExists(Path.Combine(root, "allowlist.json.key"));
    }

    public static void DeleteMaps(string root)
    {
        var dir = Path.Combine(root, "maps");
        if (!Directory.Exists(dir))
            return;
        foreach (var file in Directory.GetFiles(dir))
            File.Delete(file);
    }

    public static IReadOnlyList<string> ExceptSettings(string root)
    {
        if (!Directory.Exists(root))
            return [];
        var settings = Path.GetFullPath(Path.Combine(root, "settings.json"));
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !path.Equals(settings, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string DescribeExceptSettings(string root)
    {
        var names = ExceptSettings(root).Select(path => Path.GetRelativePath(root, path)).ToList();
        if (names.Count == 0)
            return "Nothing will be removed. settings.json is kept.";
        return "This removes everything in the PeritumAI Protect data folder except settings.json:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, names);
    }

    public static void DeleteExceptSettings(string root)
    {
        foreach (var file in ExceptSettings(root))
            File.Delete(file);
        if (!Directory.Exists(root))
            return;
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
