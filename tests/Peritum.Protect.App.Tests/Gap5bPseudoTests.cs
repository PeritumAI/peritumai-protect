using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

[Collection("Visual tree")]
public class Gap5bPseudoTests
{
    [Fact]
    public void Main_views_in_pseudo_localization_have_no_plain_english()
    {
        StaApp.Invoke(() =>
        {
            var previous = TextCatalog.Language;
            var root = Temp();
            try
            {
                TextCatalog.Use("qps-ploc");
                var path = Path.Combine(root, "note.txt");
                File.WriteAllText(path, "please send the invoice to ada@example.com before Friday.\nThe test key is sk-testFAKEkey1234567890.");
                var rules = RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);
                var session = new ProtectService().Scan(ScanRequest.ForFile(path), rules, null);
                using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
                var window = new MainWindow(new AppState(root), clipboard) { Width = 980, Height = 680 };
                window.Show();
                window.ViewModel.Show(session, null, logNow: false);
                window.ViewModel.ShowAllFindings = true;
                window.UpdateLayout();
                Pump();
                var problems = new List<string>();
                Collect(window, problems, path);

                window.ViewModel.ShowAllFindings = false;
                window.ViewModel.Categories.Single(row => row.Id == "personal").ToggleCommand.Execute(null);
                window.UpdateLayout();
                Pump();
                Collect(window, problems, path);
                AssertCategoryListScrollsAboveTheActionBar(window);

                window.ViewModel.CurrentPage = AppPage.Activity;
                window.UpdateLayout();
                Pump();
                Collect(window, problems, path);

                window.ViewModel.CurrentPage = AppPage.Restore;
                window.UpdateLayout();
                Pump();
                Collect(window, problems, path);

                window.ViewModel.CurrentPage = AppPage.Settings;
                window.UpdateLayout();
                Pump();
                var settingsScroll = Walk<ScrollViewer>(window).First(scroll => scroll.IsVisible && scroll.ScrollableHeight > 0);
                foreach (var fraction in new[] { 0d, 0.35, 0.7, 1d })
                {
                    settingsScroll.ScrollToVerticalOffset(settingsScroll.ScrollableHeight * fraction);
                    window.UpdateLayout();
                    Pump();
                    Collect(window, problems, path);
                }

                window.ViewModel.CurrentPage = AppPage.Help;
                window.UpdateLayout();
                Pump();
                Collect(window, problems, path);

                window.ViewModel.CurrentPage = AppPage.Check;
                File.WriteAllText(Path.Combine(root, "old.rtf"), "legacy");
                var plan = BatchScan.Collect([root]);
                var summary = BatchScan.Run(plan.Files, new ProtectService(), rules, null, CancellationToken.None, null);
                window.ViewModel.ShowBatch(plan.Files.Select(file => new BatchRow
                {
                    FullPath = file.Path,
                    Name = file.Name,
                    Type = file.Type,
                    Status = file.Status,
                    StatusCode = file.StatusCode,
                    Detail = file.Detail,
                    HasSession = file.Session is not null
                }), summary.Message);
                window.UpdateLayout();
                Pump();
                Collect(window, problems, path);

                var warning = new Views.PasteWarningWindow(new PasteWarningViewModel(session, "notepad.exe", "ask"));
                warning.Show();
                warning.UpdateLayout();
                Pump();
                Collect(warning, problems, path);
                warning.Close();

                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
                window.PrepareExit();
                window.Close();
            }
            finally
            {
                TextCatalog.Use(previous);
                Directory.Delete(root, true);
            }
        });
    }

    private static void AssertCategoryListScrollsAboveTheActionBar(MainWindow window)
    {
        window.ViewModel.ShowAllFindings = false;
        if (window.ViewModel.Categories.All(row => !row.IsExpanded))
            window.ViewModel.Categories.First(row => row.HasFindings).ToggleCommand.Execute(null);
        foreach (var row in window.ViewModel.Categories)
        {
            if (row.IsExpanded)
                row.ToggleCommand.Execute(null);
        }

        window.UpdateLayout();
        Pump();
        var bar = (FrameworkElement)window.FindName("ResultActionBar")!;
        var scroll = (ScrollViewer)window.FindName("CategoryScroll")!;
        Assert.True(scroll.IsVisible);
        Assert.True(scroll.ScrollableHeight > 0, "the category list does not scroll");
        var barTop = bar.TranslatePoint(new Point(0, 0), window).Y;
        var scrollBottom = scroll.TranslatePoint(new Point(0, scroll.ActualHeight), window).Y;
        Assert.True(scrollBottom <= barTop + 1, "category list ends at " + scrollBottom + " and the action bar starts at " + barTop);

        scroll.ScrollToEnd();
        window.UpdateLayout();
        Pump();
        foreach (var block in Walk<TextBlock>(scroll))
        {
            if (!block.IsVisible || block.ActualHeight <= 0)
                continue;
            var text = Bounds(block, window);
            var action = Bounds(bar, window);
            text.Intersect(action);
            Assert.True(text.IsEmpty, "\"" + block.Text + "\" overlaps the action bar");
        }
    }

    private static void Collect(DependencyObject root, List<string> problems, string sourcePath)
    {
        var fileText = File.ReadAllText(sourcePath);
        foreach (var block in Walk<TextBlock>(root))
        {
            if (block.IsVisible)
                Check(block.Text, problems, fileText, sourcePath);
        }

        foreach (var button in Walk<Button>(root))
        {
            if (button.IsVisible && button.Content is string content)
                Check(content, problems, fileText, sourcePath);
        }

        foreach (var box in Walk<CheckBox>(root))
        {
            if (box.IsVisible && box.Content is string content)
                Check(content, problems, fileText, sourcePath);
        }

        foreach (var item in Walk<ComboBoxItem>(root))
        {
            if (item.IsVisible && item.Content is string content)
                Check(content, problems, fileText, sourcePath);
        }
    }

    private static void Check(string? text, List<string> problems, string fileText, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetter))
            return;
        if (text.StartsWith('[') && text.Contains('~'))
            return;
        if (text.Contains('*') || text.Contains('…') || text.Contains('•'))
            return;
        var name = Path.GetFileName(sourcePath);
        if (text.Contains(name, StringComparison.OrdinalIgnoreCase) || fileText.Contains(text, StringComparison.Ordinal))
            return;
        if (text.Equals("PeritumAI Protect", StringComparison.Ordinal) || text.Equals("Peritum", StringComparison.Ordinal))
            return;
        if (text.Contains("peritumai.com", StringComparison.OrdinalIgnoreCase))
            return;
        if (text.Contains('.') && !text.Contains(' '))
            return;
        if (text.Length <= 4 && text.All(char.IsUpper))
            return;
        problems.Add(text);
    }

    private static Rect Bounds(FrameworkElement element, Visual relative) =>
        element.TransformToAncestor(relative).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static IEnumerable<T> Walk<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Walk<T>(child))
                yield return nested;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-pseudo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
