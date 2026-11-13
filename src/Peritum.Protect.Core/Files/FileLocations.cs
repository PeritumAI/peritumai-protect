using Peritum.Protect.Core;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public static class FileLocations
{
    public static string Missing => TextCatalog.Get("fileMissing");
    public static string ClipboardNote => TextCatalog.Get("clipboardNotStored");

    public static string? Remember(string? path, bool remember)
    {
        if (!remember || string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public static string MissingMessage(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
            return "";
        return Missing;
    }

    public static string? ParentIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : null;
        }
        catch
        {
            return null;
        }
    }

    public static string FindingsCsv(IEnumerable<FindingsExportRow> rows)
    {
        var lines = new List<string> { "path,type,status,personal,payment,secrets,custom,locations" };
        foreach (var row in rows)
        {
            lines.Add(string.Join(",",
                Cell(row.Path),
                Cell(row.Type),
                Cell(row.Status),
                row.Personal.ToString(),
                row.Payment.ToString(),
                row.Secrets.ToString(),
                row.Custom.ToString(),
                Cell(row.Locations)));
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    public static FindingsExportRow FromScan(string path, string type, string status, IReadOnlyList<Finding> findings)
    {
        var groups = ResultGroups.Build(findings);
        int Count(string id) => groups.FirstOrDefault(group => group.Id == id)?.Count ?? 0;
        var locations = findings
            .OrderBy(finding => finding.Start)
            .Select(finding => finding.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location));
        return new FindingsExportRow
        {
            Path = path,
            Type = type,
            Status = status,
            Personal = Count("personal"),
            Payment = Count("payment"),
            Secrets = Count("secrets"),
            Custom = Count("custom"),
            Locations = string.Join("; ", locations)
        };
    }

    private static string Cell(string value)
    {
        var text = value ?? "";
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        return text;
    }
}

public sealed class FindingsExportRow
{
    public string Path { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; init; } = "";
    public int Personal { get; init; }
    public int Payment { get; init; }
    public int Secrets { get; init; }
    public int Custom { get; init; }
    public string Locations { get; init; } = "";
}
