using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class TextRedactorTests
{
    [Fact]
    public void Same_placeholder_is_used_for_every_email()
    {
        var text = "ada@example.com and bob@example.com";
        var findings = new RuleScanner().Find(text, Active());
        var redacted = TextRedactor.Apply(text, findings);
        Assert.Equal("[EMAIL] and [EMAIL]", redacted);
        Assert.DoesNotContain("example.com", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Sk_key_becomes_api_key_placeholder()
    {
        var text = "key sk-testFAKEkey1234567890 end";
        var findings = new RuleScanner().Find(text, Active());
        var redacted = TextRedactor.Apply(text, findings);
        Assert.Contains("[API_KEY]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-test", redacted, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Rule> Active() =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
}
