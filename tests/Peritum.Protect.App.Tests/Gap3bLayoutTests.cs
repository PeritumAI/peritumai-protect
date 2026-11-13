using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

[Collection("Visual tree")]
public class Gap3bLayoutTests
{
    [Fact]
    public void Expanded_findings_get_half_the_card_and_header_actions_share_a_baseline()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                var path = Path.Combine(root, "note.txt");
                File.WriteAllText(path, "please send the invoice to ada@example.com before Friday.");
                using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
                var window = new MainWindow(new AppState(root), clipboard) { Width = 980, Height = 680 };
                window.Show();
                window.ViewModel.Show(Session(path), null, logNow: false);
                window.ViewModel.Categories.Single(row => row.Id == "personal").ToggleCommand.Execute(null);
                window.UpdateLayout();
                Pump();

                var card = (FrameworkElement)window.FindName("ResultCard")!;
                var show = Find<Button>(window, button => button.IsVisible && button.Content as string == "Show");
                var list = Ancestor<ListBox>(show);
                Assert.True(list.ActualHeight >= card.ActualHeight / 2 - 1, "findings " + list.ActualHeight + " card " + card.ActualHeight);
                var buttonRect = show.TransformToAncestor(list).TransformBounds(new Rect(0, 0, show.ActualWidth, show.ActualHeight));
                Assert.True(buttonRect.Bottom <= list.ActualHeight + 1, "Show is clipped at " + buttonRect.Bottom);

                var open = Find<Button>(window, button => button.IsVisible && button.Content as string == "Open file");
                var folder = Find<Button>(window, button => button.IsVisible && button.Content as string == "Show in folder");
                Assert.Equal(open.ActualHeight, folder.ActualHeight);
                var openY = open.TransformToAncestor(card).Transform(new Point(0, open.ActualHeight)).Y;
                var folderY = folder.TransformToAncestor(card).Transform(new Point(0, folder.ActualHeight)).Y;
                Assert.True(Math.Abs(openY - folderY) < 1, "baselines " + openY + " and " + folderY);

                Assert.DoesNotContain(Walk<CheckBox>(window), box => box.IsVisible && box.Content as string == "Accept tracked changes");
                Assert.DoesNotContain(Walk<CheckBox>(window), box => box.IsVisible && box.Content as string == "Remove speaker notes");
                window.PrepareExit();
                window.Close();
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void Twenty_findings_keep_action_buttons_inside_the_window()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                var path = Path.Combine(root, "note.txt");
                File.WriteAllText(path, "ada@example.com");
                using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
                var window = new MainWindow(new AppState(root), clipboard) { Width = 980, Height = 680 };
                window.Show();
                window.ViewModel.Show(ManyFindings(path, 20), null, logNow: false);
                window.ViewModel.Categories.Single(row => row.Id == "personal").ToggleCommand.Execute(null);
                window.UpdateLayout();
                Pump();

                var windowBox = new Rect(0, 0, window.ActualWidth, window.ActualHeight);
                foreach (var label in new[] { "Make protected copy", "Make reversible copy", "Clean for sharing", "Close" })
                {
                    var button = Find<Button>(window, item => item.IsVisible && item.Content as string == label);
                    var rect = BoundsIn(button, window);
                    Assert.True(windowBox.Contains(rect), label + " " + rect + " window " + windowBox);
                }

                var list = (FrameworkElement)window.FindName("CategoryFindingsList")!;
                var listBox = BoundsIn(list, window);
                var findingButtons = Walk<Button>(list).Where(button =>
                    button.IsVisible && button.Content is string label &&
                    label is "Show" or "Not sensitive" or "Copy location").ToList();
                Assert.NotEmpty(findingButtons);
                foreach (var button in findingButtons)
                {
                    var rect = BoundsIn(button, window);
                    var seen = Rect.Intersect(rect, listBox);
                    if (seen.IsEmpty || seen.Width < 1 || seen.Height < 1)
                        continue;
                    Assert.True(listBox.Contains(rect), "clipped " + button.Content + " " + rect + " list " + listBox);
                    Assert.True(windowBox.Contains(rect), "outside " + button.Content + " " + rect);
                }

                window.PrepareExit();
                window.Close();
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void Clean_options_follow_the_file_type_and_stay_out_of_the_action_bar()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
                var window = new MainWindow(new AppState(root), clipboard) { Width = 980, Height = 680 };
                window.Show();
                var docx = Path.Combine(root, "brief.docx");
                File.WriteAllText(docx, "ada@example.com");
                window.ViewModel.Show(Session(docx), null, logNow: false);
                window.UpdateLayout();
                Pump();
                var expander = Find<Expander>(window, item => item.Header as string == "Clean for sharing options");
                Assert.Equal(Visibility.Visible, expander.Visibility);
                var tracked = Find<CheckBox>(expander, box => box.Content as string == "Accept tracked changes");
                var notes = Find<CheckBox>(expander, box => box.Content as string == "Remove speaker notes");
                Assert.Equal(Visibility.Visible, tracked.Visibility);
                Assert.Equal(Visibility.Collapsed, notes.Visibility);
                Assert.IsNotType<WrapPanel>(Ancestor<Panel>(expander));

                var pptx = Path.Combine(root, "deck.pptx");
                File.WriteAllText(pptx, "ada@example.com");
                window.ViewModel.Show(Session(pptx), null, logNow: false);
                window.UpdateLayout();
                Pump();
                Assert.Equal(Visibility.Collapsed, Find<CheckBox>(expander, box => box.Content as string == "Accept tracked changes").Visibility);
                Assert.Equal(Visibility.Visible, Find<CheckBox>(expander, box => box.Content as string == "Remove speaker notes").Visibility);
                window.PrepareExit();
                window.Close();
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void Five_activity_rows_fit_and_batch_columns_line_up()
    {
        StaApp.Invoke(() =>
        {
            var root = Temp();
            try
            {
                using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
                var window = new MainWindow(new AppState(root), clipboard) { Width = 980, Height = 680 };
                window.Show();
                for (var i = 0; i < 5; i++)
                {
                    var path = Path.Combine(root, "note" + i + ".txt");
                    File.WriteAllText(path, "ada@example.com");
                    window.ViewModel.Show(Session(path), null, logNow: true);
                }

                window.ViewModel.CurrentPage = AppPage.Activity;
                window.UpdateLayout();
                Pump();
                var activity = (FrameworkElement)window.FindName("ActivityPage")!;
                var list = Find<ListBox>(activity, item => item.IsVisible);
                var fifth = list.ItemContainerGenerator.ContainerFromIndex(4) as FrameworkElement;
                Assert.NotNull(fifth);
                var viewport = Walk<ScrollViewer>(list).OrderByDescending(item => item.ViewportHeight).First();
                Assert.True(viewport.VerticalOffset == 0 && viewport.ViewportHeight >= 5, "visible rows " + viewport.ViewportHeight);

                var batchPath = Path.Combine(root, "batch.txt");
                File.WriteAllText(batchPath, "ada@example.com");
                window.ViewModel.CurrentPage = AppPage.Check;
                window.ViewModel.ShowBatch(
                [
                    new BatchRow { FullPath = batchPath, Name = "batch.txt", Type = "Text", Status = "1 finding", HasSession = true }
                ], "1 file");
                window.UpdateLayout();
                Pump();
                var typeHeader = Find<TextBlock>(window, block => block.IsVisible && block.Text == "Type" && block.FontWeight == FontWeights.SemiBold);
                var typeCell = Find<TextBlock>(window, block => block.IsVisible && block.Text == "Text");
                var statusHeader = Find<TextBlock>(window, block => block.IsVisible && block.Text == "Status" && block.FontWeight == FontWeights.SemiBold);
                var statusCell = Find<TextBlock>(window, block => block.IsVisible && block.Text == "1 finding");
                var headerX = typeHeader.TransformToAncestor(window).Transform(new Point(0, 0)).X;
                var cellX = typeCell.TransformToAncestor(window).Transform(new Point(0, 0)).X;
                Assert.True(Math.Abs(headerX - cellX) <= 2, "Type header " + headerX + " cell " + cellX);
                headerX = statusHeader.TransformToAncestor(window).Transform(new Point(0, 0)).X;
                cellX = statusCell.TransformToAncestor(window).Transform(new Point(0, 0)).X;
                Assert.True(Math.Abs(headerX - cellX) <= 2, "Status header " + headerX + " cell " + cellX);
                window.PrepareExit();
                window.Close();
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private static ScanSession ManyFindings(string path, int count)
    {
        var findings = new List<Finding>(count);
        for (var i = 0; i < count; i++)
        {
            findings.Add(new Finding
            {
                RuleId = "pii.email",
                Label = "Email",
                DisplayLabel = "email",
                Placeholder = "EMAIL",
                Severity = Severity.Warn,
                Start = 0,
                Length = 1,
                Snippet = "a***@example.com",
                Category = "personal",
                Location = "line " + (i + 1) + ", column 1",
                Context = "…a***…"
            });
        }

        return new ScanSession
        {
            Request = ScanRequest.ForFile(path),
            Text = "ada@example.com",
            Result = new ScanResult
            {
                Findings = findings,
                ContentHash = "abc",
                Source = ScanSource.File,
                DisplayName = Path.GetFileName(path)
            }
        };
    }

    private static Rect BoundsIn(FrameworkElement element, Visual ancestor)
    {
        return element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    }

    private static ScanSession Session(string path)
    {
        const string text = "please send the invoice to ada@example.com before Friday.";
        var at = text.IndexOf("ada@example.com", StringComparison.Ordinal);
        return new ScanSession
        {
            Request = ScanRequest.ForFile(path),
            Text = text,
            Result = new ScanResult
            {
                Findings =
                [
                    new Finding
                    {
                        RuleId = "pii.email",
                        Label = "Email",
                        DisplayLabel = "email",
                        Placeholder = "EMAIL",
                        Severity = Severity.Warn,
                        Start = at,
                        Length = "ada@example.com".Length,
                        Snippet = "a***@example.com",
                        Category = "personal",
                        Location = "line 1, column 1",
                        Context = "…ada@…"
                    }
                ],
                ContentHash = "abc",
                Source = ScanSource.File,
                DisplayName = Path.GetFileName(path)
            }
        };
    }

    private static T Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        var found = Walk<T>(root).FirstOrDefault(match);
        Assert.NotNull(found);
        return found;
    }

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

    private static T Ancestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null && current is not T)
            current = VisualTreeHelper.GetParent(current);
        var found = current as T;
        Assert.NotNull(found);
        return found;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
