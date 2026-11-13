using System.Windows;
using System.Windows.Controls;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.App.Views;

public partial class NumberFormatWindow : Window
{
    private NumberPattern _pattern = new();
    private bool _advanced;

    public NumberFormatWindow()
    {
        InitializeComponent();
        Category.SelectionChanged += (_, _) =>
        {
            if (Category.SelectedIndex == 2)
                Reversible.IsChecked = false;
        };
    }

    public UserTerm? Term { get; private set; }

    private void OnChanged(object sender, TextChangedEventArgs e)
    {
        if (Words is null || Advanced is null || Sample is null)
            return;
        if (_advanced)
        {
            ShowHits();
            return;
        }

        _pattern = NumberPattern.FromExamples(Examples());
        Advanced.TextChanged -= OnAdvanced;
        Advanced.Text = _pattern.Pattern;
        Advanced.TextChanged += OnAdvanced;
        Words.Text = _pattern.Words;
        Warning.Text = _pattern.Refused
            ? _pattern.Error ?? ""
            : _pattern.TooBroad
                ? TextCatalog.Get("patternBroad")
                : "";
        ShowHits();
    }

    private void OnAdvanced(object sender, TextChangedEventArgs e)
    {
        _advanced = true;
        _pattern = NumberPattern.FromRegex(Advanced.Text);
        Words.Text = _pattern.Error is null ? TextCatalog.Get("customPattern") : "";
        Warning.Text = _pattern.Error ?? (_pattern.TooBroad ? TextCatalog.Get("patternBroad") : "");
        ShowHits();
    }

    private void ShowHits()
    {
        if (_pattern.Error is not null || string.IsNullOrWhiteSpace(Sample.Text))
        {
            Hits.Text = "";
            return;
        }

        var hits = _pattern.Hits(Sample.Text);
        Hits.Text = hits.Count == 0 ? TextCatalog.Get("noHits") : TextCatalog.Format("hitsLabel", ("hits", string.Join(", ", hits)));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_pattern.Refused || _pattern.Error is not null || string.IsNullOrWhiteSpace(_pattern.Pattern))
        {
            Warning.Text = _pattern.Error ?? TextCatalog.Get("patternTooMuch");
            return;
        }

        var category = (Category.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "custom";
        Term = new UserTerm
        {
            Label = string.IsNullOrWhiteSpace(FormatName.Text) ? TextCatalog.Get("reference") : FormatName.Text.Trim(),
            Kind = "regex",
            Text = _pattern.Pattern,
            Category = category,
            Reversible = category != "secrets" && Reversible.IsChecked == true
        };
        DialogResult = true;
    }

    private IReadOnlyList<string> Examples() =>
    [
        Example1.Text, Example2.Text, Example3.Text, Example4.Text, Example5.Text
    ];
}
