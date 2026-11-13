using System.Text;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class EmlFileAdapter : IFileAdapter, IPackageFile
{
    public const string RemovedLead = "Removed from the protected copy: ";
    public const string RemovedInside =
        "PeritumAI Protect removed these attachments because they could not be checked:";

    private FileAdapterRouter? _router;

    public void Bind(FileAdapterRouter router) => _router = router;

    public bool CanHandle(string extension) =>
        extension.Equals(".eml", StringComparison.OrdinalIgnoreCase);

    public DocumentRead Read(string path) => Read(path, new PackageLimits.Budget(), 1);

    public DocumentRead Read(string path, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            return new DocumentRead { Incomplete = true, Note = PackageLimits.NestedTooDeep };
        if (_router is null)
            return DocumentRead.Fail(UncheckedFiles.Corrupt);

        byte[] raw;
        try
        {
            var length = new FileInfo(path).Length;
            if (length > FileAdapterRouter.MaxBytes || length > PackageLimits.MaxEntryUncompressed)
                return DocumentRead.Fail("This file is too large to scan.");
            raw = File.ReadAllBytes(path);
        }
        catch
        {
            return DocumentRead.Fail(UncheckedFiles.Corrupt);
        }

        MimeParser.Part? root;
        try
        {
            root = MimeParser.Parse(raw);
        }
        catch (PackageLimitException ex)
        {
            return new DocumentRead { Incomplete = true, Note = ex.Message };
        }
        catch
        {
            return DocumentRead.Fail(UncheckedFiles.Corrupt);
        }

        if (root is null)
            return DocumentRead.Fail(UncheckedFiles.Corrupt);

        try
        {
            return Compose(root, budget, depth);
        }
        catch (PackageLimitException ex)
        {
            return new DocumentRead { Incomplete = true, Note = ex.Message };
        }
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        if (read.AdapterState is not Model model)
            throw new InvalidDataException("Could not read this file. Original was not changed.");
        var mode = redaction ?? CopyRedaction.Permanent;
        var boundary = Boundary(model, findings, mode);
        var sb = new StringBuilder();
        sb.Append("MIME-Version: 1.0\r\n");
        foreach (var piece in model.Pieces)
        {
            if (piece.Role != "header")
                continue;
            sb.Append(piece.Header).Append(": ").Append(OneLine(Redact(piece, findings, mode))).Append("\r\n");
        }

        sb.Append("Content-Type: multipart/mixed; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
        var note = LinkCopyNote.ForEmail(read.Text + "\n" + read.ExtraText, read.FromHeader);
        var wroteBody = false;
        foreach (var piece in model.Pieces)
        {
            if (piece.Role != "body")
                continue;
            var text = Redact(piece, findings, mode);
            if (note is not null)
            {
                text = note + "\n" + text;
                note = null;
            }

            WriteText(sb, boundary, text, null);
            wroteBody = true;
        }

        if (note is not null)
        {
            WriteText(sb, boundary, note, null);
            wroteBody = true;
        }

        if (!wroteBody)
            WriteText(sb, boundary, "", null);

        var values = Values(read.Text, findings);
        foreach (var piece in model.Pieces)
        {
            if (piece.Role != "file" || piece.Bytes is null || piece.Nested is null || piece.Extension is null)
                continue;
            var fileName = RedactedName(model, piece.FileName ?? "attachment", findings, mode);
            var local = FindingsInside(findings, piece.Start, piece.Text.Length);
            var temp = WriteTemp(piece.Bytes, piece.Extension);
            try
            {
                var protectedBytes = _router!.BuildProtected(temp, piece.Nested, local, mode);
                if (IsOffice(piece.Extension))
                    protectedBytes = OfficePackageSanitizer.Apply(protectedBytes, values);
                if (IsTextExtension(piece.Extension))
                    WriteText(sb, boundary, Encoding.UTF8.GetString(protectedBytes), fileName);
                else
                    WriteBinary(sb, boundary, protectedBytes, fileName);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        if (model.Removed.Count > 0)
        {
            var lines = new StringBuilder(RemovedInside);
            foreach (var name in model.Removed)
            {
                lines.Append("\r\n");
                lines.Append(RedactedName(model, name, findings, mode));
            }

            WriteText(sb, boundary, lines.ToString(), null);
        }

        if (mode.Reversible)
            WriteText(sb, boundary, mode.Session!.MapLine, null);

        sb.Append("--").Append(boundary).Append("--\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static string? FindLeak(byte[] eml, IReadOnlyCollection<string> values)
    {
        MimeParser.Part? root;
        try
        {
            root = MimeParser.Parse(eml);
        }
        catch
        {
            root = null;
        }

        if (root is null)
            return OfficePackageSanitizer.FindInText(Encoding.UTF8.GetString(eml), values, "the copy");
        return Leak(root, values);
    }

    private DocumentRead Compose(MimeParser.Part root, PackageLimits.Budget budget, int depth)
    {
        var model = new Model();
        var sb = new StringBuilder();
        var html = new StringBuilder();
        var segments = new List<TextSegment>();
        DocumentRead? stopped = null;

        void AddText(string text, Piece piece, SegmentPlace place)
        {
            PackageLimits.Charge(budget, Encoding.UTF8.GetByteCount(text));
            if (sb.Length > 0 && text.Length > 0)
                sb.Append('\n');
            piece.Start = sb.Length;
            piece.Text = text;
            if (text.Length > 0)
            {
                sb.Append(text);
                segments.Add(new TextSegment { Start = piece.Start, Text = text, Place = place });
            }

            model.Pieces.Add(piece);
        }

        void Consume(MimeParser.Part part, bool headers)
        {
            if (stopped is not null)
                return;
            if (headers)
            {
                foreach (var name in new[] { "From", "To", "Cc", "Subject" })
                {
                    if (!part.Headers.TryGetValue(name, out var value) || value.Length == 0)
                        continue;
                    AddText(value, new Piece { Role = "header", Header = name }, SegmentPlace.Fixed(name));
                }
            }

            if (part.Children.Count > 0)
            {
                var inner = part.MediaType.Equals("message/rfc822", StringComparison.OrdinalIgnoreCase);
                foreach (var child in part.Children)
                    Consume(child, inner);
                return;
            }

            if (part.Broken)
            {
                var brokenName = FileName(part);
                model.Removed.Add(brokenName);
                AddText(brokenName, new Piece { Role = "name", FileName = brokenName }, SegmentPlace.Fixed("Attachment '" + brokenName + "'"));
                return;
            }

            if (!part.Attachment && part.Text is not null)
            {
                AddText(part.Text, new Piece { Role = "body" }, SegmentPlace.Text("body, "));
                if (!string.IsNullOrEmpty(part.Html))
                    html.AppendLine(part.Html);
                return;
            }

            if (part.Data is null && part.Text is null)
                return;

            var fileName = FileName(part);
            var extension = Path.GetExtension(fileName);
            AddText(fileName, new Piece { Role = "name", FileName = fileName }, SegmentPlace.Fixed("Attachment '" + fileName + "'"));
            if (!SupportedFileTypes.IsSupported(fileName))
            {
                model.Removed.Add(fileName);
                return;
            }

            var payload = part.Data ?? Encoding.UTF8.GetBytes(part.Text ?? "");
            if (part.Text is not null && IsTextExtension(extension))
                payload = Encoding.UTF8.GetBytes(part.Text);
            var childDepth = IsPackage(extension) ? depth + 1 : depth;
            var temp = WriteTemp(payload, extension);
            try
            {
                var nested = _router!.ReadNested(temp, budget, childDepth);
                if (nested.Incomplete)
                {
                    stopped = nested;
                    return;
                }

                if (nested.Error is not null)
                {
                    model.Removed.Add(fileName);
                    return;
                }

                var origin = sb.Length == 0 ? 0 : sb.Length + 1;
                var prefix = "Attachment '" + fileName + "' → ";
                if (sb.Length > 0 && (nested.Text.Length > 0 || nested.ExtraText.Length > 0))
                    sb.Append('\n');
                if (nested.Text.Length > 0)
                    sb.Append(nested.Text);
                if (nested.Segments.Count == 0 && nested.Text.Length > 0)
                    segments.Add(new TextSegment { Start = origin, Text = nested.Text, Place = SegmentPlace.Text(prefix) });
                else
                {
                    foreach (var segment in nested.Segments)
                        segments.Add(segment.Relocated(origin + segment.Start, prefix));
                }

                if (nested.ExtraText.Length > 0)
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    var extraStart = sb.Length;
                    sb.Append(nested.ExtraText);
                    segments.Add(new TextSegment { Start = extraStart, Text = nested.ExtraText, Place = SegmentPlace.Text(prefix) });
                    PackageLimits.Charge(budget, Encoding.UTF8.GetByteCount(nested.ExtraText));
                }

                model.Pieces.Add(new Piece
                {
                    Role = "file",
                    FileName = fileName,
                    Extension = extension,
                    Bytes = payload,
                    Nested = nested,
                    Start = origin,
                    Text = nested.Text
                });
                if (!string.IsNullOrWhiteSpace(nested.Note))
                    model.Notes.Add(nested.Note);
            }
            finally
            {
                TryDelete(temp);
            }
        }

        Consume(root, headers: true);
        if (stopped is not null)
            return stopped;

        string? note = null;
        if (model.Removed.Count > 0)
            note = RemovedLead + string.Join(", ", model.Removed) + ".";
        if (model.Notes.Count > 0)
            note = Join(note, string.Join(" ", model.Notes));

        root.Headers.TryGetValue("From", out var fromHeader);
        root.Headers.TryGetValue("Reply-To", out var replyHeader);
        return new DocumentRead
        {
            Text = sb.ToString(),
            Segments = segments,
            Note = note,
            AdapterState = model,
            FromHeader = fromHeader ?? "",
            ReplyToHeader = replyHeader ?? "",
            ExtraText = html.ToString()
        };
    }

    private static string? Leak(MimeParser.Part part, IReadOnlyCollection<string> values)
    {
        foreach (var header in part.Headers.Values)
        {
            var hit = OfficePackageSanitizer.FindInText(header, values, "a header");
            if (hit is not null)
                return hit;
        }

        if (part.Text is not null)
        {
            var hit = OfficePackageSanitizer.FindInText(part.Text, values, "the message");
            if (hit is not null)
                return hit;
        }

        if (part.Data is { Length: > 0 })
        {
            if (PackageLimits.IsZip(part.Data))
            {
                var hit = OfficePackageSanitizer.FindLeak(part.Data, values);
                if (hit is not null)
                    return hit;
            }
            else
            {
                var hit = OfficePackageSanitizer.FindInText(Encoding.UTF8.GetString(part.Data), values, part.FileName ?? "an attachment");
                if (hit is not null)
                    return hit;
            }
        }

        foreach (var child in part.Children)
        {
            var hit = Leak(child, values);
            if (hit is not null)
                return hit;
        }

        return null;
    }

    private static string FileName(MimeParser.Part part)
    {
        var name = Safe(part.FileName ?? "");
        if (name.Length == 0)
            name = "attachment" + ExtensionFor(part.MediaType);
        if (Path.GetExtension(name).Length == 0)
            name += ExtensionFor(part.MediaType);
        return name;
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "text/plain" => ".txt",
        "text/html" => ".html",
        "text/csv" => ".csv",
        "application/pdf" => ".pdf",
        "application/json" => ".json",
        "application/xml" or "text/xml" => ".xml",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        "application/zip" => ".zip",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tif",
        "image/webp" => ".webp",
        _ => ""
    };

    private static bool IsPackage(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".eml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsOffice(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextExtension(string extension) =>
        extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".log", StringComparison.OrdinalIgnoreCase);

    private string RedactedName(Model model, string name, IReadOnlyList<Finding> findings, CopyRedaction mode)
    {
        var piece = model.Pieces.FirstOrDefault(item =>
            item.Role == "name" && string.Equals(item.FileName, name, StringComparison.Ordinal));
        var redacted = piece is null ? name : Redact(piece, findings, mode);
        return Safe(OneLine(redacted));
    }

    private static string Redact(Piece piece, IReadOnlyList<Finding> findings, CopyRedaction mode)
    {
        var local = FindingsInside(findings, piece.Start, piece.Text.Length);
        return mode.Apply(piece.Text, local);
    }

    private static List<Finding> FindingsInside(IReadOnlyList<Finding> findings, int start, int length)
    {
        var local = new List<Finding>();
        var end = start + length;
        foreach (var finding in findings)
        {
            if (finding.Start >= start && finding.Start + finding.Length <= end)
                local.Add(finding.Shift(-start));
        }

        return local;
    }

    private static List<string> Values(string text, IReadOnlyList<Finding> findings)
    {
        var values = new List<string>();
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > text.Length)
                continue;
            values.Add(text.Substring(finding.Start, finding.Length));
        }

        return values;
    }

    private static void WriteText(StringBuilder sb, string boundary, string text, string? fileName)
    {
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
        sb.Append("Content-Transfer-Encoding: 8bit\r\n");
        if (!string.IsNullOrEmpty(fileName))
            sb.Append("Content-Disposition: attachment; filename=\"").Append(fileName).Append("\"\r\n");
        sb.Append("\r\n");
        sb.Append(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        if (text.Length == 0 || text[^1] != '\n')
            sb.Append("\r\n");
        sb.Append("\r\n");
    }

    private static void WriteBinary(StringBuilder sb, string boundary, byte[] bytes, string fileName)
    {
        sb.Append("--").Append(boundary).Append("\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append("Content-Disposition: attachment; filename=\"").Append(fileName).Append("\"\r\n\r\n");
        sb.Append(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
        sb.Append("\r\n\r\n");
    }

    private static string Boundary(Model model, IReadOnlyList<Finding> findings, CopyRedaction mode)
    {
        var probe = new StringBuilder();
        foreach (var piece in model.Pieces)
            probe.Append(piece.Text);
        for (var i = 0; i < 4; i++)
        {
            var boundary = "peritum_" + Guid.NewGuid().ToString("n");
            if (!probe.ToString().Contains(boundary, StringComparison.Ordinal))
                return boundary;
        }

        return "peritum_" + Guid.NewGuid().ToString("n");
    }

    private static string OneLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    private static string Safe(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        var cleaned = new string(file.Where(ch => ch >= 32 && ch is not '"' and not ';' and not '\\' and not '/').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "attachment" : cleaned;
    }

    private static string Join(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
            return second ?? "";
        if (string.IsNullOrWhiteSpace(second))
            return first;
        return first + " " + second;
    }

    private static string WriteTemp(byte[] bytes, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-protect-" + Guid.NewGuid().ToString("n") + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp cleanup is best-effort. The original was not opened for write.
        }
    }

    private sealed class Model
    {
        public List<Piece> Pieces { get; } = [];
        public List<string> Removed { get; } = [];
        public List<string> Notes { get; } = [];
    }

    private sealed class Piece
    {
        public string Role { get; init; } = "";
        public string Header { get; init; } = "";
        public string Text { get; set; } = "";
        public int Start { get; set; }
        public string? FileName { get; init; }
        public string? Extension { get; init; }
        public byte[]? Bytes { get; init; }
        public DocumentRead? Nested { get; init; }
    }
}
