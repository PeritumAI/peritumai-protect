using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class DisplayLabelTests
{
    [Fact]
    public void Every_builtin_rule_has_a_display_label_and_plural()
    {
        var rules = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.DisplayLabel), rule.Id + " is missing displayLabel");
            Assert.False(string.IsNullOrWhiteSpace(rule.PluralLabel), rule.Id + " is missing plural");
        });
    }

    [Fact]
    public void Counts_keep_display_casing_and_use_plurals()
    {
        var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
        var scanned = new RuleScanner().Scan(
            ScanRequest.ForText("ada@example.com bob@example.com sk-testFAKEkey1234567890", ScanSource.Manual),
            rules);
        var groups = ResultGroups.Build(scanned.Findings);
        Assert.Equal("2 emails", groups.Single(group => group.Id == "personal").CountsText);
        Assert.Equal("1 OpenAI-style key", groups.Single(group => group.Id == "secrets").CountsText);

        var iban = FindingFrom(rules.Single(rule => rule.Id == "fin.iban"));
        var api = FindingFrom(rules.Single(rule => rule.Id == "secret.google_api"));
        var mixed = ResultGroups.Build([iban, api, api, api]);
        Assert.Equal("1 IBAN", mixed.Single(group => group.Id == "payment").CountsText);
        Assert.Equal("3 API keys", mixed.Single(group => group.Id == "secrets").CountsText);
        Assert.DoesNotContain("openai-style key", groups.Single(group => group.Id == "secrets").CountsText, StringComparison.Ordinal);
    }

    [Fact]
    public void Trial_stats_use_display_labels_and_csv_keeps_the_rule_id()
    {
        var events = new[]
        {
            new ProtectEvent
            {
                Action = EventActions.Warn,
                Hits =
                [
                    new HitCount { RuleId = "pii.email", Count = 2 },
                    new HitCount { RuleId = "secret.openai_sk", Count = 1 }
                ]
            }
        };
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pii.email"] = "email",
            ["secret.openai_sk"] = "OpenAI-style key"
        };
        var stats = ActivityStats.Summarize(events, labels);
        var display = stats.ToDisplay();
        Assert.Contains("email: hits 2", display, StringComparison.Ordinal);
        Assert.Contains("OpenAI-style key: hits 1", display, StringComparison.Ordinal);
        Assert.DoesNotContain("pii.email", display, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.openai_sk", display, StringComparison.Ordinal);

        var csv = stats.ToCsv();
        Assert.Contains("rule,label,hits,false_positives,false_positive_rate", csv, StringComparison.Ordinal);
        Assert.Contains("pii.email,email,2,0,0", csv, StringComparison.Ordinal);
        Assert.Contains("secret.openai_sk,OpenAI-style key,1,0,0", csv, StringComparison.Ordinal);
    }

    private static Finding FindingFrom(Rule rule) => new()
    {
        RuleId = rule.Id,
        Label = rule.Label,
        DisplayLabel = rule.DisplayLabel,
        PluralLabel = rule.PluralLabel,
        Category = rule.Category,
        Placeholder = rule.Placeholder,
        Snippet = "x",
        Severity = rule.Severity
    };
}
