using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Config;

namespace Peritum.Protect.App.Tests;

public class ProfileUiTests
{
    [Fact]
    public void Switching_profiles_in_settings_changes_the_active_rules()
    {
        var root = Temp();
        try
        {
            var state = new AppState(root);
            var model = new SettingsViewModel(state, () => { });
            Assert.Equal("Everyday checks for emails, phones, cards, and keys.", model.ProfileDescription);
            Assert.True(model.Rules.Single(rule => rule.Id == "secret.entropy").IsOn);

            model.SelectedProfileId = "clinic";
            Assert.True(model.Rules.Single(rule => rule.Id == "pii.dob").IsOn);
            Assert.True(model.Rules.Single(rule => rule.Id == "pii.health_id").IsOn);
            Assert.False(model.Rules.Single(rule => rule.Id == "secret.entropy").IsOn);
            Assert.Contains("Patient name", model.SuggestedTermsLine, StringComparison.Ordinal);

            model.SelectedProfileId = "general";
            Assert.False(model.Rules.Single(rule => rule.Id == "secret.entropy").IsOn);
            Assert.False(model.Rules.Single(rule => rule.Id == "pii.dob").IsOn);

            model.SelectedProfileId = "developer";
            Assert.Equal("stop", model.DefaultPasteAction);
            Assert.True(model.Rules.Single(rule => rule.Id == "secret.entropy").IsOn);
            Assert.False(model.Rules.Single(rule => rule.Id == "pii.dob").IsOn);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_locked_profile_cannot_be_changed_from_settings()
    {
        var root = Temp();
        try
        {
            var policyPath = Path.Combine(root, "policy.json");
            File.WriteAllText(policyPath, """{"profile":"lawyer"}""");
            var state = new AppState(root, new PolicyBinding(policyPath, EmptyPolicyRegistry.Instance));
            var model = new SettingsViewModel(state, () => { });

            Assert.False(model.ProfileEdit.CanEdit);
            Assert.True(model.ShowProfileLock);
            Assert.Equal("lawyer", model.SelectedProfileId);
            Assert.Equal("Profile: Lawyer", model.ProfileLine);
            Assert.False(model.CanAddSuggestedTerms);
            Assert.Equal("ask", model.DefaultPasteAction);
            Assert.False(model.Rules.Single(rule => rule.Id == "pii.dob").IsOn);

            model.SelectedProfileId = "clinic";
            Assert.Equal("ask", model.DefaultPasteAction);
            Assert.False(model.Rules.Single(rule => rule.Id == "pii.dob").IsOn);
            Assert.Equal("lawyer", state.Settings.Profile);
            Assert.Equal("general", state.UserSettings.Profile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-profile-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }
}
