using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class Gap8dSitesTests
{
    [Fact]
    public void A_site_I_use_warns_on_lookalikes_and_not_on_the_real_site()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        state.TrustedSites.Add("mybank.example");
        state.TrustedSites.Add("bank.com");
        var path = Path.Combine(dir.Root, "links.html");
        File.WriteAllText(path, """
            <p>https://rnybank.example/</p>
            <p>https://mybank-login.example/</p>
            <p>https://mybank.example.secure-verify.xyz/</p>
            <p>https://mybank.example/login</p>
            <p>https://bnak.com/</p>
            """);
        var session = state.Engine.Scan(ScanRequest.ForFile(path), state.ActiveRules());
        foreach (var url in new[]
        {
            "https://rnybank.example/",
            "https://mybank-login.example/",
            "https://mybank.example.secure-verify.xyz/"
        })
            Assert.True(LinkNotice.Build(url, true, state.RegionLinkCatalog(), state.TrustedSites.Contains).Warns, url);
        Assert.False(LinkNotice.Build("https://mybank.example/login", true, state.RegionLinkCatalog(), state.TrustedSites.Contains).Warns);
        Assert.False(LinkNotice.Build("https://bnak.com/", true, state.RegionLinkCatalog(), state.TrustedSites.Contains).Warns);
        var findings = session.Result.LinkFindings;
        Assert.Contains(findings, finding => finding.Snippet.Contains("rnybank.example", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Snippet.Contains("mybank-login.example", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Snippet.Contains("secure-verify.xyz", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Snippet.Contains("mybank.example/login", StringComparison.Ordinal)
            || finding.Snippet.Contains("goes to mybank.example", StringComparison.OrdinalIgnoreCase)
            && !finding.Snippet.Contains("secure-verify", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Snippet.Contains("bnak.com", StringComparison.Ordinal));
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void The_list_comes_back_after_a_restart_and_is_not_plain_text_on_disk()
    {
        using var dir = Temp();
        var blank = new SettingsViewModel(new AppState(Path.Combine(dir.Root, "blank")), () => { });
        Assert.True(blank.ShowSitesEmpty);
        Assert.Empty(blank.Sites);
        var state = new AppState(dir.Root);
        state.TrustedSites.Add("mybank.example");
        var again = new AppState(dir.Root);
        var export = Path.Combine(dir.Root, "sites-export.json");
        var settings = new SettingsViewModel(again, () => { }, saveSitesFile: () => export);
        Assert.Contains("mybank.example", settings.Sites);
        Assert.Equal("Links to these sites are not warned about, and links that imitate them are.", Peritum.Protect.Core.TextCatalog.Get("sitesHint"));
        Assert.Equal("example: mybank.com", Peritum.Protect.Core.TextCatalog.Get("sitesPlaceholder"));
        Assert.Equal("No sites yet.", Peritum.Protect.Core.TextCatalog.Get("sitesEmpty"));
        settings.ExportSitesCommand.Execute(null);
        Assert.Contains("mybank.example", File.ReadAllText(export), StringComparison.Ordinal);
        var disk = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir.Root, "sites.json")));
        Assert.DoesNotContain("mybank", disk, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_migrated_hmac_entry_still_silences_warnings()
    {
        using var dir = Temp();
        var path = Path.Combine(dir.Root, "sites.json");
        new AllowListStore(path).Add("paypa1.example");
        var state = new AppState(dir.Root);
        var settings = new SettingsViewModel(state, () => { });
        Assert.Equal(
            "Site added before this update (re-add it to protect against lookalikes)",
            Assert.Single(settings.Sites));
        Assert.False(LinkNotice.Build(
            "https://paypa1.example/signin",
            enabled: true,
            BrandCatalog.LoadAll(),
            state.TrustedSites.Contains).Warns);
        var html = Path.Combine(dir.Root, "legacy.html");
        File.WriteAllText(html, "<p>https://paypa1.example/signin</p>");
        var session = state.Engine.Scan(ScanRequest.ForFile(html), state.ActiveRules());
        Assert.DoesNotContain(session.Result.LinkFindings, finding => finding.Snippet.Contains("paypa1.example", StringComparison.Ordinal));
        var disk = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("paypa1", disk, StringComparison.OrdinalIgnoreCase);
    }

    private static TempDir Temp() => new(Path.Combine(Path.GetTempPath(), "peritum-gap8d-" + Guid.NewGuid().ToString("N")));

    private sealed class TempDir : IDisposable
    {
        public TempDir(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
