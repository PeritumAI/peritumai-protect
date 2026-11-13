using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Media = System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peritum.Protect.App.Services;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly Action<string> _setClipboard;
    private readonly List<ProtectEvent> _recent = [];
    private readonly HashSet<string> _notSensitiveLogged = new(StringComparer.Ordinal);
    private string _summary = "";
    private int _linkCount;

    public MainViewModel(AppState state, Action<string> setClipboard)
    {
        _state = state;
        _setClipboard = setClipboard;
        StatusText = TextCatalog.Get("protectionOn");
        PauseButtonText = TextCatalog.Get("trayPause");
        ApplyScreen();
        UpdateWatchHint();
        RefreshPolicyNotice();
        ReloadEvents();
        OnCurrentPageChanged(CurrentPage);
        TextCatalog.LanguageChanged += () => OnCurrentPageChanged(CurrentPage);
    }

    public void RefreshPolicyNotice() => PolicyNotice = _state.PolicyWarning ?? "";

    [ObservableProperty] private string _pauseButtonText = "Pause";

    public ObservableCollection<HitRow> Hits { get; } = [];
    public ObservableCollection<HitRow> AllFindings { get; } = [];
    public ObservableCollection<HitRow> ActiveFindings { get; } = [];
    public ObservableCollection<EventRow> Events { get; } = [];
    public ObservableCollection<CategoryRow> Categories { get; } = [];
    public ObservableCollection<BatchRow> BatchRows { get; } = [];

    /// <summary>Hides a revealed value after the delay. Tests replace this.</summary>
    public Action<Action, TimeSpan> RevealScheduler { get; set; } = ScheduleReveal;

    [ObservableProperty] private bool _showAllFindings;
    [ObservableProperty] private bool _previewVisible;
    [ObservableProperty] private HitRow? _selectedFinding;
    [ObservableProperty] private int _currentFindingIndex = -1;
    [ObservableProperty] private bool _previewIsImage;
    [ObservableProperty] private string _previewImagePath = "";
    [ObservableProperty] private int _imageWidth;
    [ObservableProperty] private int _imageHeight;
    public ObservableCollection<PreviewLine> PreviewLines { get; } = [];
    public ObservableCollection<PreviewBox> PreviewBoxes { get; } = [];
    public string FindingsCountLabel
    {
        get
        {
            var count = AllFindings.Count(row => !row.Exclude);
            return TextCatalog.Plural("findingCount", count, ("count", count));
        }
    }
    public string CurrentLocation => SelectedFinding?.Location ?? "";
    public string CurrentIndexLine =>
        SelectedFinding is null || AllFindings.Count == 0
            ? ""
            : SelectedFinding.IndexLine;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _watchHint = "";
    [ObservableProperty] private bool _showFormatTip;
    [ObservableProperty] private string _formatTip = "";
    [ObservableProperty] private bool _resultVisible;
    [ObservableProperty] private string _resultTitle = "";
    [ObservableProperty] private string _resultWhyText = "";
    [ObservableProperty] private string _resultWhyTip = "";
    public bool HasResultWhy => ResultWhyText.Length > 0;
    public bool ShowWhy => HasResultWhy || Hits.Any(row => row.HasWhy);
    partial void OnResultWhyTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasResultWhy));
        OnPropertyChanged(nameof(ShowWhy));
    }
    [ObservableProperty] private string _resultBody = "";
    [ObservableProperty] private string _hashLine = "";
    [ObservableProperty] private bool _canMakeCopy;
    [ObservableProperty] private bool _canCopyText;
    [ObservableProperty] private bool _detailsVisible;
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _activityFilter = "all";
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private ScanScreenState _screen = ScanScreenState.Idle;
    [ObservableProperty] private AppPage _currentPage = AppPage.Check;
    [ObservableProperty] private string _pageTitle = "";
    [ObservableProperty] private Media.Brush _statusBrush = Media.Brushes.LimeGreen;
    [ObservableProperty] private string? _lastCopyPath;
    [ObservableProperty] private bool _copyActionsVisible;
    [ObservableProperty] private bool _pendingActionsVisible = true;
    [ObservableProperty] private string _policyNotice = "";
    [ObservableProperty] private bool _batchVisible;
    [ObservableProperty] private bool _batchRunning;
    [ObservableProperty] private bool _batchReturnVisible;
    [ObservableProperty] private string _batchSummary = "";

    public void RefreshPause(DateTimeOffset now)
    {
        var paused = _state.Pause.IsPaused(now);
        StatusText = _state.Pause.Status(now);
        PauseButtonText = paused ? TextCatalog.Get("trayResume") : TextCatalog.Get("trayPause");
        StatusBrush = paused ? Media.Brushes.Gray : Media.Brushes.LimeGreen;
    }

    public void UpdateWatchHint()
    {
        WatchHint = _state.Settings.OnlyWhenTargetFocused
            ? TextCatalog.Get("watchTarget")
            : TextCatalog.Get("watchEvery");
        RefreshFormatTip();
    }

    public void RefreshFormatTip()
    {
        var profile = _state.Settings.Profile;
        ShowFormatTip = !_state.UserSettings.FormatTipSeen && profile is "lawyer" or "clinic" or "hr" or "accountant";
        FormatTip = profile switch
        {
            "clinic" => TextCatalog.Get("formatTipClinic"),
            "hr" => TextCatalog.Get("formatTipHr"),
            "accountant" => TextCatalog.Get("formatTipAccountant"),
            _ => TextCatalog.Get("formatTipLawyer")
        };
    }

    public void DismissFormatTip()
    {
        _state.UserSettings.FormatTipSeen = true;
        _state.Settings.FormatTipSeen = true;
        _state.SaveSettings();
        RefreshFormatTip();
    }

    public void BeginScan(string path)
    {
        _state.Last = null;
        _state.LastProcess = null;
        LastCopyPath = null;
        BatchVisible = false;
        BatchReturnVisible = false;
        CurrentPage = AppPage.Check;
        Screen = ScanScreenState.Scanning(Path.GetFileName(path));
        DetailsVisible = false;
        HashLine = "";
        ApplyScreen();
    }

    public void ShowBatch(IEnumerable<BatchRow> rows, string summary)
    {
        BatchRows.Clear();
        foreach (var row in rows)
            BatchRows.Add(row);
        BatchSummary = summary;
        BatchVisible = true;
        BatchReturnVisible = false;
        BatchRunning = true;
        CurrentPage = AppPage.Check;
        Screen = ScanScreenState.Idle;
        ApplyScreen();
    }

    public bool BatchIdle => !BatchRunning;

    partial void OnBatchRunningChanged(bool value) => OnPropertyChanged(nameof(BatchIdle));

    public void FinishBatch(string summary)
    {
        BatchSummary = summary;
        BatchRunning = false;
        BatchVisible = true;
    }

    public void ShowBatchFile(BatchRow row)
    {
        if (row.Session is null)
            return;
        Show(row.Session, null, logNow: false);
        BatchVisible = false;
        BatchReturnVisible = true;
    }

    public void ReturnToBatch()
    {
        BatchVisible = true;
        BatchReturnVisible = false;
        Screen = ScanScreenState.Idle;
        ApplyScreen();
    }

    public void LogScan(ScanSession session)
    {
        var action = session.Result.Error is not null
            ? EventActions.Error
            : session.Result.HitCount == 0 ? EventActions.Allow : EventActions.Warn;
        Log(session.Result, action, null, session.Request.FilePath);
    }

    public void ShowError(string title, string message)
    {
        LastCopyPath = null;
        BatchVisible = false;
        BatchReturnVisible = false;
        CurrentPage = AppPage.Check;
        Screen = new ScanScreenState(
            ScanScreenKind.Error,
            title,
            message,
            true,
            false,
            false,
            false);
        DetailsVisible = false;
        ApplyScreen();
    }

    public void Show(ScanSession session, string? process, bool logNow)
    {
        _state.Last = session;
        _state.LastProcess = process;
        Hits.Clear();
        AllFindings.Clear();
        ActiveFindings.Clear();
        Categories.Clear();
        _notSensitiveLogged.Clear();
        ShowAllFindings = false;
        PreviewVisible = false;
        CurrentFindingIndex = -1;
        SelectedFinding = null;
        var ordered = session.Result.Findings.OrderBy(finding => finding.Start).ToList();
        var rows = new List<HitRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var finding = ordered[i];
            var value = finding.Start >= 0 && finding.Start + finding.Length <= session.Text.Length
                ? session.Text.Substring(finding.Start, finding.Length)
                : finding.Snippet;
            var row = new HitRow(finding, value, i + 1, ordered.Count, _setClipboard, (action, delay) => RevealScheduler(action, delay));
            row.MarkedNotSensitive += MarkNotSensitive;
            row.UndoRequested += UndoNotSensitive;
            rows.Add(row);
            Hits.Add(row);
            AllFindings.Add(row);
        }

        OnPropertyChanged(nameof(FindingsCountLabel));
        foreach (var group in ResultGroups.Build(session.Result.Findings))
        {
            var category = new CategoryRow(group.Id, group.Title, group.Description, group.CountsText, group.Chip, group.ChipText, IconFor(group.Id));
            foreach (var row in rows)
            {
                if (ResultGroups.CategoryOf(row.Finding) == group.Id)
                    category.Findings.Add(row);
            }

            category.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CategoryRow.IsExpanded))
                    RefreshActiveFindings();
            };
            Categories.Add(category);
        }

        _linkCount = 0;
        if (session.Result.LinkCount > 0 || session.Result.LinkFindings.Count > 0)
        {
            _linkCount = session.Result.LinkCount;
            var suspicious = session.Result.LinkFindings.Count;
            var chip = suspicious == 0 ? "None" : "Check";
            var category = new CategoryRow(
                "links",
                TextCatalog.Get("cat.links.title"),
                TextCatalog.Get("cat.links.body"),
                ResultGroups.LinkSummary(session.Result.LinkCount, suspicious),
                chip,
                TextCatalog.Get("chip." + chip.ToLowerInvariant()),
                IconFor("links"));
            foreach (var finding in session.Result.LinkFindings)
            {
                var row = new HitRow(finding, finding.Snippet, copy: _setClipboard);
                row.TrustRequested += TrustSite;
                row.UndoRequested += UndoNotSensitive;
                category.Findings.Add(row);
            }
            category.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CategoryRow.IsExpanded))
                    RefreshActiveFindings();
            };
            Categories.Add(category);
        }

        RefreshActiveFindings();
        BuildPreview(session);
        NextFindingCommand.NotifyCanExecuteChanged();
        PreviousFindingCommand.NotifyCanExecuteChanged();

        LastCopyPath = null;
        CurrentPage = AppPage.Check;
        Screen = ScanScreenState.FromSession(session);
        ApplyScreen();
        RefreshSourceActions();
        DetailsVisible = false;
        HashLine = "";

        if (!string.IsNullOrWhiteSpace(session.Result.Note))
            ResultBody += Environment.NewLine + session.Result.Note;
        var why = RiskExplanations.ForNote(session.Result.Error)
            ?? RiskExplanations.ForNote(session.Result.Note)
            ?? RiskExplanations.ForNote(ResultBody);
        ResultWhyText = why?.Text ?? "";
        ResultWhyTip = why?.Tip ?? "";
        OnPropertyChanged(nameof(ShowWhy));

        _summary = Screen.Body;

        if (logNow)
        {
            var action = session.Result.Error is not null
                ? EventActions.Error
                : session.Result.HitCount == 0 ? EventActions.Allow : EventActions.Warn;
            Log(session.Result, action, process, session.Request.FilePath);
        }
    }

    partial void OnSelectedFindingChanged(HitRow? value)
    {
        if (value is null)
            return;
        var index = AllFindings.IndexOf(value);
        if (index >= 0)
            CurrentFindingIndex = index;
        PreviewVisible = true;
        MarkPreviewCurrent();
        OnPropertyChanged(nameof(CurrentLocation));
        OnPropertyChanged(nameof(CurrentIndexLine));
    }

    [RelayCommand]
    private void ShowFileList()
    {
        if (BatchRows.Count == 0)
            return;
        BatchVisible = true;
        BatchReturnVisible = false;
    }

    [RelayCommand]
    private void ShowListedFindings()
    {
        if (AllFindings.Count == 0)
            return;
        ShowAllFindings = true;
    }

    [RelayCommand(CanExecute = nameof(CanMoveFindings))]
    private void NextFinding() => MoveFinding(1);

    [RelayCommand(CanExecute = nameof(CanMoveFindings))]
    private void PreviousFinding() => MoveFinding(-1);

    private bool CanMoveFindings() => AllFindings.Count > 0;

    private void MoveFinding(int delta)
    {
        if (AllFindings.Count == 0)
            return;
        var start = CurrentFindingIndex < 0 ? (delta > 0 ? -1 : 0) : CurrentFindingIndex;
        var index = Math.Clamp(start + delta, 0, AllFindings.Count - 1);
        CurrentFindingIndex = index;
        SelectedFinding = AllFindings[index];
    }

    private void BuildPreview(ScanSession session)
    {
        PreviewLines.Clear();
        PreviewBoxes.Clear();
        PreviewIsImage = session.ImageWidth > 0 && session.Boxes.Count > 0;
        PreviewImagePath = PreviewIsImage ? session.Request.FilePath ?? "" : "";
        ImageWidth = session.ImageWidth;
        ImageHeight = session.ImageHeight;
        var text = session.Text ?? "";
        var hits = AllFindings.ToList();
        var cursor = 0;
        while (cursor <= text.Length)
        {
            var newline = text.IndexOf('\n', cursor);
            var lineEnd = newline < 0 ? text.Length : newline;
            var contentEnd = lineEnd;
            if (contentEnd > cursor && text[contentEnd - 1] == '\r')
                contentEnd--;
            PreviewLines.Add(new PreviewLine(SpansFor(text, cursor, contentEnd, hits)));
            if (newline < 0)
                break;
            cursor = newline + 1;
            if (cursor == text.Length)
            {
                PreviewLines.Add(new PreviewLine([]));
                break;
            }
        }

        if (PreviewIsImage)
        {
            foreach (var box in session.Boxes)
            {
                HitRow? hit = null;
                foreach (var row in hits)
                {
                    var end = row.Finding.Start + row.Finding.Length;
                    if (row.Finding.Start < box.Start + box.Length && box.Start < end)
                    {
                        hit = row;
                        break;
                    }
                }

                if (hit is null)
                    continue;
                PreviewBoxes.Add(new PreviewBox(hit, box.X, box.Y, box.Width, box.Height));
            }
        }
    }

    private static List<PreviewSpan> SpansFor(string text, int start, int end, IReadOnlyList<HitRow> hits)
    {
        var spans = new List<PreviewSpan>();
        var cursor = start;
        foreach (var hit in hits)
        {
            var hitEnd = hit.Finding.Start + hit.Finding.Length;
            if (hitEnd <= start || hit.Finding.Start >= end)
                continue;
            var from = Math.Max(hit.Finding.Start, start);
            var to = Math.Min(hitEnd, end);
            if (from > cursor)
                spans.Add(new PreviewSpan(text[cursor..from], null, ""));
            var icon = hit.Finding.Start >= start && hit.Finding.Start < end ? IconFor(ResultGroups.CategoryOf(hit.Finding)) : "";
            spans.Add(new PreviewSpan("", hit, icon));
            cursor = to;
        }

        if (cursor < end)
            spans.Add(new PreviewSpan(text[cursor..end], null, ""));
        return spans;
    }

    private void MarkPreviewCurrent()
    {
        foreach (var line in PreviewLines)
            line.SetCurrent(SelectedFinding);
        foreach (var box in PreviewBoxes)
            box.IsCurrent = SelectedFinding is not null && ReferenceEquals(box.Hit, SelectedFinding);
    }

    public string ReversibleHint => UiText.ReversibleCopy;

    public Func<DateTime> Today { get; set; } = () => DateTime.Today;

    public DateTime BuildDate { get; set; } = DateTime.Today;

    public bool ShowVersionNotice => VersionAge.ShowQuietLine(BuildDate, Today());

    [ObservableProperty] private bool _acceptTrackedChanges = true;
    [ObservableProperty] private bool _removeSpeakerNotes = true;

    [RelayCommand]
    private void MakeCopy() => WriteCopy(reversible: false);

    [RelayCommand]
    private void CleanForSharing()
    {
        var session = _state.Last;
        var path = session?.Request.FilePath;
        if (session is null || string.IsNullOrEmpty(path))
            return;
        var copy = _state.Engine.MakeCleanCopy(
            path,
            new CleanOptions { AcceptTrackedChanges = AcceptTrackedChanges, RemoveSpeakerNotes = RemoveSpeakerNotes },
            _state.Settings.CopyFolderMode,
            _state.Settings.CopyFolder);
        if (!string.IsNullOrEmpty(copy.Error) || string.IsNullOrEmpty(copy.OutputPath))
        {
            var message = string.IsNullOrEmpty(copy.Error)
                ? TextCatalog.Get("couldNotRead")
                : copy.Error;
            if (copy.VerificationRefused)
                Log(session.Result, EventActions.CopyRefused, _state.LastProcess, path);
            ShowError(ResultTitle, message);
            return;
        }

        LastCopyPath = copy.OutputPath;
        Screen = ScanScreenState.CopyCreated(Path.GetFileName(copy.OutputPath));
        ApplyScreen();
        ResultTitle = TextCatalog.Get("cleanSaved");
        if (!string.IsNullOrWhiteSpace(copy.Note))
            ResultBody += Environment.NewLine + copy.Note;
        if (copy.OriginalUnchanged)
        {
            var hash = copy.OriginalHashAfter.Length >= 12 ? copy.OriginalHashAfter[..12] : copy.OriginalHashAfter;
            HashLine = TextCatalog.Format("hashUnchanged", ("hash", hash));
        }
        Log(session.Result, EventActions.CopyMade, _state.LastProcess, path, LastCopyPath, "permanent");
        RefreshSourceActions();
    }

    [RelayCommand]
    private void MakeReversibleCopy() => WriteCopy(reversible: true);

    private void WriteCopy(bool reversible)
    {
        var session = _state.Last;
        var path = session?.Request.FilePath;
        if (session is null || string.IsNullOrEmpty(path))
            return;
        var excluded = Hits.Where(h => h.Exclude).Select(h => h.Value).ToList();
        var copy = _state.Engine.MakeProtectedCopy(
            path,
            _state.ActiveRules(),
            _state.Settings.CopyNamePattern,
            _state.AllowList.Contains,
            excluded,
            _state.Settings.CopyFolderMode,
            _state.Settings.CopyFolder,
            reversible,
            reversible ? Paths.MapsDir : null);
        if (!string.IsNullOrEmpty(copy.Error) || string.IsNullOrEmpty(copy.OutputPath))
        {
            var message = string.IsNullOrEmpty(copy.Error)
                ? TextCatalog.Get("couldNotRead")
                : copy.Error;
            if (copy.VerificationRefused)
                Log(session.Result, EventActions.CopyRefused, _state.LastProcess, path);
            ShowError(ResultTitle, message);
            return;
        }

        LastCopyPath = copy.OutputPath;
        Screen = ScanScreenState.CopyCreated(Path.GetFileName(copy.OutputPath));
        ApplyScreen();
        if (!string.IsNullOrWhiteSpace(copy.SidecarPath))
            ResultBody += Environment.NewLine + TextCatalog.Format("alsoWrote", ("file", Path.GetFileName(copy.SidecarPath)));
        if (!string.IsNullOrWhiteSpace(copy.Note))
            ResultBody += Environment.NewLine + copy.Note;
        if (copy.Reversible)
        {
            ResultTitle = TextCatalog.Get("reversibleSaved");
            ResultBody += Environment.NewLine + UiText.ReversibleCopy;
        }
        if (copy.OriginalUnchanged)
        {
            var hash = copy.OriginalHashAfter.Length >= 12 ? copy.OriginalHashAfter[..12] : copy.OriginalHashAfter;
            HashLine = TextCatalog.Format("hashUnchanged", ("hash", hash));
        }
        else
        {
            HashLine = TextCatalog.Get("hashChanged");
        }
        Log(session.Result, EventActions.CopyMade, _state.LastProcess, path, LastCopyPath, reversible ? "reversible" : "permanent");
        RefreshSourceActions();
    }

    [RelayCommand]
    private void CopyRedacted()
    {
        var session = _state.Last;
        if (session is null || session.Text.Length == 0)
            return;
        var findings = Hits.Where(h => !h.Exclude).Select(h => h.Finding).ToList();
        var text = session.VisibleLength >= 0 && session.VisibleLength <= session.Text.Length
            ? session.Text[..session.VisibleLength]
            : session.Text;
        _setClipboard(TextRedactor.Apply(text, findings));
        ResultBody = TextCatalog.Get("redactedOnClipboard");
        if (session.Request.Source == ScanSource.Clipboard)
            Log(session.Result, EventActions.Redact, _state.LastProcess, null);
    }

    public void OpenDetails() => DetailsVisible = true;

    [RelayCommand]
    private void ToggleDetails() => DetailsVisible = !DetailsVisible;

    public static void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    public static void ShowPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
            return;
        }

        var parent = FileLocations.ParentIfExists(path);
        if (parent is null)
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + parent + "\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(LastCopyPath))
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "/select,\"" + LastCopyPath + "\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (string.IsNullOrEmpty(LastCopyPath) || !File.Exists(LastCopyPath))
            return;
        Process.Start(new ProcessStartInfo { FileName = LastCopyPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (!string.IsNullOrEmpty(LastCopyPath))
            _setClipboard(LastCopyPath);
    }

    [RelayCommand]
    private void Dismiss()
    {
        LastCopyPath = null;
        Screen = ScanScreenState.Idle;
        ApplyScreen();
        DetailsVisible = false;
        Hits.Clear();
    }

    [RelayCommand]
    private void Navigate(AppPage page) => CurrentPage = page;

    public void ReloadEvents()
    {
        _recent.Clear();
        _recent.AddRange(_state.Events.ReadRecent(200));
        ApplyFilter();
        StatsText = ActivityStats.Summarize(_state.Events.ReadAll(), DisplayLabels()).ToDisplay();
    }

    public string BuildStatsExport() => ActivityStats.Summarize(_state.Events.ReadAll(), DisplayLabels()).ToCsv();

    private IReadOnlyDictionary<string, string> DisplayLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in _state.Packs)
        {
            foreach (var rule in pack.Rules)
                labels[rule.Id] = string.IsNullOrWhiteSpace(rule.DisplayLabel) ? rule.Label : rule.DisplayLabel;
        }
        return labels;
    }

    public string BuildLogExport()
    {
        var events = _state.Events.ReadRecent(100_000);
        if (events.Count == 0)
            return TextCatalog.Get("logHeader") + "\r\n" + TextCatalog.Get("logCreated") + "\r\n" + TextCatalog.Get("noEventsYet") + "\r\n";
        var lines = new List<string>
        {
            TextCatalog.Get("logHeader"),
            TextCatalog.Get("logCreated"),
            TextCatalog.Get("logNote"),
            ""
        };
        foreach (var evt in events)
        {
            var hits = evt.Hits.Count == 0
                ? ""
                : string.Join(", ", evt.Hits.Select(h => TextCatalog.Format("hitTimes", ("rule", h.RuleId), ("count", h.Count))));
            lines.Add(string.Join("\t",
                evt.Ts.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                evt.Source,
                evt.Action,
                evt.Process ?? "",
                evt.FileName ?? "",
                IncludePathsInExport ? evt.FilePath ?? "" : "",
                IncludePathsInExport ? evt.CopyPath ?? "" : "",
                hits,
                evt.Error ?? ""));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    public void LogDecision(ScanSession session, string action)
    {
        Log(session.Result, action, _state.LastProcess, session.Request.FilePath);
    }

    partial void OnCurrentPageChanged(AppPage value) => PageTitle = value switch
    {
        AppPage.Activity => TextCatalog.Get("navActivity"),
        AppPage.Settings => TextCatalog.Get("navSettings"),
        AppPage.Help => TextCatalog.Get("navHelp"),
        AppPage.Restore => TextCatalog.Get("navRestore"),
        _ => TextCatalog.Get("navCheck")
    };

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnActivityFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void FilterActivity(string? filter) => ActivityFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;

    public event Action<string>? CheckAgainRequested;

    public bool IncludePathsInExport { get; set; }
    public bool IncludeReportFileNames { get; set; }

    public string SourcePath => _state.Last?.Request.FilePath ?? "";

    public bool HasSourceFile => !string.IsNullOrWhiteSpace(SourcePath);

    public bool ShowTrackedChangesOption => IsWord(SourcePath);

    public bool ShowSpeakerNotesOption => IsPowerPoint(SourcePath);

    public bool HasCleanOptions => ShowTrackedChangesOption || ShowSpeakerNotesOption;

    [ObservableProperty] private double _findingsMinHeight = 160;

    private double _resultCardHeight;

    public void SetResultCardHeight(double height)
    {
        _resultCardHeight = height;
        UpdateFindingsMin();
    }

    partial void OnPreviewVisibleChanged(bool value) => UpdateFindingsMin();

    private void UpdateFindingsMin()
    {
        if (_resultCardHeight <= 0)
            return;
        FindingsMinHeight = PreviewVisible ? Math.Max(150, _resultCardHeight * 0.32) : _resultCardHeight / 2;
    }

    private static bool IsWord(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerPoint(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase);
    }

    public bool HasExpandedCategory => Categories.Any(category => category.IsExpanded);

    public string SourceMissing => FileLocations.MissingMessage(SourcePath);

    private void RefreshActiveFindings()
    {
        ActiveFindings.Clear();
        foreach (var category in Categories)
        {
            if (!category.IsExpanded)
                continue;
            foreach (var row in category.Findings)
                ActiveFindings.Add(row);
        }

        OnPropertyChanged(nameof(HasExpandedCategory));
    }

    private void RefreshSourceActions()
    {
        OnPropertyChanged(nameof(HasSourceFile));
        OnPropertyChanged(nameof(SourceMissing));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(ShowTrackedChangesOption));
        OnPropertyChanged(nameof(ShowSpeakerNotesOption));
        OnPropertyChanged(nameof(HasCleanOptions));
    }

    [RelayCommand]
    private void OpenScannedFile() => OpenPath(SourcePath);

    [RelayCommand]
    private void ShowScannedInFolder() => ShowPath(SourcePath);

    [RelayCommand]
    private void OpenActivityFile(string? path) => OpenPath(path);

    [RelayCommand]
    private void ShowActivityFolder(string? path) => ShowPath(path);

    [RelayCommand]
    private void CheckAgain(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            CheckAgainRequested?.Invoke(path);
    }

    public string BuildFindingsCsv()
    {
        var rows = new List<FindingsExportRow>();
        foreach (var row in BatchRows)
        {
            var findings = row.Session?.Result.Findings ?? [];
            rows.Add(FileLocations.FromScan(row.FullPath, row.Type, row.Status, findings));
        }

        return FileLocations.FindingsCsv(rows);
    }

    private void Log(ScanResult result, string action, string? process, string? filePath, string? copyPath = null, string? mode = null)
    {
        _state.Events.Append(_state.Events.FromScan(
            result,
            action,
            process,
            filePath,
            _state.Settings.RememberFileLocations,
            copyPath,
            mode));
        ReloadEvents();
    }

    public string BuildMonthlyReport(bool pdf)
    {
        var now = DateTimeOffset.Now;
        var report = SafetyReport.Build(_state.Events.ReadAll(), now.Year, now.Month, IncludeReportFileNames, DisplayLabels());
        return pdf ? "" : SafetyReport.Csv(report, IncludeReportFileNames);
    }

    public byte[] BuildMonthlyPdf()
    {
        var now = DateTimeOffset.Now;
        var report = SafetyReport.Build(_state.Events.ReadAll(), now.Year, now.Month, IncludeReportFileNames, DisplayLabels());
        return SafetyReport.Pdf(report, IncludeReportFileNames);
    }

    private void ApplyFilter()
    {
        var matched = _recent.Where(evt => ActivityFilter switch
        {
            "clipboard" => evt.Source.Equals("clipboard", StringComparison.OrdinalIgnoreCase),
            "files" => evt.Source.Equals("file", StringComparison.OrdinalIgnoreCase),
            "errors" => evt.Action.Equals(EventActions.Error, StringComparison.OrdinalIgnoreCase),
            _ => true
        });
        Events.Clear();
        foreach (var line in ActivityTimeline.Build(matched, DateTimeOffset.Now))
        {
            var evt = line.Event;
            var hits = evt.Hits.Count == 0
                ? ""
                : string.Join(", ", evt.Hits.Select(FriendlyHit));
            var summary = string.IsNullOrEmpty(evt.FileName) ? hits : string.IsNullOrEmpty(hits) ? evt.FileName : evt.FileName + "  " + hits;
            var clipboard = evt.Source.Equals("clipboard", StringComparison.OrdinalIgnoreCase);
            Events.Add(new EventRow
            {
                DayHeader = line.Heading,
                When = line.Time,
                Source = evt.Source,
                Action = evt.Action,
                SourceLabel = FriendlySource(evt.Source),
                ActionLabel = FriendlyAction(evt.Action),
                Summary = summary,
                Search = (evt.Source + " " + evt.Action + " " + evt.FileName + " " + hits).ToLowerInvariant(),
                FilePath = evt.FilePath ?? "",
                CopyPath = evt.CopyPath ?? "",
                IsClipboard = clipboard,
                Note = clipboard
                    ? FileLocations.ClipboardNote
                    : FileLocations.MissingMessage(evt.FilePath),
                CanOpen = !clipboard && !string.IsNullOrEmpty(evt.FilePath) && File.Exists(evt.FilePath),
                CanShowFolder = !clipboard && FileLocations.ParentIfExists(evt.FilePath) is not null,
                CanOpenCopy = !string.IsNullOrEmpty(evt.CopyPath) && File.Exists(evt.CopyPath),
                CanCheckAgain = !clipboard && !string.IsNullOrEmpty(evt.FilePath) && File.Exists(evt.FilePath)
            });
        }
    }

    private string FriendlyHit(HitCount hit)
    {
        var label = RuleLabel(hit.RuleId);
        return hit.Count <= 1 ? label : label + " ×" + hit.Count;
    }

    private string RuleLabel(string ruleId)
    {
        foreach (var pack in _state.Packs)
        {
            foreach (var rule in pack.Rules)
            {
                if (rule.Id.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
                    return rule.Label;
            }
        }
        return ruleId;
    }

    private static string FriendlySource(string source) => source.ToLowerInvariant() switch
    {
        "clipboard" => TextCatalog.Get("sourceClipboard"),
        "file" => TextCatalog.Get("sourceFile"),
        _ => TextCatalog.Get("sourceActivity")
    };

    private static string FriendlyAction(string action) => action switch
    {
        EventActions.Ignore => TextCatalog.Get("actionIgnored"),
        EventActions.Warn => TextCatalog.Get("actionWarned"),
        EventActions.Allow => TextCatalog.Get("actionChecked"),
        EventActions.Stop => TextCatalog.Get("actionStopped"),
        EventActions.Redact => TextCatalog.Get("actionRedacted"),
        EventActions.CopyMade => TextCatalog.Get("protectedCopy"),
        EventActions.Error => TextCatalog.Get("actionError"),
        EventActions.Dismiss => TextCatalog.Get("actionDismissed"),
        EventActions.FalsePositive => TextCatalog.Get("notSensitive"),
        EventActions.CopyRefused => TextCatalog.Get("actionCopyRefused"),
        _ => action
    };

    private static string IconFor(string category) => category switch
    {
        "personal" => "\uE716",
        "payment" => "\uE8C7",
        "secrets" => "\uE72E",
        "links" => "\uE71B",
        _ => "\uE70F"
    };

    private static void ScheduleReveal(Action action, TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            action();
        };
        timer.Start();
    }

    private void MarkNotSensitive(HitRow row)
    {
        if (row.Dismissed)
            return;
        var remember = !_state.Policy.LockIgnore && row.Value.Length > 0 && row.Value.Length <= 256;
        if (remember)
            _state.AllowList.Add(row.Value);
        row.Dismiss(remember);
        if (_notSensitiveLogged.Add(row.Value))
        {
            _state.Events.Append(_state.Events.FalsePositive(row.Finding.RuleId));
            ReloadEvents();
        }

        RefreshSensitiveCounts();
    }

    private void UndoNotSensitive(HitRow row)
    {
        if (!row.Dismissed)
            return;
        if (row.IsLink)
        {
            if (row.Remembered && row.Finding.LinkDomain.Length > 0)
                _state.TrustedSites.Remove(row.Finding.LinkDomain);
            row.Restore();
            RefreshSensitiveCounts();
            return;
        }

        if (row.Remembered)
            _state.AllowList.Remove(row.Value);
        row.Restore();
        RefreshSensitiveCounts();
    }

    private void TrustSite(HitRow row)
    {
        if (row.Dismissed || !row.IsLink)
            return;
        var domain = row.Finding.LinkDomain;
        var remember = domain.Length > 0 && !_state.Policy.Locks(PolicyKeys.LockSites);
        if (remember)
            _state.TrustedSites.Add(domain);
        row.MarkTrusted(remember);
        RefreshSensitiveCounts();
    }

    private void RefreshSensitiveCounts()
    {
        var kept = AllFindings.Where(row => !row.Exclude).Select(row => row.Finding).ToList();
        var links = Categories.FirstOrDefault(category => category.Id == "links");
        var suspicious = links?.Findings.Count(row => !row.Exclude) ?? 0;
        var summary = ResultGroups.FileSummary(kept, _linkCount, suspicious);
        if (!string.IsNullOrEmpty(_summary) && ResultBody.Contains(_summary, StringComparison.Ordinal))
            ResultBody = ResultBody.Replace(_summary, summary, StringComparison.Ordinal);
        else
            ResultBody = summary;
        _summary = summary;
        OnPropertyChanged(nameof(FindingsCountLabel));
        foreach (var category in Categories)
        {
            if (category.Id == "links")
            {
                var open = category.Findings.Count(row => !row.Exclude);
                var chip = open == 0 ? "None" : "Check";
                category.CountsText = ResultGroups.LinkSummary(_linkCount, open);
                category.Chip = chip;
                category.ChipText = TextCatalog.Get("chip." + chip.ToLowerInvariant());
                continue;
            }

            var rows = category.Findings.Where(row => !row.Exclude).Select(row => row.Finding).ToList();
            var group = ResultGroups.Build(rows).FirstOrDefault(item => item.Id == category.Id);
            category.CountsText = group?.CountsText ?? "";
            category.Chip = group?.Chip ?? "None";
            category.ChipText = group?.ChipText ?? TextCatalog.Get("chip.none");
        }
    }

    private void ApplyScreen()
    {
        ResultVisible = Screen.ResultVisible;
        ResultTitle = Screen.Title;
        ResultBody = Screen.Body;
        CanMakeCopy = Screen.CanMakeCopy;
        CanCopyText = Screen.CanCopyText;
        CopyActionsVisible = Screen.Kind == ScanScreenKind.Success;
        PendingActionsVisible = ResultVisible && !Screen.IsBusy && !CopyActionsVisible;
    }
}

public partial class BatchRow : ObservableObject
{
    public required string FullPath { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _statusCode = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _hasSession;
    public ScanSession? Session { get; set; }
}

public partial class CategoryRow : ObservableObject
{
    public CategoryRow(string id, string title, string description, string countsText, string chip, string chipText, string icon)
    {
        Id = id;
        Title = title;
        Description = description;
        CountsText = countsText;
        Chip = chip;
        ChipText = chipText;
        Icon = icon;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    [ObservableProperty] private string _countsText;
    [ObservableProperty] private string _chip;
    [ObservableProperty] private string _chipText;
    partial void OnChipChanged(string value) => OnPropertyChanged(nameof(BadgeIcon));
    public bool ShowCounts => CountsText.Length > 0;
    partial void OnCountsTextChanged(string value) => OnPropertyChanged(nameof(ShowCounts));
    public string Icon { get; }
    public string BadgeIcon => Chip is "High" or "Found" or "Check" ? "\uE7BA" : "\uE73E";
    public ObservableCollection<HitRow> Findings { get; } = [];
    public bool HasFindings => Findings.Count > 0;
    public string ExpandName => TextCatalog.Format(IsExpanded ? "collapse" : "expand", ("title", Title));

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandName));

    [RelayCommand]
    private void Toggle()
    {
        if (HasFindings)
            IsExpanded = !IsExpanded;
    }
}


public sealed class EventRow
{
    public string DayHeader { get; init; } = "";
    public string When { get; init; } = "";
    public string Source { get; init; } = "";
    public string Action { get; init; } = "";
    public string SourceLabel { get; init; } = "";
    public string ActionLabel { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Search { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string CopyPath { get; init; } = "";
    public bool IsClipboard { get; init; }
    public string Note { get; init; } = "";
    public bool CanOpen { get; init; }
    public bool CanShowFolder { get; init; }
    public bool CanOpenCopy { get; init; }
    public bool CanCheckAgain { get; init; }
}
