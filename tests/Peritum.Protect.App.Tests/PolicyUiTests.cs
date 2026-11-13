using System.Text.RegularExpressions;
using Peritum.Protect.App;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class PolicyUiTests
{
    [Fact]
    public void Locked_settings_are_disabled_and_are_not_written_over_the_user()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-policy-ui-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            File.WriteAllText(policyPath, """
                {"watchClipboard":false,"disabledRuleIds":["pii.email"],"lockIgnore":true,"profile":"lawyer","redactionMode":"permanent"}
                """);
            var state = new AppState(root, new PolicyBinding(policyPath, EmptyPolicyRegistry.Instance));
            var model = new SettingsViewModel(state, () => { });

            Assert.False(state.Settings.WatchClipboard);
            Assert.True(state.UserSettings.WatchClipboard);
            Assert.False(model.ClipboardEdit.CanEdit);
            Assert.True(model.ClipboardEdit.ShowLock);
            Assert.False(model.WatchClipboard);
            Assert.True(model.ShowIgnoreLock);
            Assert.True(model.ShowProfileLock);
            Assert.Equal("Profile: Lawyer", model.ProfileLine);
            Assert.Equal("permanent", model.RedactionMode);
            Assert.False(model.RedactionEdit.CanEdit);
            Assert.Equal("reversible", state.UserSettings.RedactionMode);
            var email = Assert.Single(model.Rules, rule => rule.Id == "pii.email");
            Assert.False(email.IsOn);
            Assert.True(email.IsLocked);
            Assert.False(email.CanEdit);
            Assert.True(model.Rules.First(rule => rule.Id == "pii.phone").CanEdit);

            state.SaveSettings();
            var saved = new SettingsStore(Path.Combine(root, "settings.json")).Load();
            Assert.True(saved.WatchClipboard);
            Assert.DoesNotContain("pii.email", saved.DisabledRuleIds);
            Assert.Equal("reversible", saved.RedactionMode);

            File.WriteAllText(policyPath, "{");
            state.ReloadPolicy();
            model.RefreshPolicy();
            Assert.Equal(PolicyWarnings.File, state.PolicyWarning);
            Assert.Equal(PolicyWarnings.File, model.PolicyWarning);
            Assert.True(state.Settings.WatchClipboard);
            Assert.True(model.ClipboardEdit.CanEdit);
            Assert.True(model.WatchClipboard);
            Assert.False(model.ShowIgnoreLock);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Not_sensitive_is_not_remembered_when_policy_locks_it()
    {
        var model = new PasteWarningViewModel(Session(), "chrome", "ask", allowAlwaysIgnore: false);
        PasteChoice? choice = null;
        model.Decided += picked => choice = picked;

        Assert.False(model.CanAlwaysIgnore);
        Assert.True(model.ShowIgnoreLock);
        model.Hits[0].NotSensitiveCommand.Execute(null);

        Assert.Equal(PasteChoice.Ignore, choice);
        Assert.Equal("Your organization does not allow remembering this.", model.Hits[0].StatusText);
        Assert.True(model.Hits[0].Exclude);
    }

    [Fact]
    public void Policy_warning_text_avoids_forbidden_claims()
    {
        var banned = new Regex(
            @"\b(blocked|block|safe|secure|guaranteed|smart|addresses)\b|100% protected|ai-powered",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.DoesNotMatch(banned, PolicyWarnings.File);
        Assert.DoesNotMatch(banned, PolicyWarnings.Registry);
        Assert.DoesNotMatch(banned, PolicyWarnings.Both);
        Assert.Equal("Set by your organization", UiText.SetByOrganization);
    }

    private static ScanSession Session() => new()
    {
        Request = ScanRequest.ForText("ada@example.com", ScanSource.Clipboard),
        Text = "ada@example.com",
        Result = new ScanResult
        {
            Source = ScanSource.Clipboard,
            Findings =
            [
                new Finding
                {
                    RuleId = "pii.email",
                    Label = "Email",
                    Placeholder = "EMAIL",
                    Start = 0,
                    Length = 15,
                    Snippet = "ada@example.com"
                }
            ],
            ContentHash = "test-hash"
        }
    };
}
