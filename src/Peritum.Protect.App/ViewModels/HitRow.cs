using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.ViewModels;

public partial class HitRow : ObservableObject
{
    public static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(10);
    private readonly Action<string>? _copy;
    private readonly Action<Action, TimeSpan>? _schedule;
    private int _revealGeneration;

    public HitRow(
        Finding finding,
        string value,
        int index = 0,
        int total = 0,
        Action<string>? copy = null,
        Action<Action, TimeSpan>? schedule = null)
    {
        Finding = finding;
        Value = value;
        var linkRow = finding.Category == "links";
        Masked = linkRow ? value : ValueMask.Mask(value, finding.RuleId);
        TypeLabel = RuleLabels.Name(finding);
        Location = finding.Location;
        Context = finding.Context;
        Index = index;
        Total = total;
        IndexLine = total > 0 ? TextCatalog.Format("findingOf", ("index", index), ("total", total)) : "";
        var place = string.IsNullOrWhiteSpace(finding.Location) ? TextCatalog.Get("locationUnknown") : finding.Location;
        AccessibleName = TypeLabel + ", " + place;
        ShowAccess = TextCatalog.Format("showAccess", ("name", AccessibleName));
        NotSensitiveAccess = TextCatalog.Format("notSensitiveAccess", ("name", AccessibleName));
        CopyLocationAccess = TextCatalog.Format("copyLocationAccess", ("name", AccessibleName));
        TrustAccess = TextCatalog.Format("trustAccess", ("name", AccessibleName));
        CopyAddressAccess = TextCatalog.Format("copyAddressAccess", ("name", AccessibleName));
        Line = TypeLabel + ": " + finding.Snippet;
        var why = RiskExplanations.ForFinding(finding);
        WhyText = why?.Text ?? "";
        WhyTip = why?.Tip ?? "";
        var placeWhy = RiskExplanations.ForPlace(finding);
        PlaceWhy = placeWhy is null ? "" : placeWhy.Paragraph;
        _copy = copy;
        _schedule = schedule;
    }

    public Finding Finding { get; }
    public string Value { get; }
    public string Masked { get; }
    public string TypeLabel { get; }
    public string Location { get; }
    public string Context { get; }
    public int Index { get; }
    public int Total { get; }
    public string IndexLine { get; }
    public string AccessibleName { get; }
    public string ShowAccess { get; }
    public string NotSensitiveAccess { get; }
    public string CopyLocationAccess { get; }
    public string TrustAccess { get; }
    public string CopyAddressAccess { get; }
    public string Line { get; }
    public string WhyText { get; }
    public string WhyTip { get; }
    public string PlaceWhy { get; }
    public bool IsLink => Finding.Category == "links";
    public bool HasWhy => WhyText.Length > 0;
    public bool ShowInlineWhy => IsLink && HasWhy;
    public bool HasPlaceWhy => PlaceWhy.Length > 0;
    public string ShownValue => Revealed ? Value : Masked;
    public bool IsMuted => Dismissed;
    public bool ShowNotSensitive => !Dismissed && !IsLink;
    public bool ShowTrust => !Dismissed && IsLink;
    public bool ShowUndo => Dismissed;
    public bool ShowCopyLocation => !IsLink;
    public bool ShowCopyAddress => IsLink;
    public bool HasStatus => StatusText.Length > 0;
    public double RowOpacity => Dismissed ? 0.55 : 1;

    public event Action<HitRow>? MarkedNotSensitive;
    public event Action<HitRow>? TrustRequested;
    public event Action<HitRow>? UndoRequested;

    [ObservableProperty] private bool _exclude;
    [ObservableProperty] private bool _revealed;
    [ObservableProperty] private bool _dismissed;
    [ObservableProperty] private bool _remembered;
    [ObservableProperty] private string _statusText = "";

    partial void OnRevealedChanged(bool value) => OnPropertyChanged(nameof(ShownValue));

    partial void OnDismissedChanged(bool value)
    {
        Exclude = value;
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(ShowNotSensitive));
        OnPropertyChanged(nameof(ShowTrust));
        OnPropertyChanged(nameof(ShowUndo));
        OnPropertyChanged(nameof(RowOpacity));
    }

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    public void Dismiss(bool remembered)
    {
        Remembered = remembered;
        StatusText = TextCatalog.Get(remembered ? "markedNotSensitive" : "cannotRemember");
        Dismissed = true;
    }

    public void Restore()
    {
        Remembered = false;
        StatusText = "";
        Dismissed = false;
    }

    public void MarkTrusted(bool remembered)
    {
        Remembered = remembered;
        StatusText = TextCatalog.Get(remembered ? "trustedLink" : "cannotRemember");
        Dismissed = true;
    }

    [RelayCommand]
    private void NotSensitive() => MarkedNotSensitive?.Invoke(this);

    [RelayCommand]
    private void Trust() => TrustRequested?.Invoke(this);

    [RelayCommand]
    private void Undo() => UndoRequested?.Invoke(this);

    [RelayCommand]
    private void Show()
    {
        Revealed = true;
        var generation = ++_revealGeneration;
        _schedule?.Invoke(() =>
        {
            if (generation == _revealGeneration)
                Revealed = false;
        }, RevealDuration);
    }

    [RelayCommand]
    private void CopyAddress()
    {
        var address = string.IsNullOrWhiteSpace(Finding.LinkAddress) ? Finding.LinkDomain : Finding.LinkAddress;
        if (!string.IsNullOrWhiteSpace(address))
            _copy?.Invoke(address);
    }

    [RelayCommand]
    private void CopyLocation()
    {
        if (!string.IsNullOrWhiteSpace(Location))
            _copy?.Invoke(Location);
    }
}
