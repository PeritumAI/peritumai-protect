using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Tests;

public class RuleExchangeTests
{
    [Fact]
    public void Valid_export_round_trips_and_does_not_run_patterns()
    {
        var pack = new RulePack
        {
            Id = "user.example",
            Name = "Example",
            Version = "1.0.0",
            Rules =
            [
                new Rule
                {
                    Id = "user.example.client",
                    Label = "Client",
                    Kind = "exact",
                    Pattern = "Jane Q. Client",
                    Patterns = ["Jane Q. Client"],
                    Placeholder = "CLIENT"
                },
                new Rule
                {
                    Id = "user.example.matter",
                    Label = "Matter",
                    Kind = "regex",
                    Pattern = @"\bMAT-\d{3}\b",
                    Placeholder = "MATTER"
                }
            ]
        };
        var json = RuleExchange.Export([pack], [new UserTerm { Label = "Source", Kind = "exact", Text = "Source Example" }]);
        var result = RuleExchange.Read(json);
        Assert.True(result.Accepted, result.Message);
        Assert.Equal("Jane Q. Client", result.Packs[0].Rules[0].Pattern);
        Assert.Equal(@"\bMAT-\d{3}\b", result.Packs[0].Rules.Single(rule => rule.Id == "user.example.matter").Pattern);
        Assert.Equal("Source Example", result.Terms[0].Text);
        Assert.DoesNotContain("sk-", json, StringComparison.Ordinal);

        var dir = Path.Combine(Path.GetTempPath(), "peritum-exchange-" + Guid.NewGuid().ToString("n"));
        RuleExchange.WritePacks(dir, result.Packs);
        var loaded = RulePackLoader.LoadFile(Path.Combine(dir, "user.example.json"));
        Assert.Equal("Jane Q. Client", loaded.Rules[0].Pattern);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Invalid_files_are_rejected_with_a_clear_message()
    {
        var broken = RuleExchange.Read("{");
        Assert.False(broken.Accepted);
        Assert.Contains("not valid JSON", broken.Message, StringComparison.Ordinal);
        Assert.Empty(broken.Packs);

        var wrong = RuleExchange.Read("""{"kind":"script","version":1,"terms":[{"text":"hello"}]}""");
        Assert.False(wrong.Accepted);
        Assert.Contains("kind", wrong.Message, StringComparison.Ordinal);

        var field = RuleExchange.Read("""
            {"kind":"peritum-protect-export","version":1,"script":"calc.exe","terms":[{"label":"A","kind":"exact","text":"hello"}]}
            """);
        Assert.False(field.Accepted);
        Assert.Contains("Unexpected field", field.Message, StringComparison.Ordinal);
        Assert.Empty(field.Terms);

        var regex = RuleExchange.Read("""
            {"kind":"peritum-protect-export","version":1,"packs":[{"id":"user.bad","name":"Bad","version":"1","rules":[{"id":"user.bad.re","label":"Bad","kind":"regex","pattern":"("}]}]}
            """);
        Assert.False(regex.Accepted);
        Assert.Contains("not valid", regex.Message, StringComparison.Ordinal);
        Assert.Empty(regex.Packs);
    }

    [Fact]
    public void Duplicate_ids_are_reported_and_nothing_is_merged()
    {
        var json = """
            {"kind":"peritum-protect-export","version":1,"packs":[{"id":"user.dup","name":"Dup","version":"1","rules":[{"id":"pii.email","label":"Email","kind":"exact","pattern":"ada@example.com"},{"id":"user.dup.other","label":"Other","kind":"exact","pattern":"other"}]}]}
            """;
        var result = RuleExchange.Read(json, existingRuleIds: ["pii.email"]);
        Assert.False(result.Accepted);
        Assert.Contains("Duplicate rule id pii.email", result.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was merged", result.Message, StringComparison.Ordinal);
        Assert.Empty(result.Packs);

        var twice = """
            {"kind":"peritum-protect-export","version":1,"packs":[{"id":"user.dup","name":"Dup","version":"1","rules":[{"id":"user.dup.one","label":"One","kind":"exact","pattern":"one"},{"id":"user.dup.one","label":"Again","kind":"exact","pattern":"two"}]}]}
            """;
        var internalDup = RuleExchange.Read(twice);
        Assert.False(internalDup.Accepted);
        Assert.Contains("user.dup.one", internalDup.Message, StringComparison.Ordinal);
        Assert.Empty(internalDup.Packs);
        Assert.Empty(internalDup.Terms);
    }
}
