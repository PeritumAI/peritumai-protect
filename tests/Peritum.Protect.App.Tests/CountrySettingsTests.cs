using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;

namespace Peritum.Protect.App.Tests;

public class CountrySettingsTests
{
    [Fact]
    public void Checks_are_grouped_by_country_and_the_region_turns_that_country_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-country-ui-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var serbia = new SettingsViewModel(new AppState(root, null, "RS"), () => { });
            var serbian = serbia.RuleGroups.Single(group => group.Title == "Serbia");
            Assert.Contains(serbian.Rules, rule => rule.Id == "pii.jmbg");
            Assert.Contains(serbian.Rules, rule => rule.Id == "rs.pib" && rule.IsOn);
            Assert.Contains(serbian.Rules, rule => rule.Id == "rs.phone" && rule.IsOn);
            Assert.DoesNotContain(serbia.RuleGroups.Single(group => group.Title == "Personal data").Rules, rule => rule.Id == "pii.jmbg");

            Directory.CreateDirectory(root + "-hr");
            var croatia = new SettingsViewModel(new AppState(root + "-hr", null, "HR"), () => { });
            Assert.True(croatia.Rules.Single(rule => rule.Id == "hr.oib").IsOn);
            Assert.False(croatia.Rules.Single(rule => rule.Id == "rs.pib").IsOn);
            Assert.True(croatia.Rules.Single(rule => rule.Id == "pii.jmbg").IsOn);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
            if (Directory.Exists(root + "-hr"))
                Directory.Delete(root + "-hr", true);
        }
    }
}
