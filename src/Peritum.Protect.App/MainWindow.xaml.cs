using System.Windows;
using Microsoft.Win32;
using Peritum.Protect.App.Services;
using Peritum.Protect.App.Views;
using Peritum.Protect.App.ViewModels;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App;

public partial class MainWindow : Window
{
    private bool _allowExit;
    private CancellationTokenSource? _batchCancel;

    public MainWindow(AppState state, ClipboardWatcher clipboard)
    {
        InitializeComponent();
        AppIcon.Apply(this);
        ThemeService.BindWindow(this);
        State = state;
        ViewModel = new MainViewModel(state, clipboard.SetText);
        ViewModel.BuildDate = HelpView.BuildDate();
        DataContext = ViewModel;
        RestorePage.SetClipboard(clipboard.SetText);
        RestorePage.SetRetention(() => State.Settings.MapRetentionDays);
        DropZone.BrowseRequested += (_, _) => Browse();
        ActivityPage.ExportRequested += (_, _) => ExportLog();
        ActivityPage.ExportStatsRequested += (_, _) => ExportStats();
        ActivityPage.MonthlyPdfRequested += (_, _) => ExportMonthlyPdf();
        ActivityPage.MonthlyCsvRequested += (_, _) => ExportMonthlyCsv();
        SettingsPage.BackRequested += (_, _) => ShowPage(AppPage.Check);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.DetailsVisible))
                Details.Visibility = ViewModel.DetailsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (e.PropertyName is nameof(MainViewModel.Screen) or nameof(MainViewModel.ResultVisible) or nameof(MainViewModel.BatchVisible) or nameof(MainViewModel.BatchReturnVisible))
                UpdateResultVisibility();
            if (e.PropertyName == nameof(MainViewModel.CurrentPage))
                UpdatePageVisibility();
            if (e.PropertyName == nameof(MainViewModel.SelectedFinding))
                ScrollPreviewToCurrent();
        };
        UpdatePageVisibility();
        UpdateResultVisibility();
        ApplyFlow();
        TextCatalog.LanguageChanged += ApplyFlow;
        State.PolicyChanged += OnPolicyChanged;
        ViewModel.CheckAgainRequested += path => _ = ScanFileAsync(path);
    }

    private void ApplyFlow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(ApplyFlow);
            return;
        }
        FlowDirection = TextCatalog.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    private void ScrollPreviewToCurrent()
    {
        if (!ViewModel.PreviewVisible)
            return;
        foreach (var line in ViewModel.PreviewLines)
        {
            if (!line.IsCurrent)
                continue;
            PreviewList.ScrollIntoView(line);
            break;
        }
    }

    private void OnPolicyChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ViewModel.RefreshPolicyNotice();
            if (SettingsPage.DataContext is SettingsViewModel settings)
                settings.RefreshPolicy();
        });
    }

    public AppState State { get; }
    public MainViewModel ViewModel { get; }

    public void ShowHome() => ShowPage(AppPage.Check);

    public void ShowSettings() => ShowPage(AppPage.Settings);

    private void ShowPage(AppPage page)
    {
        if (ViewModel.CurrentPage == AppPage.Settings &&
            page != AppPage.Settings &&
            !TryLeaveSettings())
        {
            return;
        }

        if (page == AppPage.Settings && SettingsPage.DataContext is not SettingsViewModel)
        {
            SettingsPage.DataContext = new SettingsViewModel(State, () =>
            {
                ViewModel.UpdateWatchHint();
                ViewModel.ReloadEvents();
            }, PickTextFile, PickRulesFile, SaveRulesFile, PickBookmarksFile, SaveSitesFile);
        }
        ViewModel.CurrentPage = page;
        UpdatePageVisibility();
        if (page == AppPage.Check)
            ViewModel.UpdateWatchHint();
    }

    public async Task ScanPathsAsync(IReadOnlyList<string> paths)
    {
        if (ViewModel.BatchRunning || ViewModel.Screen.IsBusy)
        {
            ViewModel.ShowError(TextCatalog.Get("alreadyChecking"), TextCatalog.Get("alreadyCheckingBody"));
            return;
        }

        var plan = BatchScan.Collect(paths);
        var rows = plan.Files.Select(file => new BatchRow
        {
            FullPath = file.Path,
            Name = file.Name,
            Type = file.Type
        }).ToList();
        var notice = BatchScan.Notice(plan);
        ViewModel.ShowBatch(rows, string.IsNullOrWhiteSpace(notice) ? TextCatalog.Get("checking") : notice);
        ShowPage(AppPage.Check);
        Show();
        Activate();
        _batchCancel?.Dispose();
        _batchCancel = new CancellationTokenSource();
        var cancel = _batchCancel.Token;
        var rules = State.ActiveRules().ToList();
        bool Allowed(string value) => State.AllowList.Contains(value);
        BatchSummary summary;
        try
        {
            summary = await Task.Run(() => BatchScan.Run(plan.Files, State.Engine, rules, Allowed, cancel, file =>
            {
                Dispatcher.Invoke(() =>
                {
                    var row = ViewModel.BatchRows.FirstOrDefault(item => item.FullPath == file.Path);
                    if (row is null)
                        return;
                    row.Status = file.Status;
                    row.StatusCode = file.StatusCode;
                    row.Detail = file.Detail;
                    if (file.Session is null || file.StatusCode == BatchScan.CodeChecking)
                        return;
                    row.Session = file.Session;
                    row.HasSession = true;
                    ViewModel.LogScan(file.Session);
                });
            }));
        }
        catch
        {
            ViewModel.FinishBatch(TextCatalog.Get("couldNotFinishBatch"));
            return;
        }

        var text = summary.Message;
        var extra = BatchScan.Notice(plan);
        if (!string.IsNullOrWhiteSpace(extra))
            text += " " + extra;
        ViewModel.FinishBatch(text);
    }

    public async Task ScanFileAsync(string path)
    {
        if (ViewModel.Screen.IsBusy)
        {
            ViewModel.ShowError(
                Path.GetFileName(path),
                TextCatalog.Get("fileAlreadyChecking"));
            return;
        }

        ViewModel.BeginScan(path);
        var rules = State.ActiveRules().ToList();
        bool Allowed(string value) => State.AllowList.Contains(value);
        ScanSession session;
        try
        {
            session = await Task.Run(() =>
                State.Engine.Scan(ScanRequest.ForFile(path), rules, Allowed));
        }
        catch
        {
            ViewModel.ShowError(
                Path.GetFileName(path),
                TextCatalog.Get("couldNotRead"));
            return;
        }

        ViewModel.Show(session, null, logNow: true);
        ShowPage(AppPage.Check);
        Show();
        Activate();
    }

    public bool PrepareExit()
    {
        if (!TryLeaveSettings())
            return false;
        _allowExit = true;
        return true;
    }

    private string? PickTextFile()
    {
        var dialog = new OpenFileDialog { Filter = TextCatalog.Get("filterTextCsv") };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickRulesFile()
    {
        var dialog = new OpenFileDialog { Filter = TextCatalog.Get("filterJson") };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? SaveRulesFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = TextCatalog.Get("filterJsonSave"),
            FileName = TextCatalog.Get("rulesFileName")
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? PickBookmarksFile()
    {
        var dialog = new OpenFileDialog { Filter = TextCatalog.Get("filterHtml") };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? SaveSitesFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = TextCatalog.Get("filterJsonSave"),
            FileName = "sites.json"
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void OnCheckAnother(object sender, RoutedEventArgs e) => Browse();

    private async void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Filter = SupportedFileTypes.DialogFilter
        };
        dialog.Multiselect = true;
        if (dialog.ShowDialog(this) != true)
            return;
        if (dialog.FileNames.Length == 1)
            await ScanFileAsync(dialog.FileNames[0]);
        else
            await ScanPathsAsync(dialog.FileNames);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files is not { Length: > 0 })
            return;
        if (ViewModel.CurrentPage == AppPage.Restore && files.Length == 1 && File.Exists(files[0]))
        {
            RestorePage.RestoreFile(files[0]);
            return;
        }
        if (files.Length > 1 || files.Any(Directory.Exists))
        {
            await ScanPathsAsync(files);
            return;
        }
        await ScanFileAsync(files[0]);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        DropZone.SetDragOver(e.Effects == DragDropEffects.Copy);
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropZone.SetDragOver(false);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = ActualWidth < 900;
        SidebarColumn.Width = new GridLength(compact ? 72 : 220);
        Wordmark.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Tagline.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnResultCardSize(object sender, SizeChangedEventArgs e) =>
        ViewModel.SetResultCardHeight(e.NewSize.Height);

    private void OnCheck(object sender, RoutedEventArgs e) => ShowPage(AppPage.Check);

    private void OnRestore(object sender, RoutedEventArgs e) => ShowPage(AppPage.Restore);

    private void OnActivity(object sender, RoutedEventArgs e) => ShowPage(AppPage.Activity);

    private void OnHelp(object sender, RoutedEventArgs e) => ShowPage(AppPage.Help);

    private void OnFormatTip(object sender, RoutedEventArgs e)
    {
        ViewModel.DismissFormatTip();
        ShowPage(AppPage.Settings);
    }

    private void ExportLog()
    {
        var dialog = new SaveFileDialog
        {
            Title = TextCatalog.Get("exportLogTitle"),
            Filter = TextCatalog.Get("filterTextSave"),
            FileName = TextCatalog.Get("logFileName")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllText(dialog.FileName, ViewModel.BuildLogExport());
        ViewModel.ResultBody = TextCatalog.Get("logExported");
    }

    private void ExportStats()
    {
        var dialog = new SaveFileDialog
        {
            Title = TextCatalog.Get("exportStatsTitle"),
            Filter = TextCatalog.Get("filterCsvSave"),
            FileName = TextCatalog.Get("statsFileName")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllText(dialog.FileName, ViewModel.BuildStatsExport());
        ViewModel.ResultBody = TextCatalog.Get("statsExported");
    }

    private void ExportMonthlyPdf()
    {
        var dialog = new SaveFileDialog
        {
            Title = TextCatalog.Get("monthlyReportTitle"),
            Filter = TextCatalog.Get("filterPdfSave"),
            FileName = TextCatalog.Get("reportPdfFile")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllBytes(dialog.FileName, ViewModel.BuildMonthlyPdf());
    }

    private void OnCheckNewer(object sender, RoutedEventArgs e) => HelpView.OpenProductPage();

    private void OnExportBundle(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        var excluded = ViewModel.Hits.Where(hit => hit.Exclude).Select(hit => hit.Value).ToList();
        var exported = AiBundle.ExportFile(path, State.ActiveRules().ToList(), State.AllowList.Contains, new MapStore(Paths.MapsDir), excluded);
        ViewModel.ResultBody = exported.Message;
    }

    private void ExportMonthlyCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = TextCatalog.Get("monthlyReportTitle"),
            Filter = TextCatalog.Get("filterCsvSave"),
            FileName = TextCatalog.Get("reportCsvFile")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllText(dialog.FileName, ViewModel.BuildMonthlyReport(false));
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (State.Paused)
        {
            State.Pause.Resume();
            PauseChanged?.Invoke();
            return;
        }

        if (sender is System.Windows.Controls.Button button && button.ContextMenu is System.Windows.Controls.ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void OnPauseFor15(object sender, RoutedEventArgs e)
    {
        State.Pause.PauseFor(TimeSpan.FromMinutes(15), DateTimeOffset.Now);
        PauseChanged?.Invoke();
    }

    private void OnPauseForHour(object sender, RoutedEventArgs e)
    {
        State.Pause.PauseFor(TimeSpan.FromHours(1), DateTimeOffset.Now);
        PauseChanged?.Invoke();
    }

    private void OnPauseUntilResume(object sender, RoutedEventArgs e)
    {
        State.Pause.PauseUntilResume(DateTimeOffset.Now);
        PauseChanged?.Invoke();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
        => ShowPage(AppPage.Settings);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
            return;
        if (!TryLeaveSettings())
        {
            e.Cancel = true;
            return;
        }
        e.Cancel = true;
        Hide();
    }

    public event Action? PauseChanged;

    private void UpdatePageVisibility()
    {
        CheckPage.Visibility = ViewModel.CurrentPage == AppPage.Check
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActivityPage.Visibility = ViewModel.CurrentPage == AppPage.Activity
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsPage.Visibility = ViewModel.CurrentPage == AppPage.Settings
            ? Visibility.Visible
            : Visibility.Collapsed;
        HelpPage.Visibility = ViewModel.CurrentPage == AppPage.Help
            ? Visibility.Visible
            : Visibility.Collapsed;
        RestorePage.Visibility = ViewModel.CurrentPage == AppPage.Restore
            ? Visibility.Visible
            : Visibility.Collapsed;

        CheckNav.Tag = ViewModel.CurrentPage == AppPage.Check ? "Active" : null;
        ActivityNav.Tag = ViewModel.CurrentPage == AppPage.Activity ? "Active" : null;
        SettingsNav.Tag = ViewModel.CurrentPage == AppPage.Settings ? "Active" : null;
        HelpNav.Tag = ViewModel.CurrentPage == AppPage.Help ? "Active" : null;
        RestoreNav.Tag = ViewModel.CurrentPage == AppPage.Restore ? "Active" : null;
    }

    private void OnBatchDetails(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: BatchRow row })
            ViewModel.ShowBatchFile(row);
    }

    private void OnBatchOpen(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: BatchRow row })
            MainViewModel.OpenPath(row.FullPath);
    }

    private void OnBatchShowFolder(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { DataContext: BatchRow row })
            MainViewModel.ShowPath(row.FullPath);
    }

    private void OnExportFindings(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = TextCatalog.Get("exportFindingsName"),
            Filter = TextCatalog.Get("filterCsvShort"),
            FileName = TextCatalog.Get("findingsFileName")
        };
        if (dialog.ShowDialog(this) != true)
            return;
        File.WriteAllText(dialog.FileName, ViewModel.BuildFindingsCsv());
    }

    private void OnBackToBatch(object sender, RoutedEventArgs e) => ViewModel.ReturnToBatch();

    private void OnCancelBatch(object sender, RoutedEventArgs e) => _batchCancel?.Cancel();

    private async void OnCopyAll(object sender, RoutedEventArgs e) => await CopyBatchAsync(reversible: false);

    private async void OnCopyAllReversible(object sender, RoutedEventArgs e) => await CopyBatchAsync(reversible: true);

    private static bool HasFindings(BatchRow row) => row.StatusCode == BatchScan.CodeFindings;

    private async Task CopyBatchAsync(bool reversible)
    {
        if (ViewModel.BatchRunning)
            return;
        var rows = ViewModel.BatchRows.Where(row => reversible
            ? HasFindings(row) || row.StatusCode == BatchScan.CodeClear
            : HasFindings(row)).ToList();
        if (rows.Count == 0)
        {
            ViewModel.BatchSummary += " " + TextCatalog.Get("noFilesReady");
            return;
        }

        ViewModel.BatchRunning = true;
        var made = 0;
        var failed = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var row in rows)
                {
                    var copy = State.Engine.MakeProtectedCopy(
                        row.FullPath,
                        State.ActiveRules(),
                        State.Settings.CopyNamePattern,
                        State.AllowList.Contains,
                        null,
                        State.Settings.CopyFolderMode,
                        State.Settings.CopyFolder,
                        reversible,
                        reversible ? Paths.MapsDir : null);
                    if (string.IsNullOrEmpty(copy.Error) && !string.IsNullOrEmpty(copy.OutputPath))
                        made++;
                    else
                        failed++;
                }
            });
        }
        finally
        {
            ViewModel.BatchRunning = false;
        }

        var kind = reversible ? TextCatalog.Get("reversibleCopiesWord") : TextCatalog.Get("protectedCopiesWord");
        ViewModel.BatchSummary += " " + TextCatalog.Format("copiesMade", ("kind", kind), ("count", made));
        if (failed > 0)
            ViewModel.BatchSummary += " " + TextCatalog.Format("copiesFailed", ("count", failed));
    }

    private void UpdateResultVisibility()
    {
        var batch = ViewModel.BatchVisible;
        var back = ViewModel.BatchReturnVisible;
        BatchPanel.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;
        BackToBatch.Visibility = back ? Visibility.Visible : Visibility.Collapsed;
        if (batch)
        {
            DropZone.Visibility = Visibility.Collapsed;
            ResultCard.Visibility = Visibility.Collapsed;
            ClipboardCard.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Collapsed;
            CheckAnother.Visibility = Visibility.Collapsed;
            AllowDrop = !ViewModel.BatchRunning;
            return;
        }

        var busy = ViewModel.Screen.IsBusy;
        var showResult = ViewModel.ResultVisible && !busy;
        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ResultCard.Visibility = showResult ? Visibility.Visible : Visibility.Collapsed;
        DropZone.Visibility = showResult ? Visibility.Collapsed : Visibility.Visible;
        ClipboardCard.Visibility = showResult ? Visibility.Collapsed : Visibility.Visible;
        CheckAnother.Visibility = showResult && !back ? Visibility.Visible : Visibility.Collapsed;
        DropZone.IsEnabled = !busy;
        AllowDrop = !busy;
    }

    private bool TryLeaveSettings()
    {
        if (SettingsPage.DataContext is not SettingsViewModel settings || !settings.IsDirty)
            return true;

        var choice = MessageBox.Show(
            TextCatalog.Get("saveSettingsTitle"),
            TextCatalog.Get("product"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Cancel)
            return false;
        if (choice == MessageBoxResult.Yes)
            return settings.SaveSettings();
        settings.DiscardChanges();
        return true;
    }
}
