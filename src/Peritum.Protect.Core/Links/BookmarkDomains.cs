using System.Net;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Links;

public static class BookmarkDomains
{
    private static readonly Regex Href = new(
        @"href\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static IReadOnlyList<string> FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return [];
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Match match in Href.Matches(html))
            {
                var raw = First(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
                var domain = Normalize(WebUtility.HtmlDecode(raw));
                if (domain is null || !seen.Add(domain))
                    continue;
                found.Add(domain);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return found;
        }

        return found;
    }

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = raw.Trim();
        string host;
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var name = text[..scheme];
            if (!name.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("https", StringComparison.OrdinalIgnoreCase))
                return null;
            var rest = text[(scheme + 3)..];
            var cut = rest.IndexOfAny(['/', '?', '#']);
            host = cut < 0 ? rest : rest[..cut];
        }
        else
        {
            if (text.Contains(':') || text.Contains(' '))
                return null;
            var cut = text.IndexOfAny(['/', '?', '#']);
            host = cut < 0 ? text : text[..cut];
        }

        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        var at = host.LastIndexOf('@');
        if (at >= 0)
            host = host[(at + 1)..];
        var port = host.LastIndexOf(':');
        if (port > 0 && host[(port + 1)..].All(char.IsDigit))
            host = host[..port];
        if (host.Length == 0 || !host.Contains('.'))
            return null;
        return PublicSuffixList.RegistrableDomain(host);
    }

    private static string First(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }
}
