using Peritum.Protect.Core;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8cLinkCopyTests
{
    private const string Jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
    private const string Note = "Peritum note: this email contains 1 suspicious link (looks like PayPal, goes to account-verify.example).";

    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void The_protected_copy_and_the_bundle_keep_the_real_destination()
    {
        var raw = """
            From: PayPal <billing@account-verify.example>
            To: Ada <ada@example.com>
            Subject: Sign in
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p><a href="https://account-verify.example/login">https://www.paypal.com/signin</a></p>
            """;
        using var dir = Temp();
        var path = dir.Write("phish.eml", raw);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules, "{name}_protect{ext}");
        Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.Contains("account-verify.example", text, StringComparison.Ordinal);
        Assert.Contains("https://www.paypal.com/signin <https://account-verify.example/login>", text, StringComparison.Ordinal);
        Assert.Contains(Note, text, StringComparison.Ordinal);

        var exported = AiBundle.ExportFile(path, Rules, _ => false, new MapStore(dir.Maps));
        Assert.True(exported.Succeeded, exported.Message);
        var bundle = File.ReadAllText(exported.OutputPath!);
        Assert.Contains("account-verify.example", bundle, StringComparison.Ordinal);
        Assert.Contains("[https://www.paypal.com/signin](https://account-verify.example/login)", bundle, StringComparison.Ordinal);
        Assert.Contains(Note, bundle, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_email_copy_keeps_its_links()
    {
        var raw = """
            From: News <news@example.com>
            To: Ada <ada@example.com>
            Subject: Hello
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p><a href="https://www.example.com/story">Read the story</a></p>
            <p><a href="https://www.example.com/about">https://www.example.com/about</a></p>
            """;
        using var dir = Temp();
        var path = dir.Write("news.eml", raw);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules, "{name}_protect{ext}");
        Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.Contains("Read the story <https://www.example.com/story>", text, StringComparison.Ordinal);
        Assert.Contains("https://www.example.com/about", text, StringComparison.Ordinal);
        Assert.DoesNotContain("https://www.example.com/about <https://www.example.com/about>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Peritum note:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_token_in_a_link_is_redacted_and_the_link_stays()
    {
        var raw = """
            From: Desk <desk@example.com>
            To: Ada <ada@example.com>
            Subject: Reset
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p><a href="https://reset.example/login?token=JWT&amp;next=/home">Reset</a></p>
            """.Replace("JWT", Jwt, StringComparison.Ordinal);
        using var dir = Temp();
        var path = dir.Write("reset.eml", raw);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules, "{name}_protect{ext}");
        Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.Contains("https://reset.example/login?token=", text, StringComparison.Ordinal);
        Assert.Contains("next=/home", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Jwt, text, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_and_the_clipboard_keep_the_real_destination()
    {
        var read = HtmlClipboard.Read("<a href=\"https://account-verify.example/login\">https://www.paypal.com/signin</a>");
        Assert.Contains("https://www.paypal.com/signin <https://account-verify.example/login>", read.VisibleText, StringComparison.Ordinal);
        var same = HtmlClipboard.Read("<a href=\"https://www.example.com/about\">https://www.example.com/about</a>");
        Assert.DoesNotContain("<https://www.example.com/about>", same.VisibleText, StringComparison.Ordinal);
        Assert.Contains("https://www.example.com/about", same.VisibleText, StringComparison.Ordinal);
    }

    private static Work Temp() => new();

    private sealed class Work : IDisposable
    {
        public Work()
        {
            Root = Path.Combine(Path.GetTempPath(), "peritum-gap8c-copy-" + Guid.NewGuid().ToString("N"));
            Maps = Path.Combine(Root, "maps");
            Directory.CreateDirectory(Maps);
        }

        public string Root { get; }
        public string Maps { get; }

        public string Write(string name, string raw)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
