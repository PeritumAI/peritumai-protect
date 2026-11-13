using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Links;

namespace Peritum.Protect.App.ViewModels;

public enum LinkChoice
{
    Clear,
    Keep,
    Trust,
    Redact
}

public sealed class LinkDisplay
{
    public LinkDisplay(string before, string domain, string after)
    {
        Before = before;
        Domain = domain;
        After = after;
    }

    public string Before { get; }
    public string Domain { get; }
    public string After { get; }
}

public partial class LinkWarningViewModel : ObservableObject
{
    public LinkWarningViewModel(LinkNoticePlan plan, bool command, bool data)
    {
        Headline = TextCatalog.Get("linkWarnTitle");
        CommandLine = command ? TextCatalog.Get("linkCommand") : "";
        ShowCommand = command;
        ShowData = data;
        var lines = new List<string>();
        var links = new List<LinkDisplay>();
        var domains = new List<string>();
        foreach (var sentence in plan.Sentences())
        {
            if (sentence.Code == "link")
                links.Add(new LinkDisplay(sentence.Before, sentence.Domain, sentence.After));
            else if (sentence.Text.Length > 0)
                lines.Add(sentence.Text);
        }

        foreach (var link in plan.Links)
        {
            if (!string.IsNullOrEmpty(link.RegistrableDomain))
                domains.Add(link.RegistrableDomain);
        }

        Lines = lines;
        Links = links;
        Domains = domains;
        CanTrust = domains.Count > 0;
        RememberKeys = plan.RememberKeys;
    }

    public string Headline { get; }
    public string CommandLine { get; }
    public bool ShowCommand { get; }
    public bool ShowData { get; }
    public bool CanTrust { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<LinkDisplay> Links { get; }
    public IReadOnlyList<string> Domains { get; }
    public IReadOnlyList<string> RememberKeys { get; }
    public string ClearLabel { get; } = TextCatalog.Get("linkClear");
    public string KeepLabel { get; } = TextCatalog.Get("linkKeep");
    public string TrustLabel { get; } = TextCatalog.Get("linkTrust");
    public string WhyLabel { get; } = TextCatalog.Get("linkWhy");
    public string RedactLabel { get; } = TextCatalog.Get("redact");

    public event Action<LinkChoice>? Decided;
    public event Action? WhyRequested;

    [RelayCommand]
    private void Clear() => Decided?.Invoke(LinkChoice.Clear);

    [RelayCommand]
    private void Keep() => Decided?.Invoke(LinkChoice.Keep);

    [RelayCommand]
    private void Trust() => Decided?.Invoke(LinkChoice.Trust);

    [RelayCommand]
    private void Redact() => Decided?.Invoke(LinkChoice.Redact);

    [RelayCommand]
    private void Why() => WhyRequested?.Invoke();
}
