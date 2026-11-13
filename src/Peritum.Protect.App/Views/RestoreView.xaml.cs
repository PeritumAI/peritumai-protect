using System.Windows;
using Microsoft.Win32;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Redaction;

namespace Peritum.Protect.App.Views;

public partial class RestoreView
{
    private Action<string> _setClipboard = _ => { };
    private Func<int> _retentionDays = () => 30;

    public RestoreView()
    {
        InitializeComponent();
    }

    public void SetClipboard(Action<string> setClipboard) => _setClipboard = setClipboard;

    public void SetRetention(Func<int> retentionDays) => _retentionDays = retentionDays;

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var result = FileRestore.RestoreText(Input.Text ?? "", new MapStore(Paths.MapsDir), Retention());
        Show(result, toClipboard: true);
    }

    private void OnSaveText(object sender, RoutedEventArgs e) => SaveAnswer(".txt", TextCatalog.Get("filterTextSave"));

    private void OnSaveMarkdown(object sender, RoutedEventArgs e) => SaveAnswer(".md", TextCatalog.Get("filterMarkdownSave"));

    private void OnSaveWord(object sender, RoutedEventArgs e) => SaveAnswer(".docx", TextCatalog.Get("filterWordSave"));

    private void SaveAnswer(string extension, string filter)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = "answer" + extension
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var result = AiBundle.ImportAnswer(Input.Text ?? "", new MapStore(Paths.MapsDir), Retention(), dialog.FileName);
        Show(result, toClipboard: false);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = TextCatalog.Get("filterAnswer")
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        RestoreFile(dialog.FileName);
    }

    public void RestoreFile(string path)
    {
        var result = FileRestore.RestoreFile(path, new MapStore(Paths.MapsDir), Retention());
        if (result.Report is not null)
            Input.Text = "";
        Show(result, toClipboard: false);
        if (result.Succeeded && result.OutputPath is not null)
            Report.Text = result.Message + " " + TextCatalog.Format("savedAnswer", ("file", System.IO.Path.GetFileName(result.OutputPath)));
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is not { Length: 1 })
            return;
        RestoreFile(files[0]);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private TimeSpan Retention()
    {
        var days = _retentionDays();
        if (days < 1 || days > 3650)
            days = 30;
        return TimeSpan.FromDays(days);
    }

    private void Show(FileRestoreResult result, bool toClipboard)
    {
        if (result.Report is null)
        {
            Output.Text = "";
            Report.Text = result.Message;
            ShowDetails(result);
            return;
        }

        Output.Text = result.Report.Text;
        if (toClipboard)
            _setClipboard(result.Report.Text);
        Report.Text = result.Message;
        ShowDetails(result);
    }

    private void ShowDetails(FileRestoreResult result)
    {
        if (result.Details.Count == 0)
        {
            Details.Text = "";
            Details.Visibility = Visibility.Collapsed;
            return;
        }

        Details.Text = "Details" + Environment.NewLine + string.Join(Environment.NewLine, result.Details);
        Details.Visibility = Visibility.Visible;
    }
}
