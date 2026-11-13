using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Files;

public sealed class XlsxFileAdapter : IFileAdapter, IPackageFile
{
    public bool CanHandle(string extension) =>
        extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase);

    public DocumentRead Read(string path) => Read(path, new PackageLimits.Budget(), 1);

    public DocumentRead Read(string path, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            return new DocumentRead { Incomplete = true, Note = PackageLimits.NestedTooDeep };
        var blocked = PackageLimits.Blocked(path, budget, depth);
        if (blocked is not null)
            return blocked;
        using var stream = new MemoryStream(File.ReadAllBytes(path), writable: false);
        using var doc = SpreadsheetDocument.Open(stream, false);
        return Capture(doc).Read;
    }

    public byte[] BuildProtected(string originalPath, DocumentRead read, IReadOnlyList<Finding> findings, CopyRedaction? redaction = null)
    {
        var temp = TempCopy(originalPath, ".xlsx");
        try
        {
            var mode = redaction ?? CopyRedaction.Permanent;
            using (var doc = SpreadsheetDocument.Open(temp, true))
            {
                var pieces = Capture(doc).Pieces;
                if (pieces.Count != read.Segments.Count)
                    throw new InvalidDataException("Could not read this file. Original was not changed.");

                for (var i = 0; i < pieces.Count; i++)
                {
                    var segment = read.Segments[i];
                    var local = FindingsInside(findings, segment);
                    if (local.Count == 0)
                        continue;
                    Apply(pieces[i], mode.Apply(segment.Text, local));
                }

                if (mode.Reversible)
                    AddFooter(doc, mode.Session!.MapLine);

                doc.Save();
            }

            return File.ReadAllBytes(temp);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static CaptureResult Capture(SpreadsheetDocument doc)
    {
        var sb = new StringBuilder();
        var segments = new List<TextSegment>();
        var pieces = new List<Piece>();

        void Add(string? text, Piece piece, string location, string header = "")
        {
            var value = text ?? "";
            if (sb.Length > 0)
                sb.Append('\n');
            segments.Add(new TextSegment { Start = sb.Length, Text = value, Place = SegmentPlace.Fixed(location), Header = header });
            sb.Append(value);
            pieces.Add(piece);
        }

        var table = doc.WorkbookPart?.SharedStringTablePart?.SharedStringTable;
        var shared = table?.Elements<SharedStringItem>().ToList() ?? [];
        var sheets = SheetMap(doc);
        var headers = HeaderMap(doc, shared);

        if (doc.WorkbookPart is not null)
        {
            foreach (var worksheet in OrderedSheets(doc))
            {
                var info = sheets.TryGetValue(worksheet, out var known) ? known : new SheetInfo("Sheet", false);
                foreach (var cell in worksheet.Worksheet.Descendants<Cell>())
                {
                    var header = ColumnHeader(cell, headers, worksheet);
                    var type = cell.DataType?.Value;
                    if (type is not null && type == CellValues.InlineString)
                        Add(cell.InlineString?.InnerText, new Piece(PieceKind.Element, cell, null), CellLocation(info, cell, headers, worksheet), header);
                    else if (type == CellValues.SharedString &&
                             int.TryParse(cell.CellValue?.Text, out var index) &&
                             index >= 0 && index < shared.Count)
                    {
                        Add(shared[index].InnerText, new Piece(PieceKind.CellValue, cell, shared[index]), CellLocation(info, cell, headers, worksheet), header);
                    }
                    else if (type is null || type == CellValues.Number || type == CellValues.String)
                    {
                        var value = cell.CellValue?.Text;
                        if (!string.IsNullOrEmpty(value))
                            Add(value, new Piece(PieceKind.CellValue, cell, null), CellLocation(info, cell, headers, worksheet), header);
                    }
                }

                var comments = worksheet.WorksheetCommentsPart?.Comments?.Descendants<Comment>();
                if (comments is not null)
                {
                    foreach (var comment in comments)
                    {
                        var value = comment.InnerText;
                        if (string.IsNullOrEmpty(value))
                            continue;
                        var reference = comment.Reference?.Value ?? "";
                        var label = string.IsNullOrEmpty(reference) ? info.Name + ", comment" : info.Name + "!" + reference + ", comment";
                        if (info.Hidden)
                            label += ", hidden sheet";
                        Add(value, new Piece(PieceKind.Element, comment, null), label);
                    }
                }

                foreach (var footer in worksheet.Worksheet.Elements<HeaderFooter>())
                {
                    AddHeader(footer, "OddHeader", footer.OddHeader?.Text, "header");
                    AddHeader(footer, "EvenHeader", footer.EvenHeader?.Text, "header");
                    AddHeader(footer, "FirstHeader", footer.FirstHeader?.Text, "header");
                    AddHeader(footer, "OddFooter", footer.OddFooter?.Text, "footer");
                    AddHeader(footer, "EvenFooter", footer.EvenFooter?.Text, "footer");
                    AddHeader(footer, "FirstFooter", footer.FirstFooter?.Text, "footer");
                }

                void AddHeader(HeaderFooter footer, string slot, string? value, string kind)
                {
                    if (string.IsNullOrEmpty(value))
                        return;
                    var label = info.Name + ", " + kind;
                    if (info.Hidden)
                        label += ", hidden sheet";
                    Add(value, new Piece(PieceKind.HeaderFooter, footer, null, slot), label);
                }
            }
        }

        return new CaptureResult(
            new DocumentRead
            {
                Text = sb.ToString(),
                Segments = segments,
                ExtraText = Extra(doc)
            },
            pieces);
    }

    private static void AddFooter(SpreadsheetDocument doc, string mapLine)
    {
        var sheetData = doc.WorkbookPart?.WorksheetParts.FirstOrDefault()?.Worksheet.GetFirstChild<SheetData>();
        if (sheetData is null)
            return;
        uint next = 1;
        foreach (var existing in sheetData.Elements<Row>())
        {
            if (existing.RowIndex is not null && existing.RowIndex.Value >= next)
                next = existing.RowIndex.Value + 1;
        }

        var row = new Row { RowIndex = next };
        row.Append(new Cell
        {
            CellReference = "A" + next,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(mapLine) { Space = SpaceProcessingModeValues.Preserve })
        });
        sheetData.Append(row);
    }

    private static void Apply(Piece piece, string redacted)
    {
        if (piece.Kind == PieceKind.HeaderFooter && piece.Element is HeaderFooter footer && piece.Slot is not null)
        {
            switch (piece.Slot)
            {
                case "OddHeader": footer.OddHeader = new OddHeader { Text = redacted }; break;
                case "EvenHeader": footer.EvenHeader = new EvenHeader { Text = redacted }; break;
                case "FirstHeader": footer.FirstHeader = new FirstHeader { Text = redacted }; break;
                case "OddFooter": footer.OddFooter = new OddFooter { Text = redacted }; break;
                case "EvenFooter": footer.EvenFooter = new EvenFooter { Text = redacted }; break;
                case "FirstFooter": footer.FirstFooter = new FirstFooter { Text = redacted }; break;
            }
            return;
        }

        if (piece.Element is Comment comment)
        {
            comment.CommentText = new CommentText(new Run(new Text(redacted) { Space = SpaceProcessingModeValues.Preserve }));
            return;
        }

        if (piece.Kind == PieceKind.CellValue && piece.Element is Cell cell)
        {
            cell.CellFormula = null;
            cell.DataType = CellValues.InlineString;
            cell.CellValue = null;
            cell.InlineString = new InlineString(new Text(redacted) { Space = SpaceProcessingModeValues.Preserve });
            if (piece.Shared is not null)
            {
                piece.Shared.RemoveAllChildren();
                piece.Shared.AppendChild(new Text(redacted) { Space = SpaceProcessingModeValues.Preserve });
            }
            return;
        }

        var texts = piece.Element.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            if (piece.Element is SharedStringItem item)
                item.AppendChild(new Text(redacted) { Space = SpaceProcessingModeValues.Preserve });
            else if (piece.Element is Cell inline && inline.InlineString is not null)
                inline.InlineString.AppendChild(new Text(redacted) { Space = SpaceProcessingModeValues.Preserve });
            return;
        }

        texts[0].Text = redacted;
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        for (var i = 1; i < texts.Count; i++)
            texts[i].Text = "";
    }

    private static List<Finding> FindingsInside(IReadOnlyList<Finding> findings, TextSegment segment)
    {
        var local = new List<Finding>();
        foreach (var finding in findings)
        {
            if (finding.Start >= segment.Start && finding.Start + finding.Length <= segment.Start + segment.Text.Length)
                local.Add(finding.Shift(-segment.Start));
        }
        return local;
    }

    internal static string TempCopy(string originalPath, string extension)
    {
        var temp = Path.Combine(Path.GetTempPath(), "peritum-protect-" + Guid.NewGuid().ToString("n") + extension);
        File.Copy(originalPath, temp, overwrite: false);
        return temp;
    }

    internal static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The temp file is not the original.
        }
    }

    private enum PieceKind { Element, CellValue, HeaderFooter }

    private readonly record struct SheetInfo(string Name, bool Hidden);

    private static Dictionary<WorksheetPart, SheetInfo> SheetMap(SpreadsheetDocument doc)
    {
        var map = new Dictionary<WorksheetPart, SheetInfo>();
        var workbook = doc.WorkbookPart;
        if (workbook?.Workbook is null)
            return map;
        foreach (var sheet in workbook.Workbook.Descendants<Sheet>())
        {
            if (sheet.Id?.Value is null)
                continue;
            if (workbook.GetPartById(sheet.Id.Value) is not WorksheetPart part)
                continue;
            var state = sheet.State?.Value;
            var hidden = state == SheetStateValues.Hidden || state == SheetStateValues.VeryHidden;
            map[part] = new SheetInfo(sheet.Name?.Value ?? "Sheet", hidden);
        }

        return map;
    }

    private static List<WorksheetPart> OrderedSheets(SpreadsheetDocument doc)
    {
        var workbook = doc.WorkbookPart;
        var list = new List<WorksheetPart>();
        if (workbook is null)
            return list;
        var seen = new HashSet<WorksheetPart>();
        foreach (var sheet in workbook.Workbook?.Descendants<Sheet>() ?? [])
        {
            if (sheet.Id?.Value is null)
                continue;
            if (workbook.GetPartById(sheet.Id.Value) is not WorksheetPart part || !seen.Add(part))
                continue;
            list.Add(part);
        }

        foreach (var part in workbook.WorksheetParts)
        {
            if (seen.Add(part))
                list.Add(part);
        }

        return list;
    }

    private static Dictionary<(WorksheetPart Part, string Column), string> HeaderMap(SpreadsheetDocument doc, List<SharedStringItem> shared)
    {
        var headers = new Dictionary<(WorksheetPart, string), string>();
        foreach (var worksheet in OrderedSheets(doc))
        {
            foreach (var cell in worksheet.Worksheet.Descendants<Cell>())
            {
                if (!TrySplitRef(cell.CellReference?.Value, out var column, out var row) || row != 1)
                    continue;
                var text = CellText(cell, shared);
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (text.Length > 40)
                    text = text[..40];
                headers[(worksheet, column)] = text;
            }
        }

        return headers;
    }

    private static string ColumnHeader(Cell cell, Dictionary<(WorksheetPart Part, string Column), string> headers, WorksheetPart worksheet)
    {
        if (!TrySplitRef(cell.CellReference?.Value, out var column, out var row) || row == 1)
            return "";
        return headers.TryGetValue((worksheet, column), out var header) ? header : "";
    }

    private static string CellLocation(SheetInfo info, Cell cell, Dictionary<(WorksheetPart Part, string Column), string> headers, WorksheetPart worksheet)
    {
        var reference = cell.CellReference?.Value ?? "";
        var core = string.IsNullOrEmpty(reference) ? info.Name : info.Name + "!" + reference;
        if (TrySplitRef(reference, out var column, out var row) && row != 1 &&
            headers.TryGetValue((worksheet, column), out var header) && header.Length > 0)
            core += " (" + header + ")";
        if (info.Hidden)
            core += ", hidden sheet";
        return core;
    }

    private static string? CellText(Cell cell, List<SharedStringItem> shared)
    {
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString)
            return cell.InlineString?.InnerText;
        if (type == CellValues.SharedString &&
            int.TryParse(cell.CellValue?.Text, out var index) &&
            index >= 0 && index < shared.Count)
            return shared[index].InnerText;
        if (type is null || type == CellValues.Number || type == CellValues.String)
            return cell.CellValue?.Text;
        return null;
    }

    private static bool TrySplitRef(string? reference, out string column, out int row)
    {
        column = "";
        row = 0;
        if (string.IsNullOrEmpty(reference))
            return false;
        var i = 0;
        while (i < reference.Length && char.IsLetter(reference[i]))
            i++;
        if (i == 0 || i >= reference.Length || !int.TryParse(reference[i..], out row))
            return false;
        column = reference[..i].ToUpperInvariant();
        return true;
    }

    private static string Extra(SpreadsheetDocument doc)
    {
        var sb = new StringBuilder();
        var table = doc.WorkbookPart?.SharedStringTablePart?.SharedStringTable;
        if (table is not null)
            sb.AppendLine(table.InnerText);
        if (doc.WorkbookPart?.Workbook is not null)
        {
            foreach (var sheet in doc.WorkbookPart.Workbook.Descendants<Sheet>())
                sb.AppendLine(sheet.Name);
            foreach (var name in doc.WorkbookPart.Workbook.Descendants<DefinedName>())
                sb.AppendLine(name.Text);
        }
        foreach (var part in doc.WorkbookPart?.GetPartsOfType<ConnectionsPart>() ?? [])
            sb.AppendLine(part.Connections?.InnerText);
        foreach (var part in doc.WorkbookPart?.GetPartsOfType<PivotTableCacheDefinitionPart>() ?? [])
            sb.AppendLine(part.PivotCacheDefinition?.InnerText);
        foreach (var sheet in doc.WorkbookPart?.WorksheetParts ?? [])
        {
            foreach (var rel in sheet.HyperlinkRelationships)
                sb.AppendLine(rel.Uri.ToString());
        }

        return sb.ToString();
    }

    private sealed record Piece(PieceKind Kind, OpenXmlElement Element, SharedStringItem? Shared, string? Slot = null);

    private sealed record CaptureResult(DocumentRead Read, List<Piece> Pieces);
}
