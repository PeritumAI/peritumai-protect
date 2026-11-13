using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Scanning;

public static class CommandClassifier
{
    private static readonly Regex Dangerous = new(
        @"powershell.*-(?:enc|e|encodedcommand|w\s+hidden|nop)|invoke-expression|\biex\b|downloadstring|frombase64string|invoke-webrequest|iwr\s|irm\s|mshta\s+http|rundll32|regsvr32\s+/i:http|certutil\s+-urlcache|bitsadmin\s+/transfer|cmd\s+/c\s+https?://|curl\s+.+\|\s*(?:sh|bash)|#\s*(?:✅|verification id:)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    public static bool IsCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return Dangerous.IsMatch(text);
    }

    public static bool IsBrowser(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
            return false;
        return process.Contains("chrome", StringComparison.OrdinalIgnoreCase)
            || process.Contains("msedge", StringComparison.OrdinalIgnoreCase)
            || process.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            || process.Contains("brave", StringComparison.OrdinalIgnoreCase)
            || process.Contains("opera", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShellTarget(string? process, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(process))
            return false;
        if (process.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || process.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || process.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || process.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase)
            || process.Equals("conhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return process.Equals("explorer", StringComparison.OrdinalIgnoreCase)
            && (windowTitle?.Contains("Run", StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
