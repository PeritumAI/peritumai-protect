using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.App.Views;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

[Collection("Visual tree")]
public class Gap7bUiTests
{
    [Fact]
    public void The_ocr_note_is_not_under_the_united_states_group()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                var window = Host(root);
                var note = Walk<TextBlock>(window).First(block => block.Text.Contains("Nothing is uploaded", StringComparison.Ordinal));
                var unitedStates = Walk<TextBlock>(window).First(block => block.Text == "United States");
                var noteTop = note.TranslatePoint(new Point(0, 0), window).Y;
                var groupTop = unitedStates.TranslatePoint(new Point(0, 0), window).Y;
                Assert.True(noteTop < groupTop, "OCR note is under United States at " + noteTop + " vs " + groupTop);
                window.Close();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void The_my_terms_buttons_share_one_height_and_one_baseline()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                var window = Host(root, 720);
                var format = Walk<Button>(window).First(button =>
                    AutomationProperties.GetName(button) == "Add a number format" || button.Content as string == "Add a number format");
                var row = (Panel)format.Parent;
                var buttons = row.Children.OfType<Button>().Where(button => button.IsVisible && button.ActualHeight > 0).ToList();
                Assert.True(buttons.Count >= 3);
                var rows = buttons.GroupBy(button => Math.Round(button.TranslatePoint(new Point(0, 0), window).Y / 4) * 4);
                Assert.Contains(rows, group => group.Contains(format));
                foreach (var group in rows)
                {
                    var members = group.ToList();
                    var height = members[0].ActualHeight;
                    var bottom = members[0].TranslatePoint(new Point(0, members[0].ActualHeight), window).Y;
                    foreach (var button in members)
                    {
                        Assert.Equal(height, button.ActualHeight);
                        var buttonBottom = button.TranslatePoint(new Point(0, button.ActualHeight), window).Y;
                        Assert.Equal(bottom, buttonBottom, 1);
                    }
                }
                window.Close();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void The_clipboard_card_masks_the_value_until_show()
    {
        StaApp.Invoke(() =>
        {
            var window = new PasteWarningWindow(Warning());
            window.Show();
            window.UpdateLayout();
            var texts = Walk<TextBlock>(window).Select(block => block.Text).Where(text => !string.IsNullOrEmpty(text)).ToList();
            Assert.Contains(texts, text => text.Contains("a***@example.com", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, text => text.Contains("ada@example.com", StringComparison.Ordinal));
            var show = Walk<Button>(window).First(button => button.Content as string == "Show");
            Assert.True(show.ActualWidth > 20);
            var notSensitive = Walk<Button>(window).First(button => button.Content as string == "Not sensitive");
            var edge = notSensitive.TranslatePoint(new Point(notSensitive.ActualWidth, 0), window).X;
            Assert.True(edge <= window.ActualWidth - 8, "Not sensitive is clipped at " + edge);
            window.Close();
        });
    }

    private static Window Host(string root, double width = 760)
    {
        var model = new SettingsViewModel(new AppState(root), () => { });
        var view = new SettingsView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = 900, WindowStyle = WindowStyle.None };
        window.Show();
        window.UpdateLayout();
        view.UpdateLayout();
        return window;
    }

    private static PasteWarningViewModel Warning() => new(
        new ScanSession
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
                        DisplayLabel = "email",
                        Placeholder = "EMAIL",
                        Start = 0,
                        Length = 15,
                        Snippet = "ada@example.com"
                    }
                ],
                ContentHash = "test-hash"
            }
        },
        "chrome",
        "ask");

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-gap7b-ui-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static IEnumerable<T> Walk<T>(DependencyObject node) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Walk<T>(child))
                yield return nested;
        }
    }
}
