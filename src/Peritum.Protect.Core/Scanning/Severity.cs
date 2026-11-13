namespace Peritum.Protect.Core.Scanning;

public enum Severity
{
    Info = 0,
    Warn = 1,
    High = 2,
    Block = 3
}

public static class SeverityParser
{
    public static Severity Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "info" => Severity.Info,
        "high" => Severity.High,
        "block" => Severity.Block,
        _ => Severity.Warn
    };
}
