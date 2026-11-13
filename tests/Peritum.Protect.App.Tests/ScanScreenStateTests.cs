using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class ScanScreenStateTests
{
    [Fact]
    public void Idle_has_no_result_panel() =>
        Assert.False(ScanScreenState.Idle.ResultVisible);

    [Fact]
    public void Safe_wording_does_not_guarantee_safety()
    {
        var session = new ScanSession
        {
            Request = ScanRequest.ForText("hello", ScanSource.Manual),
            Text = "hello",
            Result = new ScanResult
            {
                Source = ScanSource.Manual,
                DisplayName = "notes.txt",
                Findings = [],
                ContentHash = "test-hash"
            }
        };

        var state = ScanScreenState.FromSession(session);

        Assert.Equal(ScanScreenKind.Safe, state.Kind);
        Assert.Contains("Nothing found by the checks that are on", state.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Safe to use", state.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Error_disables_copy_actions()
    {
        var session = new ScanSession
        {
            Request = ScanRequest.ForFile("broken.docx"),
            Text = "",
            Result = ScanResult.Failed(
                ScanSource.File,
                "broken.docx",
                "Could not read this file. Original was not changed.")
        };

        var state = ScanScreenState.FromSession(session);

        Assert.Equal(ScanScreenKind.Error, state.Kind);
        Assert.False(state.CanMakeCopy);
        Assert.False(state.CanCopyText);
    }

    [Fact]
    public void Copy_created_is_a_success_state()
    {
        var state = ScanScreenState.CopyCreated("Invoice_protect.xlsx");

        Assert.Equal(ScanScreenKind.Success, state.Kind);
        Assert.Contains("Invoice_protect.xlsx", state.Body, StringComparison.Ordinal);
    }
}
