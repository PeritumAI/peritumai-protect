namespace Peritum.Protect.App.Services;

public static class FriendlyProcessNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["firefox"] = "Firefox",
        ["brave"] = "Brave",
        ["opera"] = "Opera",
        ["vivaldi"] = "Vivaldi",
        ["chatgpt"] = "ChatGPT",
        ["claude"] = "Claude",
        ["code"] = "Visual Studio Code",
        ["cursor"] = "Cursor",
        ["windsurf"] = "Windsurf",
        ["slack"] = "Slack",
        ["ollama"] = "Ollama",
        ["ollama app"] = "Ollama",
        ["lmstudio"] = "LM Studio",
        ["lm studio"] = "LM Studio"
    };

    public static string Display(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return "An app";
        var key = processName.Trim();
        if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            key = key[..^4];
        return Names.TryGetValue(key, out var name) ? name : processName.Trim();
    }
}
