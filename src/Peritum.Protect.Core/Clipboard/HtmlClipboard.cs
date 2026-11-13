using System.Globalization;
using System.Text;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Clipboard;

public sealed class HtmlRead
{
    public string Fragment { get; init; } = "";
    public string VisibleText { get; init; } = "";
    public IReadOnlyList<HiddenHit> Hidden { get; init; } = [];
}

public static class HtmlClipboard
{
    public static HtmlRead Read(string? cfHtmlOrFragment)
    {
        if (string.IsNullOrWhiteSpace(cfHtmlOrFragment))
            return new HtmlRead();
        var fragment = ExtractFragment(cfHtmlOrFragment);
        return Parse(fragment);
    }

    public static string ExtractFragment(string source)
    {
        var start = HeaderNumber(source, "StartFragment:");
        var end = HeaderNumber(source, "EndFragment:");
        if (start is null || end is null || end <= start)
            return source;
        var bytes = Encoding.UTF8.GetBytes(source);
        if (start < 0 || end > bytes.Length)
            return source;
        return Encoding.UTF8.GetString(bytes, start.Value, end.Value - start.Value);
    }

    public static string WrapFragment(string fragment)
    {
        var body = "<html>\r\n<body>\r\n<!--StartFragment-->" + fragment + "<!--EndFragment-->\r\n</body>\r\n</html>";
        var header = "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n";
        var startHtml = Encoding.UTF8.GetByteCount(header);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount("<html>\r\n<body>\r\n<!--StartFragment-->");
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = startHtml + Encoding.UTF8.GetByteCount(body);
        header = string.Format(
            CultureInfo.InvariantCulture,
            "Version:0.9\r\nStartHTML:{0:0000000000}\r\nEndHTML:{1:0000000000}\r\nStartFragment:{2:0000000000}\r\nEndFragment:{3:0000000000}\r\n",
            startHtml,
            endHtml,
            startFragment,
            endFragment);
        return header + body;
    }

    public static string Sanitize(string fragment, IReadOnlyList<(string Value, string Token)> replacements)
    {
        var sb = new StringBuilder(fragment.Length);
        var suppress = 0;
        var i = 0;
        while (i < fragment.Length)
        {
            if (fragment[i] != '<')
            {
                var next = fragment.IndexOf('<', i);
                if (next < 0)
                    next = fragment.Length;
                if (suppress == 0)
                    sb.Append(Encode(Replace(HiddenText.StripInvisible(Decode(fragment[i..next])), replacements)));
                i = next;
                continue;
            }

            if (fragment.AsSpan(i).StartsWith("<!--"))
            {
                var close = fragment.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = close < 0 ? fragment.Length : close + 3;
                continue;
            }

            var end = FindTagEnd(fragment, i);
            if (end < 0)
                break;
            var raw = fragment[(i + 1)..end];
            i = end + 1;
            var closing = raw.StartsWith('/');
            var name = TagName(raw);
            var selfClosing = raw.EndsWith('/') || name is "br" or "img" or "meta" or "hr" or "input";
            if (closing)
            {
                if (suppress > 0)
                    suppress--;
                else
                    sb.Append("</").Append(name).Append('>');
                continue;
            }

            var hides = Hides(name, raw);
            if (suppress > 0)
            {
                if (!selfClosing)
                    suppress++;
                continue;
            }

            if (hides)
            {
                if (!selfClosing)
                    suppress = 1;
                continue;
            }

            sb.Append('<').Append(Replace(WithoutAltTitle(raw), replacements)).Append('>');
        }

        return sb.ToString();
    }

    private static HtmlRead Parse(string fragment)
    {
        var visible = new StringBuilder();
        var hidden = new List<HiddenHit>();
        var frames = new List<Frame>();
        var links = new Stack<OpenLink>();
        var i = 0;
        while (i < fragment.Length)
        {
            if (fragment[i] != '<')
            {
                var next = fragment.IndexOf('<', i);
                if (next < 0)
                    next = fragment.Length;
                AddText(Decode(fragment[i..next]), visible, frames);
                i = next;
                continue;
            }

            if (fragment.AsSpan(i).StartsWith("<!--"))
            {
                var close = fragment.IndexOf("-->", i + 4, StringComparison.Ordinal);
                var inner = (close < 0 ? fragment[(i + 4)..] : fragment[(i + 4)..close]).Trim();
                if (inner.Length > 0 && inner is not "StartFragment" and not "EndFragment")
                    hidden.Add(new HiddenHit { Reason = "HTML comment", Text = inner });
                i = close < 0 ? fragment.Length : close + 3;
                continue;
            }

            var end = FindTagEnd(fragment, i);
            if (end < 0)
                break;
            var raw = fragment[(i + 1)..end];
            i = end + 1;
            var closing = raw.StartsWith('/');
            var name = TagName(raw);
            if (closing)
            {
                if (name.Equals("a", StringComparison.OrdinalIgnoreCase))
                    FinishLink(links, frames, visible);
                Pop(frames, name, hidden);
                continue;
            }

            foreach (var extra in AltAndTitle(raw))
                hidden.Add(extra);

            var selfClosing = raw.EndsWith('/') || name is "br" or "img" or "meta" or "hr" or "input";
            var reason = HideReason(name, raw);
            if (name is "br" or "p" or "div" or "li" or "tr" or "h1" or "h2" or "h3" or "h4" or "blockquote")
                Break(visible, frames);
            if (!selfClosing)
                frames.Add(new Frame(name, reason));
            if (!selfClosing && name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                var href = Decode(Attribute(raw, "href") ?? "").Trim();
                if (href.Length > 0)
                {
                    var target = TargetOf(frames, visible);
                    links.Push(new OpenLink(href, target, target.Length));
                }
            }
        }

        while (frames.Count > 0)
            Pop(frames, frames[^1].Name, hidden);

        return new HtmlRead
        {
            Fragment = fragment,
            VisibleText = visible.ToString().Trim(),
            Hidden = hidden
        };
    }

    private static void FinishLink(Stack<OpenLink> links, List<Frame> frames, StringBuilder visible)
    {
        if (links.Count == 0)
            return;
        var link = links.Pop();
        var target = TargetOf(frames, visible);
        if (!ReferenceEquals(target, link.Target))
            return;
        HtmlLinks.AppendDestination(link.Target, link.Start, link.Href);
    }

    private static StringBuilder TargetOf(List<Frame> frames, StringBuilder visible)
    {
        for (var n = frames.Count - 1; n >= 0; n--)
        {
            if (frames[n].Reason is not null)
                return frames[n].Text;
        }

        return visible;
    }

    private readonly record struct OpenLink(string Href, StringBuilder Target, int Start);

    private static void AddText(string text, StringBuilder visible, List<Frame> frames)
    {
        if (text.Length == 0)
            return;
        for (var n = frames.Count - 1; n >= 0; n--)
        {
            if (frames[n].Reason is null)
                continue;
            frames[n].Text.Append(text);
            return;
        }

        visible.Append(text);
    }

    private static void Break(StringBuilder visible, List<Frame> frames)
    {
        if (frames.Any(frame => frame.Reason is not null))
            return;
        if (visible.Length > 0 && visible[^1] != '\n')
            visible.Append('\n');
    }

    private static void Pop(List<Frame> frames, string name, List<HiddenHit> hidden)
    {
        for (var n = frames.Count - 1; n >= 0; n--)
        {
            var frame = frames[n];
            frames.RemoveAt(n);
            var text = frame.Text.ToString().Trim();
            if (frame.Reason is not null && text.Length > 0)
                hidden.Add(new HiddenHit { Reason = frame.Reason, Text = text });
            if (frame.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                break;
        }
    }

    private static bool Hides(string name, string raw) => HideReason(name, raw) is not null;

    private static string? HideReason(string name, string raw)
    {
        if (name is "script" or "style" or "noscript" or "head" or "template")
            return "Hidden HTML";
        if (HasAttribute(raw, "hidden"))
            return "Hidden HTML";
        var style = Attribute(raw, "style");
        if (style is null)
            return AriaHidden(raw) ? "Hidden HTML" : null;
        var compact = style.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        if (compact.Contains("display:none", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase))
            return "Hidden HTML";
        if (IsZero(compact, "font-size:") || IsZero(compact, "opacity:"))
            return "Hidden HTML";
        if (OffScreen(compact))
            return "Off-screen text";
        return AriaHidden(raw) ? "Hidden HTML" : null;
    }

    private static bool AriaHidden(string raw)
    {
        var value = Attribute(raw, "aria-hidden");
        return value is not null && value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool OffScreen(string compact)
    {
        foreach (var key in new[] { "left:-", "top:-", "text-indent:-" })
        {
            var i = 0;
            while ((i = compact.IndexOf(key, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var digits = 0;
                var rest = compact[(i + key.Length)..];
                foreach (var c in rest)
                {
                    if (!char.IsDigit(c))
                        break;
                    digits++;
                }

                if (digits >= 3)
                    return true;
                i += key.Length;
            }
        }

        return false;
    }

    private static bool IsZero(string compact, string key)
    {
        var i = 0;
        while ((i = compact.IndexOf(key, i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var rest = compact[(i + key.Length)..];
            if (rest.Length > 0 && rest[0] == '0' && ZeroTail(rest))
                return true;
            i += key.Length;
        }

        return false;
    }

    private static bool ZeroTail(string rest)
    {
        var j = 1;
        if (j < rest.Length && rest[j] == '.')
        {
            j++;
            var saw = false;
            while (j < rest.Length && rest[j] == '0')
            {
                saw = true;
                j++;
            }

            if (!saw || (j < rest.Length && char.IsDigit(rest[j])))
                return false;
        }

        return true;
    }

    private static List<HiddenHit> AltAndTitle(string raw)
    {
        var hits = new List<HiddenHit>();
        foreach (var name in new[] { "alt", "title" })
        {
            var value = Attribute(raw, name);
            if (!string.IsNullOrWhiteSpace(value))
                hits.Add(new HiddenHit { Reason = "Alt or title text", Text = Decode(value.Trim()) });
        }

        return hits;
    }

    private static string WithoutAltTitle(string raw)
    {
        var result = raw;
        foreach (var name in new[] { "alt", "title" })
            result = RemoveAttribute(result, name);
        return result.Trim();
    }

    private static string RemoveAttribute(string raw, string name)
    {
        var index = FindAttribute(raw, name);
        if (index < 0)
            return raw;
        var i = index + name.Length;
        while (i < raw.Length && char.IsWhiteSpace(raw[i]))
            i++;
        if (i >= raw.Length || raw[i] != '=')
            return raw.Remove(index, name.Length);
        i++;
        while (i < raw.Length && char.IsWhiteSpace(raw[i]))
            i++;
        if (i < raw.Length && raw[i] is '"' or '\'')
        {
            var quote = raw[i];
            var end = raw.IndexOf(quote, i + 1);
            i = end < 0 ? raw.Length : end + 1;
        }
        else
        {
            while (i < raw.Length && !char.IsWhiteSpace(raw[i]) && raw[i] is not '>' and not '/')
                i++;
        }

        return raw.Remove(index, i - index);
    }

    private static bool HasAttribute(string raw, string name) => FindAttribute(raw, name) >= 0;

    private static string? Attribute(string raw, string name)
    {
        var index = FindAttribute(raw, name);
        if (index < 0)
            return null;
        var i = index + name.Length;
        while (i < raw.Length && char.IsWhiteSpace(raw[i]))
            i++;
        if (i >= raw.Length || raw[i] != '=')
            return "";
        i++;
        while (i < raw.Length && char.IsWhiteSpace(raw[i]))
            i++;
        if (i >= raw.Length)
            return "";
        if (raw[i] is '"' or '\'')
        {
            var quote = raw[i];
            var end = raw.IndexOf(quote, i + 1);
            return end < 0 ? raw[(i + 1)..] : raw[(i + 1)..end];
        }

        var start = i;
        while (i < raw.Length && !char.IsWhiteSpace(raw[i]))
            i++;
        return raw[start..i];
    }

    private static int FindAttribute(string raw, string name)
    {
        var i = 0;
        while (i < raw.Length)
        {
            if (!char.IsWhiteSpace(raw[i]))
            {
                i++;
                continue;
            }

            i++;
            while (i < raw.Length && char.IsWhiteSpace(raw[i]))
                i++;
            if (raw.AsSpan(i).StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                var after = i + name.Length;
                if (after >= raw.Length || char.IsWhiteSpace(raw[after]) || raw[after] is '=' or '/' or '>')
                    return i;
            }
        }

        return -1;
    }

    private static string TagName(string raw)
    {
        var start = raw.StartsWith('/') ? 1 : 0;
        while (start < raw.Length && char.IsWhiteSpace(raw[start]))
            start++;
        var end = start;
        while (end < raw.Length && (char.IsLetterOrDigit(raw[end]) || raw[end] is '-' or ':'))
            end++;
        return raw[start..end].ToLowerInvariant();
    }

    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        for (var i = start; i < html.Length; i++)
        {
            var c = html[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '>')
                return i;
        }

        return -1;
    }

    private static int? HeaderNumber(string source, string key)
    {
        var at = source.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
            return null;
        var i = at + key.Length;
        var start = i;
        while (i < source.Length && char.IsDigit(source[i]))
            i++;
        if (i == start || !int.TryParse(source[start..i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return null;
        return value;
    }

    public static string Decode(string text)
    {
        if (!text.Contains('&'))
            return text;
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                sb.Append(text[i]);
                continue;
            }

            var semi = text.IndexOf(';', i + 1);
            if (semi < 0 || semi - i > 12)
            {
                sb.Append('&');
                continue;
            }

            var token = text[(i + 1)..semi];
            var decoded = token switch
            {
                "amp" => "&",
                "lt" => "<",
                "gt" => ">",
                "quot" => "\"",
                "apos" or "#39" => "'",
                "nbsp" => " ",
                _ => DecodeNumeric(token)
            };
            if (decoded is null)
            {
                sb.Append('&');
                continue;
            }

            sb.Append(decoded);
            i = semi;
        }

        return sb.ToString();
    }

    private static string? DecodeNumeric(string token)
    {
        if (token.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)
            && hex is > 0 and <= 0x10FFFF)
            return char.ConvertFromUtf32(hex);
        if (token.StartsWith('#')
            && int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var dec)
            && dec is > 0 and <= 0x10FFFF)
            return char.ConvertFromUtf32(dec);
        return null;
    }

    private static string Encode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Replace(string text, IReadOnlyList<(string Value, string Token)> replacements)
    {
        foreach (var (value, token) in replacements.OrderByDescending(pair => pair.Value.Length))
        {
            if (value.Length == 0)
                continue;
            text = text.Replace(value, token, StringComparison.Ordinal);
        }

        return text;
    }

    private sealed class Frame(string name, string? reason)
    {
        public string Name { get; } = name;
        public string? Reason { get; } = reason;
        public StringBuilder Text { get; } = new();
    }
}
