using System.Diagnostics;
using System.Text;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core;

public sealed class ProtectService
{
    private readonly RuleScanner _scanner = new();
    private readonly FileAdapterRouter _files;

    public ProtectService(IOcrEngine? ocr = null)
    {
        Ocr = ocr;
        _files = new FileAdapterRouter(ocr);
        if (ocr is null || !ocr.IsAvailable)
            OcrStatus = ocr?.UnavailableReason ?? "OCR is not available on this PC. Image scan is off.";
        else
            OcrStatus = "Image text uses Windows OCR on this PC. Nothing is uploaded.";
    }

    public IOcrEngine? Ocr { get; }
    public string OcrStatus { get; }
    public Func<BrandCatalog>? LinkCatalog { get; set; }
    public Func<string, bool>? TrustedSite { get; set; }

    public ScanSession Scan(ScanRequest request, IReadOnlyList<Rule> rules, Func<string, bool>? isAllowed = null)
    {
        var clock = Stopwatch.StartNew();
        if (!string.IsNullOrEmpty(request.FilePath))
        {
            var read = _files.Read(request.FilePath);
            var name = Path.GetFileName(request.FilePath);
            if (read.Error is not null)
            {
                return new ScanSession
                {
                    Request = request,
                    Text = "",
                    Result = ScanResult.Failed(request.Source, name, read.Error, read.ReasonCode)
                };
            }

            if (read.Incomplete)
            {
                return new ScanSession
                {
                    Request = request,
                    Text = "",
                    Result = new ScanResult
                    {
                        Findings = [],
                        ContentHash = "",
                        Source = request.Source,
                        DisplayName = name,
                        Incomplete = true,
                        Note = read.Note,
                        DurationMs = (int)clock.ElapsedMilliseconds,
                        FileType = Path.GetExtension(request.FilePath)
                    }
                };
            }

            var match = _scanner.MatchSegments(read.Text, read.Segments, rules, limit: true);
            var findings = ExceptAllowed(read.Text, match.Findings, isAllowed);
            var picture = read.Segments.FirstOrDefault(segment => segment.ImageWidth > 0);
            var extension = Path.GetExtension(request.FilePath);
            var links = FileLinkScan.Applies(extension)
                ? FileLinkScan.Inspect(read.Text + "\n" + read.ExtraText, read.FromHeader, read.ReplyToHeader, LinkCatalog?.Invoke(), TrustedSite)
                : null;
            return new ScanSession
            {
                Request = request,
                Text = read.Text,
                Boxes = read.Boxes,
                ImageWidth = picture?.ImageWidth ?? 0,
                ImageHeight = picture?.ImageHeight ?? 0,
                Result = new ScanResult
                {
                    Findings = findings,
                    ContentHash = Hashing.Sha256Text(read.Text),
                    Source = request.Source,
                    DisplayName = name,
                    Note = WithEntropyNote(read.Note, match.Incomplete, match.FailedRuleIds),
                    Incomplete = match.Incomplete || read.Text.Length > 2_000_000,
                    FailedRuleIds = match.FailedRuleIds,
                    DurationMs = (int)clock.ElapsedMilliseconds,
                    SegmentCount = read.Segments.Count,
                    FileType = extension,
                    LinkFindings = links?.Findings ?? [],
                    LinkCount = links?.LinkCount ?? 0
                }
            };
        }

        var text = request.Text ?? "";
        var result = _scanner.Scan(ScanRequest.ForText(text, request.Source), rules);
        var filtered = ExceptAllowed(text.Length > 2_000_000 ? text[..2_000_000] : text, result.Findings, isAllowed);
        return new ScanSession
        {
            Request = request,
            Text = text.Length > 2_000_000 ? text[..2_000_000] : text,
            Result = new ScanResult
            {
                Findings = filtered,
                ContentHash = result.ContentHash,
                Source = request.Source,
                DisplayName = result.DisplayName,
                Truncated = result.Truncated,
                Incomplete = result.Incomplete,
                FailedRuleIds = result.FailedRuleIds,
                Error = result.Error,
                Note = WithEntropyNote(null, result.Incomplete, result.FailedRuleIds),
                DurationMs = (int)clock.ElapsedMilliseconds,
                SegmentCount = 1,
                FileType = "text"
            }
        };
    }

    public CopyResult MakeProtectedCopy(
        string originalPath,
        IReadOnlyList<Rule> rules,
        string? pattern,
        Func<string, bool>? isAllowed = null,
        IReadOnlyCollection<string>? excludeValues = null,
        string? copyFolderMode = null,
        string? copyFolder = null,
        bool reversible = false,
        string? mapsDirectory = null)
    {
        string before;
        try
        {
            before = Hashing.Sha256File(originalPath);
        }
        catch
        {
            return CopyResult.Fail("Could not read this file. Original was not changed.", "");
        }

        var read = _files.Read(originalPath);
        if (read.Error is not null)
            return CopyResult.Fail(read.Error, before);
        if (read.Incomplete)
            return CopyResult.Fail(string.IsNullOrWhiteSpace(read.Note) ? "This file was not fully checked, so no protected copy was written." : read.Note, before);

        bool Blocked(string value) =>
            (isAllowed?.Invoke(value) ?? false) ||
            (excludeValues?.Contains(value, StringComparer.Ordinal) ?? false);

        var structural = _scanner.MatchSegments(read.Text, read.Segments, rules, limit: false);
        var extra = string.IsNullOrEmpty(read.ExtraText)
            ? new MatchResult([], [], false)
            : _scanner.Match(read.ExtraText, rules, limit: false);
        var failed = structural.FailedRuleIds.Concat(extra.FailedRuleIds).Distinct(StringComparer.Ordinal).ToList();
        var incomplete = structural.Incomplete || extra.Incomplete;
        var truncated = read.Text.Length > 2_000_000 || read.ExtraText.Length > 2_000_000;
        if (truncated || (incomplete && !ScanCoverage.OnlyEntropyIncomplete(incomplete, failed)))
        {
            return CopyResult.Fail("This file was not fully checked, so no protected copy was written.", before);
        }
        var entropyNote = ScanCoverage.OnlyEntropyIncomplete(incomplete, failed)
            ? ScanCoverage.EntropySkipNote
            : null;

        var findings = ExceptAllowed(read.Text, structural.Findings, Blocked);
        var extraFindings = ExceptAllowed(read.ExtraText, extra.Findings, Blocked);
        var values = MatchedValues(read.Text, findings)
            .Concat(MatchedValues(read.ExtraText, extraFindings))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var session = reversible ? new ReversibleSession() : null;
        var redaction = session is null ? CopyRedaction.Permanent : new CopyRedaction(session);
        try
        {
            var bytes = _files.BuildProtected(originalPath, read, findings, redaction);
            if (IsOffice(originalPath))
            {
                bytes = OfficePackageSanitizer.Apply(bytes, values);
                var originalBytes = File.ReadAllBytes(originalPath);
                var invalid = OfficePackageValidator.Check(bytes, Path.GetExtension(originalPath), originalBytes);
                if (invalid is not null)
                    return CopyResult.Fail(invalid, before) with { VerificationRefused = true };
            }
            var leak = Verify(originalPath, bytes, values);
            if (leak is not null)
                return CopyResult.Fail("Could not make a clean copy. Sensitive text remains in: " + leak + ".", before) with { VerificationRefused = true };
            var sidecarBody = SidecarBody(read, findings, redaction);
            if (sidecarBody is not null)
            {
                var sideLeak = OfficePackageSanitizer.FindInText(sidecarBody, values, "the text copy");
                if (sideLeak is not null)
                    return CopyResult.Fail("Could not make a clean copy. Sensitive text remains in: " + sideLeak + ".", before) with { VerificationRefused = true };
            }
            var (folder, fallback) = CopyDestination.Resolve(copyFolderMode, copyFolder);
            var dest = ProtectedCopyWriter.Write(originalPath, bytes, pattern, folder);
            var after = Hashing.Sha256File(originalPath);
            var sidecar = WriteSidecar(originalPath, dest, sidecarBody);
            if (session is not null && !string.IsNullOrWhiteSpace(mapsDirectory))
            {
                try
                {
                    new MapStore(mapsDirectory).Save(session.Map);
                }
                catch (Exception ex)
                {
                    fallback = JoinNotes(fallback, "The restore map could not be stored. " + ex.Message);
                }
            }
            return new CopyResult
            {
                OutputPath = dest,
                OriginalHashBefore = before,
                OriginalHashAfter = after,
                Note = JoinNotes(JoinNotes(read.Note, entropyNote), fallback),
                SidecarPath = sidecar,
                Reversible = session is not null,
                MapId = session?.Map.Id
            };
        }
        catch (Exception ex)
        {
            string after;
            try { after = Hashing.Sha256File(originalPath); }
            catch { after = before; }
            return new CopyResult
            {
                Error = ex.Message,
                OriginalHashBefore = before,
                OriginalHashAfter = after
            };
        }
    }

    private static string? WithEntropyNote(string? note, bool incomplete, IReadOnlyList<string> failed)
    {
        if (!ScanCoverage.OnlyEntropyIncomplete(incomplete, failed))
            return note;
        return JoinNotes(note, ScanCoverage.EntropySkipNote);
    }

    private static string? JoinNotes(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
            return second;
        if (string.IsNullOrWhiteSpace(second))
            return first;
        return first + " " + second;
    }

    private static string? SidecarBody(DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction redaction)
    {
        if (read.WriteRedactedTextBeside)
            return redaction.WithMapLine(redaction.Apply(read.Text, findings));
        if (!read.EmitFindingSidecar)
            return null;
        if (redaction.Reversible)
            return redaction.WithMapLine(redaction.Apply(read.Text, findings));
        return string.Join(
            Environment.NewLine,
            findings
                .GroupBy(finding => finding.Label, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key} ×{group.Count()}"));
    }

    private static string? WriteSidecar(string originalPath, string dest, string? body)
    {
        if (body is null)
            return null;

        var directory = Path.GetDirectoryName(dest) ?? Path.GetDirectoryName(originalPath)!;
        var side = Path.Combine(directory, Path.GetFileNameWithoutExtension(dest) + ".txt");
        var originalFull = Path.GetFullPath(originalPath);
        if (string.Equals(Path.GetFullPath(side), originalFull, StringComparison.OrdinalIgnoreCase))
            side = Path.Combine(directory, Path.GetFileNameWithoutExtension(originalPath) + "_protect_ocr.txt");
        if (File.Exists(side))
        {
            var stem = Path.GetFileNameWithoutExtension(side);
            var ext = Path.GetExtension(side);
            for (var n = 2; n < 1000 && File.Exists(side); n++)
                side = Path.Combine(directory, stem + "_" + n + ext);
        }

        File.WriteAllText(side, body);
        return side;
    }

    private static IReadOnlyList<Finding> ExceptAllowed(string text, IReadOnlyList<Finding> findings, Func<string, bool>? isAllowed)
    {
        if (isAllowed is null)
            return findings;
        var kept = new List<Finding>(findings.Count);
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > text.Length)
                continue;
            if (!isAllowed(text.Substring(finding.Start, finding.Length)))
                kept.Add(finding);
        }
        return kept;
    }

    private static IEnumerable<string> MatchedValues(string text, IReadOnlyList<Finding> findings)
    {
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > text.Length)
                continue;
            yield return text.Substring(finding.Start, finding.Length);
        }
    }

    public CopyResult MakeCleanCopy(string originalPath, CleanOptions? options = null, string? copyFolderMode = null, string? copyFolder = null)
    {
        string before;
        try
        {
            before = Hashing.Sha256File(originalPath);
        }
        catch
        {
            return CopyResult.Fail("Could not read this file. Original was not changed.", "");
        }

        var cleaned = ShareCleaner.Clean(originalPath, options);
        if (cleaned.Error is not null)
            return CopyResult.Fail(cleaned.Error, before);
        var leftover = ShareCleaner.Leftover(cleaned.Bytes, cleaned.DroppedEntries);
        if (leftover is not null)
            return CopyResult.Fail("Could not make a clean copy. A removed part is still in the file.", before) with { VerificationRefused = true };

        var (folder, fallback) = CopyDestination.Resolve(copyFolderMode, copyFolder);
        string dest;
        try
        {
            dest = ProtectedCopyWriter.Write(originalPath, cleaned.Bytes, "{name}_clean{ext}", folder, cleaned.Extension);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return CopyResult.Fail(ex.Message, before);
        }

        var after = Hashing.Sha256File(originalPath);
        var note = string.Join(Environment.NewLine, cleaned.Removed.Select(ReasonText.RemovedLine));
        if (!string.IsNullOrEmpty(fallback))
            note += Environment.NewLine + fallback;
        return new CopyResult
        {
            OutputPath = dest,
            OriginalHashBefore = before,
            OriginalHashAfter = after,
            Note = note
        };
    }

    private static string? Verify(string originalPath, byte[] output, IReadOnlyList<string> values)
    {
        var extension = Path.GetExtension(originalPath);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (IsOffice(originalPath))
            return OfficePackageSanitizer.FindLeak(output, values);
        if (extension.Equals(".eml", StringComparison.OrdinalIgnoreCase))
            return EmlFileAdapter.FindLeak(output, values);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return ZipFileAdapter.FindLeak(output, values);
        var text = Encoding.UTF8.GetString(output);
        return OfficePackageSanitizer.FindInText(text, values, "the copy");
    }

    private static bool IsOffice(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);
    }
}
