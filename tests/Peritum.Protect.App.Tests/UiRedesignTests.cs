using System.Text.RegularExpressions;
using Peritum.Protect.App;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.Themes;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class UiRedesignTests
{
    [Fact]
    public void Text_pairs_meet_wcag_aa()
    {
        foreach (var (theme, foreground, background) in ThemePalettes.TextPairs())
        {
            var ratio = Contrast.Ratio(ThemePalettes.Hex(theme, foreground), ThemePalettes.Hex(theme, background));
            Assert.True(ratio >= 4.5, theme + " " + foreground + " on " + background + " is " + ratio.ToString("0.00"));
        }
    }

    [Fact]
    public void Resource_strings_avoid_forbidden_claims()
    {
        var banned = new Regex(
            @"\b(blocked|block|safe|secure|guaranteed|smart|addresses)\b|100% protected|ai-powered|confidential business info|your privacy, our priority",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var text in UiText.All())
            Assert.DoesNotMatch(banned, text);
    }

    [Fact]
    public void Every_builtin_rule_has_a_category_and_custom_terms_stay_custom()
    {
        var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
        Assert.All(rules, rule => Assert.Contains(rule.Category, new[] { "personal", "payment", "secrets", "custom", "hidden" }));
        Assert.Contains(rules, rule => rule.Category == "personal");
        Assert.Contains(rules, rule => rule.Category == "secrets");
        Assert.False(rules.First(rule => rule.Id.StartsWith("secret.", StringComparison.Ordinal)).Reversible);
    }

    [Fact]
    public void Result_groups_count_types_and_masking_hides_the_full_value()
    {
        var findings = new List<Finding>
        {
            Hit("pii.email", "Email", "personal", "ada@example.com", "email", "emails"),
            Hit("pii.email", "Email", "personal", "bob@example.com", "email", "emails"),
            Hit("pii.email", "Email", "personal", "cy@example.com", "email", "emails"),
            Hit("pii.phone", "Phone", "personal", "+381641234567", "phone", "phones")
        };
        var groups = ResultGroups.Build(findings);
        var personal = groups.Single(group => group.Id == "personal");
        Assert.Equal("3 emails · 1 phone", personal.CountsText);
        Assert.Equal("None", groups.Single(group => group.Id == "secrets").Chip);
        Assert.Equal("a***@example.com", ValueMask.Mask("ada@example.com", "pii.email"));
        Assert.Equal("**** **** **** 1111", ValueMask.Mask("4111111111111111", "pii.card"));
        Assert.NotEqual("sk-testFAKEkey1234567890", ValueMask.Mask("sk-testFAKEkey1234567890", "secret.openai_sk"));
    }

    [Fact]
    public void Clipboard_labels_follow_the_stop_setting_and_update_in_place()
    {
        var model = new PasteWarningViewModel(Session(), "chrome", "redact", "restore-previous");
        Assert.Equal("Undo copy", model.StopLabel);
        Assert.Equal("Leave as is", model.LeaveLabel);
        Assert.Equal("Redact", model.RedactLabel);
        var image = Session();
        image = new ScanSession
        {
            Request = image.Request,
            Text = image.Text,
            ClipboardImage = [1, 2, 3],
            Result = image.Result
        };
        Assert.Equal("Redacted image to clipboard", new PasteWarningViewModel(image, "chrome", "redact").RedactLabel);
        Assert.Equal("Chrome is in front", model.ProcessLine);
        model.Retarget(Session(), "notepad");
        Assert.Equal("notepad is in front", model.ProcessLine);
        Assert.Equal("Edge", FriendlyProcessNames.Display("msedge"));
    }

    [Theory]
    [InlineData("light", false)]
    [InlineData("dark", true)]
    [InlineData("system", null)]
    public void Theme_choice_parses_and_explicit_modes_override_system(string choice, bool? dark)
    {
        Assert.Equal(choice == "light" ? ThemeChoice.Light : choice == "dark" ? ThemeChoice.Dark : ThemeChoice.System, ThemeService.Parse(choice));
        if (dark is bool expected)
            Assert.Equal(expected, choice == "dark");
    }

    private static Finding Hit(string id, string label, string category, string snippet, string? display = null, string? plural = null) => new()
    {
        RuleId = id,
        Label = label,
        DisplayLabel = display ?? "",
        PluralLabel = plural ?? "",
        Category = category,
        Placeholder = label,
        Snippet = snippet,
        Start = 0,
        Length = snippet.Length
    };

    private static ScanSession Session() => new()
    {
        Request = ScanRequest.ForText("ada@example.com", ScanSource.Clipboard),
        Text = "ada@example.com",
        Result = new ScanResult
        {
            Source = ScanSource.Clipboard,
            Findings = [Hit("pii.email", "Email", "personal", "ada@example.com")],
            ContentHash = "hash"
        }
    };
}
