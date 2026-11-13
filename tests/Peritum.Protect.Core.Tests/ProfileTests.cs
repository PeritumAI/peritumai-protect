using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Profiles;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Tests;

public class ProfileTests
{
    [Fact]
    public void Every_profile_enables_only_rules_that_exist()
    {
        var known = Known();
        var profiles = ProfileCatalog.LoadEmbedded();
        Assert.Equal(
            ["general", "lawyer", "clinic", "accountant", "hr", "developer", "journalist"],
            profiles.Select(profile => profile.Id).ToArray());
        Assert.All(profiles, profile =>
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.Name));
            Assert.False(string.IsNullOrWhiteSpace(profile.Description));
            Assert.All(profile.EnabledRuleIds, id => Assert.Contains(id, known));
            Assert.All(profile.DisabledRuleIds, id => Assert.Contains(id, known));
        });

        var secrets = BuiltinRules.LoadEmbedded()
            .SelectMany(pack => pack.Rules)
            .Select(rule => rule.Id)
            .Where(id => id.StartsWith("secret.", StringComparison.Ordinal))
            .ToList();
        var developer = profiles.Single(profile => profile.Id == "developer");
        Assert.All(secrets, id => Assert.Contains(id, developer.EnabledRuleIds));
        Assert.Equal("stop", developer.DefaultPasteAction);

        var clinic = profiles.Single(profile => profile.Id == "clinic");
        Assert.Contains("pii.dob", clinic.EnabledRuleIds);
        Assert.Contains("pii.health_id", clinic.EnabledRuleIds);

        var hr = profiles.Single(profile => profile.Id == "hr");
        Assert.Contains("pii.jmbg", hr.EnabledRuleIds);
        Assert.Contains("fin.iban", hr.EnabledRuleIds);
        Assert.Contains("fin.salary", hr.EnabledRuleIds);

        var lawyer = profiles.Single(profile => profile.Id == "lawyer");
        Assert.Contains("ref.labels", lawyer.EnabledRuleIds);
        Assert.DoesNotContain("pii.matter", lawyer.EnabledRuleIds);
        Assert.Contains("ref.finance", profiles.Single(profile => profile.Id == "accountant").EnabledRuleIds);
        Assert.DoesNotContain("ref.finance", lawyer.EnabledRuleIds);
        Assert.Contains(lawyer.SuggestedTerms, term => term.Label == "Client name" && term.Kind == "exact");

        Assert.Contains(profiles.Single(profile => profile.Id == "accountant").EnabledRuleIds, id => id == "fin.salary");
        Assert.Contains(profiles.Single(profile => profile.Id == "journalist").SuggestedTerms, term => term.Label == "Source name");
    }

    [Fact]
    public void Switching_profiles_changes_the_active_rules()
    {
        var known = Known();
        var settings = AppSettings.CreateDefault();
        Assert.True(ProfileCatalog.TryApply("general", settings, known, profileLocked: false).Applied);
        var general = Active(settings);
        Assert.DoesNotContain("secret.entropy", general);
        Assert.DoesNotContain("pii.dob", general);

        Assert.True(ProfileCatalog.TryApply("developer", settings, known, profileLocked: false).Applied);
        var developer = Active(settings);
        Assert.Contains("secret.entropy", developer);
        Assert.NotEqual(general, developer);

        Assert.True(ProfileCatalog.TryApply("clinic", settings, known, profileLocked: false).Applied);
        var clinic = Active(settings);
        Assert.Contains("pii.dob", clinic);
        Assert.Contains("pii.health_id", clinic);
        Assert.DoesNotContain("secret.entropy", clinic);
        Assert.NotEqual(developer, clinic);
    }

    [Fact]
    public void A_locked_profile_cannot_be_changed_and_unknown_rules_are_refused()
    {
        var known = Known();
        var settings = AppSettings.CreateDefault();
        Assert.True(ProfileCatalog.TryApply("lawyer", settings, known, profileLocked: false).Applied);
        var enabled = settings.EnabledRuleIds.ToList();

        var locked = ProfileCatalog.TryApply("developer", settings, known, profileLocked: true);
        Assert.False(locked.Applied);
        Assert.Equal("The profile is set by your organization.", locked.Reason);
        Assert.Equal("lawyer", settings.Profile);
        Assert.Equal(enabled, settings.EnabledRuleIds);

        var before = settings.Copy();
        var unknown = ProfileCatalog.TryApply(
            new ProtectionProfile
            {
                Id = "custom",
                Name = "Custom",
                Description = "Not installed.",
                EnabledRuleIds = ["no.such.rule"]
            },
            settings,
            known,
            profileLocked: false);
        Assert.False(unknown.Applied);
        Assert.Contains("no.such.rule", unknown.Reason, StringComparison.Ordinal);
        Assert.Equal(before.Profile, settings.Profile);
        Assert.Equal(before.EnabledRuleIds, settings.EnabledRuleIds);
    }

    private static HashSet<string> Known() =>
        BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Select(rule => rule.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Active(AppSettings settings) =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), settings.EnabledRuleIds, settings.DisabledRuleIds)
            .Select(rule => rule.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
