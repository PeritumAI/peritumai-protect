using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Links;

public static class LinkInspector
{
    private static readonly HashSet<string> DangerousSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "javascript", "data", "vbscript", "search-ms",
        "ms-msdt", "ms-appinstaller", "ms-officecmd", "ms-cxh", "ms-cxh-full"
    };

    private static readonly HashSet<string> RiskyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".js", ".vbs", ".hta", ".lnk", ".iso", ".img", ".msi", ".bat", ".cmd", ".ps1"
    };

    private static readonly HashSet<string> Shorteners = new(StringComparer.Ordinal)
    {
        "bit.ly", "bitly.com", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "is.gd", "buff.ly",
        "adf.ly", "tiny.cc", "cutt.ly", "shorturl.at", "rebrand.ly", "t.ly", "rb.gy", "bl.ink",
        "db.tt", "v.gd", "soo.gd", "bc.vc", "short.gy", "tiny.one", "clck.ru", "s.id"
    };

    private static readonly HashSet<string> BareTlds = new(StringComparer.Ordinal)
    {
        "com", "net", "org", "edu", "gov", "mil", "int", "info", "biz", "app", "dev", "io", "ai",
        "co", "me", "tv", "cc", "ly", "gl", "xyz", "online", "site", "store", "shop", "cloud",
        "example", "test", "invalid", "localhost",
        "uk", "de", "fr", "it", "es", "nl", "pl", "be", "at", "ch", "se", "no", "dk", "fi", "pt",
        "ie", "us", "ca", "au", "nz", "br", "in", "jp", "cn", "kr", "ru", "ua", "tr", "za", "mx",
        "ar", "sg", "hk", "tw", "id", "th", "my", "ph", "vn", "ae", "sa", "eg", "ng", "ke", "il",
        "pk", "rs", "hr", "si", "ba", "me", "mk", "hu", "ro", "cz", "sk", "bg", "gr", "lu", "lt",
        "lv", "ee", "cy", "mt"
    };

    private static readonly Regex Anchor = new(
        @"<a\b[^>]*?\bhref\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s>]+))[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex Markdown = new(
        @"\[([^\]\n]*)\]\(\s*([^)\s]+)(?:\s+""[^""]*"")?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex SchemeUrl = new(
        @"\b(?:[a-z][a-z0-9+.-]*):(?:\[[^\]]+\]|[^[\s<>""')\]])+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex BareDomain = new(
        @"(?<!@)\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+(?:[a-z]{2,24})(?:/[^\s<>""')\]]*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static IReadOnlyList<InspectedLink> Inspect(string? text) =>
        Inspect(text, BrandCatalog.LoadAll());

    public static IReadOnlyList<InspectedLink> Inspect(
        string? text,
        BrandCatalog catalog,
        LinkScope scope = LinkScope.Document,
        string? senderDomain = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var found = new List<InspectedLink>();
        foreach (var raw in Extract(text))
            found.Add(Evaluate(raw.Url, raw.Visible, catalog, scope, senderDomain));
        return found;
    }

    public static InspectedLink InspectUrl(string url, string? visibleText, BrandCatalog? catalog = null) =>
        Evaluate(url, string.IsNullOrWhiteSpace(visibleText) ? null : visibleText.Trim(), catalog ?? BrandCatalog.LoadAll(), LinkScope.Document);

    public readonly record struct UrlRange(int Start, int Length);

    public static IReadOnlyList<UrlRange> Locate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var found = new List<UrlRange>();
        void Take(int start, int length)
        {
            if (length <= 0)
                return;
            var end = start + length;
            foreach (var span in found)
            {
                if (start < span.Start + span.Length && end > span.Start)
                    return;
            }

            found.Add(new UrlRange(start, length));
        }

        try
        {
            foreach (Match match in SchemeUrl.Matches(text))
                Take(match.Index, match.Length);
            foreach (Match match in BareDomain.Matches(text))
            {
                if (match.Index > 0 && text[match.Index - 1] == '@')
                    continue;
                if (!BareAllowed(match.Value))
                    continue;
                Take(match.Index, match.Length);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return found;
        }

        return found;
    }

    private static InspectedLink Evaluate(string rawUrl, string? visible, BrandCatalog catalog, LinkScope scope, string? senderDomain = null)
    {
        var unwrapped = Unwrap(rawUrl.Trim());
        var parts = Split(unwrapped);
        var flags = new List<LinkFlag>();
        string? brand = null;
        var asciiHost = Confusables.ToAscii(parts.Host);
        var ip = IsIp(parts.Host);
        var registrable = ip || string.IsNullOrEmpty(asciiHost) ? null : PublicSuffixList.RegistrableDomain(asciiHost);
        var web = parts.Scheme is "http" or "https";

        if (DangerousSchemes.Contains(parts.Scheme) || RemoteFile(parts))
            flags.Add(new LinkFlag(LinkCodes.DangerousScheme, LinkSeverity.High));

        if (web && parts.UserInfo.Length > 0)
            flags.Add(new LinkFlag(LinkCodes.UserinfoTrick, LinkSeverity.High));

        if (web && (ip || parts.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            var kind = ClassifyHost(parts.Host);
            if (kind == IpKind.Public)
                flags.Add(new LinkFlag(LinkCodes.IpHost, LinkSeverity.Medium));
            else if (kind == IpKind.Private)
                flags.Add(new LinkFlag(LinkCodes.IpHost, LinkSeverity.Low));
        }

        var doubled = false;
        var script = false;
        var risky = web && RiskyFile(parts.Path, out doubled, out script);

        if (web && !IsIp(parts.Host) && registrable is not null && !catalog.IsOfficial(registrable))
        {
            if (TryLookalike(parts.Host, registrable, catalog, out var lookalikeBrand))
            {
                flags.Add(new LinkFlag(LinkCodes.Lookalike, LinkSeverity.High));
                brand ??= lookalikeBrand;
            }

            if (TryElsewhere(parts.Host, registrable, parts.Path, catalog, out var elsewhereBrand, out var elsewhereSeverity))
            {
                flags.Add(new LinkFlag(LinkCodes.BrandElsewhere, elsewhereSeverity));
                brand ??= elsewhereBrand;
            }
        }

        if (web && registrable is not null && Shorteners.Contains(registrable))
            flags.Add(new LinkFlag(LinkCodes.Shortener, LinkSeverity.Low));

        if (web && parts.Port is int port && port != DefaultPort(parts.Scheme))
            flags.Add(new LinkFlag(LinkCodes.OddPort, LinkSeverity.Low));

        if (VisibleDomain(visible) is string shown
            && !string.IsNullOrEmpty(registrable)
            && !shown.Equals(registrable, StringComparison.OrdinalIgnoreCase)
            && !ClickTrackers.ExcusesMismatch(parts.Host, shown, senderDomain, catalog))
            flags.Add(new LinkFlag(LinkCodes.TextMismatch, LinkSeverity.High));

        if (risky)
            AddDownload(flags, scope, doubled, script);

        return new InspectedLink
        {
            Raw = rawUrl,
            Url = unwrapped,
            Scheme = parts.Scheme,
            Host = parts.Host,
            UserInfo = parts.UserInfo,
            Path = parts.Path,
            Port = parts.Port,
            RegistrableDomain = registrable,
            VisibleText = visible,
            ShownDomain = VisibleDomain(visible),
            BrandName = brand,
            Flags = flags
        };
    }

    private static bool TryLookalike(string host, string registrable, BrandCatalog catalog, out string? brand)
    {
        brand = null;
        var unicode = Confusables.ToUnicode(host);
        var mixed = false;
        foreach (var label in unicode.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Confusables.MixedScript(label))
                mixed = true;
        }

        var foldedHost = Confusables.Fold(unicode);
        var foldedRegistrable = PublicSuffixList.RegistrableDomain(Confusables.ToAscii(foldedHost));
        if (foldedRegistrable is not null
            && catalog.IsOfficial(foldedRegistrable)
            && !foldedRegistrable.Equals(registrable, StringComparison.Ordinal))
        {
            brand = catalog.Owner(foldedRegistrable);
            return true;
        }

        var candidate = Confusables.Fold(RegistrableLabel(unicode, registrable));
        if (candidate.Length < 5)
            return mixed;

        foreach (var (label, name) in catalog.LongLabels)
        {
            if (candidate == label || Visual(candidate, false) == label || Visual(candidate, true) == label)
            {
                brand = name;
                return true;
            }

            if (WithinOne(candidate, label))
            {
                brand = name;
                return true;
            }
        }

        if (TryHyphenTypo(unicode, catalog, out brand))
            return true;

        return mixed;
    }

    private static bool TryHyphenTypo(string unicodeHost, BrandCatalog catalog, out string? brand)
    {
        brand = null;
        foreach (var label in unicodeHost.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!label.Contains('-') && !label.Contains('_'))
                continue;
            foreach (var part in label.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length < 5)
                    continue;
                var folded = Confusables.Fold(part);
                foreach (var (known, name) in catalog.LongLabels)
                {
                    if (folded == known)
                        continue;
                    if (Visual(folded, false) == known || Visual(folded, true) == known || WithinOne(folded, known))
                    {
                        brand = name;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string RegistrableLabel(string unicodeHost, string registrable)
    {
        var ascii = Confusables.ToAscii(unicodeHost);
        var suffixLength = ascii.Length - registrable.Length;
        var unicodeLabels = Confusables.ToUnicode(unicodeHost).Split('.', StringSplitOptions.RemoveEmptyEntries);
        var asciiLabels = ascii.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (unicodeLabels.Length == 0 || asciiLabels.Length == 0)
            return registrable.Split('.')[0];
        var skip = asciiLabels.Length - registrable.Split('.').Length;
        if (skip < 0 || skip >= unicodeLabels.Length)
            return unicodeLabels[0];
        return unicodeLabels[skip];
    }

    private static string Visual(string label, bool oneAsI)
    {
        var value = label.Replace("rn", "m", StringComparison.Ordinal).Replace("vv", "w", StringComparison.Ordinal);
        value = value.Replace('0', 'o');
        return oneAsI ? value.Replace('1', 'i') : value.Replace('1', 'l');
    }

    private static bool WithinOne(string left, string right)
    {
        var n = left.Length;
        var m = right.Length;
        if (Math.Abs(n - m) > 1 || left == right)
            return false;
        var rows = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
            rows[i, 0] = i;
        for (var j = 0; j <= m; j++)
            rows[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                var best = Math.Min(Math.Min(rows[i - 1, j] + 1, rows[i, j - 1] + 1), rows[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && left[i - 1] == right[j - 2] && left[i - 2] == right[j - 1])
                    best = Math.Min(best, rows[i - 2, j - 2] + 1);
                rows[i, j] = best;
            }
        }

        return rows[n, m] == 1;
    }

    private static bool TryElsewhere(
        string host,
        string registrable,
        string path,
        BrandCatalog catalog,
        out string? brand,
        out LinkSeverity severity)
    {
        brand = null;
        severity = LinkSeverity.High;
        var ascii = Confusables.ToAscii(host);
        var left = ascii.EndsWith("." + registrable, StringComparison.Ordinal)
            ? ascii[..^(registrable.Length + 1)]
            : "";
        if (ContainsOfficial(left, catalog, out brand))
            return true;

        foreach (var label in left.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TokenHit(label, catalog, out brand))
                return true;
        }

        var own = registrable.Split('.')[0];
        if ((own.Contains('-') || own.Contains('_')) && TokenHit(own, catalog, out brand))
            return true;

        var decodedPath = WebUtility.UrlDecode(path);
        foreach (var segment in decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!ContainsOfficial(segment, catalog, out brand) && !catalog.IsOfficial(segment))
                continue;
            if (brand is null && catalog.IsOfficial(segment))
                brand = catalog.Owner(segment);
            severity = LinkSeverity.Medium;
            return true;
        }

        return false;
    }

    private static bool ContainsOfficial(string dotted, BrandCatalog catalog, out string? brand)
    {
        brand = null;
        if (dotted.Length == 0)
            return false;
        var labels = dotted.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < labels.Length; i++)
        {
            var suffix = string.Join('.', labels[i..]);
            if (!catalog.IsOfficial(suffix))
                continue;
            brand = catalog.Owner(suffix);
            return true;
        }

        return false;
    }

    private static bool TokenHit(string value, BrandCatalog catalog, out string? brand)
    {
        brand = null;
        var lower = value.ToLowerInvariant();
        var tokens = lower.Split(['-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (catalog.TryKeyword(token, out var name))
            {
                brand = name;
                return true;
            }

            if (token.Length >= 8 && catalog.TryDomainLabel(token, out name))
            {
                brand = name;
                return true;
            }
        }

        if (lower.Contains('-') || lower.Contains('_'))
        {
            var joined = lower.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
            if (catalog.TryKeyword(joined, out var name) && !catalog.IsCommonWord(joined))
            {
                brand = name;
                return true;
            }
        }

        return false;
    }

    private static bool RemoteFile(LinkParts parts)
    {
        if (!parts.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            return false;
        if (parts.Host.Length == 0)
            return false;
        return !parts.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && parts.Host != "127.0.0.1"
            && parts.Host != "::1";
    }

    private static readonly HashSet<string> BenignExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "jpg", "jpeg", "png", "gif", "txt", "zip", "html", "htm", "csv", "rtf"
    };

    private static bool RiskyFile(string path, out bool doubled, out bool script)
    {
        doubled = false;
        script = false;
        var file = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        var cut = file.IndexOfAny(['?', '#']);
        if (cut >= 0)
            file = file[..cut];
        foreach (var ext in RiskyExtensions)
        {
            if (!file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                continue;
            var stem = file[..^ext.Length];
            var dot = stem.LastIndexOf('.');
            if (dot >= 0 && BenignExtensions.Contains(stem[(dot + 1)..]))
                doubled = true;
            script = ext.Equals(".js", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        return false;
    }

    private static void AddDownload(List<LinkFlag> flags, LinkScope scope, bool doubled, bool script)
    {
        if (doubled)
        {
            flags.Add(new LinkFlag(LinkCodes.RiskyDownload, LinkSeverity.High));
            return;
        }

        if (scope == LinkScope.Document)
        {
            flags.Add(new LinkFlag(LinkCodes.RiskyDownload, LinkSeverity.Medium));
            return;
        }

        var combined = flags.Count > 0;
        if (script && !combined)
            return;
        flags.Add(new LinkFlag(LinkCodes.RiskyDownload, combined ? LinkSeverity.Medium : LinkSeverity.Low));
    }

    private enum IpKind { None, Loopback, Private, Public }

    private static IpKind ClassifyHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return IpKind.Loopback;
        if (!TryNumericAddress(host, out var address))
            return IpKind.None;
        if (IPAddress.IsLoopback(address))
            return IpKind.Loopback;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                return IpKind.Private;
            return IpKind.Public;
        }

        if (bytes.Length != 4)
            return IpKind.Public;
        if (bytes[0] == 127)
            return IpKind.Loopback;
        if (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254))
            return IpKind.Private;
        if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            return IpKind.Private;
        return IpKind.Public;
    }

    private static bool TryNumericAddress(string host, out IPAddress address)
    {
        address = IPAddress.None;
        var value = host.Trim().Trim('[', ']');
        if (IPAddress.TryParse(value, out var parsed) && parsed.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            address = parsed;
            return true;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexWhole))
        {
            address = FromUInt(hexWhole);
            return true;
        }

        if (value.Length >= 2 && value.All(char.IsDigit) && ulong.TryParse(value, out var whole) && whole <= uint.MaxValue)
        {
            address = FromUInt((uint)whole);
            return true;
        }

        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;
        var bytes = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            if (!TryPartValue(parts[i], out bytes[i]))
                return false;
        }

        address = new IPAddress(bytes);
        return true;
    }

    private static IPAddress FromUInt(uint value) =>
        new(new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value });

    private static bool TryPartValue(string part, out byte value)
    {
        value = 0;
        if (part.Length == 0)
            return false;
        if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) || hex > 255)
                return false;
            value = (byte)hex;
            return true;
        }

        if (part.Length > 1 && part[0] == '0' && part.All(char.IsDigit))
        {
            try
            {
                var octal = Convert.ToInt32(part, 8);
                if (octal > 255)
                    return false;
                value = (byte)octal;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsIp(string host)
    {
        if (host.Length == 0)
            return false;
        var value = host.Trim().Trim('[', ']');
        if (IPAddress.TryParse(value, out var address) && address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
            return true;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && value.Length is > 2 and <= 10
            && uint.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return true;
        if (value.Length >= 2 && value.All(char.IsDigit) && ulong.TryParse(value, out var whole) && whole <= uint.MaxValue)
            return true;
        var parts = value.Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var part in parts)
        {
            if (!IpPart(part))
                return false;
        }

        return true;
    }

    private static bool IpPart(string part)
    {
        if (part.Length == 0)
            return false;
        if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(part[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) && hex <= 255;
        if (part.Length > 1 && part[0] == '0' && part.All(char.IsDigit))
        {
            try
            {
                return Convert.ToInt32(part, 8) <= 255;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        return byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static int DefaultPort(string scheme) => scheme == "https" ? 443 : 80;

    private static string Unwrap(string url)
    {
        var current = url;
        for (var i = 0; i < 3; i++)
        {
            var next = UnwrapOnce(current);
            if (next is null || next.Equals(current, StringComparison.Ordinal))
                break;
            current = next;
        }

        return current;
    }

    private static string? UnwrapOnce(string url)
    {
        var parts = Split(url);
        var host = Confusables.ToAscii(parts.Host);
        if (host.Equals("safelinks.protection.outlook.com", StringComparison.Ordinal)
            || host.EndsWith(".safelinks.protection.outlook.com", StringComparison.Ordinal))
            return QueryUrl(parts.Query, "url");

        if (host is "urldefense.proofpoint.com" or "urldefense.com")
        {
            if (parts.Path.Contains("/v2/", StringComparison.OrdinalIgnoreCase))
            {
                var encoded = QueryRaw(parts.Query, "u");
                if (encoded is not null)
                    return ProofpointV2(encoded);
            }

            if (parts.Path.Contains("/v3/", StringComparison.OrdinalIgnoreCase))
                return ProofpointV3(url);
        }

        if (host.EndsWith("mimecast.com", StringComparison.Ordinal) || host.EndsWith("mimecastprotect.com", StringComparison.Ordinal))
            return QueryUrl(parts.Query, "url") ?? EmbeddedHttp(url);

        if ((host == "google.com" || host.EndsWith(".google.com", StringComparison.Ordinal))
            && parts.Path.StartsWith("/url", StringComparison.OrdinalIgnoreCase))
            return QueryUrl(parts.Query, "q") ?? QueryUrl(parts.Query, "url");

        return null;
    }

    private static string? QueryUrl(string query, string key)
    {
        var value = QueryRaw(query, key);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var decoded = value;
        for (var i = 0; i < 2; i++)
        {
            var next = WebUtility.UrlDecode(decoded);
            if (next == decoded)
                break;
            decoded = next;
        }

        return decoded.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || decoded.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? decoded
            : null;
    }

    private static string? QueryRaw(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var cut = pair.IndexOf('=');
            var name = cut < 0 ? pair : pair[..cut];
            if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            return cut < 0 ? "" : pair[(cut + 1)..];
        }

        return null;
    }

    private static string ProofpointV2(string encoded)
    {
        var sb = new StringBuilder(encoded.Length);
        for (var i = 0; i < encoded.Length; i++)
        {
            var ch = encoded[i];
            if (ch == '_')
            {
                sb.Append('/');
                continue;
            }

            if (ch == '-' && i + 2 < encoded.Length && IsHex(encoded[i + 1]) && IsHex(encoded[i + 2]))
            {
                sb.Append((char)Convert.ToInt32(encoded.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string? ProofpointV3(string url)
    {
        var marker = url.IndexOf("/v3/__", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return null;
        var start = marker + "/v3/__".Length;
        var end = url.IndexOf("__", start, StringComparison.Ordinal);
        var inner = end < 0 ? url[start..] : url[start..end];
        return inner.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? inner
            : null;
    }

    private static string? EmbeddedHttp(string url)
    {
        var index = url.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            index = url.IndexOf("http://", StringComparison.OrdinalIgnoreCase);
        if (index <= 0)
            return null;
        var rest = url[index..];
        var end = rest.IndexOfAny([' ', '"', '\'', '<', '>']);
        return end < 0 ? rest : rest[..end];
    }

    private static bool IsHex(char ch) =>
        ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private readonly record struct RawLink(string Url, string? Visible);

    private static List<RawLink> Extract(string text)
    {
        var taken = new List<(int Start, int End)>();
        var found = new List<RawLink>();
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Remember(string? visible)
        {
            if (string.IsNullOrWhiteSpace(visible))
                return;
            foreach (Match match in SchemeUrl.Matches(visible))
                labels.Add(TrimUrl(match.Value));
            foreach (Match match in BareDomain.Matches(visible))
            {
                if (BareAllowed(match.Value))
                    labels.Add(TrimUrl(match.Value));
            }
        }

        void Take(int start, int length, string url, string? visible)
        {
            if (length <= 0 || string.IsNullOrWhiteSpace(url))
                return;
            var end = start + length;
            foreach (var span in taken)
            {
                if (start < span.End && end > span.Start)
                    return;
            }

            taken.Add((start, end));
            found.Add(new RawLink(TrimUrl(url), visible));
        }

        bool SkippedTag(int index)
        {
            var open = text.LastIndexOf('<', index);
            if (open < 0)
                return false;
            var close = text.IndexOf('>', open);
            if (close < index)
                return false;
            var name = "";
            var i = open + 1;
            if (i < text.Length && text[i] == '/')
                i++;
            while (i < text.Length && char.IsLetter(text[i]))
            {
                name += char.ToLowerInvariant(text[i]);
                i++;
            }

            return name is "img" or "image" or "link" or "script" or "style" or "source";
        }

        try
        {
            foreach (Match match in Anchor.Matches(text))
            {
                var href = First(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
                var visible = WebUtility.HtmlDecode(Regex.Replace(match.Groups[4].Value, "<[^>]+>", "")).Trim();
                Remember(visible);
                Take(match.Index, match.Length, href, visible.Length == 0 ? null : visible);
            }

            foreach (Match match in Markdown.Matches(text))
            {
                Remember(match.Groups[1].Value);
                Take(match.Index, match.Length, match.Groups[2].Value, match.Groups[1].Value.Trim());
            }

            foreach (Match match in SchemeUrl.Matches(text))
            {
                if (SkippedTag(match.Index) || labels.Contains(TrimUrl(match.Value)))
                    continue;
                Take(match.Index, match.Length, match.Value, null);
            }

            foreach (Match match in BareDomain.Matches(text))
            {
                if (match.Index > 0 && text[match.Index - 1] == '@')
                    continue;
                if (!BareAllowed(match.Value) || SkippedTag(match.Index) || labels.Contains(TrimUrl(match.Value)))
                    continue;
                Take(match.Index, match.Length, match.Value, null);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return found;
        }

        return found;
    }

    private static bool BareAllowed(string value)
    {
        var host = value.Split('/')[0];
        var tld = host.Split('.').LastOrDefault() ?? "";
        return BareTlds.Contains(tld);
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

    private static string TrimUrl(string url)
    {
        var value = url.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        while (value.Length > 0 && value[^1] == ')' && value.Count(ch => ch == '(') < value.Count(ch => ch == ')'))
            value = value[..^1];
        return value;
    }

    private static string? VisibleDomain(string? visible)
    {
        if (string.IsNullOrWhiteSpace(visible))
            return null;
        foreach (Match match in SchemeUrl.Matches(visible))
        {
            var domain = NameOf(Split(TrimUrl(match.Value)).Host);
            if (domain is not null)
                return domain;
        }

        foreach (Match match in BareDomain.Matches(visible))
        {
            if (!BareAllowed(match.Value))
                continue;
            var domain = NameOf(match.Value.Split('/')[0]);
            if (domain is not null)
                return domain;
        }

        return null;
    }

    private static string? NameOf(string host)
    {
        if (host.Length == 0 || IsIp(host))
            return null;
        return PublicSuffixList.RegistrableDomain(Confusables.ToAscii(host));
    }

    private readonly record struct LinkParts(string Scheme, string Host, string UserInfo, string Path, string Query, int? Port);

    private static LinkParts Split(string url)
    {
        var value = url.Trim();
        var scheme = "";
        var rest = value;
        var colon = value.IndexOf(':');
        if (colon > 0 && value[..colon].All(ch => char.IsLetterOrDigit(ch) || ch is '+' or '-' or '.'))
        {
            scheme = value[..colon].ToLowerInvariant();
            rest = value[(colon + 1)..];
        }

        if (scheme.Length == 0)
            scheme = "https";
        else if (!rest.StartsWith("//", StringComparison.Ordinal))
            return new LinkParts(scheme, "", "", rest, "", null);
        else
            rest = rest[2..];
        var cut = rest.IndexOfAny(['/', '?', '#']);
        var authority = cut < 0 ? rest : rest[..cut];
        var tail = cut < 0 ? "" : rest[cut..];
        var path = tail;
        var query = "";
        var hash = path.IndexOfAny(['?', '#']);
        if (hash >= 0)
        {
            query = path[hash] == '?' ? path[hash..] : "";
            var queryEnd = query.IndexOf('#');
            if (queryEnd >= 0)
                query = query[..queryEnd];
            path = path[..hash];
        }

        var user = "";
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            user = authority[..at];
            authority = authority[(at + 1)..];
        }

        string host;
        int? port = null;
        if (authority.StartsWith('['))
        {
            var end = authority.IndexOf(']');
            host = end < 0 ? authority : authority[1..end];
            var after = end < 0 ? "" : authority[(end + 1)..];
            if (after.StartsWith(':') && int.TryParse(after[1..], out var parsed))
                port = parsed;
        }
        else
        {
            var portCut = authority.LastIndexOf(':');
            if (portCut >= 0 && int.TryParse(authority[(portCut + 1)..], out var parsed))
            {
                host = authority[..portCut];
                port = parsed;
            }
            else
            {
                host = authority;
            }
        }

        return new LinkParts(scheme, host, user, path.Length == 0 ? "/" : path, query, port);
    }
}
