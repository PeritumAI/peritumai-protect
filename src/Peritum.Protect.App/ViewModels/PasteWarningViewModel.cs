using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peritum.Protect.App.Services;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.ViewModels;

public enum PasteChoice
{
    Ignore,
    Stop,
    Redact,
    AlwaysIgnore,
    Dismiss
}

public partial class PasteWarningViewModel : ObservableObject
{
    private readonly AllowListStore? _remembered;
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);
    private bool _closed;

    public PasteWarningViewModel(
        ScanSession session,
        string? processName,
        string defaultAction,
        string stopBehavior = "restore-previous",
        bool allowAlwaysIgnore = true,
        AllowListStore? remembered = null)
    {
        Session = session;
        CanAlwaysIgnore = allowAlwaysIgnore;
        _remembered = remembered;
        IgnoreIsDefault = defaultAction == "ignore";
        StopIsDefault = defaultAction == "stop";
        RedactIsDefault = defaultAction == "redact";
        StopLabel = stopBehavior == "clear-clipboard" ? UiText.ClearClipboard : UiText.UndoCopy;
        Headline = UiText.PopupTitle;
        ProcessLine = TextCatalog.Format("inFront", ("app", FriendlyProcessNames.Display(processName)));
        RedactLabel = LabelFor(session);
        FillHits(session);
    }

    public string LeaveLabel { get; } = UiText.LeaveAsIs;
    public string RedactLabel { get; private set; } = UiText.Redact;
    public string StopLabel { get; }
    public bool CanAlwaysIgnore { get; }
    public bool ShowIgnoreLock => !CanAlwaysIgnore;
    public string DetailsLabel { get; } = UiText.Details;
    public string NotSensitiveLabel { get; } = UiText.NotSensitive;
    public string Summary { get; private set; } = "";

    public event Action<string>? NotSensitive;

    public void Retarget(ScanSession session, string? processName)
    {
        if (_closed)
            return;
        Session = session;
        Headline = UiText.PopupTitle;
        ProcessLine = TextCatalog.Format("inFront", ("app", FriendlyProcessNames.Display(processName)));
        RedactLabel = LabelFor(session);
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(ProcessLine));
        OnPropertyChanged(nameof(RedactLabel));
        Hits.Clear();
        _logged.Clear();
        FillHits(session);
        Selected = null;
        Message = "";
    }

    private static string LabelFor(ScanSession session) =>
        session.ClipboardImage is not null ? UiText.RedactedImage : UiText.Redact;

    private void FillHits(ScanSession session)
    {
        foreach (var finding in session.Result.Findings)
        {
            var value = finding.Start >= 0 && finding.Start + finding.Length <= session.Text.Length
                ? session.Text.Substring(finding.Start, finding.Length)
                : finding.Snippet;
            var row = new HitRow(finding, value);
            row.MarkedNotSensitive += MarkNotSensitive;
            row.UndoRequested += UndoNotSensitive;
            Hits.Add(row);
        }

        RefreshSummary();
    }

    private void MarkNotSensitive(HitRow row)
    {
        if (row.Dismissed || _closed)
            return;
        var remember = CanAlwaysIgnore && row.Value.Length > 0 && row.Value.Length <= 256;
        if (remember)
            _remembered?.Add(row.Value);
        row.Dismiss(remember);
        if (_logged.Add(row.Value))
            NotSensitive?.Invoke(row.Finding.RuleId);
        RefreshSummary();
        if (Hits.Count > 0 && Hits.All(hit => hit.Dismissed))
            Close(PasteChoice.Ignore);
    }

    private void UndoNotSensitive(HitRow row)
    {
        if (!row.Dismissed || _closed)
            return;
        if (row.Remembered)
            _remembered?.Remove(row.Value);
        row.Restore();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        Summary = ResultGroups.Summary(Included.Select(hit => hit.Finding).ToList());
        OnPropertyChanged(nameof(Summary));
    }

    public ScanSession Session { get; private set; }
    public bool IgnoreIsDefault { get; }
    public bool StopIsDefault { get; }
    public bool RedactIsDefault { get; }
    public string Headline { get; private set; }
    public string ProcessLine { get; private set; }
    public ObservableCollection<HitRow> Hits { get; } = [];

    [ObservableProperty] private HitRow? _selected;
    [ObservableProperty] private string _message = "";

    public event Action<PasteChoice>? Decided;
    public event Action? DetailsRequested;

    public IReadOnlyList<HitRow> Included => Hits.Where(h => !h.Exclude).ToList();

    [RelayCommand]
    private void Ignore() => Close(PasteChoice.Ignore);

    [RelayCommand]
    private void Stop() => Close(PasteChoice.Stop);

    [RelayCommand]
    private void Redact() => Close(PasteChoice.Redact);

    [RelayCommand]
    private void Details() => DetailsRequested?.Invoke();

    public void Dismiss() => Close(PasteChoice.Dismiss);

    private void Close(PasteChoice choice)
    {
        if (_closed)
            return;
        _closed = true;
        Decided?.Invoke(choice);
    }
}
