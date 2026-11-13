using Peritum.Protect.Core.Scanning;
using CoreText = Peritum.Protect.Core.TextCatalog;

namespace Peritum.Protect.Core.Links;

public sealed class FileLinkReport
{
    public FileLinkReport(int linkCount, IReadOnlyList<Finding> findings)
    {
        LinkCount = linkCount;
        Findings = findings;
    }

    public int LinkCount { get; }
    public IReadOnlyList<Finding> Findings { get; }
}

public static class FileLinkScan
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".eml", ".html", ".htm", ".docx", ".pptx", ".xlsx", ".pdf"
    };

    public static bool Applies(string? extension) =>
        !string.IsNullOrEmpty(extension) && Extensions.Contains(extension);

    public static FileLinkReport Inspect(
        string? text,
        string? from = null,
        string? replyTo = null,
        BrandCatalog? catalog = null,
        Func<string, bool>? trusted = null)
    {
        catalog ??= BrandCatalog.LoadAll();
        var senderDomain = DomainOf(SplitAddress(from).Address);
        var links = string.IsNullOrWhiteSpace(text)
            ? Array.Empty<InspectedLink>()
            : Distinct(LinkInspector.Inspect(text, catalog, LinkScope.Document, senderDomain.Length == 0 ? null : senderDomain));
        var findings = new List<Finding>();
        foreach (var link in links)
        {
            var top = link.Flags
                .Where(flag => flag.Severity >= LinkSeverity.Medium)
                .OrderByDescending(flag => flag.Severity)
                .FirstOrDefault();
            if (top is null)
                continue;
            if (link.RegistrableDomain is { } domain && trusted?.Invoke(domain) == true)
                continue;
            var code = link.Has(LinkCodes.TextMismatch) ? LinkCodes.TextMismatch : top.Code;
            var severity = link.Has(LinkCodes.TextMismatch) ? LinkSeverity.High : top.Severity;
            findings.Add(FindingFor(
                code,
                severity,
                Sentence(link),
                "in this file",
                string.IsNullOrEmpty(link.RegistrableDomain) ? link.Host : link.RegistrableDomain,
                string.IsNullOrEmpty(link.Url) ? link.Raw : link.Url));
        }

        var sender = Sender(from, catalog);
        if (sender is not null)
            findings.Add(sender);
        if (ReplyDiffers(from, replyTo) && findings.Count > 0)
            findings.Add(FindingFor(LinkCodes.ReplyToDiffers, LinkSeverity.Low, "", "Reply-To"));
        return new FileLinkReport(links.Count, findings);
    }

    private static IReadOnlyList<InspectedLink> Distinct(IReadOnlyList<InspectedLink> links)
    {
        var chosen = new List<InspectedLink>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var key = string.IsNullOrEmpty(link.Url) ? link.Raw : link.Url;
            if (!index.TryGetValue(key, out var at))
            {
                index[key] = chosen.Count;
                chosen.Add(link);
                continue;
            }

            if (Rank(link) > Rank(chosen[at]))
                chosen[at] = link;
        }

        return chosen;
    }

    private static int Rank(InspectedLink link)
    {
        var rank = 0;
        foreach (var flag in link.Flags)
            rank = Math.Max(rank, (int)flag.Severity);
        return rank * 10 + link.Flags.Count;
    }

    private static Finding? Sender(string? from, BrandCatalog catalog)
    {
        var (display, address) = SplitAddress(from);
        if (address.Length == 0)
            return null;
        var domain = DomainOf(address);
        if (domain.Length == 0)
            return null;
        foreach (var word in Words(display))
        {
            if (!catalog.TryKeyword(word, out var brand))
                continue;
            if (string.Equals(catalog.Owner(domain), brand, StringComparison.Ordinal))
                continue;
            return FindingFor(LinkCodes.SenderMismatch, LinkSeverity.Medium, domain, "From");
        }

        foreach (var word in Words(display))
        {
            if (!word.Contains('@'))
                continue;
            var shown = DomainOf(word);
            if (shown.Length > 0 && !shown.Equals(domain, StringComparison.OrdinalIgnoreCase))
                return FindingFor(LinkCodes.SenderMismatch, LinkSeverity.Medium, domain, "From");
        }

        return null;
    }

    private static bool ReplyDiffers(string? from, string? replyTo)
    {
        var fromDomain = DomainOf(SplitAddress(from).Address);
        var replyDomain = DomainOf(SplitAddress(replyTo).Address);
        return fromDomain.Length > 0 && replyDomain.Length > 0 && !fromDomain.Equals(replyDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static (string Display, string Address) SplitAddress(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return ("", "");
        var start = header.LastIndexOf('<');
        var end = header.LastIndexOf('>');
        if (start >= 0 && end > start)
            return (header[..start].Trim().Trim('"'), header[(start + 1)..end].Trim());
        return ("", header.Trim());
    }

    private static string DomainOf(string address)
    {
        var at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1)
            return "";
        var host = address[(at + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
        return PublicSuffixList.RegistrableDomain(host) ?? host;
    }

    private static IEnumerable<string> Words(string display)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in display)
        {
            if (char.IsLetterOrDigit(ch) || ch is '@' or '.' or '-' or '_')
            {
                current.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    private static string Sentence(InspectedLink link)
    {
        var goes = string.IsNullOrEmpty(link.RegistrableDomain) ? link.Host : link.RegistrableDomain;
        if (link.Has(LinkCodes.TextMismatch))
        {
            var shows = ShownHost(link.VisibleText);
            if (string.IsNullOrEmpty(shows))
                shows = link.ShownDomain;
            if (string.IsNullOrEmpty(shows))
                shows = "another site";
            return "Shows " + shows + " but goes to " + goes;
        }

        if (!string.IsNullOrEmpty(link.BrandName))
            return "Looks like " + link.BrandName + ", but it goes to " + goes;
        if (goes.Length == 0)
            return "This link needs a closer look";
        return "Goes to " + goes;
    }

    private static string? ShownHost(string? visible)
    {
        if (string.IsNullOrWhiteSpace(visible))
            return null;
        var text = visible.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var rest = text[(scheme + 3)..];
            var cut = rest.IndexOfAny(['/', '?', '#', ' ', '"', '\'']);
            var host = (cut < 0 ? rest : rest[..cut]).Trim().TrimEnd('.');
            var at = host.LastIndexOf('@');
            if (at >= 0)
                host = host[(at + 1)..];
            var port = host.LastIndexOf(':');
            if (port > 0 && host[(port + 1)..].All(char.IsDigit))
                host = host[..port];
            return host.Length == 0 ? null : host;
        }

        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.Contains('.'))
                continue;
            var cut = part.IndexOfAny(['/', '?', '#']);
            var host = (cut < 0 ? part : part[..cut]).Trim().TrimEnd('.');
            if (host.Contains('.'))
                return host;
        }

        return null;
    }

    private static Finding FindingFor(
        string code,
        LinkSeverity severity,
        string sentence,
        string location,
        string linkDomain = "",
        string linkAddress = "") => new()
    {
        RuleId = "link." + code,
        Label = CoreText.Get("linkName." + code),
        DisplayLabel = CoreText.Get("linkName." + code),
        PluralLabel = CoreText.Get("linkName." + code),
        Severity = severity switch
        {
            LinkSeverity.High => Severity.High,
            LinkSeverity.Low => Severity.Info,
            _ => Severity.Warn
        },
        Start = 0,
        Length = 0,
        Placeholder = "LINK",
        Category = "links",
        Snippet = sentence,
        Location = location,
        LinkDomain = linkDomain,
        LinkAddress = linkAddress
    };
}
