using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;

namespace Peritum.Protect.Core.Tests;

public class EnglishOnlyTests
{
    [Fact]
    public void A_hidden_language_is_not_offered_and_falls_back_to_english()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-lang-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "de.json"), """
                {"language":"German","nativeName":"Deutsch","status":"reviewed","strings":{"sectionLanguage":"Sprache"}}
                """);
            File.WriteAllText(Path.Combine(dir, "sr-Latn.json"), """
                {"language":"Serbian Latin","nativeName":"srpski","status":"reviewed","strings":{}}
                """);
            TextCatalog.LoadDirectory(dir);
            var codes = TextCatalog.UserLanguages().Select(item => item.Code).ToArray();
            Assert.Equal(["en"], codes);
            Assert.Equal("", TextCatalog.NormalizeSetting("de"));
            Assert.Equal("", TextCatalog.NormalizeSetting("sr-Latn"));
            Assert.Equal("", TextCatalog.NormalizeSetting("en"));
            Assert.Equal("", TextCatalog.NormalizeSetting(""));
            Assert.Equal("qps-ploc", TextCatalog.NormalizeSetting("qps-ploc"));

            TextCatalog.Use("de");
            Assert.Equal("en", TextCatalog.Language);
            Assert.Equal("Language", TextCatalog.Get("sectionLanguage"));

            TextCatalog.Use("qps-rtl");
            Assert.Equal("qps-rtl", TextCatalog.Language);
            Assert.True(TextCatalog.IsRightToLeft);
        }
        finally
        {
            TextCatalog.Use("en");
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void An_old_de_setting_loads_as_english()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-de-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "settings.json"), """{"uiLanguage":"de","showFirstRun":false}""");
            var settings = new SettingsStore(Path.Combine(root, "settings.json")).Load();
            Assert.Equal("", settings.UiLanguage);
            TextCatalog.Use(settings.UiLanguage);
            Assert.Equal("en", TextCatalog.Language);
        }
        finally
        {
            TextCatalog.Use("en");
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void The_app_project_installs_english_only()
    {
        var csproj = File.ReadAllText(Path.Combine(Repo(), "src", "Peritum.Protect.App", "Peritum.Protect.App.csproj"));
        Assert.Contains("lang\\en.json", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("lang\\*.json", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("de.json", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("sr-Latn.json", csproj, StringComparison.Ordinal);
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lang", "en.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo was not found.");
    }
}
