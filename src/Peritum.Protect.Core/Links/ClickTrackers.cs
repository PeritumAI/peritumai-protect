using System.Text.Json;

namespace Peritum.Protect.Core.Links;

public static class ClickTrackers
{
    private static readonly TrackerFile File = Load();
    private static readonly HashSet<string> Domains = new(File.Domains ?? [], StringComparer.Ordinal);

    public static string AsOf => File.AsOf ?? "";

    public static bool IsTracker(string? host)
    {
        var ascii = Confusables.ToAscii(host);
        if (ascii.Length == 0)
            return false;
        var registrable = PublicSuffixList.RegistrableDomain(ascii) ?? ascii;
        if (Domains.Contains(registrable))
            return true;
        var label = registrable.Split('.')[0];
        foreach (var prefix in File.LabelPrefixes ?? [])
        {
            if (prefix.Length > 0 && label.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        foreach (var glob in File.DomainGlobs ?? [])
        {
            if (Glob(registrable, glob))
                return true;
        }

        return false;
    }

    public static bool ExcusesMismatch(string? destinationHost, string? shownDomain, string? senderDomain, BrandCatalog catalog)
    {
        if (string.IsNullOrEmpty(shownDomain) || string.IsNullOrEmpty(senderDomain))
            return false;
        if (!IsTracker(destinationHost))
            return false;
        var shown = Registrable(shownDomain);
        var sender = Registrable(senderDomain);
        if (shown.Length == 0 || sender.Length == 0)
            return false;
        if (shown.Equals(sender, StringComparison.OrdinalIgnoreCase))
            return true;
        var shownBrand = catalog.Owner(shown);
        var senderBrand = catalog.Owner(sender);
        return shownBrand is not null && shownBrand == senderBrand;
    }

    private static string Registrable(string domain) =>
        PublicSuffixList.RegistrableDomain(domain) ?? Confusables.ToAscii(domain);

    private static bool Glob(string domain, string pattern)
    {
        var left = domain.Split('.');
        var right = pattern.ToLowerInvariant().Split('.');
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!Label(left[i], right[i]))
                return false;
        }

        return true;
    }

    private static bool Label(string value, string pattern)
    {
        var star = pattern.IndexOf('*');
        if (star < 0)
            return value.Equals(pattern, StringComparison.Ordinal);
        var head = pattern[..star];
        var tail = pattern[(star + 1)..];
        return value.Length >= head.Length + tail.Length
            && value.StartsWith(head, StringComparison.Ordinal)
            && value.EndsWith(tail, StringComparison.Ordinal);
    }

    private static TrackerFile Load()
    {
        using var stream = typeof(ClickTrackers).Assembly.GetManifestResourceStream("links.click_trackers.json")
            ?? throw new InvalidOperationException("Missing links.click_trackers.json");
        return JsonSerializer.Deserialize<TrackerFile>(stream, JsonDefaults.Compact) ?? new TrackerFile();
    }

    private sealed class TrackerFile
    {
        public string? AsOf { get; set; }
        public List<string>? Sources { get; set; }
        public List<string>? Domains { get; set; }
        public List<string>? LabelPrefixes { get; set; }
        public List<string>? DomainGlobs { get; set; }
    }
}
