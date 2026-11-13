using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core;

namespace Peritum.Protect.App.Tests;

public class EnglishOnlyUiTests
{
    [Fact]
    public void The_language_picker_lists_only_english_and_an_old_de_setting_opens_in_english()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-picker-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"), """{"uiLanguage":"de","showFirstRun":false}""");
            var state = new AppState(root);
            Assert.Equal("", state.UserSettings.UiLanguage);
            TextCatalog.Use(TextCatalog.IsDeveloperLanguage(state.UserSettings.UiLanguage) ? state.UserSettings.UiLanguage : "en");
            Assert.Equal("en", TextCatalog.Language);

            var model = new SettingsViewModel(state, () => { });
            Assert.Equal(["English"], model.InterfaceLanguages.Select(item => item.Label).ToArray());
            Assert.Equal(["en"], model.InterfaceLanguages.Select(item => item.Code).ToArray());
            Assert.Equal("en", model.InterfaceLanguage);
            Assert.Equal("", model.UiLanguage);

            var xaml = File.ReadAllText(Path.Combine(Repo(), "src", "Peritum.Protect.App", "Views", "SettingsView.xaml"));
            Assert.Contains("AutomationProperties.AutomationId=\"LanguagePicker\"", xaml, StringComparison.Ordinal);
            Assert.Contains("InterfaceLanguages", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Tag=\"de\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("sr-Latn", xaml, StringComparison.Ordinal);
        }
        finally
        {
            TextCatalog.Use("en");
            Directory.Delete(root, true);
        }
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Peritum.Protect.App", "Views", "SettingsView.xaml")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo was not found.");
    }
}
