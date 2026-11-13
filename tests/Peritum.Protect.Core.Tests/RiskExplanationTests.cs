using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class RiskExplanationTests
{
    [Fact]
    public void Every_builtin_rule_and_reason_code_has_an_explanation()
    {
        var map = RiskExplanations.Parse(File.ReadAllText(LangFile()));
        foreach (var rule in BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules))
            AssertCopy(map, rule.Id);
        foreach (var code in RiskExplanations.ReasonCodes)
            AssertCopy(map, code);
        Assert.Equal(RiskExplanations.ReasonCodes.Distinct().Count(), RiskExplanations.ReasonCodes.Length);
    }

    [Fact]
    public void Shown_reasons_map_to_an_explanation()
    {
        var samples = new[]
        {
            UncheckedFiles.PasswordProtected,
            UncheckedFiles.Corrupt,
            UncheckedFiles.LegacyType,
            ZipFileAdapter.EncryptedNote,
            ClipboardImageLimits.NotChecked,
            ClipboardImageLimits.OcrOff,
            PackageLimits.ExpandsTooFar,
            PackageLimits.EntryTooBig,
            PackageLimits.TooManyEntries,
            PackageLimits.RatioTooHigh,
            PackageLimits.NestedTooDeep,
            PackageLimits.PdfTooManyPages,
            PackageLimits.PdfTooSlow,
            ScanCoverage.EntropySkipNote,
            "Not fully checked. A slow or very large scan was stopped before the end.",
            "Not checked: file type",
            "Not checked: password",
            "Not checked: encrypted",
            "Not checked: unreadable",
            "Not checked: too large",
            "Invisible Unicode",
            "Unicode tags",
            "Mixed scripts in one word",
            "Text addressed to an AI",
            "HTML comment",
            "Hidden HTML",
            "Off-screen text",
            "Alt or title text",
            "Hidden text",
            "Hidden sheet or row",
            "White text",
            "invisible text",
            "text under 2 pt",
            "white text",
            "text outside the page",
            "hidden optional content",
            "page 2, annotation",
            "page 2, form field",
            "document info"
        };
        var map = RiskExplanations.Parse(File.ReadAllText(LangFile()));
        foreach (var sample in samples)
        {
            var key = RiskExplanations.Match(sample);
            Assert.False(string.IsNullOrEmpty(key), sample);
            Assert.True(map.ContainsKey(key!), sample + " -> " + key);
        }
    }

    private static void AssertCopy(IReadOnlyDictionary<string, RiskCopy> map, string key)
    {
        Assert.True(map.TryGetValue(key, out var copy), key);
        Assert.NotNull(copy);
        var textSentences = Sentences(copy.Text);
        var tipSentences = Sentences(copy.Tip);
        Assert.True(textSentences is >= 1 and <= 2, key + " text " + textSentences + " " + copy.Text);
        Assert.True(tipSentences is >= 1 and <= 2, key + " tip " + tipSentences + " " + copy.Tip);
        Assert.DoesNotContain("ada@example.com", copy.Paragraph, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-", copy.Paragraph, StringComparison.Ordinal);
    }

    private static int Sentences(string text)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c is '.' or '?' or '!')
                count++;
        }
        return count;
    }

    private static string LangFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "lang", "en.json");
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("lang/en.json was not found.");
    }
}
