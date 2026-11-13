using System.IO.Compression;
using System.Text;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class ZipFileAdapter : IFileAdapter, IPackageFile
{
    public const string LeftOutLead = "Left out of the protected copy: ";
    public const string EncryptedNote = "Not checked. This entry is encrypted, so it cannot be checked.";

    private FileAdapterRouter? _router;

    public void Bind(FileAdapterRouter router) => _router = router;

    public bool CanHandle(string extension) =>
        extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);

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
            if (new FileInfo(path).Length > PackageLimits.MaxEntryUncompressed)
                return new DocumentRead { Incomplete = true, Note = PackageLimits.EntryTooBig };
            raw = File.ReadAllBytes(path);
        }
        catch
        {
            return DocumentRead.Fail(UncheckedFiles.Corrupt);
        }

        return ReadArchive(raw, budget, depth);
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        if (read.AdapterState is not Model model)
            throw new InvalidDataException("Could not read this file. Original was not changed.");
        var mode = redaction ?? CopyRedaction.Permanent;
        var values = Values(read.Text, findings);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in model.Pieces)
            {
                if (piece.Role != "file" || piece.Bytes is null || piece.Nested is null || piece.Extension is null)
                    continue;
                var entryName = Unique(Safe(RedactedName(model, piece.EntryName, findings, mode)), used);
                var local = FindingsInside(findings, piece.Start, piece.Text.Length);
                var temp = WriteTemp(piece.Bytes, piece.Extension);
                try
                {
                    var protectedBytes = _router!.BuildProtected(temp, piece.Nested, local, mode);
                    if (IsOffice(piece.Extension))
                        protectedBytes = OfficePackageSanitizer.Apply(protectedBytes, values);
                    var created = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
                    using var writer = created.Open();
                    writer.Write(protectedBytes);
                }
                finally
                {
                    TryDelete(temp);
                }
            }
        }

        return output.ToArray();
    }

    public static string? FindLeak(byte[] zipBytes, IReadOnlyCollection<string> values)
    {
        try
        {
            using var stream = new MemoryStream(zipBytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (IsDirectory(entry.FullName))
                    continue;
                var data = PackageLimits.ReadEntry(entry, new PackageLimits.Budget());
                var hit = Leak(entry.FullName, data, values, 1);
                if (hit is not null)
                    return hit;
            }
        }
        catch (PackageLimitException)
        {
            return "the copy";
        }
        catch (InvalidDataException)
        {
            return OfficePackageSanitizer.FindInText(Encoding.UTF8.GetString(zipBytes), values, "the copy");
        }

        return null;
    }

    private static string? Leak(string name, byte[] data, IReadOnlyCollection<string> values, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            return "the copy";
        var extension = Path.GetExtension(name);
        if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            return EmlFileAdapter.FindLeak(data, values);
        if (IsOffice(extension))
            return OfficePackageSanitizer.FindLeak(data, values);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (IsDirectory(entry.FullName))
                        continue;
                    var inner = PackageLimits.ReadEntry(entry, new PackageLimits.Budget());
                    var hit = Leak(entry.FullName, inner, values, depth + 1);
                    if (hit is not null)
                        return hit;
                }
            }
            catch (InvalidDataException)
            {
                return OfficePackageSanitizer.FindInText(Encoding.UTF8.GetString(data), values, name);
            }

            return null;
        }

        return OfficePackageSanitizer.FindInText(Encoding.UTF8.GetString(data), values, name);
    }

    private DocumentRead ReadArchive(byte[] raw, PackageLimits.Budget budget, int depth)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(raw, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return DocumentRead.Fail(UncheckedFiles.Corrupt);
        }

        using (archive)
        {
            var encrypted = EncryptedNames(raw);
            var model = new Model();
            var sb = new StringBuilder();
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

            foreach (var entry in archive.Entries)
            {
                if (stopped is not null)
                    break;
                if (IsDirectory(entry.FullName))
                    continue;
                var entryName = entry.FullName.Replace('\\', '/');
                if (!TrySafeName(entryName, out var safeName))
                {
                    model.LeftOut.Add(entryName);
                    continue;
                }

                AddText(safeName, new Piece { Role = "name", EntryName = safeName }, SegmentPlace.Fixed(safeName));
                if (encrypted.Contains(entry.FullName) || encrypted.Contains(safeName))
                {
                    model.Encrypted.Add(safeName);
                    continue;
                }

                var extension = Path.GetExtension(safeName);
                if (!SupportedFileTypes.IsSupported(safeName))
                {
                    model.LeftOut.Add(safeName);
                    continue;
                }

                byte[] payload;
                try
                {
                    payload = PackageLimits.ReadEntry(entry, budget);
                }
                catch (PackageLimitException)
                {
                    throw;
                }
                catch
                {
                    model.LeftOut.Add(safeName);
                    continue;
                }

                var temp = WriteTemp(payload, extension.Length == 0 ? ".bin" : extension);
                try
                {
                    var nested = IsPackage(extension)
                        ? _router!.ReadNested(temp, budget, depth + 1)
                        : _router!.ReadLoaded(temp);
                    if (nested.Incomplete)
                    {
                        stopped = nested;
                        break;
                    }

                    if (nested.Error is not null)
                    {
                        model.LeftOut.Add(safeName);
                        continue;
                    }

                    var origin = sb.Length == 0 ? 0 : sb.Length + 1;
                    var prefix = safeName + " → ";
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
                    }

                    model.Pieces.Add(new Piece
                    {
                        Role = "file",
                        EntryName = safeName,
                        Extension = extension,
                        Bytes = payload,
                        Nested = nested,
                        Start = origin,
                        Text = nested.Text
                    });
                }
                finally
                {
                    TryDelete(temp);
                }
            }

            if (stopped is not null)
                return stopped;

            string? note = null;
            if (model.LeftOut.Count > 0)
                note = TextCatalog.Format("leftOutLead", ("names", string.Join(", ", model.LeftOut)));
            if (model.Encrypted.Count > 0)
            {
                var encryptedNote = TextCatalog.Format("uncheckedEncryptedList", ("names", string.Join(", ", model.Encrypted)));
                note = string.IsNullOrWhiteSpace(note) ? encryptedNote : note + " " + encryptedNote;
            }

            return new DocumentRead
            {
                Text = sb.ToString(),
                Segments = segments,
                Note = note,
                AdapterState = model
            };
        }
    }

    private static HashSet<string> EncryptedNames(byte[] zip)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var eocd = FindEocd(zip);
        if (eocd < 0 || eocd + 22 > zip.Length)
            return names;
        var offset = BitConverter.ToUInt32(zip, eocd + 16);
        var count = BitConverter.ToUInt16(zip, eocd + 10);
        if (offset == uint.MaxValue || offset > zip.Length)
            return names;
        var cursor = (int)offset;
        for (var n = 0; n < count && cursor + 46 <= zip.Length; n++)
        {
            if (zip[cursor] != 0x50 || zip[cursor + 1] != 0x4B || zip[cursor + 2] != 0x01 || zip[cursor + 3] != 0x02)
                break;
            var flags = BitConverter.ToUInt16(zip, cursor + 8);
            var nameLength = BitConverter.ToUInt16(zip, cursor + 28);
            var extraLength = BitConverter.ToUInt16(zip, cursor + 30);
            var commentLength = BitConverter.ToUInt16(zip, cursor + 32);
            if (cursor + 46 + nameLength > zip.Length)
                break;
            var encoding = (flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.UTF8;
            var name = encoding.GetString(zip, cursor + 46, nameLength);
            if ((flags & 1) != 0)
                names.Add(name.Replace('\\', '/'));
            cursor += 46 + nameLength + extraLength + commentLength;
        }

        return names;
    }

    private static int FindEocd(byte[] zip)
    {
        var start = Math.Max(0, zip.Length - 22 - 65535);
        for (var i = zip.Length - 22; i >= start; i--)
        {
            if (zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 0x05 && zip[i + 3] == 0x06)
                return i;
        }

        return -1;
    }

    private static bool IsDirectory(string name) =>
        name.Length == 0 || name.EndsWith('/') || name.EndsWith('\\');

    private static bool TrySafeName(string name, out string safe)
    {
        safe = "";
        var cleaned = name.Replace('\\', '/').Trim();
        if (cleaned.Length == 0 || cleaned.Contains("..", StringComparison.Ordinal) || cleaned.Contains(':', StringComparison.Ordinal))
            return false;
        cleaned = cleaned.TrimStart('/');
        if (cleaned.Length == 0)
            return false;
        safe = cleaned;
        return true;
    }

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

    private static string RedactedName(Model model, string name, IReadOnlyList<Finding> findings, CopyRedaction mode)
    {
        var piece = model.Pieces.FirstOrDefault(item =>
            item.Role == "name" && string.Equals(item.EntryName, name, StringComparison.Ordinal));
        var redacted = piece is null ? name : mode.Apply(piece.Text, FindingsInside(findings, piece.Start, piece.Text.Length));
        redacted = redacted.Replace('\r', ' ').Replace('\n', ' ').Replace('\\', '/');
        return TrySafeName(redacted, out var safe) ? safe : "entry";
    }

    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var dir = Path.GetDirectoryName(name)?.Replace('\\', '/') ?? "";
        for (var n = 2; n < 1000; n++)
        {
            var next = (dir.Length == 0 ? "" : dir + "/") + stem + "_" + n + ext;
            if (used.Add(next))
                return next;
        }

        return name;
    }

    private static string Safe(string name) => name;

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
        public List<string> LeftOut { get; } = [];
        public List<string> Encrypted { get; } = [];
    }

    private sealed class Piece
    {
        public string Role { get; init; } = "";
        public string Text { get; set; } = "";
        public int Start { get; set; }
        public string EntryName { get; init; } = "";
        public string? Extension { get; init; }
        public byte[]? Bytes { get; init; }
        public DocumentRead? Nested { get; init; }
    }
}
