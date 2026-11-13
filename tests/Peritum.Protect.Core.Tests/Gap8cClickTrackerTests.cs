using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8cClickTrackerTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void A_sendgrid_newsletter_that_names_the_sender_does_not_warn()
    {
        var raw = """
            From: Example Shop <news@example-shop.com>
            To: Ada <ada@example.com>
            Subject: This week
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Visit <a href="https://u1234.ct.sendgrid.net/ls/click?upn=made-up-token">www.example-shop.com</a>.</p>
            """;
        var session = Scan(raw, "shop.eml");
        Assert.Empty(session.Result.LinkFindings);
    }

    [Fact]
    public void A_mailchimp_newsletter_that_names_the_sender_does_not_warn()
    {
        var raw = """
            From: Peritum News <hello@peritumai.com>
            To: Ada <ada@example.com>
            Subject: Blog
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Read <a href="https://peritumai.us21.list-manage.com/track/click?u=made-up">https://peritumai.com/blog</a>.</p>
            """;
        var session = Scan(raw, "news.eml");
        Assert.Empty(session.Result.LinkFindings);
    }

    [Fact]
    public void A_tracker_link_that_names_another_brand_stays_high()
    {
        var raw = """
            From: News <news@evil.example>
            To: Ada <ada@example.com>
            Subject: Account
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Open <a href="https://u1234.ct.sendgrid.net/ls/click?upn=made-up-token">www.paypal.com</a>.</p>
            """;
        var session = Scan(raw, "phish.eml");
        var finding = Assert.Single(session.Result.LinkFindings, item => item.RuleId == "link.text_mismatch");
        Assert.Equal(Severity.High, finding.Severity);
    }

    [Fact]
    public void A_tracker_link_to_the_same_brand_as_the_sender_does_not_warn()
    {
        var raw = """
            From: PayPal <hello@paypal-communication.com>
            To: Ada <ada@example.com>
            Subject: Receipt
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>See <a href="https://u9.ct.sendgrid.net/ls/click?upn=made-up">www.paypal.com</a>.</p>
            """;
        var session = Scan(raw, "receipt.eml");
        Assert.Empty(session.Result.LinkFindings);
    }

    [Fact]
    public void An_existing_phishing_link_still_warns()
    {
        var raw = """
            From: PayPal Support <billing@secure-login.example>
            To: Ada <ada@example.com>
            Subject: Invoice
            MIME-Version: 1.0
            Content-Type: text/html; charset=utf-8

            <p>Please <a href="https://secure-login.example/signin">www.paypal.com</a> review this.</p>
            <p><a href="https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpaypa1.example%2Fsignin">Open</a></p>
            """;
        var session = Scan(raw, "old.eml");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.text_mismatch");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.lookalike");
        Assert.Contains(session.Result.LinkFindings, finding => finding.RuleId == "link.sender_mismatch");
    }

    private static ScanSession Scan(string raw, string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-gap8c-track-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, name);
            File.WriteAllText(path, raw.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            var session = new ProtectService().Scan(ScanRequest.ForFile(path), Rules);
            Assert.True(string.IsNullOrEmpty(session.Result.Error), session.Result.Error);
            return session;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
