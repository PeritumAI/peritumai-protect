using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Clipboard;

public sealed class RichClipboardWrite
{
    public string PlainText { get; init; } = "";
    public string? Html { get; init; }
    public byte[]? ImagePng { get; init; }
    public RedactionMap? Map { get; init; }
    public string? Error { get; init; }
}

public static class RichClipboard
{
    public static ScanSession Inspect(ClipboardScanInput input, IReadOnlyList<Rule> rules, IOcrEngine? ocr, Func<string, bool>? isAllowed = null)
    {
        var imageTooBig = input.ImageExceedsLimit
            || (input.ImageBytes is not null && ClipboardImageLimits.Exceeds(input.ImageBytes.Length, input.ImageWidth, input.ImageHeight))
            || (input.ImageBytes is null && ClipboardImageLimits.Exceeds(0, input.ImageWidth, input.ImageHeight));

        string? imageText = null;
        IReadOnlyList<OcrBox> boxes = [];
        byte[]? imageBytes = null;
        var width = input.ImageWidth;
        var height = input.ImageHeight;
        if (!imageTooBig && input.ImageBytes is { Length: > 0 })
        {
            if (ocr is null || !ocr.IsAvailable)
            {
                return Failed(ClipboardImageLimits.OcrOff);
            }

            OcrDocument? recognized;
            try
            {
                recognized = ocr.RecognizeBytes(input.ImageBytes);
            }
            catch
            {
                recognized = null;
            }

            if (recognized is null)
                return Failed(ClipboardImageLimits.OcrOff);
            imageText = recognized.Text;
            boxes = recognized.Words;
            imageBytes = input.ImageBytes;
        }

        var html = HtmlClipboard.Read(input.Html);
        var rtf = RtfText.ToPlain(input.Rtf);
        var scanner = new RuleScanner();
        var visible = new System.Text.StringBuilder();
        var segments = new List<TextSegment>();

        var imageTextStart = -1;
        var imageTextLength = 0;
        void Add(string? part, SegmentPlace place, IReadOnlyList<OcrBox>? partBoxes = null, bool always = false)
        {
            if (string.IsNullOrWhiteSpace(part))
                return;
            if (!always && visible.Length > 0 && visible.ToString().Contains(part, StringComparison.Ordinal))
                return;
            if (visible.Length > 0)
                visible.Append('\n');
            var start = visible.Length;
            visible.Append(part);
            if (partBoxes is not null)
            {
                imageTextStart = start;
                imageTextLength = part.Length;
            }

            segments.Add(new TextSegment
            {
                Start = start,
                Text = part,
                Place = place,
                Boxes = partBoxes,
                ImageWidth = width,
                ImageHeight = height
            });
        }

        Add(input.Text, SegmentPlace.Clipboard());
        Add(html.VisibleText, SegmentPlace.Clipboard());
        Add(rtf, SegmentPlace.Clipboard());
        Add(imageText, SegmentPlace.Image(), boxes, always: true);

        var visibleText = visible.ToString();
        var findings = new List<Finding>();
        var failed = new List<string>();
        var incomplete = false;
        if (visibleText.Length > 0)
        {
            var match = scanner.MatchSegments(visibleText, segments, rules, limit: true);
            findings.AddRange(match.Findings);
            failed.AddRange(match.FailedRuleIds);
            incomplete |= match.Incomplete;
        }

        foreach (var hit in HiddenText.Scan(visibleText))
            findings.Add(PhraseFinding(visibleText, hit, info: hit.Reason == "Text addressed to an AI"));

        var full = new System.Text.StringBuilder(visibleText);
        foreach (var hit in html.Hidden)
        {
            if (string.IsNullOrWhiteSpace(hit.Text))
                continue;
            if (full.Length > 0)
                full.Append('\n');
            var start = full.Length;
            full.Append(hit.Text);
            findings.Add(HiddenFinding(full.ToString(), hit.Reason, hit.Text, start));
            var piece = scanner.Match(hit.Text, rules, limit: true);
            foreach (var finding in piece.Findings)
                findings.Add(finding.Shift(start).Located("clipboard HTML, " + hit.Reason, FindingPlaces.Context(full.ToString(), start + finding.Start, finding.Length, finding.RuleId)));
            failed.AddRange(piece.FailedRuleIds);
            incomplete |= piece.Incomplete;
            foreach (var inner in HiddenText.Scan(hit.Text))
            {
                if (inner.Reason == "Text addressed to an AI")
                    findings.Add(PhraseFinding(full.ToString(), inner, info: false, origin: start));
            }
        }

        var text = full.ToString();
        findings = ExceptAllowed(text, findings, isAllowed);
        if (imageTooBig && findings.Count == 0)
            return Failed(ClipboardImageLimits.NotChecked);

        return new ScanSession
        {
            Request = ScanRequest.ForText(text, ScanSource.Clipboard),
            Text = text,
            Boxes = boxes,
            ImageWidth = imageBytes is null ? 0 : width,
            ImageHeight = imageBytes is null ? 0 : height,
            VisibleLength = visibleText.Length,
            ClipboardImage = imageBytes,
            ClipboardHtmlFragment = string.IsNullOrEmpty(input.Html) ? null : html.Fragment,
            ImageTextStart = imageTextStart,
            ImageTextLength = imageTextLength,
            Result = new ScanResult
            {
                Findings = findings,
                ContentHash = Hashing.Sha256Text(text),
                Source = ScanSource.Clipboard,
                DisplayName = "clipboard",
                Incomplete = incomplete,
                FailedRuleIds = failed.Distinct(StringComparer.Ordinal).ToList(),
                Note = imageTooBig ? ClipboardImageLimits.NotChecked : null,
                FileType = imageBytes is null ? "clipboard" : "image"
            }
        };
    }

    public static RichClipboardWrite Redact(ScanSession session, IReadOnlyList<Finding> findings, bool reversible)
    {
        var visibleLength = session.VisibleLength >= 0 && session.VisibleLength <= session.Text.Length
            ? session.VisibleLength
            : session.Text.Length;
        var visible = session.Text[..visibleLength];
        var visibleFindings = findings
            .Where(finding => finding.Start >= 0 && finding.Length > 0 && finding.Start + finding.Length <= visibleLength)
            .ToList();

        string plain;
        RedactionMap? map = null;
        IReadOnlyList<(string Value, string Token)> pairs;
        if (reversible)
        {
            var sessionMap = new ReversibleSession();
            plain = sessionMap.Apply(visible, visibleFindings, reversible: true);
            pairs = Pairs(visible, visibleFindings, sessionMap, reversible: true);
            map = sessionMap.Map;
            if (map.Tokens.Count > 0)
                plain = ReversibleSession.AppendMapLine(plain, sessionMap.MapLine);
        }
        else
        {
            plain = TextRedactor.Apply(visible, visibleFindings);
            pairs = visibleFindings
                .Select(finding => (visible.Substring(finding.Start, finding.Length), PlaceholderStyle.Wrap(finding.Placeholder)))
                .ToList();
        }

        plain = HiddenText.StripInvisible(plain);
        string? html = null;
        if (session.ClipboardHtmlFragment is not null)
            html = HtmlClipboard.WrapFragment(HtmlClipboard.Sanitize(session.ClipboardHtmlFragment, pairs));

        byte[]? image = null;
        if (session.ClipboardImage is not null)
        {
            var refs = new List<FindingRef>();
            if (session.ImageTextStart >= 0)
            {
                var imageEnd = session.ImageTextStart + session.ImageTextLength;
                foreach (var finding in visibleFindings)
                {
                    if (finding.Start >= session.ImageTextStart && finding.Start < imageEnd)
                        refs.Add(new FindingRef(finding.Start - session.ImageTextStart, finding.Length));
                }
            }

            image = ImageBars.Cover(session.ClipboardImage, session.Boxes, refs, ".png", refuseIfUncovered: refs.Count > 0);
            if (image is null)
                return new RichClipboardWrite { Error = "Could not make a clean copy. Sensitive text remains in: the image." };
        }

        var values = Values(session.Text, findings);
        if (OfficePackageSanitizer.FindInText(plain, values, "the clipboard") is not null)
            return new RichClipboardWrite { Error = "Could not make a clean copy. Sensitive text remains in: the clipboard." };
        if (html is not null && OfficePackageSanitizer.FindInText(html, values, "the clipboard") is not null)
            return new RichClipboardWrite { Error = "Could not make a clean copy. Sensitive text remains in: the clipboard." };

        return new RichClipboardWrite
        {
            PlainText = plain,
            Html = html,
            ImagePng = image,
            Map = map
        };
    }

    private static List<(string Value, string Token)> Pairs(string visible, IReadOnlyList<Finding> findings, ReversibleSession session, bool reversible)
    {
        var pairs = new List<(string Value, string Token)>();
        foreach (var finding in findings.OrderBy(item => item.Start))
        {
            if (finding.Start < 0 || finding.Start + finding.Length > visible.Length)
                continue;
            var value = visible.Substring(finding.Start, finding.Length);
            pairs.Add((value, session.Placeholder(value, finding, reversible)));
        }

        return pairs;
    }

    private static List<string> Values(string text, IReadOnlyList<Finding> findings)
    {
        var values = new List<string>();
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length < 4 || finding.Start + finding.Length > text.Length)
                continue;
            var value = text.Substring(finding.Start, finding.Length);
            if (!values.Contains(value, StringComparer.Ordinal))
                values.Add(value);
        }

        return values;
    }

    private static Finding PhraseFinding(string text, HiddenHit hit, bool info, int origin = 0)
    {
        var at = text.IndexOf(hit.Text, origin, StringComparison.Ordinal);
        if (at < 0)
            at = origin;
        var length = Math.Min(hit.Text.Length, Math.Max(0, text.Length - at));
        return HiddenFinding(text, hit.Reason, text.Substring(at, length), at, info);
    }

    private static Finding HiddenFinding(string text, string reason, string value, int start, bool info = false)
    {
        var length = Math.Min(value.Length, Math.Max(0, text.Length - start));
        var ruleId = info ? "hidden.instruction" : "hidden.content";
        return new Finding
        {
            RuleId = ruleId,
            Label = reason,
            DisplayLabel = reason,
            PluralLabel = "hidden items",
            Severity = info ? Severity.Info : Severity.Warn,
            Start = start,
            Length = length,
            Placeholder = "HIDDEN",
            Category = "hidden",
            Snippet = ValueMask.Mask(value, ruleId),
            Location = "clipboard HTML, " + reason,
            Context = FindingPlaces.Context(text, start, length, ruleId)
        }.Located("clipboard HTML, " + reason, FindingPlaces.Context(text, start, length, ruleId));
    }

    private static List<Finding> ExceptAllowed(string text, List<Finding> findings, Func<string, bool>? isAllowed)
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

    private static ScanSession Failed(string error) => new()
    {
        Request = ScanRequest.ForText("", ScanSource.Clipboard),
        Text = "",
        Result = ScanResult.Failed(ScanSource.Clipboard, "clipboard", error)
    };
}
