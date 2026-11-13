namespace Peritum.Protect.Core.Events;

public sealed class HitCount
{
    public string RuleId { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>One JSONL line. Never includes clipboard or file body.</summary>
public sealed class ProtectEvent
{
    public DateTimeOffset Ts { get; set; }
    public string Source { get; set; } = "manual";
    public string? Process { get; set; }
    public string? FileName { get; set; }
    /// <summary>Full path on this PC. Omitted unless Remember file locations is on.</summary>
    public string? FilePath { get; set; }
    /// <summary>Full path of a protected or reversible copy, when one was made and locations are remembered.</summary>
    public string? CopyPath { get; set; }
    public string Action { get; set; } = "warn";
    public List<HitCount> Hits { get; set; } = [];
    public string? SnippetHash { get; set; }
    public string? Error { get; set; }
    public int? DurationMs { get; set; }
    public bool? Incomplete { get; set; }
    public List<string>? TimedOutRuleIds { get; set; }
    public string? FileType { get; set; }
    public int? SegmentCount { get; set; }
    /// <summary>reversible or permanent, when a copy or a redaction was made.</summary>
    public string? Mode { get; set; }
}

public static class EventActions
{
    public const string Allow = "allow";
    public const string Warn = "warn";
    public const string Ignore = "ignore";
    public const string Stop = "stop";
    public const string Redact = "redact";
    public const string CopyMade = "copy-made";
    public const string Error = "error";
    public const string Dismiss = "dismiss";
    public const string FalsePositive = "false-positive";
    public const string CopyRefused = "copy-refused";
    public const string ClickFix = "clickfix";
    public const string LinkWarning = "link_warning";
}
