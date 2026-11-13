using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class Gap8bNotSensitiveTests
{
    [Fact]
    public void Not_sensitive_mutes_the_row_and_undo_restores_it()
    {
        using var dir = Temp();
        var model = Card(dir.Root, "ada@example.com and bob@example.com");
        var row = model.Hits[0];

        row.NotSensitiveCommand.Execute(null);

        Assert.True(row.IsMuted);
        Assert.True(row.Exclude);
        Assert.False(row.ShowNotSensitive);
        Assert.True(row.ShowUndo);
        Assert.Equal("Marked not sensitive. It stays as is.", row.StatusText);
        Assert.DoesNotContain(model.Included, hit => hit.Value == "ada@example.com");

        row.UndoCommand.Execute(null);

        Assert.False(row.IsMuted);
        Assert.False(row.Exclude);
        Assert.True(row.ShowNotSensitive);
        Assert.False(row.ShowUndo);
        Assert.Equal("", row.StatusText);
        Assert.Contains(model.Included, hit => hit.Value == "ada@example.com");
    }

    [Fact]
    public void Redact_and_copies_skip_a_marked_value()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var copied = "";
        var model = new MainViewModel(state, value => copied = value);
        var text = "ada@example.com and bob@example.com";
        model.Show(Scan(state, text), "chrome", false);
        model.Hits.Single(hit => hit.Value == "ada@example.com").NotSensitiveCommand.Execute(null);

        model.CopyRedactedCommand.Execute(null);

        Assert.Contains("ada@example.com", copied, StringComparison.Ordinal);
        Assert.DoesNotContain("bob@example.com", copied, StringComparison.Ordinal);

        using var files = Temp();
        var fileState = new AppState(files.Root);
        var fileModel = new MainViewModel(fileState, _ => { });
        var path = Path.Combine(files.Root, "notes.txt");
        File.WriteAllText(path, text);
        fileModel.Show(fileState.Engine.Scan(ScanRequest.ForFile(path), fileState.ActiveRules()), null, false);
        fileModel.Hits.Single(hit => hit.Value == "ada@example.com").NotSensitiveCommand.Execute(null);
        fileModel.MakeCopyCommand.Execute(null);
        var permanent = File.ReadAllText(fileModel.LastCopyPath!);
        Assert.Contains("ada@example.com", permanent, StringComparison.Ordinal);
        Assert.DoesNotContain("bob@example.com", permanent, StringComparison.Ordinal);

        fileModel.MakeReversibleCopyCommand.Execute(null);
        var reversible = File.ReadAllText(fileModel.LastCopyPath!);
        Assert.Contains("ada@example.com", reversible, StringComparison.Ordinal);
        Assert.DoesNotContain("bob@example.com", reversible, StringComparison.Ordinal);
    }

    [Fact]
    public void A_remembered_value_is_not_flagged_on_the_next_scan()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var model = new MainViewModel(state, _ => { });
        const string text = "ada@example.com and bob@example.com";
        model.Show(Scan(state, text), "chrome", false);
        model.Hits.Single(hit => hit.Value == "ada@example.com").NotSensitiveCommand.Execute(null);

        Assert.True(state.AllowList.Contains("ada@example.com"));
        var again = state.Engine.Scan(ScanRequest.ForText(text, ScanSource.Clipboard), state.ActiveRules(), state.AllowList.Contains);
        Assert.DoesNotContain(again.Result.Findings, finding => text.Substring(finding.Start, finding.Length) == "ada@example.com");
        Assert.Contains(again.Result.Findings, finding => text.Substring(finding.Start, finding.Length) == "bob@example.com");
    }

    [Fact]
    public void A_locked_policy_excludes_the_value_only_this_time()
    {
        using var dir = Temp();
        var policy = Path.Combine(dir.Root, "policy.json");
        File.WriteAllText(policy, """{"lockIgnore":true}""");
        var state = new AppState(dir.Root, new PolicyBinding(policy, EmptyPolicyRegistry.Instance));
        var model = new MainViewModel(state, _ => { });
        const string text = "ada@example.com";
        model.Show(Scan(state, text), "chrome", false);
        var row = Assert.Single(model.Hits);
        row.NotSensitiveCommand.Execute(null);

        Assert.True(row.Exclude);
        Assert.Equal("Your organization does not allow remembering this.", row.StatusText);
        Assert.False(state.AllowList.Contains("ada@example.com"));
        var again = state.Engine.Scan(ScanRequest.ForText(text, ScanSource.Clipboard), state.ActiveRules(), state.AllowList.Contains);
        Assert.Contains(again.Result.Findings, finding => text.Substring(finding.Start, finding.Length) == "ada@example.com");
    }

    [Fact]
    public void Marking_every_clipboard_finding_closes_the_card_as_leave_as_is()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var session = Scan(state, "ada@example.com and bob@example.com");
        var model = new PasteWarningViewModel(session, "chrome", "ask", remembered: state.AllowList);
        PasteChoice? choice = null;
        model.Decided += picked => choice = picked;

        model.Hits[0].NotSensitiveCommand.Execute(null);
        Assert.Null(choice);
        Assert.Contains("1 finding", model.Summary, StringComparison.Ordinal);

        model.Hits[1].NotSensitiveCommand.Execute(null);
        Assert.Equal(PasteChoice.Ignore, choice);
    }

    [Fact]
    public void A_false_positive_is_written_once_per_value_and_undo_writes_none()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var model = new MainViewModel(state, _ => { });
        model.Show(Scan(state, "ada@example.com and bob@example.com"), "chrome", false);
        var ada = model.Hits.Single(hit => hit.Value == "ada@example.com");
        var bob = model.Hits.Single(hit => hit.Value == "bob@example.com");

        ada.NotSensitiveCommand.Execute(null);
        ada.NotSensitiveCommand.Execute(null);
        ada.UndoCommand.Execute(null);
        ada.NotSensitiveCommand.Execute(null);
        bob.NotSensitiveCommand.Execute(null);

        var events = state.Events.ReadAll().Where(evt => evt.Action == EventActions.FalsePositive).ToList();
        Assert.Equal(2, events.Count);
        Assert.True(state.AllowList.Contains("ada@example.com"));
        Assert.True(state.AllowList.Contains("bob@example.com"));
    }

    [Fact]
    public void The_result_summary_drops_a_marked_finding()
    {
        using var dir = Temp();
        var state = new AppState(dir.Root);
        var model = new MainViewModel(state, _ => { });
        model.Show(Scan(state, "ada@example.com and bob@example.com"), null, false);
        Assert.Equal("2 findings", model.FindingsCountLabel);
        Assert.Contains("2 findings", model.ResultBody, StringComparison.Ordinal);

        model.Hits[0].NotSensitiveCommand.Execute(null);

        Assert.Equal("1 finding", model.FindingsCountLabel);
        Assert.Contains("1 finding", model.ResultBody, StringComparison.Ordinal);
        Assert.DoesNotContain("2 findings", model.ResultBody, StringComparison.Ordinal);
    }

    private static PasteWarningViewModel Card(string root, string text)
    {
        var state = new AppState(root);
        return new PasteWarningViewModel(Scan(state, text), "chrome", "ask", remembered: state.AllowList);
    }

    private static ScanSession Scan(AppState state, string text) =>
        state.Engine.Scan(ScanRequest.ForText(text, ScanSource.Clipboard), state.ActiveRules(), state.AllowList.Contains);

    private static TempDir Temp() => new();

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "peritum-gap8b-ns-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
