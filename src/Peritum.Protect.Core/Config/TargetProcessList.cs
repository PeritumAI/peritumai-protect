namespace Peritum.Protect.Core.Config;

public static class TargetProcessList
{
    public static readonly string[] Default =
    [
        "chrome.exe",
        "msedge.exe",
        "brave.exe",
        "opera.exe",
        "firefox.exe",
        "chatgpt.exe",
        "claude.exe",
        "vivaldi.exe",
        "arc.exe",
        "perplexity.exe",
        "lmstudio.exe",
        "LM Studio.exe",
        "cursor.exe",
        "code.exe",
        "windsurf.exe",
        "ollama.exe",
        "ollama app.exe",
        "ms-teams.exe",
        "slack.exe",
        "copilot.exe"
    ];

    public static bool Matches(string? processName, IEnumerable<string>? targets)
    {
        if (string.IsNullOrWhiteSpace(processName) || targets is null)
            return false;

        foreach (var raw in targets)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var name = raw.Trim().Trim('"');
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
