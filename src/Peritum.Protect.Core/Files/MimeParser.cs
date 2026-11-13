using System.Text;
using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Files;

/// <summary>
/// In-house MIME reader for .eml. No network.
/// </summary>
internal static class MimeParser
{
    private const int MaxMimeDepth = 20;

    internal sealed class Part
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string MediaType { get; set; } = "text/plain";
        public string? Charset { get; set; }
        public string? Boundary { get; set; }
        public string TransferEncoding { get; set; } = "7bit";
        public string? FileName { get; set; }
        public bool Attachment { get; set; }
        public string? Text { get; set; }
        public string? Html { get; set; }
        public byte[]? Data { get; set; }
        public bool Broken { get; set; }
        public List<Part> Children { get; } = [];

        public string? Header(string name) =>
            Headers.TryGetValue(name, out var value) ? value : null;
    }

    public static Part? Parse(byte[] message)
    {
        if (message.Length >= 3 && message[0] == 0xEF && message[1] == 0xBB && message[2] == 0xBF)
            message = message[3..];
        return Parse(message, 0);
    }

    private static Part? Parse(byte[] message, int depth)
    {
        if (depth > MaxMimeDepth)
            return null;
        if (!TrySplit(message, out var headerBytes, out var body))
            return null;
        var headers = ParseHeaders(headerBytes);
        if (headers.Count == 0)
            return null;

        var part = new Part();
        foreach (var pair in headers)
            part.Headers[pair.Key] = pair.Value;
        ReadContentType(part);
        ReadDisposition(part);
        var encoding = part.Header("Content-Transfer-Encoding");
        if (!string.IsNullOrWhiteSpace(encoding))
            part.TransferEncoding = encoding.Trim().ToLowerInvariant();

        if (part.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(part.Boundary))
                return null;
            foreach (var chunk in SplitMultipart(body, part.Boundary))
            {
                var child = Parse(chunk, depth + 1);
                if (child is null)
                    return null;
                part.Children.Add(child);
            }
            return part;
        }

        byte[] decoded;
        try
        {
            decoded = DecodeTransfer(body, part.TransferEncoding);
        }
        catch (FormatException)
        {
            part.Broken = true;
            part.Attachment = true;
            part.Data = [];
            return part;
        }

        if (part.MediaType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase))
        {
            var child = Parse(decoded, depth + 1);
            if (child is null)
                return null;
            part.Children.Add(child);
            return part;
        }

        if (IsBodyText(part))
        {
            var text = DecodeCharset(decoded, part.Charset);
            if (part.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            {
                part.Html = text;
                text = HtmlToText(text);
            }
            part.Text = text;
            return part;
        }

        part.Attachment = true;
        part.Data = decoded;
        if (part.MediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            part.Text = DecodeCharset(decoded, part.Charset);
        return part;
    }

    private static bool IsBodyText(Part part) =>
        !part.Attachment &&
        part.FileName is null &&
        (part.MediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) ||
         part.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));

    private static bool TrySplit(byte[] message, out byte[] headers, out byte[] body)
    {
        headers = [];
        body = [];
        for (var i = 0; i < message.Length - 1; i++)
        {
            if (i + 3 < message.Length &&
                message[i] == (byte)'\r' && message[i + 1] == (byte)'\n' &&
                message[i + 2] == (byte)'\r' && message[i + 3] == (byte)'\n')
            {
                headers = message[..i];
                body = message[(i + 4)..];
                return true;
            }

            if (message[i] == (byte)'\n' && message[i + 1] == (byte)'\n')
            {
                headers = message[..i];
                body = message[(i + 2)..];
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> ParseHeaders(byte[] headerBytes)
    {
        var text = Encoding.GetEncoding(28591).GetString(headerBytes).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n');
        var raw = new List<string>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            if ((line[0] == ' ' || line[0] == '\t') && raw.Count > 0)
                raw[^1] = raw[^1] + " " + line.Trim();
            else
                raw.Add(line);
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in raw)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line[..colon].Trim();
            var value = DecodeHeaderValue(line[(colon + 1)..].Trim());
            if (name.Length == 0)
                continue;
            if (headers.TryGetValue(name, out var existing) && existing.Length > 0)
                headers[name] = existing + ", " + value;
            else
                headers[name] = value;
        }

        return headers;
    }

    private static readonly Regex EncodedWord = new(
        @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static string DecodeHeaderValue(string value)
    {
        var joined = Regex.Replace(value, @"\?=\s+=\?", "?==?", RegexOptions.CultureInvariant);
        return EncodedWord.Replace(joined, match =>
        {
            var charset = match.Groups[1].Value;
            var mode = match.Groups[2].Value;
            var data = match.Groups[3].Value;
            try
            {
                var bytes = mode.Equals("B", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(data)
                    : DecodeQuotedWord(data);
                return DecodeCharset(bytes, charset);
            }
            catch (FormatException)
            {
                return match.Value;
            }
        });
    }

    private static byte[] DecodeQuotedWord(string data)
    {
        var bytes = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == '_')
            {
                bytes.Add((byte)' ');
                continue;
            }

            if (data[i] == '=' && i + 2 < data.Length && IsHex(data[i + 1]) && IsHex(data[i + 2]))
            {
                bytes.Add(Convert.ToByte(data.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            bytes.Add((byte)data[i]);
        }

        return bytes.ToArray();
    }

    private static void ReadContentType(Part part)
    {
        var header = part.Header("Content-Type");
        if (string.IsNullOrWhiteSpace(header))
            return;
        var bits = SplitParameters(header);
        if (bits.Count == 0)
            return;
        part.MediaType = bits[0].Trim().ToLowerInvariant();
        foreach (var bit in bits.Skip(1))
        {
            var (key, value) = Parameter(bit);
            if (key.Equals("charset", StringComparison.OrdinalIgnoreCase))
                part.Charset = value;
            else if (key.Equals("boundary", StringComparison.OrdinalIgnoreCase))
                part.Boundary = value;
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && part.FileName is null)
                part.FileName = value;
        }
    }

    private static void ReadDisposition(Part part)
    {
        var header = part.Header("Content-Disposition");
        if (string.IsNullOrWhiteSpace(header))
            return;
        var bits = SplitParameters(header);
        if (bits.Count > 0 && bits[0].Trim().Equals("attachment", StringComparison.OrdinalIgnoreCase))
            part.Attachment = true;
        string? plain = null;
        string? starred = null;
        foreach (var bit in bits.Skip(1))
        {
            var (key, value) = Parameter(bit);
            if (key.Equals("filename", StringComparison.OrdinalIgnoreCase))
                plain = value;
            else if (key.Equals("filename*", StringComparison.OrdinalIgnoreCase))
                starred = DecodeExtended(value);
        }

        part.FileName = starred ?? plain ?? part.FileName;
        if (part.FileName is not null)
            part.Attachment = true;
    }

    private static string DecodeExtended(string value)
    {
        var parts = value.Split('\'', 3);
        if (parts.Length < 3)
            return value;
        return Uri.UnescapeDataString(parts[2]);
    }

    private static List<string> SplitParameters(string value)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = false;
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                quote = !quote;
                continue;
            }

            if (ch == ';' && !quote)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());
        return parts;
    }

    private static (string Key, string Value) Parameter(string bit)
    {
        var eq = bit.IndexOf('=');
        if (eq < 0)
            return (bit.Trim(), "");
        return (bit[..eq].Trim(), bit[(eq + 1)..].Trim().Trim('"'));
    }

    private static List<byte[]> SplitMultipart(byte[] body, string boundary)
    {
        var marker = Encoding.ASCII.GetBytes("--" + boundary);
        var lines = LineRanges(body);
        var starts = new List<(int Line, bool Closing)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var (from, to) = lines[i];
            if (!LineIsBoundary(body, from, to, marker, out var closing))
                continue;
            starts.Add((i, closing));
        }

        var parts = new List<byte[]>();
        for (var i = 0; i < starts.Count; i++)
        {
            if (starts[i].Closing)
                break;
            var contentFrom = lines[starts[i].Line].To;
            var contentTo = i + 1 < starts.Count ? lines[starts[i + 1].Line].From : body.Length;
            if (contentFrom < body.Length && body[contentFrom] == (byte)'\n')
                contentFrom++;
            else if (contentFrom + 1 < body.Length && body[contentFrom] == (byte)'\r' && body[contentFrom + 1] == (byte)'\n')
                contentFrom += 2;
            if (contentTo > contentFrom && body[contentTo - 1] == (byte)'\n')
            {
                contentTo--;
                if (contentTo > contentFrom && body[contentTo - 1] == (byte)'\r')
                    contentTo--;
            }

            if (contentTo < contentFrom)
                contentTo = contentFrom;
            var chunk = new byte[contentTo - contentFrom];
            Buffer.BlockCopy(body, contentFrom, chunk, 0, chunk.Length);
            parts.Add(chunk);
        }

        return parts;
    }

    private static List<(int From, int To)> LineRanges(byte[] body)
    {
        var lines = new List<(int, int)>();
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != (byte)'\n')
                continue;
            var end = i;
            if (end > start && body[end - 1] == (byte)'\r')
                end--;
            lines.Add((start, end));
            start = i + 1;
        }

        if (start <= body.Length)
            lines.Add((start, body.Length));
        return lines;
    }

    private static bool LineIsBoundary(byte[] body, int from, int to, byte[] marker, out bool closing)
    {
        closing = false;
        if (to - from < marker.Length)
            return false;
        for (var i = 0; i < marker.Length; i++)
        {
            if (body[from + i] != marker[i])
                return false;
        }

        var rest = from + marker.Length;
        if (rest + 1 < to && body[rest] == (byte)'-' && body[rest + 1] == (byte)'-')
        {
            closing = true;
            rest += 2;
        }

        for (var i = rest; i < to; i++)
        {
            if (body[i] is not ((byte)' ' or (byte)'\t'))
                return false;
        }

        return true;
    }

    private static byte[] DecodeTransfer(byte[] body, string encoding)
    {
        if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
            return DecodeBase64(body);
        if (encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
            return DecodeQuotedPrintable(body);
        return body;
    }

    private static byte[] DecodeBase64(byte[] body)
    {
        var text = Encoding.ASCII.GetString(body);
        var cleaned = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch))
                cleaned.Append(ch);
        }

        var remainder = cleaned.Length % 4;
        if (remainder == 1)
            throw new FormatException("The base64 text is not valid.");
        if (remainder > 0)
            cleaned.Append('=', 4 - remainder);
        var estimated = cleaned.Length * 3L / 4;
        if (estimated > PackageLimits.MaxEntryUncompressed)
            throw new PackageLimitException(PackageLimits.EntryTooBig);
        var bytes = Convert.FromBase64String(cleaned.ToString());
        if (bytes.Length > PackageLimits.MaxEntryUncompressed)
            throw new PackageLimitException(PackageLimits.EntryTooBig);
        return bytes;
    }

    private static byte[] DecodeQuotedPrintable(byte[] body)
    {
        using var output = new MemoryStream(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == (byte)'=' && i + 1 < body.Length && body[i + 1] is (byte)'\n' or (byte)'\r')
            {
                i++;
                if (body[i] == (byte)'\r' && i + 1 < body.Length && body[i + 1] == (byte)'\n')
                    i++;
                continue;
            }

            if (body[i] == (byte)'=' && i + 2 < body.Length && IsHex((char)body[i + 1]) && IsHex((char)body[i + 2]))
            {
                output.WriteByte(Convert.ToByte(((char)body[i + 1]).ToString() + (char)body[i + 2], 16));
                i += 2;
            }
            else
            {
                output.WriteByte(body[i]);
            }

            if (output.Length > PackageLimits.MaxEntryUncompressed)
                throw new PackageLimitException(PackageLimits.EntryTooBig);
        }

        return output.ToArray();
    }

    private static bool IsHex(char ch) =>
        ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    internal static string DecodeCharset(byte[] bytes, string? charset)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try
        {
            if (string.IsNullOrWhiteSpace(charset) ||
                charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase) ||
                charset.Equals("utf8", StringComparison.OrdinalIgnoreCase))
            {
                encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
            }
            else if (charset.Equals("us-ascii", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Encoding.ASCII;
            }
            else
            {
                encoding = Encoding.GetEncoding(charset);
            }
        }
        catch (ArgumentException)
        {
            encoding = Encoding.GetEncoding(1252);
        }

        var text = encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        return text;
    }

    internal static string HtmlToText(string html) => HtmlLinks.ToPlain(html);
}
