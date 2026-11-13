using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Peritum.Protect.Core.Files;

public sealed class PptxFileAdapter : IFileAdapter, IPackageFile
{
    public bool CanHandle(string extension) =>
        extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase);

    public DocumentRead Read(string path) => Read(path, new PackageLimits.Budget(), 1);

    public DocumentRead Read(string path, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            return new DocumentRead { Incomplete = true, Note = PackageLimits.NestedTooDeep };
        var blocked = PackageLimits.Blocked(path, budget, depth);
        if (blocked is not null)
            return blocked;
        using var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
        using var doc = PresentationDocument.Open(stream, false);
        var sb = new StringBuilder();
        var segments = new List<TextSegment>();
        foreach (var group in Groups(doc))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            segments.Add(new TextSegment { Start = sb.Length, Text = group.Text, Place = SegmentPlace.Fixed(group.Label) });
            sb.Append(group.Text);
        }

        return new DocumentRead
        {
            Text = sb.ToString(),
            Segments = segments,
            ExtraText = SlideExtra(doc)
        };
    }

    private static string SlideExtra(PresentationDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var part in doc.PresentationPart?.GetPartsOfType<SlideCommentsPart>() ?? [])
            sb.AppendLine(part.CommentList?.InnerText);
        if (doc.PresentationPart is null)
            return sb.ToString();
        foreach (var slide in doc.PresentationPart.SlideParts)
        {
            foreach (var rel in slide.HyperlinkRelationships)
                sb.AppendLine(rel.Uri.ToString());
        }

        return sb.ToString();
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var temp = XlsxFileAdapter.TempCopy(originalPath, ".pptx");
        try
        {
            var mode = redaction ?? CopyRedaction.Permanent;
            using (var doc = PresentationDocument.Open(temp, true))
            {
                var nodes = Groups(doc).ToList();
                if (nodes.Count != read.Segments.Count)
                    throw new InvalidDataException("Could not read this file. Original was not changed.");

                for (var i = 0; i < nodes.Count; i++)
                {
                    var segment = read.Segments[i];
                    var local = new List<Finding>();
                    foreach (var finding in findings)
                    {
                        if (finding.Start >= segment.Start && finding.Start + finding.Length <= segment.Start + segment.Text.Length)
                            local.Add(finding.Shift(-segment.Start));
                    }
                    if (local.Count == 0)
                        continue;
                    var redacted = mode.Apply(segment.Text, local);
                    nodes[i].Group.Runs[0].Text = redacted;
                    for (var run = 1; run < nodes[i].Group.Runs.Count; run++)
                        nodes[i].Group.Runs[run].Text = "";
                }

                if (mode.Reversible)
                    AddMapLine(doc, mode.Session!.MapLine);

                doc.Save();
            }

            return File.ReadAllBytes(temp);
        }
        finally
        {
            XlsxFileAdapter.TryDelete(temp);
        }
    }

    private static void AddMapLine(PresentationDocument doc, string mapLine)
    {
        var parent = doc.PresentationPart?.SlideParts.FirstOrDefault()?.Slide
            .Descendants<A.Paragraph>()
            .LastOrDefault()
            ?.Parent;
        parent?.AppendChild(new A.Paragraph(new A.Run(new A.Text(mapLine))));
    }

    private static IEnumerable<LocatedGroup> Groups(PresentationDocument doc)
    {
        var presentation = doc.PresentationPart;
        if (presentation is null)
            yield break;

        var numbers = new Dictionary<SlidePart, int>();
        var next = 0;
        foreach (var id in presentation.Presentation?.SlideIdList?.Elements<SlideId>() ?? [])
        {
            if (id.RelationshipId?.Value is null)
                continue;
            if (presentation.GetPartById(id.RelationshipId.Value) is SlidePart slide)
                numbers[slide] = ++next;
        }

        foreach (var part in presentation.SlideMasterParts)
        {
            foreach (var group in Paragraphs(part.SlideMaster))
                yield return new LocatedGroup(group, "slide master, " + PartOf(group.Paragraph));
            foreach (var layout in part.SlideLayoutParts)
            {
                foreach (var group in Paragraphs(layout.SlideLayout))
                    yield return new LocatedGroup(group, "layout, " + PartOf(group.Paragraph));
            }
        }

        foreach (var slide in presentation.SlideParts)
        {
            if (!numbers.TryGetValue(slide, out var number))
                number = ++next;
            foreach (var group in Paragraphs(slide.Slide))
                yield return new LocatedGroup(group, $"Slide {number}, {PartOf(group.Paragraph)}");
            if (slide.NotesSlidePart?.NotesSlide is not null)
            {
                foreach (var group in Paragraphs(slide.NotesSlidePart.NotesSlide))
                    yield return new LocatedGroup(group, $"Slide {number}, notes");
            }
        }
    }

    private static string PartOf(A.Paragraph paragraph)
    {
        if (paragraph.Ancestors<A.Table>().Any())
            return "table";
        var shape = paragraph.Ancestors<P.Shape>().FirstOrDefault();
        var type = shape?.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value;
        if (type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle)
            return "title";
        return "body";
    }

    private static IEnumerable<RunGroup> Paragraphs(OpenXmlElement root)
    {
        foreach (var paragraph in root.Descendants<A.Paragraph>())
        {
            var runs = paragraph.Descendants<A.Text>().ToList();
            if (runs.Count == 0)
                continue;
            yield return new RunGroup(runs, paragraph);
        }
    }

    private sealed class RunGroup(List<A.Text> runs, A.Paragraph paragraph)
    {
        public List<A.Text> Runs { get; } = runs;
        public A.Paragraph Paragraph { get; } = paragraph;
        public string Text => string.Concat(Runs.Select(run => run.Text));
    }

    private readonly record struct LocatedGroup(RunGroup Group, string Label)
    {
        public string Text => Group.Text;
    }
}
