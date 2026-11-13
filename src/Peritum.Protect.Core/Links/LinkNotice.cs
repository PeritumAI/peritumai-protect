namespace Peritum.Protect.Core.Links;

public sealed class LinkNoticePlan
{
    public static LinkNoticePlan Empty { get; } = new([]);

    public LinkNoticePlan(IReadOnlyList<InspectedLink> links) => Links = links;

    public IReadOnlyList<InspectedLink> Links { get; }

    public bool Warns => Links.Count > 0;

    public IReadOnlyList<string> ReasonCodes =>
        Links.SelectMany(link => link.Flags.Select(flag => flag.Code)).ToList();

    public IReadOnlyList<string> RememberKeys => Links.Select(link => link.Url).ToList();

    public IReadOnlyList<LinkSentence> Sentences()
    {
        var lines = new List<LinkSentence>();
        foreach (var link in Links)
        {
            foreach (var flag in link.Flags)
            {
                var text = Sentence(flag.Code, link);
                if (text.Length > 0)
                    lines.Add(new LinkSentence(flag.Code, text));
            }

            var highlight = string.IsNullOrEmpty(link.RegistrableDomain) ? link.Host : link.RegistrableDomain;
            var (before, domain, after) = Split(link.Url, highlight);
            lines.Add(new LinkSentence("link", "", before, domain, after));
        }

        return lines;
    }

    public static (string Before, string Domain, string After) Split(string url, string? domain)
    {
        if (string.IsNullOrEmpty(domain))
            return ("", url, "");
        var index = url.IndexOf(domain, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return (url, "", "");
        return (url[..index], url.Substring(index, domain.Length), url[(index + domain.Length)..]);
    }

    private static string Sentence(string code, InspectedLink link)
    {
        var domain = link.RegistrableDomain ?? link.Host;
        var brand = string.IsNullOrWhiteSpace(link.BrandName) ? domain : link.BrandName;
        return code switch
        {
            LinkCodes.Lookalike or LinkCodes.BrandElsewhere => TextCatalog.Format(
                "linkLooksLike", ("brand", brand), ("domain", domain)),
            LinkCodes.UserinfoTrick => TextCatalog.Format("linkUserinfo", ("domain", domain)),
            LinkCodes.DangerousScheme => TextCatalog.Get("linkDangerous"),
            LinkCodes.IpHost => TextCatalog.Get("linkIp"),
            LinkCodes.RiskyDownload => TextCatalog.Get("linkDownload"),
            LinkCodes.Shortener => TextCatalog.Get("linkShortener"),
            LinkCodes.OddPort => TextCatalog.Get("linkOddPort"),
            _ => ""
        };
    }
}

public readonly record struct LinkSentence(string Code, string Text, string Before = "", string Domain = "", string After = "");

public static class LinkNotice
{
    public static LinkNoticePlan Build(
        string? text,
        bool enabled,
        BrandCatalog catalog,
        Func<string, bool>? trusted = null,
        ISet<string>? seen = null)
    {
        if (!enabled || string.IsNullOrWhiteSpace(text))
            return LinkNoticePlan.Empty;
        var links = new List<InspectedLink>();
        foreach (var link in LinkInspector.Inspect(text, catalog, LinkScope.Clipboard))
        {
            if (!link.Warns)
                continue;
            if (link.RegistrableDomain is { } domain && trusted?.Invoke(domain) == true)
                continue;
            if (seen is not null && seen.Contains(link.Url))
                continue;
            links.Add(link);
        }

        return new LinkNoticePlan(links);
    }
}

public static class WarningOrder
{
    public const string Command = "command";
    public const string Link = "link";
    public const string Data = "data";

    public static IReadOnlyList<string> Sections(bool command, bool link, bool data)
    {
        var order = new List<string>(3);
        if (command)
            order.Add(Command);
        if (link)
            order.Add(Link);
        if (data)
            order.Add(Data);
        return order;
    }
}
