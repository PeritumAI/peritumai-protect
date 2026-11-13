using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.Themes;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.App.Views;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.Tests;

[Collection("Visual tree")]
public class VisualContrastTests
{
    [Fact]
    public void Visible_text_meets_wcag_aa_in_light_dark_and_high_contrast()
    {
        var failures = new List<string>();
        Exception? error = null;
        try
        {
            StaApp.Invoke(() => failures.AddRange(CheckThemes()));
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (error is not null)
            throw new Xunit.Sdk.XunitException(error.ToString());
        if (failures.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join(Environment.NewLine, failures));
    }

    private static List<string> CheckThemes()
    {
        var theme = new ThemeService();
        var failures = new List<string>();
        using var clipboard = new ClipboardWatcher(Dispatcher.CurrentDispatcher);
        var state = new AppState();

        theme.Apply("light", false);
        var live = CreateWindow(state, clipboard);
        var lightBg = WindowColor(live);
        theme.Apply("dark", false);
        Pump();
        var darkBg = WindowColor(live);
        if (lightBg == darkBg)
            failures.Add("Dark theme did not restyle the open window (background stayed #" + lightBg + ")");
        live.PrepareExit();
        live.Close();

        foreach (var (name, choice, highContrast) in new (string, string, bool)[]
        {
            ("Light", "light", false),
            ("Dark", "dark", false),
            ("High contrast", "light", true)
        })
        {
            theme.Apply(choice, highContrast);
            var window = CreateWindow(state, clipboard);
            try
            {
                failures.AddRange(CheckPages(window, name));
            }
            finally
            {
                window.PrepareExit();
                window.Close();
            }
        }

        theme.Dispose();
        return failures;
    }

    private static MainWindow CreateWindow(AppState state, ClipboardWatcher clipboard)
    {
        var window = new MainWindow(state, clipboard)
        {
            Width = 980,
            Height = 680,
            Left = 40,
            Top = 40
        };
        window.Show();
        Pump();
        return window;
    }

    private static string WindowColor(Window window)
    {
        if (window.Background is SolidColorBrush solid)
            return Hex(Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B));
        return "none";
    }

    private static List<string> CheckPages(MainWindow window, string theme)
    {
        var failures = new List<string>();
        window.ShowHome();
        window.ViewModel.DismissCommand.Execute(null);
        Pump();
        failures.AddRange(Walk(window, theme + " / Check idle"));

        window.ViewModel.Show(SampleSession(), "chrome", logNow: false);
        Pump();
        failures.AddRange(Walk(window, theme + " / Check result"));

        window.ViewModel.Events.Clear();
        window.ViewModel.Events.Add(new EventRow
        {
            When = "2026-09-29 09:00",
            Source = "clipboard",
            Action = "warn",
            Summary = "Email",
            Search = "clipboard warn email"
        });
        Click(window, "ActivityNav");
        Pump();
        failures.AddRange(Walk(window, theme + " / Activity"));

        Click(window, "RestoreNav");
        Pump();
        failures.AddRange(Walk(window, theme + " / Restore"));

        Click(window, "SettingsNav");
        Pump();
        OpenCombos(window);
        failures.AddRange(Walk(window, theme + " / Settings"));
        CloseCombos(window);

        Click(window, "HelpNav");
        Pump();
        failures.AddRange(Walk(window, theme + " / Help"));

        var warning = new PasteWarningWindow(new PasteWarningViewModel(SampleSession(), "chrome", "redact"));
        warning.Show();
        Pump();
        failures.AddRange(Walk(warning, theme + " / Clipboard"));
        warning.Close();

        var first = new FirstRunView();
        first.Show();
        Pump();
        failures.AddRange(Walk(first, theme + " / First run"));
        first.Close();

        if (failures.Count == 0 && !Counted(window))
            failures.Add(theme + ": visual tree had no text to check");
        return failures;
    }

    private static bool Counted(DependencyObject root)
    {
        var count = 0;
        Visit(root, _ => count++);
        return count > 0;
    }

    private static void Visit(DependencyObject node, Action<DependencyObject> hit)
    {
        if (node is UIElement element && !element.IsVisible)
            return;
        if (node is TextBlock or TextBox or ComboBox)
            hit(node);
        var children = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
            Visit(VisualTreeHelper.GetChild(node, i), hit);
    }

    private static List<string> Walk(DependencyObject root, string page)
    {
        var failures = new List<string>();
        var seen = 0;
        Visit(root, node =>
        {
            foreach (var sample in Samples(node))
            {
                seen++;
                var failure = Measure(page, sample);
                if (failure is not null)
                    failures.Add(failure);
            }
        });
        if (seen == 0)
            failures.Add(page + ": no visible text");
        return failures;
    }

    private static IEnumerable<TextSample> Samples(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock block:
                foreach (var link in block.Inlines.OfType<System.Windows.Documents.Hyperlink>())
                {
                    var linkText = string.Concat(link.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));
                    if (!string.IsNullOrWhiteSpace(linkText))
                        yield return new TextSample("Hyperlink", linkText, link.Foreground, block.FontSize, block.FontWeight, block);
                }
                var text = block.Text;
                if (string.IsNullOrWhiteSpace(text))
                    text = string.Concat(block.Inlines.OfType<System.Windows.Documents.Run>().Select(run => run.Text));
                if (string.IsNullOrWhiteSpace(text))
                    yield break;
                yield return new TextSample(Kind(block), text, block.Foreground, block.FontSize, block.FontWeight, block);
                break;
            case TextBox box:
                yield return new TextSample("TextBox", string.IsNullOrEmpty(box.Text) ? "(empty)" : box.Text, box.Foreground, box.FontSize, box.FontWeight, box);
                break;
            case ComboBox combo:
                var label = combo.Text;
                if (string.IsNullOrWhiteSpace(label) && combo.SelectedItem is not null)
                    label = combo.SelectedItem.ToString() ?? "";
                yield return new TextSample("ComboBox", string.IsNullOrWhiteSpace(label) ? "(combo)" : label, combo.Foreground, combo.FontSize, combo.FontWeight, combo);
                break;
        }
    }

    private static string? Measure(string page, TextSample sample)
    {
        if (sample.Element is FrameworkElement box && (box.ActualWidth < 1 || box.ActualHeight < 1))
            return null;
        var foreground = BrushColor(sample.Foreground);
        if (foreground is null)
            return Line(page, sample, "n/a", "n/a", 0) + " foreground is not a solid color";
        var background = OpaqueBackground(sample.Element);
        var opacity = OpacityUntil(sample.Element, background.Owner);
        var effective = Blend(foreground.Value, background.Color, (foreground.Value.A / 255d) * opacity);
        var ratio = Contrast.Ratio(Hex(effective), Hex(background.Color));
        var large = sample.Size >= 24 || (sample.Size >= 18.66 && sample.Weight >= FontWeights.Bold);
        var minimum = large ? 3.0 : 4.5;
        if (ratio + 0.01 >= minimum)
            return null;
        return Line(page, sample, Hex(effective), Hex(background.Color), ratio);
    }

    private static string Line(string page, TextSample sample, string foreground, string background, double ratio)
    {
        var text = sample.Text.Replace("\r", " ").Replace("\n", " ").Trim();
        if (text.Length > 30)
            text = text[..30];
        return page + " | " + sample.Kind + " | \"" + text + "\" | fg #" + foreground + " | bg #" + background + " | " + ratio.ToString("0.00");
    }

    private static string Kind(DependencyObject node)
    {
        if (Ancestor<ComboBox>(node) is not null)
            return "ComboBox";
        if (Ancestor<CheckBox>(node) is not null)
            return "CheckBox";
        if (Ancestor<Button>(node) is not null)
            return "Button";
        return "TextBlock";
    }

    private static T? Ancestor<T>(DependencyObject node) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(node);
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static Color? BrushColor(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : null;

    private static (Color Color, DependencyObject? Owner) OpaqueBackground(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            var brush = current switch
            {
                Control control => control.Background,
                Border border => border.Background,
                Panel panel => panel.Background,
                TextBlock block => block.Background,
                _ => null
            };
            if (brush is SolidColorBrush solid && solid.Color.A >= 250)
                return (Color.FromRgb(solid.Color.R, solid.Color.G, solid.Color.B), current);
            current = VisualTreeHelper.GetParent(current);
        }
        return (Colors.White, null);
    }

    private static double OpacityUntil(DependencyObject start, DependencyObject? stop)
    {
        double opacity = 1;
        DependencyObject? current = start;
        while (current is not null && !ReferenceEquals(current, stop))
        {
            if (current is UIElement element)
                opacity *= element.Opacity;
            current = VisualTreeHelper.GetParent(current);
        }
        if (stop is UIElement owner)
            opacity *= owner.Opacity;
        return opacity;
    }

    private static Color Blend(Color foreground, Color background, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        byte Channel(byte front, byte back) => (byte)Math.Round((front * alpha) + (back * (1 - alpha)));
        return Color.FromRgb(Channel(foreground.R, background.R), Channel(foreground.G, background.G), Channel(foreground.B, background.B));
    }

    private static string Hex(Color color) => color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");

    private static void Click(FrameworkElement root, string name)
    {
        if (root.FindName(name) is not Button button)
            throw new InvalidOperationException("Missing " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void OpenCombos(DependencyObject root)
    {
        Visit(root, node =>
        {
            if (node is ComboBox combo && combo.IsVisible && combo.Items.Count > 0)
                combo.IsDropDownOpen = true;
        });
        Pump();
    }

    private static void CloseCombos(DependencyObject root)
    {
        Visit(root, node =>
        {
            if (node is ComboBox combo)
                combo.IsDropDownOpen = false;
        });
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static ScanSession SampleSession() => new()
    {
        Request = ScanRequest.ForText("ada@example.com sk-test", ScanSource.File),
        Text = "ada@example.com sk-test",
        Result = new ScanResult
        {
            Source = ScanSource.File,
            DisplayName = "notes.txt",
            ContentHash = "abc123",
            Findings =
            [
                new Finding
                {
                    RuleId = "pii.email",
                    Label = "Email",
                    Severity = Severity.Warn,
                    Start = 0,
                    Length = 15,
                    Placeholder = "EMAIL",
                    Category = "personal",
                    Snippet = "ada@example.com"
                },
                new Finding
                {
                    RuleId = "secret.openai_sk",
                    Label = "OpenAI-style key",
                    Severity = Severity.High,
                    Start = 16,
                    Length = 7,
                    Placeholder = "SECRET",
                    Category = "secrets",
                    Snippet = "sk-test"
                }
            ]
        }
    };

    private sealed record TextSample(string Kind, string Text, Brush Foreground, double Size, FontWeight Weight, DependencyObject Element);
}

[CollectionDefinition("Visual tree", DisableParallelization = true)]
public sealed class VisualTreeCollection;
