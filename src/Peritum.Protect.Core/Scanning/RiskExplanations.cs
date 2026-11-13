using System.Text.Json;

namespace Peritum.Protect.Core.Scanning;

public sealed record RiskCopy(string Text, string Tip)
{
    public string Paragraph => Text + " " + Tip;
}

/// <summary>
/// Plain explanations from lang/en.json. Keys are built-in rule ids and reason codes.
/// </summary>
public static class RiskExplanations
{
    public const string ClickFix = "clickfix.command";
    public const string CryptoSwap = "crypto.swap";
    public const string HiddenContent = "hidden.content";
    public const string HiddenInstruction = "hidden.instruction";
    public const string PdfAnnotation = "hidden.pdf-annotation";
    public const string PdfForm = "hidden.pdf-form";
    public const string PdfInfo = "hidden.pdf-info";
    public const string UncheckedPassword = "unchecked.password";
    public const string UncheckedUnreadable = "unchecked.unreadable";
    public const string UncheckedLegacy = "unchecked.legacy";
    public const string UncheckedEncrypted = "unchecked.encrypted";
    public const string UncheckedImageSize = "unchecked.image-size";
    public const string UncheckedOcr = "unchecked.ocr";
    public const string IncompleteSize = "incomplete.size";
    public const string IncompletePart = "incomplete.part";
    public const string IncompleteParts = "incomplete.parts";
    public const string IncompleteRatio = "incomplete.ratio";
    public const string IncompleteNesting = "incomplete.nesting";
    public const string IncompletePdfPages = "incomplete.pdf-pages";
    public const string IncompletePdfTime = "incomplete.pdf-time";
    public const string IncompleteStopped = "incomplete.stopped";
    public const string IncompleteEntropy = "incomplete.entropy";

    public static readonly string[] ReasonCodes =
    [
        ClickFix,
        CryptoSwap,
        HiddenContent,
        HiddenInstruction,
        "hidden.invisible-unicode",
        "hidden.unicode-tags",
        "hidden.mixed-scripts",
        "hidden.html-comment",
        "hidden.html",
        "hidden.offscreen",
        "hidden.alt",
        "hidden.office-text",
        "hidden.office-sheet",
        "hidden.white-text",
        "hidden.pdf-invisible",
        "hidden.pdf-tiny",
        "hidden.pdf-white",
        "hidden.pdf-outside",
        "hidden.pdf-optional",
        PdfAnnotation,
        PdfForm,
        PdfInfo,
        UncheckedPassword,
        UncheckedUnreadable,
        UncheckedLegacy,
        UncheckedEncrypted,
        UncheckedImageSize,
        UncheckedOcr,
        IncompleteSize,
        IncompletePart,
        IncompleteParts,
        IncompleteRatio,
        IncompleteNesting,
        IncompletePdfPages,
        IncompletePdfTime,
        IncompleteStopped,
        IncompleteEntropy
    ];

    private static readonly (string Phrase, string Key)[] Phrases =
    [
        ("Invisible Unicode", "hidden.invisible-unicode"),
        ("Unicode tags", "hidden.unicode-tags"),
        ("Mixed scripts in one word", "hidden.mixed-scripts"),
        ("Text addressed to an AI", HiddenInstruction),
        ("HTML comment", "hidden.html-comment"),
        ("Off-screen text", "hidden.offscreen"),
        ("Alt or title text", "hidden.alt"),
        ("Hidden HTML", "hidden.html"),
        ("Hidden sheet or row", "hidden.office-sheet"),
        ("Hidden text", "hidden.office-text"),
        ("White text", "hidden.white-text"),
        ("invisible text", "hidden.pdf-invisible"),
        ("text under 2 pt", "hidden.pdf-tiny"),
        ("white text", "hidden.pdf-white"),
        ("text outside the page", "hidden.pdf-outside"),
        ("hidden optional content", "hidden.pdf-optional"),
        ("annotation", PdfAnnotation),
        ("form field", PdfForm),
        ("document info", PdfInfo),
        ("password-protected", UncheckedPassword),
        ("Not checked: password", UncheckedPassword),
        ("could not be read", UncheckedUnreadable),
        ("Not checked: unreadable", UncheckedUnreadable),
        ("file type cannot be checked", UncheckedLegacy),
        ("Not checked: file type", UncheckedLegacy),
        ("entry is encrypted", UncheckedEncrypted),
        ("Not checked: encrypted", UncheckedEncrypted),
        ("above the size limit", UncheckedImageSize),
        ("Not checked: too large", UncheckedImageSize),
        ("OCR is not available", UncheckedOcr),
        ("more than 256 MB", IncompleteSize),
        ("larger than 64 MB", IncompletePart),
        ("more than 10,000 parts", IncompleteParts),
        ("more than 100 to 1", IncompleteRatio),
        ("more than 3 deep", IncompleteNesting),
        ("more than 2,000 pages", IncompletePdfPages),
        ("took too long to check", IncompletePdfTime),
        ("Not fully checked", IncompleteStopped),
        ("High-entropy check skipped", IncompleteEntropy)
    ];

    private static Dictionary<string, RiskCopy> _current = new(StringComparer.Ordinal);
    private static bool _loaded;

    public static void LoadFile(string path)
    {
        if (!File.Exists(path))
            return;
        _current = Parse(File.ReadAllText(path));
        _loaded = true;
    }

    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        LoadFile(Path.Combine(AppContext.BaseDirectory, "lang", "en.json"));
        _loaded = true;
    }

    public static Dictionary<string, RiskCopy> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var map = new Dictionary<string, RiskCopy>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("explanations", out var explanations))
            return map;
        foreach (var item in explanations.EnumerateObject())
        {
            var text = item.Value.GetProperty("text").GetString() ?? "";
            var tip = item.Value.GetProperty("tip").GetString() ?? "";
            map[item.Name] = new RiskCopy(text, tip);
        }
        return map;
    }

    public static RiskCopy? ForKey(string key)
    {
        EnsureLoaded();
        return _current.TryGetValue(key, out var copy) ? copy : null;
    }

    public static RiskCopy? ForFinding(Finding finding)
    {
        EnsureLoaded();
        if (finding.RuleId is HiddenContent or HiddenInstruction)
        {
            var hidden = Match(finding.Label) ?? Match(finding.Location);
            if (hidden is not null && _current.TryGetValue(hidden, out var typed))
                return typed;
        }

        if (_current.TryGetValue(finding.RuleId, out var byRule))
            return byRule;
        var phrase = Match(finding.Label) ?? Match(finding.Location);
        return phrase is not null && _current.TryGetValue(phrase, out var byPhrase) ? byPhrase : null;
    }

    public static RiskCopy? ForPlace(Finding finding)
    {
        EnsureLoaded();
        var key = Match(finding.Location);
        if (key is null || key == finding.RuleId)
            return null;
        if (ForFinding(finding)?.Text == (_current.TryGetValue(key, out var copy) ? copy.Text : null))
            return null;
        return _current.TryGetValue(key, out var place) ? place : null;
    }

    public static RiskCopy? ForNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;
        EnsureLoaded();
        var key = Match(note);
        return key is not null && _current.TryGetValue(key, out var copy) ? copy : null;
    }

    public static string? Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        foreach (var (phrase, key) in Phrases)
        {
            if (text.Contains(phrase, StringComparison.Ordinal))
                return key;
        }
        return null;
    }
}
