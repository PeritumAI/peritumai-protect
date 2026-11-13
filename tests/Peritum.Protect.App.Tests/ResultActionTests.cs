using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class ResultActionTests
{
    [Fact]
    public void Copy_actions_stay_hidden_until_a_protected_copy_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-result-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var state = new AppState(root);
            var copied = new List<string>();
            var model = new MainViewModel(state, copied.Add);
            var source = Path.Combine(root, "note.txt");
            File.WriteAllText(source, "Contact ada@example.com about the invoice.");

            var session = state.Engine.Scan(ScanRequest.ForFile(source), state.ActiveRules());
            model.Show(session, null, logNow: false);

            Assert.True(model.PendingActionsVisible);
            Assert.False(model.CopyActionsVisible);
            Assert.True(model.CanMakeCopy);
            Assert.Null(model.LastCopyPath);
            Assert.NotEqual("Protected copy saved.", model.ResultTitle);

            model.MakeCopyCommand.Execute(null);

            Assert.False(model.PendingActionsVisible);
            Assert.True(model.CopyActionsVisible);
            Assert.Equal("Protected copy saved.", model.ResultTitle);
            Assert.Equal("note_protect.txt", model.ResultBody);
            Assert.False(string.IsNullOrEmpty(model.LastCopyPath));
            Assert.True(File.Exists(model.LastCopyPath));
            Assert.Matches("^Original not changed \\(SHA-256 [0-9a-f]{12}…\\)$", model.HashLine);

            model.CopyPathCommand.Execute(null);
            Assert.Equal(model.LastCopyPath, copied.Single());

            model.DismissCommand.Execute(null);
            Assert.False(model.CopyActionsVisible);
            Assert.False(model.PendingActionsVisible);
            Assert.Null(model.LastCopyPath);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
