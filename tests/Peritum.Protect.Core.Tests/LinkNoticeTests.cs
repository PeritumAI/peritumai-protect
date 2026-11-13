using System.Text.Json;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Redaction;

namespace Peritum.Protect.Core.Tests;

public class LinkNoticeTests
{
    private static readonly BrandCatalog Catalog = BrandCatalog.LoadAll();

    [Fact]
    public void Only_a_medium_or_high_flag_warns()
    {
        Assert.False(Plan("https://t.co/abc").Warns);
        Assert.False(Plan("https://example.com:8443/status").Warns);
        Assert.False(Plan("https://www.paypal.com/signin").Warns);
        Assert.True(Plan("https://paypa1.example/signin").Warns);
        Assert.True(Plan("http://203.0.113.10/login").Warns);
        var both = Plan("https://bit.ly/invoice.exe");
        Assert.True(both.Warns);
        Assert.Contains(LinkCodes.Shortener, both.ReasonCodes);
        Assert.Contains(LinkCodes.RiskyDownload, both.ReasonCodes);
    }

    [Fact]
    public void The_same_link_warns_once_per_session_and_a_trusted_site_stays_quiet()
    {
        const string url = "https://paypa1.example/signin";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var first = Plan(url, seen: seen);
        Assert.True(first.Warns);
        foreach (var key in first.RememberKeys)
            seen.Add(key);
        Assert.False(Plan(url, seen: seen).Warns);

        var trusted = new HashSet<string>(StringComparer.Ordinal) { "paypa1.example" };
        Assert.False(Plan(url, domain => trusted.Contains(domain)).Warns);
    }

    [Fact]
    public void Trust_stores_a_hint_and_not_the_domain()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-sites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new AllowListStore(Path.Combine(root, "sites.json"));
            store.Add("paypa1.example");
            Assert.True(store.Contains("paypa1.example"));
            var file = File.ReadAllText(Path.Combine(root, "sites.json"));
            Assert.DoesNotContain("paypa1.example", file, StringComparison.Ordinal);
            Assert.DoesNotContain("http", file, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void One_card_puts_the_command_before_the_link_and_the_link_before_the_data()
    {
        Assert.Equal(["command", "link", "data"], WarningOrder.Sections(true, true, true));
        Assert.Equal(["link", "data"], WarningOrder.Sections(false, true, true));
        Assert.Equal(["command", "data"], WarningOrder.Sections(true, false, true));
        Assert.Equal(["link"], WarningOrder.Sections(false, true, false));
    }

    [Fact]
    public void The_card_names_the_destination_and_the_log_does_not()
    {
        var plan = Plan("https://paypal.com@evil.example/login");
        var sentences = plan.Sentences().Select(line => line.Text).ToList();
        Assert.Contains(sentences, line => line.Contains("evil.example", StringComparison.Ordinal));
        Assert.Contains(sentences, line => line.Contains("before @", StringComparison.OrdinalIgnoreCase));

        var lookalike = Plan("https://paypa1.example/signin");
        Assert.Contains(lookalike.Sentences(), line => line.Text.Contains("PayPal", StringComparison.Ordinal));
        var parts = LinkNoticePlan.Split("https://paypa1.example/signin", "paypa1.example");
        Assert.Equal("paypa1.example", parts.Domain);
        Assert.Contains("https://", parts.Before, StringComparison.Ordinal);

        var evt = new ProtectEvent
        {
            Ts = DateTimeOffset.Parse("2026-10-01T08:00:00Z"),
            Source = "clipboard",
            Action = EventActions.LinkWarning,
            Hits = lookalike.ReasonCodes
                .GroupBy(code => code, StringComparer.Ordinal)
                .Select(group => new HitCount { RuleId = group.Key, Count = group.Count() })
                .ToList()
        };
        var json = JsonSerializer.Serialize(evt);
        Assert.DoesNotContain("paypa1", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lookalike", json, StringComparison.Ordinal);

        var report = SafetyReport.Build([evt], 2026, 10, true);
        var text = SafetyReport.Text(report, true);
        var csv = SafetyReport.Csv(report, true);
        Assert.Contains("lookalike", text, StringComparison.Ordinal);
        Assert.Contains("lookalike,1", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("paypa1", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paypa1", csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Lawyer_finance_hr_and_clinic_turn_link_warnings_on()
    {
        foreach (var id in new[] { "lawyer", "accountant", "hr", "clinic" })
            Assert.True(Peritum.Protect.Core.Profiles.ProfileCatalog.Find(id)!.WatchLinks);
        foreach (var id in new[] { "general", "developer", "journalist" })
            Assert.False(Peritum.Protect.Core.Profiles.ProfileCatalog.Find(id)!.WatchLinks);
    }

    private static LinkNoticePlan Plan(
        string text,
        Func<string, bool>? trusted = null,
        ISet<string>? seen = null) =>
        LinkNotice.Build(text, enabled: true, Catalog, trusted, seen);
}
