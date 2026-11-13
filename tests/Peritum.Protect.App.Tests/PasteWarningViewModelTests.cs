using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

public class PasteWarningViewModelTests
{
    [Theory]
    [InlineData("ignore", true, false, false)]
    [InlineData("stop", false, true, false)]
    [InlineData("redact", false, false, true)]
    [InlineData("ask", false, false, false)]
    public void Configures_the_named_default_button(
        string action,
        bool ignore,
        bool stop,
        bool redact)
    {
        var model = new PasteWarningViewModel(Session(), "chrome", action);

        Assert.Equal(ignore, model.IgnoreIsDefault);
        Assert.Equal(stop, model.StopIsDefault);
        Assert.Equal(redact, model.RedactIsDefault);
    }

    [Fact]
    public void Not_sensitive_does_not_ask_for_a_selection()
    {
        var model = new PasteWarningViewModel(Session(), "chrome", "ask");

        model.Hits[0].NotSensitiveCommand.Execute(null);

        Assert.Equal("", model.Message);
        Assert.True(model.Hits[0].Dismissed);
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
