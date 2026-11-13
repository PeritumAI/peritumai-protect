using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class BatchFile
{
    public required string Path { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public string Status { get; set; } = "";
    public string StatusCode { get; set; } = "";
    public string Detail { get; set; } = "";
    public ScanSession? Session { get; set; }
}

public sealed class BatchPlan
{
    public List<BatchFile> Files { get; } = [];
    public int SkippedHiddenOrSystem { get; set; }
    public bool HitFileLimit { get; set; }
    public bool HitDepthLimit { get; set; }
}

public sealed class BatchSummary
{
    public int Checked { get; init; }
    public int WithFindings { get; init; }
    public int NotChecked { get; init; }
    public string Message { get; init; } = "";

    public static BatchSummary From(IEnumerable<BatchFile> files)
    {
        var finished = files.Where(file =>
            file.StatusCode.Length > 0 &&
            !file.StatusCode.Equals(BatchScan.CodeChecking, StringComparison.Ordinal)).ToList();
        var notChecked = finished.Count(file => file.StatusCode.StartsWith("unchecked", StringComparison.Ordinal));
        var withFindings = finished.Count(file => file.StatusCode == BatchScan.CodeFindings);
        var checkedCount = finished.Count - notChecked;
        var findingTotal = finished.Sum(file => file.Session?.Result.HitCount ?? 0);
        var filesWord = TextCatalog.Plural("batchFile", finished.Count, ("count", finished.Count));
        var have = TextCatalog.Plural("batchHave", withFindings);
        var total = TextCatalog.Plural("findingCount", findingTotal, ("count", findingTotal));
        return new BatchSummary
        {
            Checked = checkedCount,
            WithFindings = withFindings,
            NotChecked = notChecked,
            Message = TextCatalog.Format("batchSummary", ("ready", withFindings), ("files", filesWord), ("have", have), ("total", total))
        };
    }
}

public static class BatchScan
{
    public const int MaxFiles = 500;
    public const int MaxDepth = 5;
    public const string CodeChecking = "checking";
    public const string CodeClear = "clear";
    public const string CodeIncomplete = "incomplete";
    public const string CodeFindings = "findings";
    public const string CodeUnchecked = "unchecked";
    public const string CodeUncheckedType = "unchecked.type";
    public const string CodeUncheckedPassword = "unchecked.password";
    public const string CodeUncheckedEncrypted = "unchecked.encrypted";
    public const string CodeUncheckedUnreadable = "unchecked.unreadable";
    public const string CodeUncheckedSize = "unchecked.size";
    public static string Checking => TextCatalog.Get("status.checking");
    public static string NothingFound => TextCatalog.Get("status.clear");
    public static string Incomplete => TextCatalog.Get("status.incomplete");
    public static string FileLimit => TextCatalog.Get("status.fileLimit");
    public static string DepthLimit => TextCatalog.Get("status.depthLimit");

    public static BatchPlan Collect(IEnumerable<string> roots)
    {
        var plan = new BatchPlan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (plan.Files.Count >= MaxFiles)
            {
                plan.HitFileLimit = true;
                break;
            }

            if (string.IsNullOrWhiteSpace(root))
                continue;
            string full;
            try
            {
                full = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            if (File.Exists(full))
                AddFile(plan, full, seen);
            else if (Directory.Exists(full))
                Walk(plan, full, 0, seen);
        }

        return plan;
    }

    public static string Notice(BatchPlan plan)
    {
        var parts = new List<string>();
        if (plan.HitFileLimit)
            parts.Add(FileLimit);
        if (plan.HitDepthLimit)
            parts.Add(DepthLimit);
        if (plan.SkippedHiddenOrSystem > 0)
            parts.Add(TextCatalog.Plural("batchSkipped", plan.SkippedHiddenOrSystem, ("count", plan.SkippedHiddenOrSystem)));
        return string.Join(" ", parts);
    }

    public static BatchSummary Run(
        IReadOnlyList<BatchFile> files,
        ProtectService engine,
        IReadOnlyList<Rule> rules,
        Func<string, bool>? isAllowed,
        CancellationToken cancel,
        Action<BatchFile>? progress)
    {
        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
                break;
            file.StatusCode = CodeChecking;
            file.Status = Checking;
            progress?.Invoke(file);
            ScanSession session;
            try
            {
                session = engine.Scan(ScanRequest.ForFile(file.Path), rules, isAllowed);
            }
            catch
            {
                session = new ScanSession
                {
                    Request = ScanRequest.ForFile(file.Path),
                    Text = "",
                    Result = ScanResult.Failed(
                        ScanSource.File,
                        file.Name,
                        UncheckedFiles.Corrupt)
                };
            }

            file.Session = session;
            var (code, text) = Classify(session);
            file.StatusCode = code;
            file.Status = text;
            file.Detail = DetailOf(session);
            progress?.Invoke(file);
        }

        return BatchSummary.From(files);
    }

    public static string StatusOf(ScanSession session) => Classify(session).Text;

    public static (string Code, string Text) Classify(ScanSession session)
    {
        if (session.Result.Error is not null)
        {
            var code = session.Result.ReasonCode ?? InferUnchecked(session.Result.Error);
            return (code, TextCatalog.Get(StatusKey(code)));
        }

        var entropyOnly = ScanCoverage.OnlyEntropyIncomplete(session.Result.Incomplete, session.Result.FailedRuleIds);
        if ((session.Result.Incomplete || session.Result.Truncated) && !entropyOnly)
            return (CodeIncomplete, Incomplete);
        if (session.Result.HitCount == 0)
            return (CodeClear, NothingFound);
        return (CodeFindings, TextCatalog.Plural("findingCount", session.Result.HitCount, ("count", session.Result.HitCount)));
    }

    private static string StatusKey(string code) => code switch
    {
        CodeUncheckedType => "status.uncheckedType",
        CodeUncheckedPassword => "status.uncheckedPassword",
        CodeUncheckedEncrypted => "status.uncheckedEncrypted",
        CodeUncheckedUnreadable => "status.uncheckedUnreadable",
        CodeUncheckedSize => "status.uncheckedSize",
        _ => "status.unchecked"
    };

    private static string InferUnchecked(string error)
    {
        if (error.Contains("file type", StringComparison.OrdinalIgnoreCase))
            return CodeUncheckedType;
        if (error.Contains("password", StringComparison.OrdinalIgnoreCase))
            return CodeUncheckedPassword;
        if (error.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return CodeUncheckedEncrypted;
        if (error.Contains("could not be read", StringComparison.OrdinalIgnoreCase) || error.Contains("corrupt", StringComparison.OrdinalIgnoreCase))
            return CodeUncheckedUnreadable;
        if (error.Contains("too large", StringComparison.OrdinalIgnoreCase) || error.Contains("size limit", StringComparison.OrdinalIgnoreCase))
            return CodeUncheckedSize;
        return CodeUnchecked;
    }

    public static string BodyKey(string code) => code switch
    {
        CodeUncheckedPassword => "unchecked.password.body",
        CodeUncheckedUnreadable => "unchecked.unreadable.body",
        CodeUncheckedType => "unchecked.legacy.body",
        CodeUncheckedEncrypted => "unchecked.encrypted.body",
        CodeUncheckedSize => "unchecked.size.body",
        _ => "status.unchecked"
    };

    public static string DetailOf(ScanSession session)
    {
        if (session.Result.ReasonCode is { Length: > 0 } code)
            return TextCatalog.Get(BodyKey(code));
        if (session.Result.Error is { } error)
            return error.StartsWith("Not checked", StringComparison.Ordinal) ? error : "Not checked. " + error;
        if (!string.IsNullOrWhiteSpace(session.Result.Note))
            return session.Result.Note;
        return "";
    }

    public static string ShortNotChecked(string error)
    {
        if (error.Contains("file type", StringComparison.OrdinalIgnoreCase))
            return "Not checked: file type";
        if (error.Contains("password", StringComparison.OrdinalIgnoreCase))
            return "Not checked: password";
        if (error.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return "Not checked: encrypted";
        if (error.Contains("could not be read", StringComparison.OrdinalIgnoreCase) || error.Contains("corrupt", StringComparison.OrdinalIgnoreCase))
            return "Not checked: unreadable";
        if (error.Contains("too large", StringComparison.OrdinalIgnoreCase) || error.Contains("size limit", StringComparison.OrdinalIgnoreCase))
            return "Not checked: too large";
        return "Not checked";
    }

    private static void Walk(BatchPlan plan, string directory, int depth, HashSet<string> seen)
    {
        if (plan.Files.Count >= MaxFiles)
        {
            plan.HitFileLimit = true;
            return;
        }

        if (depth > MaxDepth)
        {
            plan.HitDepthLimit = true;
            return;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory);
        }
        catch
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (plan.Files.Count >= MaxFiles)
            {
                plan.HitFileLimit = true;
                return;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch
            {
                continue;
            }

            if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                plan.SkippedHiddenOrSystem++;
                continue;
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (depth >= MaxDepth)
                {
                    plan.HitDepthLimit = true;
                    continue;
                }

                Walk(plan, entry, depth + 1, seen);
                continue;
            }

            AddFile(plan, entry, seen);
        }
    }

    private static void AddFile(BatchPlan plan, string path, HashSet<string> seen)
    {
        if (plan.Files.Count >= MaxFiles)
        {
            plan.HitFileLimit = true;
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!seen.Add(full))
            return;
        var extension = Path.GetExtension(full);
        plan.Files.Add(new BatchFile
        {
            Path = full,
            Name = Path.GetFileName(full),
            Type = extension.Length == 0 ? "File" : extension.TrimStart('.').ToUpperInvariant()
        });
    }
}
