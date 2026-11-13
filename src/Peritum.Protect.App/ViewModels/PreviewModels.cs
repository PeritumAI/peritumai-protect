using CommunityToolkit.Mvvm.ComponentModel;

namespace Peritum.Protect.App.ViewModels;

public partial class PreviewLine : ObservableObject
{
    public PreviewLine(IReadOnlyList<PreviewSpan> spans)
    {
        Spans = spans;
    }

    public IReadOnlyList<PreviewSpan> Spans { get; }

    [ObservableProperty] private bool _isCurrent;

    public void SetCurrent(HitRow? current)
    {
        var now = false;
        foreach (var span in Spans)
        {
            if (span.Hit is not null && ReferenceEquals(span.Hit, current))
                now = true;
        }

        IsCurrent = now;
    }
}

public partial class PreviewSpan : ObservableObject
{
    public PreviewSpan(string text, HitRow? hit, string icon)
    {
        Plain = text;
        Hit = hit;
        Icon = icon;
        if (hit is not null)
        {
            hit.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(HitRow.ShownValue) or nameof(HitRow.Revealed))
                    OnPropertyChanged(nameof(Shown));
            };
        }
    }

    public string Plain { get; }
    public HitRow? Hit { get; }
    public string Icon { get; }
    public string Category => Hit?.Finding.Category ?? "";
    public bool IsHit => Hit is not null;
    public string Shown => Hit is null ? Plain : Hit.ShownValue;
}

public partial class PreviewBox : ObservableObject
{
    public PreviewBox(HitRow hit, double x, double y, double width, double height)
    {
        Hit = hit;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        hit.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(HitRow.ShownValue) or nameof(HitRow.Revealed))
                OnPropertyChanged(nameof(Caption));
        };
    }

    public HitRow Hit { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public string Category => Hit.Finding.Category;
    public string Caption => Hit.TypeLabel + " " + Hit.ShownValue;

    [ObservableProperty] private bool _isCurrent;
}
