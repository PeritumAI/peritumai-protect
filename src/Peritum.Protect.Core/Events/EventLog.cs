using System.Text.Json;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Events;

public sealed class EventLog
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int FilesToKeep = 6;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly byte[] _key;

    public EventLog(string path, byte[]? key = null)
    {
        _path = path;
        _key = key ?? Config.InstallKey.LoadOrCreate(path + ".key");
    }

    public void Append(ProtectEvent evt)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var line = JsonSerializer.Serialize(evt, JsonDefaults.Compact);
        lock (_gate)
        {
            var target = ActiveFile();
            RotateIfNeeded(target, line.Length);
            target = ActiveFile();
            File.AppendAllText(target, line + Environment.NewLine);
            TrimOldFiles();
        }
    }

    public ProtectEvent FromScan(ScanResult result, string action, string? process, string? fileName, bool rememberLocation = false, string? copyPath = null, string? mode = null)
    {
        return new ProtectEvent
        {
            Ts = DateTimeOffset.UtcNow,
            Source = result.Source switch
            {
                ScanSource.Clipboard => "clipboard",
                ScanSource.File => "file",
                _ => "manual"
            },
            Process = string.IsNullOrWhiteSpace(process) ? null : process,
            FileName = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName),
            FilePath = FileLocations.Remember(fileName, rememberLocation),
            CopyPath = FileLocations.Remember(copyPath, rememberLocation),
            Action = action,
            Hits = result.Findings
                .GroupBy(f => f.RuleId, StringComparer.Ordinal)
                .Select(g => new HitCount { RuleId = g.Key, Count = g.Count() })
                .ToList(),
            SnippetHash = string.IsNullOrEmpty(result.ContentHash) ? null : Hashing.HmacText(result.ContentHash, _key),
            Error = result.Error,
            DurationMs = result.DurationMs,
            Incomplete = result.Incomplete ? true : null,
            TimedOutRuleIds = result.FailedRuleIds.Count == 0 ? null : result.FailedRuleIds.ToList(),
            FileType = result.FileType,
            SegmentCount = result.SegmentCount,
            Mode = string.IsNullOrWhiteSpace(mode) ? null : mode
        };
    }

    public ProtectEvent FalsePositive(string ruleId) => new()
    {
        Ts = DateTimeOffset.UtcNow,
        Source = "manual",
        Action = EventActions.FalsePositive,
        Hits = [new HitCount { RuleId = ruleId, Count = 1 }]
    };

    public IReadOnlyList<ProtectEvent> ReadRecent(int max)
    {
        if (max <= 0)
            return [];
        return ReadAll().OrderByDescending(evt => evt.Ts).Take(max).ToList();
    }

    public IReadOnlyList<ProtectEvent> ReadAll()
    {
        List<string> files;
        lock (_gate)
            files = ListFiles();
        var lines = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
        }
        return Parse(lines);
    }

    private string ActiveFile()
    {
        var dir = Path.GetDirectoryName(_path) ?? ".";
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        var stem = Path.GetFileNameWithoutExtension(_path);
        return Path.Combine(dir, stem + "-" + month + ".jsonl");
    }

    private void RotateIfNeeded(string target, int incoming)
    {
        if (!File.Exists(target))
            return;
        var length = new FileInfo(target).Length;
        if (length + incoming < MaxFileBytes)
            return;
        var dir = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        var rolled = Path.Combine(dir, name + "-" + DateTime.UtcNow.ToString("HHmmssfff") + ".jsonl");
        File.Move(target, rolled);
    }

    private void TrimOldFiles()
    {
        var files = ListFiles();
        if (files.Count <= FilesToKeep)
            return;
        foreach (var old in files.Take(files.Count - FilesToKeep))
        {
            try { File.Delete(old); }
            catch { /* The next append tries again. */ }
        }
    }

    private List<string> ListFiles()
    {
        var dir = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return [];
        var stem = Path.GetFileNameWithoutExtension(_path);
        var files = Directory.GetFiles(dir, stem + "*.jsonl")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return files;
    }

    private static List<ProtectEvent> Parse(IEnumerable<string> lines)
    {
        var list = new List<ProtectEvent>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var evt = JsonSerializer.Deserialize<ProtectEvent>(line, JsonDefaults.Compact);
                if (evt is not null)
                    list.Add(evt);
            }
            catch
            {
                // Skip a torn line.
            }
        }
        return list;
    }
}
