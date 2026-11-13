using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Files;

public static class HtmlLinks
{
    private const char Open = '\uE000';
    private const char Close = '\uE001';

    private static readonly Regex Anchor = new(
        @"<a\b[^>]*?\bhref\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s>]+))[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    public static string ToPlain(string html) => Finish(Rewrite(html, markdown: false));

    public static string ToMarkdown(string html) => Finish(Rewrite(html, markdown: true)).Trim() + Environment.NewLine;

    public static bool Same(string? visible, string? href) =>
        string.Equals((visible ?? "").Trim(), (href ?? "").Trim(), StringComparison.Ordinal);

    public static void AppendDestination(StringBuilder target, int start, string href)
    {
        if (href.Length == 0 || start < 0 || start > target.Length)
            return;
        var inner = target.ToString(start, target.Length - start).Trim();
        if (Same(inner, href))
            return;
        if (target.Length > 0 && !char.IsWhiteSpace(target[^1]))
            target.Append(' ');
        target.Append('<').Append(href).Append('>');
    }

    private static string Rewrite(string html, bool markdown)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        try
        {
            return Anchor.Replace(html, match => Render(match, markdown));
        }
        catch (RegexMatchTimeoutException)
        {
            return html;
        }
    }

    private static string Render(Match match, bool markdown)
    {
        var href = WebUtility.HtmlDecode(First(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)).Trim();
        var visible = Visible(match.Groups[4].Value);
        if (href.Length == 0 || Same(visible, href))
            return visible;
        if (markdown)
        {
            var label = visible.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
            return visible.Length == 0 ? href : "[" + label + "](" + href + ")";
        }

        return visible.Length == 0
            ? Open + href + Close
            : visible + " " + Open + href + Close;
    }

    private static string Visible(string inner)
    {
        var text = Regex.Replace(inner, "(?s)<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Finish(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var without = Regex.Replace(html, "(?i)<br\\s*/?>", "\n");
        without = Regex.Replace(without, "(?i)</p\\s*>", "\n");
        without = Regex.Replace(without, "(?i)</div\\s*>", "\n");
        without = Regex.Replace(without, "(?i)</tr\\s*>", "\n");
        without = Regex.Replace(without, "(?i)</h[1-6]\\s*>", "\n");
        without = Regex.Replace(without, "(?i)</li\\s*>", "\n");
        without = Regex.Replace(without, "(?s)<[^>]+>", " ");
        without = WebUtility.HtmlDecode(without);
        return without.Replace(Open, '<').Replace(Close, '>');
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
