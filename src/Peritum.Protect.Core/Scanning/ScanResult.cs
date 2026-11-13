namespace Peritum.Protect.Core.Scanning;

public sealed class ScanResult
{
    public required IReadOnlyList<Finding> Findings { get; init; }
    public string? Error { get; init; }
    /// <summary>Stable code for a not-checked result, such as unchecked.legacy. The view translates it.</summary>
    public string? ReasonCode { get; init; }
    /// <summary>SHA-256 of the scanned text, not the text itself.</summary>
    public required string ContentHash { get; init; }
    public ScanSource Source { get; init; }
    public string? DisplayName { get; init; }
    public bool Truncated { get; init; }
    public bool Incomplete { get; init; }
    public IReadOnlyList<string> FailedRuleIds { get; init; } = [];
    public string? Note { get; init; }
    public int DurationMs { get; init; }
    public int SegmentCount { get; init; }
    public string? FileType { get; init; }
    public IReadOnlyList<Finding> LinkFindings { get; init; } = [];
    public int LinkCount { get; init; }

    public int HitCount => Findings.Count;

    public static ScanResult Empty(ScanSource source, string? displayName, string hash) => new()
    {
        Findings = Array.Empty<Finding>(),
        ContentHash = hash,
        Source = source,
        DisplayName = displayName
    };

    public static ScanResult Failed(ScanSource source, string? displayName, string error, string? reasonCode = null) => new()
    {
        Findings = Array.Empty<Finding>(),
        ContentHash = "",
        Source = source,
        DisplayName = displayName,
        Error = error,
        ReasonCode = reasonCode
    };
}
