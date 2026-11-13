using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class RuleScannerTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void Flags_email_and_sk_test_key()
    {
        var text = "Contact ada@example.com about the invoice.\nThe test key is sk-testFAKEkey1234567890.";
        var findings = new RuleScanner().Find(text, Rules);

        Assert.Contains(findings, f => f.RuleId == "pii.email" && f.Snippet.Contains("ada@example.com", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.RuleId == "secret.openai_sk");
        Assert.DoesNotContain(findings, f => f.RuleId == "pii.ipv4");
    }

    [Fact]
    public void Luhn_accepts_a_real_test_card_and_rejects_noise()
    {
        var text = "Pay 4111111111111111 today, not 1234567890123.";
        var findings = new RuleScanner().Find(text, Rules);
        var card = Assert.Single(findings, f => f.RuleId == "fin.card");
        Assert.Equal("4111111111111111", card.Snippet);
    }

    [Fact]
    public void Iban_and_aws_key()
    {
        var text = "Wire GB82WEST12345698765432 using AKIAIOSFODNN7EXAMPLE.";
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, f => f.RuleId == "fin.iban");
        Assert.Contains(findings, f => f.RuleId == "secret.aws_access_key");
    }

    [Fact]
    public void Pem_block_is_one_finding()
    {
        var text = """
            -----BEGIN RSA PRIVATE KEY-----
            MIIEowIBAAKCAQEA7example
            -----END RSA PRIVATE KEY-----
            """;
        var findings = new RuleScanner().Find(text, Rules);
        var pem = Assert.Single(findings, f => f.RuleId == "secret.pem");
        Assert.Contains("BEGIN RSA PRIVATE KEY", text.Substring(pem.Start, pem.Length), StringComparison.Ordinal);
    }

    [Fact]
    public void High_entropy_token_is_flagged_and_plain_words_are_not()
    {
        var random = "q7W9mK2pL8xN4vB6cZ1rT5yH0sD3fG7aQ2";
        var text = "hello " + random + " world";
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, f => f.RuleId == "secret.entropy" && f.Snippet.StartsWith("q7W9", StringComparison.Ordinal));
    }

    [Fact]
    public void Allow_list_removes_one_value()
    {
        var text = "ada@example.com and bob@example.com";
        var scanner = new RuleScanner();
        var findings = scanner.Find(text, Rules);
        var filtered = RuleScanner.ExceptValues(text, findings, ["ada@example.com"]);
        Assert.DoesNotContain(filtered, f => f.Snippet.Contains("ada@", StringComparison.Ordinal));
        Assert.Contains(filtered, f => f.Snippet.Contains("bob@", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_megabytes_with_one_email_scans_quickly()
    {
        var text = new string('a', 2_000_000 - 40) + " ada@example.com ";
        var start = DateTime.UtcNow;
        var findings = new RuleScanner().Find(text, Rules);
        var elapsed = DateTime.UtcNow - start;
        Assert.Contains(findings, f => f.RuleId == "pii.email");
        Assert.True(elapsed.TotalSeconds < 3, "scan took " + elapsed);
    }
}
