using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class Gap8dLinksTests
{
    [Fact]
    public void The_links_badge_warns_for_a_suspicious_link_and_is_clear_when_there_are_none()
    {
        using var dir = Temp();
        var model = new MainViewModel(new AppState(dir.Root), _ => { });
        model.Show(Session(suspicious: 0), null, logNow: false);
        var clear = model.Categories.Single(row => row.Id == "links");
        Assert.Equal("None", clear.Chip);
        Assert.Equal("None", clear.ChipText);
        Assert.Equal("\uE73E", clear.BadgeIcon);
        Assert.Equal("\uE71B", clear.Icon);

        model.Show(Session(suspicious: 1), null, logNow: false);
        var links = model.Categories.Single(row => row.Id == "links");
        Assert.Equal("Check", links.Chip);
        Assert.Equal("Check link", links.ChipText);
        Assert.Equal("\uE7BA", links.BadgeIcon);
        Assert.Equal("\uE71B", links.Icon);
        Assert.Equal("2 findings in this file (2 emails), 1 suspicious link", model.ResultBody);
    }

    [Fact]
    public void Trust_this_site_drops_the_link_and_undo_puts_it_back()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var copied = new List<string>();
        var model = new MainViewModel(state, copied.Add);
        model.Show(Session(suspicious: 1), null, logNow: false);
        var links = model.Categories.Single(row => row.Id == "links");
        var row = links.Findings.Single();
        Assert.True(row.ShowTrust);
        Assert.True(row.ShowCopyAddress);
        Assert.False(row.ShowNotSensitive);
        Assert.False(row.ShowCopyLocation);

        row.CopyAddressCommand.Execute(null);
        Assert.Equal("https://account-verify.example/login", copied.Single());

        row.TrustCommand.Execute(null);
        Assert.True(row.IsMuted);
        Assert.True(row.ShowUndo);
        Assert.False(row.ShowTrust);
        Assert.Equal("Trusted. It will not be warned about.", row.StatusText);
        Assert.True(state.TrustedSites.Contains("account-verify.example"));
        Assert.Equal("None", links.Chip);
        Assert.Equal("None", links.ChipText);
        Assert.Equal("2 findings in this file (2 emails), 1 link, none suspicious", model.ResultBody);

        row.UndoCommand.Execute(null);
        Assert.False(row.IsMuted);
        Assert.True(row.ShowTrust);
        Assert.False(state.TrustedSites.Contains("account-verify.example"));
        Assert.Equal("Check", links.Chip);
        Assert.Equal("Check link", links.ChipText);
        Assert.Equal("2 findings in this file (2 emails), 1 suspicious link", model.ResultBody);
    }

    private static ScanSession Session(int suspicious)
    {
        var emails = new[]
        {
            Email(0, "ada@example.com"),
            Email(20, "bob@example.com")
        };
        var links = new List<Finding>();
        if (suspicious > 0)
        {
            links.Add(new Finding
            {
                RuleId = "link.text_mismatch",
                Label = "Mismatched link",
                DisplayLabel = "Mismatched link",
                PluralLabel = "Mismatched link",
                Category = "links",
                Severity = Severity.High,
                Start = 0,
                Length = 0,
                Placeholder = "LINK",
                Snippet = "Shows www.paypal.com but goes to account-verify.example",
                Location = "in this file",
                LinkDomain = "account-verify.example",
                LinkAddress = "https://account-verify.example/login"
            });
        }

        return new ScanSession
        {
            Request = ScanRequest.ForText("ada@example.com bob@example.com", ScanSource.File),
            Text = "ada@example.com bob@example.com",
            Result = new ScanResult
            {
                Findings = emails,
                LinkFindings = links,
                LinkCount = suspicious == 0 ? 4 : 1,
                ContentHash = "abc",
                Source = ScanSource.File,
                DisplayName = "note.eml"
            }
        };
    }

    private static Finding Email(int start, string value) => new()
    {
        RuleId = "pii.email",
        Label = "Email",
        DisplayLabel = "email",
        PluralLabel = "emails",
        Category = "personal",
        Severity = Severity.Warn,
        Start = start,
        Length = value.Length,
        Placeholder = "EMAIL",
        Snippet = value,
        Location = "line 1"
    };

    private static TempDir Temp() => new(Path.Combine(Path.GetTempPath(), "peritum-gap8d-links-" + Guid.NewGuid().ToString("N")));

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
