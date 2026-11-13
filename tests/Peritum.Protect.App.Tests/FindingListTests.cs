using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

[Collection("Visual tree")]
public class FindingListTests
{
    [Fact]
    public void Category_expands_and_show_reveals_for_ten_seconds_without_logging_the_value()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-findings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var state = new AppState(root);
            var copied = new List<string>();
            var model = new MainViewModel(state, copied.Add);
            Action? hide = null;
            var delay = TimeSpan.Zero;
            model.RevealScheduler = (action, scheduled) =>
            {
                hide = action;
                delay = scheduled;
            };
            var session = Sample();
            var before = state.Events.ReadAll().Count;
            model.Show(session, null, logNow: true);
            Assert.Equal("2 findings in this file (1 email, 1 OpenAI-style key)", model.ResultBody);
            model.ShowListedFindingsCommand.Execute(null);
            Assert.True(model.ShowAllFindings);

            var personal = model.Categories.Single(row => row.Id == "personal");
            Assert.False(personal.IsExpanded);
            personal.ToggleCommand.Execute(null);
            Assert.True(personal.IsExpanded);
            personal.ToggleCommand.Execute(null);
            Assert.False(personal.IsExpanded);
            personal.ToggleCommand.Execute(null);

            var email = personal.Findings.Single();
            Assert.Equal("email", email.TypeLabel);
            Assert.Equal("a***@example.com", email.ShownValue);
            Assert.Equal("line 1, column 1", email.Location);
            Assert.Equal("…please send the invoice to a***@example.com before Friday…", email.Context);
            Assert.Equal("Finding 1 of 2", email.IndexLine);
            Assert.Equal("email, line 1, column 1", email.AccessibleName);
            Assert.DoesNotContain("ada@example.com", email.Context, StringComparison.Ordinal);

            email.ShowCommand.Execute(null);
            Assert.Equal("ada@example.com", email.ShownValue);
            Assert.Equal(TimeSpan.FromSeconds(10), delay);
            hide!.Invoke();
            Assert.Equal("a***@example.com", email.ShownValue);

            email.CopyLocationCommand.Execute(null);
            Assert.Equal("line 1, column 1", copied.Single());

            model.ShowAllFindings = true;
            Assert.Equal("Finding 1 of 2", model.AllFindings[0].IndexLine);
            Assert.Equal("Finding 2 of 2", model.AllFindings[1].IndexLine);
            Assert.Equal("secrets", ResultGroups.CategoryOf(model.AllFindings[1].Finding));
            Assert.Equal("2 findings", model.FindingsCountLabel);

            Assert.Equal(before + 1, state.Events.ReadAll().Count);
            var written = new StringBuilder();
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                written.Append(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            Assert.DoesNotContain("ada@example.com", written.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("sk-testFAKEkey1234567890", written.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(email.Context, written.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Preview_masks_values_and_next_previous_move_through_findings()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var model = new MainViewModel(new AppState(root), _ => { });
            model.RevealScheduler = (_, _) => { };
            model.Show(Sample(), null, logNow: false);
            model.NextFindingCommand.Execute(null);

            Assert.True(model.PreviewVisible);
            Assert.Equal("line 1, column 1", model.CurrentLocation);
            Assert.Equal("Finding 1 of 2", model.CurrentIndexLine);
            var shown = string.Concat(model.PreviewLines.SelectMany(line => line.Spans).Select(span => span.Shown));
            Assert.Contains("a***@example.com", shown, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", shown, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-testFAKEkey1234567890", shown, StringComparison.Ordinal);
            Assert.Contains(model.PreviewLines, line => line.IsCurrent);

            var email = model.SelectedFinding!;
            email.ShowCommand.Execute(null);
            shown = string.Concat(model.PreviewLines.SelectMany(line => line.Spans).Select(span => span.Shown));
            Assert.Contains("ada@example.com", shown, StringComparison.Ordinal);

            model.NextFindingCommand.Execute(null);
            Assert.Equal("line 1, column 52", model.CurrentLocation);
            Assert.Equal("Finding 2 of 2", model.CurrentIndexLine);
            model.PreviousFindingCommand.Execute(null);
            Assert.Equal("line 1, column 1", model.CurrentLocation);

            model.SelectedFinding = model.AllFindings[1];
            Assert.Equal("line 1, column 52", model.CurrentLocation);
            Assert.True(model.PreviewVisible);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Image_preview_draws_a_box_over_the_found_text()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-preview-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "card.png");
            File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47]);
            var session = new ScanSession
            {
                Request = ScanRequest.ForFile(path),
                Text = "ada@example.com",
                ImageWidth = 80,
                ImageHeight = 40,
                Boxes = [new Peritum.Protect.Core.Files.OcrBox { Start = 0, Length = 15, X = 4, Y = 4, Width = 40, Height = 16 }],
                Result = new ScanResult
                {
                    Findings =
                    [
                        Place("pii.email", "Email", "email", "emails", "personal", 0, "ada@example.com", "EMAIL", Severity.Warn, "text in image, top left", "a***@example.com")
                    ],
                    ContentHash = "abc",
                    Source = ScanSource.File,
                    DisplayName = "card.png"
                }
            };
            var model = new MainViewModel(new AppState(root), _ => { });
            model.Show(session, null, logNow: false);
            Assert.True(model.PreviewIsImage);
            Assert.Equal(path, model.PreviewImagePath);
            var box = Assert.Single(model.PreviewBoxes);
            Assert.Equal(4, box.X);
            Assert.Equal(4, box.Y);
            Assert.Equal(40, box.Width);
            Assert.Equal(16, box.Height);
            Assert.Contains("a***@example.com", box.Caption, StringComparison.Ordinal);
            Assert.DoesNotContain("ada@example.com", box.Caption, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Five_thousand_findings_stay_virtualized()
    {
        Exception? error = null;
        try
        {
            StaApp.Invoke(CheckVirtualized);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error is not null)
            throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static void CheckVirtualized()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-virtual-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
            var window = new MainWindow(new AppState(root), clipboard)
            {
                Width = 980,
                Height = 680,
                Left = 20,
                Top = 20
            };
            window.Show();
            window.ViewModel.Show(Many(5000), null, logNow: false);
            window.ViewModel.ShowAllFindings = true;
            window.UpdateLayout();
            Pump();
            var list = (ListBox)window.FindName("AllFindingsList")!;
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            var realized = 0;
            for (var i = 0; i < list.Items.Count; i++)
            {
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is not null)
                    realized++;
            }

            Assert.True(realized > 0, "no row was realized");
            Assert.True(realized < 400, "realized " + realized + " of 5000");
            window.PrepareExit();
            window.Close();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static ScanSession Sample()
    {
        const string text = "please send the invoice to ada@example.com before Friday. key sk-testFAKEkey1234567890 end";
        var emailAt = text.IndexOf("ada@example.com", StringComparison.Ordinal);
        var keyAt = text.IndexOf("sk-testFAKEkey1234567890", StringComparison.Ordinal);
        return new ScanSession
        {
            Request = ScanRequest.ForText(text, ScanSource.File),
            Text = text,
            Result = new ScanResult
            {
                Findings =
                [
                    Place("pii.email", "Email", "email", "emails", "personal", emailAt, "ada@example.com", "EMAIL", Severity.Warn, "line 1, column 1", "…please send the invoice to a***@example.com before Friday…"),
                    Place("secret.openai", "OpenAI-style key", "OpenAI-style key", "OpenAI-style keys", "secrets", keyAt, "sk-testFAKEkey1234567890", "API_KEY", Severity.High, "line 1, column 52", "…key sk-…90 end")
                ],
                ContentHash = "abc",
                Source = ScanSource.File,
                DisplayName = "note.txt"
            }
        };
    }

    private static ScanSession Many(int count)
    {
        var findings = new List<Finding>(count);
        for (var i = 0; i < count; i++)
        {
            findings.Add(Place(
                "pii.email",
                "Email",
                "email",
                "emails",
                "personal",
                i,
                "user@example.com",
                "EMAIL",
                Severity.Warn,
                "line " + (i + 1) + ", column 1",
                "a***@example.com"));
        }

        return new ScanSession
        {
            Request = ScanRequest.ForText("user@example.com", ScanSource.File),
            Text = "",
            Result = new ScanResult
            {
                Findings = findings,
                ContentHash = "abc",
                Source = ScanSource.File,
                DisplayName = "many.txt"
            }
        };
    }

    private static Finding Place(
        string id,
        string label,
        string display,
        string plural,
        string category,
        int start,
        string value,
        string placeholder,
        Severity severity,
        string location,
        string context) => new()
    {
        RuleId = id,
        Label = label,
        DisplayLabel = display,
        PluralLabel = plural,
        Category = category,
        Severity = severity,
        Start = start,
        Length = value.Length,
        Placeholder = placeholder,
        Snippet = value,
        Location = location,
        Context = context
    };
}
