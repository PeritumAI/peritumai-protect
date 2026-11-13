namespace Peritum.Protect.Core.Links;

public static class LinkCopyNote
{
    public static string? ForEmail(string? text, string? from, BrandCatalog? catalog = null)
    {
        catalog ??= BrandCatalog.LoadAll();
        var sender = SenderDomain(from);
        var links = string.IsNullOrWhiteSpace(text)
            ? Array.Empty<InspectedLink>()
            : LinkInspector.Inspect(text, catalog, LinkScope.Document, sender);
        var risky = new List<InspectedLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            if (!link.Warns)
                continue;
            if (!seen.Add(link.Url))
                continue;
            risky.Add(link);
        }

        if (risky.Count == 0)
            return null;
        if (risky.Count == 1)
            return "Peritum note: this email contains 1 suspicious link (" + Detail(risky[0], catalog) + ").";
        return "Peritum note: this email contains " + risky.Count + " suspicious links.";
    }

    private static string Detail(InspectedLink link, BrandCatalog catalog)
    {
        var goes = string.IsNullOrEmpty(link.RegistrableDomain) ? link.Host : link.RegistrableDomain;
        var brand = link.BrandName;
        if (string.IsNullOrEmpty(brand) && !string.IsNullOrEmpty(link.ShownDomain))
            brand = catalog.Owner(link.ShownDomain);
        if (!string.IsNullOrEmpty(brand))
            return "looks like " + brand + ", goes to " + goes;
        if (!string.IsNullOrEmpty(link.ShownDomain))
            return "shows " + link.ShownDomain + ", goes to " + goes;
        return "goes to " + goes;
    }

    private static string? SenderDomain(string? from)
    {
        if (string.IsNullOrWhiteSpace(from))
            return null;
        var start = from.LastIndexOf('<');
        var end = from.LastIndexOf('>');
        var address = start >= 0 && end > start ? from[(start + 1)..end].Trim() : from.Trim();
        var at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1)
            return null;
        var host = address[(at + 1)..].Trim().TrimEnd('>').ToLowerInvariant();
        return PublicSuffixList.RegistrableDomain(host) ?? host;
    }
}
