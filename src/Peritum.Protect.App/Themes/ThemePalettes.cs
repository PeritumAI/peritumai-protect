namespace Peritum.Protect.App.Themes;

/// <summary>Token colors used by the theme dictionaries and the contrast tests.</summary>
public static class ThemePalettes
{
    public static readonly IReadOnlyDictionary<string, string> Brand = new Dictionary<string, string>
    {
        ["BrandNavy"] = "#071329",
        ["BrandNavyDeep"] = "#030712",
        ["BrandNavyRaised"] = "#0B1236",
        ["BrandCyan"] = "#22D3EE",
        ["BrandPurple"] = "#A78BFA",
        ["SidebarText"] = "#E6F1FF",
        ["SidebarMuted"] = "#9FB3D1"
    };

    public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["Background"] = "#F6F7FB",
        ["Surface"] = "#FFFFFF",
        ["SurfaceAlt"] = "#F0F2F8",
        ["Line"] = "#DCE1EC",
        ["Text"] = "#0B1236",
        ["TextMuted"] = "#4B5A73",
        ["Accent"] = "#6D28D9",
        ["AccentHover"] = "#5B21B6",
        ["AccentSoft"] = "#EDE9FE",
        ["Secondary"] = "#0E7490",
        ["WarningText"] = "#B45309",
        ["WarningSurface"] = "#FEF3C7",
        ["HighText"] = "#B91C1C",
        ["HighSurface"] = "#FEE2E2",
        ["ClearText"] = "#15803D",
        ["ClearSurface"] = "#DCFCE7",
        ["NeutralText"] = "#4B5563",
        ["NeutralSurface"] = "#F3F4F6",
        ["OnAccent"] = "#FFFFFF"
    };

    public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["Background"] = "#030712",
        ["Surface"] = "#0B1236",
        ["SurfaceAlt"] = "#111A44",
        ["Line"] = "#1E2A55",
        ["Text"] = "#E6F1FF",
        ["TextMuted"] = "#9FB3D1",
        ["Accent"] = "#A78BFA",
        ["AccentHover"] = "#C4B5FD",
        ["AccentSoft"] = "#1F1A4D",
        ["Secondary"] = "#22D3EE",
        ["WarningText"] = "#FBBF24",
        ["WarningSurface"] = "#3A2A0A",
        ["HighText"] = "#F87171",
        ["HighSurface"] = "#3F1515",
        ["ClearText"] = "#4ADE80",
        ["ClearSurface"] = "#0C2E1A",
        ["NeutralText"] = "#D1D5DB",
        ["NeutralSurface"] = "#111A44",
        ["OnAccent"] = "#0B1236"
    };

    /// <summary>Foreground/background pairs that appear as text in the UI.</summary>
    public static IEnumerable<(string Theme, string Foreground, string Background)> TextPairs()
    {
        yield return ("light", "Text", "Background");
        yield return ("light", "Text", "Surface");
        yield return ("light", "TextMuted", "Background");
        yield return ("light", "TextMuted", "Surface");
        yield return ("light", "OnAccent", "Accent");
        yield return ("light", "WarningText", "WarningSurface");
        yield return ("light", "HighText", "HighSurface");
        yield return ("light", "ClearText", "ClearSurface");
        yield return ("light", "NeutralText", "NeutralSurface");
        yield return ("light", "Secondary", "Surface");
        yield return ("dark", "Text", "Background");
        yield return ("dark", "Text", "Surface");
        yield return ("dark", "TextMuted", "Background");
        yield return ("dark", "TextMuted", "Surface");
        yield return ("dark", "OnAccent", "Accent");
        yield return ("dark", "WarningText", "WarningSurface");
        yield return ("dark", "HighText", "HighSurface");
        yield return ("dark", "ClearText", "ClearSurface");
        yield return ("dark", "NeutralText", "NeutralSurface");
        yield return ("sidebar", "SidebarText", "BrandNavy");
        yield return ("sidebar", "SidebarMuted", "BrandNavy");
    }

    public static string Hex(string theme, string name)
    {
        if (theme == "sidebar" || name.StartsWith("Brand", StringComparison.Ordinal) || name.StartsWith("Sidebar", StringComparison.Ordinal))
            return Brand[name];
        return theme == "dark" ? Dark[name] : Light[name];
    }
}
