using System.Text;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap5bUrlSecretTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Theory]
    [InlineData("https://admin:S3cretPass@db.example.com/app", "S3cretPass")]
    [InlineData("ftp://bob:hunter2hunter2@files.example.com", "hunter2hunter2")]
    [InlineData("https://admin:S3cretPass@db.example.com:5432/app", "S3cretPass")]
    [InlineData("https://ada:S3cret%2Fpass@db.example.com/app", "S3cret%2Fpass")]
    [InlineData("mqtt://sensor:p%40ss@broker.example.com", "p%40ss")]
    [InlineData("ssh://deploy:hunter2hunter2@shell.example.com/home", "hunter2hunter2")]
    public void A_password_in_a_web_address_is_a_secret_not_an_email(string text, string password)
    {
        var findings = new RuleScanner().Find("Open " + text + " now.", Rules);
        var secret = Assert.Single(findings, finding => finding.RuleId == "secret.connection");
        var value = ("Open " + text + " now.").Substring(secret.Start, secret.Length);
        Assert.Contains(password, value, StringComparison.Ordinal);
        Assert.DoesNotContain(findings, finding => finding.RuleId == "pii.email");
        Assert.StartsWith("secret.", secret.RuleId, StringComparison.Ordinal);
        Assert.Equal("secrets", secret.Category);
    }

    [Fact]
    public void A_url_with_no_password_is_not_a_secret()
    {
        var findings = new RuleScanner().Find("See https://admin@db.example.com/app today.", Rules);
        Assert.DoesNotContain(findings, finding => finding.RuleId.StartsWith("secret.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reversible_copy_does_not_keep_the_password_or_an_email_token()
    {
        const string password = "S3cretPass";
        const string text = "Connect https://admin:S3cretPass@db.example.com:5432/app later.";
        var dir = Path.Combine(Path.GetTempPath(), "peritum-gap5b-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "notes.txt");
            File.WriteAllText(source, text);
            var maps = Path.Combine(dir, "maps");
            var copy = new ProtectService().MakeProtectedCopy(
                source,
                Rules,
                "{name}_reversible{ext}",
                reversible: true,
                mapsDirectory: maps);
            Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
            Assert.True(copy.OriginalUnchanged);
            var body = File.ReadAllText(copy.OutputPath!);
            Assert.Contains(ReversibleRedactor.RemovedSecret, body, StringComparison.Ordinal);
            Assert.DoesNotContain(password, body, StringComparison.Ordinal);
            Assert.DoesNotContain("[[EMAIL_", body, StringComparison.Ordinal);
            Assert.DoesNotContain("admin:S3cretPass", body, StringComparison.Ordinal);
            var rawMap = File.ReadAllBytes(Path.Combine(maps, copy.MapId + ".map"));
            Assert.False(Contains(rawMap, password));
            var loaded = new MapStore(maps).Load(copy.MapId!);
            Assert.DoesNotContain(loaded!.Tokens.Values, value => value.Contains(password, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static bool Contains(byte[] haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        for (var i = 0; i <= haystack.Length - bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < bytes.Length; j++)
            {
                if (haystack[i + j] != bytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }
}
