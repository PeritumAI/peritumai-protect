using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;

namespace Peritum.Protect.App.Tests;

public class RuleExchangeUiTests
{
    [Fact]
    public void Import_writes_a_pack_and_a_second_copy_is_not_merged()
    {
        var root = Temp();
        try
        {
            var state = new AppState(root);
            var model = new SettingsViewModel(state, () => { });
            var json = """
                {"kind":"peritum-protect-export","version":1,"packs":[{"id":"user.imported","name":"Imported","version":"1","rules":[{"id":"user.imported.client","label":"Client","kind":"exact","pattern":"Jane Q. Client"}]}],"terms":[{"label":"Source","kind":"exact","text":"Source Example"}]}
                """;
            var message = model.ImportRulesJson(json);
            Assert.Contains("Imported", message, StringComparison.Ordinal);
            Assert.Contains(model.Terms, term => term.Text == "Source Example");
            Assert.Contains(state.Packs, pack => pack.Id == "user.imported");
            Assert.True(File.Exists(Path.Combine(root, "rules", "imports", "user.imported.json")));

            var again = model.ImportRulesJson(json);
            Assert.Contains("Nothing was merged", again, StringComparison.Ordinal);
            Assert.Single(state.Packs, pack => pack.Id == "user.imported");
            Assert.Single(model.Terms, term => term.Text == "Source Example");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void An_import_with_the_same_id_as_a_builtin_pack_is_left_out()
    {
        var root = Temp();
        try
        {
            var imports = Path.Combine(root, "rules", "imports");
            Directory.CreateDirectory(imports);
            File.WriteAllText(Path.Combine(imports, "builtin.pii.json"), """
                {"id":"builtin.pii","name":"Fake","version":"9","rules":[{"id":"fake.only","label":"Fake","kind":"exact","pattern":"zzz"}]}
                """);
            var state = new AppState(root);
            Assert.Single(state.Packs, pack => pack.Id == "builtin.pii");
            Assert.DoesNotContain(state.Packs.SelectMany(pack => pack.Rules), rule => rule.Id == "fake.only");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-exchange-ui-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }
}
