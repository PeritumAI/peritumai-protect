using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Files;

public sealed class CleanOptions
{
    public bool AcceptTrackedChanges { get; init; } = true;
    public bool RemoveSpeakerNotes { get; init; } = true;
}

public sealed class CleanPackage
{
    public byte[] Bytes { get; init; } = [];
    public string Extension { get; init; } = "";
    public IReadOnlyList<string> Removed { get; init; } = [];
    public IReadOnlyList<string> DroppedEntries { get; init; } = [];
    public string? Error { get; init; }
}

public static class ShareCleaner
{
    public static CleanPackage Clean(string path, CleanOptions? options = null)
    {
        options ??= new CleanOptions();
        var extension = Path.GetExtension(path);
        try
        {
            if (IsOffice(extension))
                return CleanOffice(File.ReadAllBytes(path), extension, options);
            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return CleanPdf(path);
            if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) || extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                return CleanHtml(File.ReadAllText(path), extension);
            if (new ImageFileAdapter(null).CanHandle(extension))
                return CleanImage(path, extension);
            var bytes = File.ReadAllBytes(path);
            return new CleanPackage { Bytes = bytes, Extension = extension, Removed = ["Nothing to remove"] };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return new CleanPackage { Error = "Could not make a clean copy. Original was not changed.", Extension = extension };
        }
    }

    public static string? Leftover(byte[] package, IReadOnlyList<string> droppedEntries)
    {
        if (droppedEntries.Count == 0 || package.Length < 4 || package[0] != (byte)'P' || package[1] != (byte)'K')
            return null;
        using var stream = new MemoryStream(package);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var name in droppedEntries)
        {
            if (zip.GetEntry(name) is not null)
                return name;
        }

        return null;
    }

    private static bool IsOffice(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".docm", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pptm", StringComparison.OrdinalIgnoreCase);

    private static CleanPackage CleanOffice(byte[] package, string extension, CleanOptions options)
    {
        var removed = new List<string>();
        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var input = new MemoryStream(package))
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;
                using var reader = entry.Open();
                using var copy = new MemoryStream();
                reader.CopyTo(copy);
                parts[entry.FullName.Replace('\\', '/')] = copy.ToArray();
            }
        }

        int slideWidth = 12_192_000;
        int slideHeight = 6_858_000;
        if (parts.TryGetValue("ppt/presentation.xml", out var presentation))
            (slideWidth, slideHeight) = SlideSize(presentation, slideWidth, slideHeight);

        foreach (var name in parts.Keys.ToList())
        {
            var reason = DropReason(name, options);
            if (reason is null)
                continue;
            dropped.Add(name);
            Note(removed, reason);
        }

        var sheetNames = new List<string>();
        var sheetError = RemoveHiddenSheets(parts, dropped, removed, sheetNames);
        if (sheetError is not null)
            return new CleanPackage { Error = sheetError, Extension = extension };
        var slideError = RemoveHiddenSlides(parts, dropped, removed);
        if (slideError is not null)
            return new CleanPackage { Error = slideError, Extension = extension };

        foreach (var name in parts.Keys.ToList())
        {
            if (dropped.Contains(name) || !LooksXml(name))
                continue;
            var xml = Encoding.UTF8.GetString(parts[name]);
            if (xml.Length > 0 && xml[0] == '\uFEFF')
                xml = xml[1..];
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            var changed = false;
            if (name.EndsWith("core.xml", StringComparison.OrdinalIgnoreCase) && ClearCore(doc))
            {
                changed = true;
                Note(removed, "Document properties");
            }

            if (name.EndsWith("app.xml", StringComparison.OrdinalIgnoreCase) && ClearApp(doc))
            {
                changed = true;
                Note(removed, "Application properties");
            }

            if (name.EndsWith("custom.xml", StringComparison.OrdinalIgnoreCase) && ClearCustom(doc))
            {
                changed = true;
                Note(removed, "Custom properties");
            }

            if (RewriteWord(doc, options, removed))
                changed = true;
            if (name.Contains("worksheets/", StringComparison.OrdinalIgnoreCase))
                changed |= HideRowsAndColumns(doc, removed);
            if (name.Contains("/slides/slide", StringComparison.OrdinalIgnoreCase))
                changed |= OffSlide(doc, slideWidth, slideHeight, removed);
            if (name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                changed |= ContentTypes(doc, dropped, removed);

            if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                changed |= Relationships(doc, name, dropped, parts);

            if (changed)
                parts[name] = Encoding.UTF8.GetBytes(doc.Declaration is null ? doc.ToString() : doc.Declaration + "\n" + doc.ToString());
        }

        foreach (var name in dropped)
            parts.Remove(name);

        var residue = Residue(parts, removed, sheetNames);
        if (residue is not null)
            return new CleanPackage { Error = residue, Extension = extension };

        var outputExt = extension.ToLowerInvariant() switch
        {
            ".docm" => ".docx",
            ".xlsm" => ".xlsx",
            ".pptm" => ".pptx",
            _ => extension
        };
        if (!outputExt.Equals(extension, StringComparison.OrdinalIgnoreCase))
            Note(removed, "Macros");

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var pair in parts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entry = zip.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var writer = entry.Open();
                writer.Write(pair.Value);
            }
        }

        if (removed.Count == 0)
            removed.Add("Nothing to remove");
        return new CleanPackage
        {
            Bytes = output.ToArray(),
            Extension = outputExt,
            Removed = removed,
            DroppedEntries = dropped.ToList()
        };
    }

    private static CleanPackage CleanPdf(string path)
    {
        var raw = File.ReadAllBytes(path);
        var latin = Encoding.Latin1.GetString(raw);
        var removed = new List<string>();
        if (latin.Contains("/Author", StringComparison.Ordinal) || latin.Contains("/Title", StringComparison.Ordinal) || latin.Contains("/Info", StringComparison.Ordinal))
            removed.Add("PDF document info");
        if (latin.Contains("xpacket", StringComparison.OrdinalIgnoreCase) || latin.Contains("/Metadata", StringComparison.Ordinal))
            removed.Add("PDF metadata");
        var read = new PdfFileAdapter().Read(path);
        if (read.Error is not null)
            return new CleanPackage { Error = read.Error, Extension = ".pdf" };
        foreach (var segment in read.Segments)
        {
            var label = segment.Place?.Label ?? "";
            var comma = label.IndexOf(',');
            if (comma > 0)
                removed.Add(label[(comma + 1)..].Trim());
            else if (label is "document info" or "document metadata")
                removed.Add(label);
        }

        var visible = string.Join("\n", read.Segments.Where(segment =>
        {
            var label = segment.Place?.Label ?? "";
            return label.StartsWith("page ", StringComparison.Ordinal) && !label.Contains(',');
        }).Select(segment => segment.Text));
        if (string.IsNullOrWhiteSpace(visible))
            visible = " ";
        var bytes = SimplePdfWriter.FromText(visible, out var fontNote);
        removed.Add(PdfFileAdapter.TextOnlyNote);
        if (!string.IsNullOrEmpty(fontNote))
            removed.Add(fontNote);
        return new CleanPackage { Bytes = bytes, Extension = ".pdf", Removed = removed.Distinct(StringComparer.Ordinal).ToList() };
    }

    private static CleanPackage CleanHtml(string html, string extension)
    {
        var read = HtmlClipboard.Read(html);
        var removed = new List<string>();
        if (read.Hidden.Any(hit => hit.Reason == "HTML comment"))
            removed.Add("HTML comments");
        if (read.Hidden.Any(hit => hit.Reason is "Hidden HTML" or "Off-screen text"))
            removed.Add("Hidden HTML");
        if (html.Contains("<meta", StringComparison.OrdinalIgnoreCase))
            removed.Add("HTML metadata");
        var sanitized = StripMeta(HtmlClipboard.Sanitize(read.Fragment, []));
        if (removed.Count == 0)
            removed.Add("Nothing to remove");
        return new CleanPackage
        {
            Bytes = Encoding.UTF8.GetBytes(sanitized),
            Extension = extension,
            Removed = removed
        };
    }

    private static CleanPackage CleanImage(string path, string extension)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var loaded = new Bitmap(stream);
        var removed = new List<string>();
        var gps = false;
        try
        {
            foreach (var item in loaded.PropertyItems)
            {
                if (item.Id == 0x8825)
                    gps = true;
            }

            if (loaded.PropertyItems.Length > 0)
                removed.Add("Image metadata");
            if (gps || Encoding.Latin1.GetString(File.ReadAllBytes(path)).Contains("GPSLatitude", StringComparison.Ordinal))
                removed.Add("GPS location");
        }
        catch (ArgumentException)
        {
            // Some formats have no property list.
        }

        using var bitmap = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormatFor(extension));
        if (removed.Count == 0)
            removed.Add("Nothing to remove");
        return new CleanPackage { Bytes = output.ToArray(), Extension = extension, Removed = removed };
    }

    private static string? DropReason(string name, CleanOptions options)
    {
        var lower = name.Replace('\\', '/');
        if (lower.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) && lower.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase))
            return "Thumbnail";
        var file = Path.GetFileName(lower);
        if (file.Contains("comment", StringComparison.OrdinalIgnoreCase) || lower.Contains("/comments", StringComparison.OrdinalIgnoreCase) || lower.EndsWith("/people.xml", StringComparison.OrdinalIgnoreCase))
            return "Comments";
        if (lower.Contains("customxml/", StringComparison.OrdinalIgnoreCase))
            return "Custom XML";
        if (lower.Contains("vba", StringComparison.OrdinalIgnoreCase))
            return "Macros";
        if (lower.Contains("/externalLinks/", StringComparison.OrdinalIgnoreCase))
            return "External links";
        if (lower.EndsWith("/connections.xml", StringComparison.OrdinalIgnoreCase))
            return "Data connections";
        if (lower.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase))
            return "Embedded objects";
        if (options.RemoveSpeakerNotes && lower.Contains("/notesSlides/", StringComparison.OrdinalIgnoreCase))
            return "Speaker notes";
        return null;
    }

    private static bool RewriteWord(XDocument doc, CleanOptions options, List<string> removed)
    {
        var changed = false;
        var inserts = doc.Descendants().Where(element => element.Name.LocalName == "ins").ToList();
        var deletes = doc.Descendants().Where(element => element.Name.LocalName == "del").ToList();
        if (inserts.Count > 0 || deletes.Count > 0)
        {
            foreach (var insert in inserts)
            {
                if (options.AcceptTrackedChanges)
                    insert.ReplaceWith(insert.Nodes());
                else
                    insert.Remove();
            }

            foreach (var delete in deletes.ToList())
            {
                if (options.AcceptTrackedChanges)
                    delete.Remove();
                else
                    delete.ReplaceWith(delete.Nodes());
            }

            Note(removed, options.AcceptTrackedChanges ? "Tracked changes (accepted)" : "Tracked changes (rejected)");
            changed = true;
        }

        var hidden = doc.Descendants().Where(element => element.Name.LocalName == "r" && element.Descendants().Any(child => child.Name.LocalName is "vanish" or "specVanish")).ToList();
        if (hidden.Count > 0)
        {
            foreach (var run in hidden)
                run.Remove();
            Note(removed, "Hidden text");
            changed = true;
        }

        var comments = doc.Descendants().Where(element => element.Name.LocalName is "commentReference" or "commentRangeStart" or "commentRangeEnd").ToList();
        if (comments.Count > 0)
        {
            foreach (var comment in comments)
                comment.Remove();
            Note(removed, "Comments");
            changed = true;
        }

        var templates = doc.Descendants().Where(element => element.Name.LocalName == "attachedTemplate").ToList();
        if (templates.Count > 0)
        {
            foreach (var template in templates)
                template.Remove();
            Note(removed, "Document template");
            changed = true;
        }

        return changed;
    }

    private static string? RemoveHiddenSheets(Dictionary<string, byte[]> parts, HashSet<string> dropped, List<string> removed, List<string> sheetNames)
    {
        var workbookName = parts.Keys.FirstOrDefault(key =>
            key.EndsWith("/workbook.xml", StringComparison.OrdinalIgnoreCase) || key.Equals("workbook.xml", StringComparison.OrdinalIgnoreCase));
        if (workbookName is null || !TryParse(parts[workbookName], out var workbook))
            return null;

        var sheets = workbook.Descendants().Where(element => element.Name.LocalName == "sheet").ToList();
        var hidden = new List<(XElement Element, int Index, string Name, string? SheetId, string? Part)>();
        for (var i = 0; i < sheets.Count; i++)
        {
            if (!IsSheetHidden(sheets[i]))
                continue;
            var id = RelId(sheets[i]);
            var name = Attr(sheets[i], "name") ?? "";
            var part = id is null ? null : RelationshipTarget(parts, workbookName, id);
            hidden.Add((sheets[i], i, name, Attr(sheets[i], "sheetId"), part));
        }

        if (hidden.Count == 0)
            return null;
        if (hidden.Any(item => item.Part is null || !parts.ContainsKey(item.Part)))
            return "Could not make a clean copy. Hidden sheet is still in the file.";

        foreach (var item in hidden)
        {
            sheetNames.Add(item.Name);
            DropTree(parts, dropped, item.Part!);
        }

        ScrubSharedStrings(parts, hidden.Select(item => item.Part!).ToHashSet(StringComparer.OrdinalIgnoreCase));
        RemoveSheetFormulas(parts, dropped, sheetNames);
        RemoveDefinedNames(workbook, hidden);
        RemovePivotCaches(workbook, parts, workbookName, dropped);
        DropCalcChain(parts, dropped);
        DropChartsAndPivots(parts, dropped, sheetNames);
        UnhookDroppedDrawings(parts, dropped);

        foreach (var item in hidden)
            item.Element.Remove();
        parts[workbookName] = SaveXml(workbook);
        Note(removed, "Hidden sheet");
        return null;
    }

    private static string? RemoveHiddenSlides(Dictionary<string, byte[]> parts, HashSet<string> dropped, List<string> removed)
    {
        var presentationName = parts.Keys.FirstOrDefault(key => key.EndsWith("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase));
        if (presentationName is null || !TryParse(parts[presentationName], out var presentation))
            return null;
        var hidden = presentation.Descendants().Where(element => element.Name.LocalName == "sldId" && IsSlideHidden(element)).ToList();
        if (hidden.Count == 0)
            return null;
        foreach (var slide in hidden)
        {
            var id = RelId(slide);
            var part = id is null ? null : RelationshipTarget(parts, presentationName, id);
            if (part is null || !parts.ContainsKey(part))
                return "Could not make a clean copy. Hidden slide is still in the file.";
            DropSlide(parts, dropped, part);
            slide.Remove();
        }

        parts[presentationName] = SaveXml(presentation);
        Note(removed, "Hidden slide");
        return null;
    }

    private static void DropSlide(Dictionary<string, byte[]> parts, HashSet<string> dropped, string part)
    {
        dropped.Add(part);
        var relsName = RelsPath(part);
        if (parts.ContainsKey(relsName))
            dropped.Add(relsName);
        if (!parts.TryGetValue(relsName, out var bytes) || !TryParse(bytes, out var rels))
            return;
        foreach (var rel in rels.Descendants().Where(element => element.Name.LocalName == "Relationship"))
        {
            var type = Attr(rel, "Type") ?? "";
            var target = Attr(rel, "Target");
            if (target is null || type.Contains("slideLayout", StringComparison.OrdinalIgnoreCase) || type.Contains("slideMaster", StringComparison.OrdinalIgnoreCase))
                continue;
            var full = FindPart(parts, relsName, target);
            if (full is null || full.Contains("slideLayout", StringComparison.OrdinalIgnoreCase) || full.Contains("slideMaster", StringComparison.OrdinalIgnoreCase))
                continue;
            dropped.Add(full);
            var child = RelsPath(full);
            if (parts.ContainsKey(child))
                dropped.Add(child);
        }
    }

    private static bool IsSlideHidden(XElement slide)
    {
        var show = Attr(slide, "show");
        return show is "0" or "false";
    }

    private static void ScrubSharedStrings(Dictionary<string, byte[]> parts, HashSet<string> hiddenParts)
    {
        var keep = new HashSet<int>();
        var clear = new HashSet<int>();
        foreach (var name in parts.Keys.Where(key => key.Contains("worksheets/", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryParse(parts[name], out var sheet))
                continue;
            foreach (var cell in sheet.Descendants().Where(element => element.Name.LocalName == "c"))
            {
                if (!string.Equals(Attr(cell, "t"), "s", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "v")?.Value;
                if (!int.TryParse(value, out var index))
                    continue;
                if (hiddenParts.Contains(name))
                    clear.Add(index);
                else
                    keep.Add(index);
            }
        }

        clear.ExceptWith(keep);
        if (clear.Count == 0)
            return;
        var tableName = parts.Keys.FirstOrDefault(key => key.EndsWith("sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
        if (tableName is null || !TryParse(parts[tableName], out var table))
            return;
        var items = table.Descendants().Where(element => element.Name.LocalName == "si").ToList();
        var changed = false;
        for (var i = 0; i < items.Count; i++)
        {
            if (!clear.Contains(i))
                continue;
            foreach (var text in items[i].Descendants().Where(element => element.Name.LocalName == "t"))
            {
                if (text.Value.Length == 0)
                    continue;
                text.Value = "";
                changed = true;
            }
        }

        if (changed)
            parts[tableName] = SaveXml(table);
    }

    private static void RemoveSheetFormulas(Dictionary<string, byte[]> parts, HashSet<string> dropped, List<string> sheetNames)
    {
        foreach (var name in parts.Keys.Where(key => key.Contains("worksheets/", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (dropped.Contains(name) || !TryParse(parts[name], out var sheet))
                continue;
            var changed = false;
            foreach (var cell in sheet.Descendants().Where(element => element.Name.LocalName == "c").ToList())
            {
                var formula = cell.Elements().FirstOrDefault(element => element.Name.LocalName == "f")?.Value ?? "";
                if (sheetNames.Any(sheetName => ReferencesSheet(formula, sheetName)))
                {
                    cell.Remove();
                    changed = true;
                }
            }

            if (changed)
                parts[name] = SaveXml(sheet);
        }
    }

    private static void RemoveDefinedNames(XDocument workbook, List<(XElement Element, int Index, string Name, string? SheetId, string? Part)> hidden)
    {
        var indexes = hidden.Select(item => item.Index).ToHashSet();
        foreach (var defined in workbook.Descendants().Where(element => element.Name.LocalName == "definedName").ToList())
        {
            var local = Attr(defined, "localSheetId");
            var scoped = local is not null && int.TryParse(local, out var index) && indexes.Contains(index);
            var refers = hidden.Any(item => ReferencesSheet(defined.Value, item.Name));
            if (scoped || refers)
            {
                defined.Remove();
                continue;
            }

            if (local is not null && int.TryParse(local, out var kept) && indexes.Any(item => item < kept))
                SetAttr(defined, "localSheetId", (kept - indexes.Count(item => item < kept)).ToString());
        }

        foreach (var parent in workbook.Descendants().Where(element => element.Name.LocalName == "definedNames").ToList())
        {
            if (!parent.Elements().Any())
                parent.Remove();
        }

        foreach (var view in workbook.Descendants().Where(element => element.Name.LocalName == "workbookView"))
        {
            var tab = Attr(view, "activeTab");
            if (tab is null || !int.TryParse(tab, out var index))
                continue;
            if (indexes.Contains(index))
                SetAttr(view, "activeTab", "0");
            else if (indexes.Any(item => item < index))
                SetAttr(view, "activeTab", (index - indexes.Count(item => item < index)).ToString());
        }
    }

    private static void RemovePivotCaches(XDocument workbook, Dictionary<string, byte[]> parts, string workbookName, HashSet<string> dropped)
    {
        foreach (var cache in workbook.Descendants().Where(element => element.Name.LocalName == "pivotCache").ToList())
        {
            var id = RelId(cache);
            var part = id is null ? null : RelationshipTarget(parts, workbookName, id);
            if (part is not null && dropped.Contains(part))
                cache.Remove();
        }

        foreach (var parent in workbook.Descendants().Where(element => element.Name.LocalName == "pivotCaches").ToList())
        {
            if (!parent.Elements().Any())
                parent.Remove();
        }
    }

    private static void DropCalcChain(Dictionary<string, byte[]> parts, HashSet<string> dropped)
    {
        foreach (var name in parts.Keys.Where(key => key.EndsWith("/calcChain.xml", StringComparison.OrdinalIgnoreCase) || key.Equals("xl/calcChain.xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            dropped.Add(name);
            var rels = RelsPath(name);
            if (parts.ContainsKey(rels))
                dropped.Add(rels);
        }
    }

    private static void DropChartsAndPivots(Dictionary<string, byte[]> parts, HashSet<string> dropped, List<string> sheetNames)
    {
        foreach (var name in parts.Keys.ToList())
        {
            if (dropped.Contains(name) || !LooksXml(name) || !TryParse(parts[name], out var doc))
                continue;
            var xml = doc.ToString();
            var chart = name.Contains("chart", StringComparison.OrdinalIgnoreCase) && sheetNames.Any(sheet => ReferencesSheet(xml, sheet));
            var pivot = doc.Descendants().Any(element => element.Name.LocalName == "worksheetSource" && sheetNames.Any(sheet => SheetAttr(element, sheet)));
            if (!chart && !pivot)
                continue;
            DropTree(parts, dropped, name);
            if (!pivot)
                continue;
            var match = System.Text.RegularExpressions.Regex.Match(name, @"pivotCacheDefinition(\d+)\.xml$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;
            var slash = name.LastIndexOf('/');
            var folder = slash < 0 ? "" : name[..(slash + 1)];
            var records = folder + "pivotCacheRecords" + match.Groups[1].Value + ".xml";
            if (parts.ContainsKey(records))
                DropTree(parts, dropped, records);
        }
    }

    private static bool SheetAttr(XElement element, string sheet)
    {
        var value = Attr(element, "sheet");
        return value is not null && value.Equals(sheet, StringComparison.OrdinalIgnoreCase);
    }

    private static void UnhookDroppedDrawings(Dictionary<string, byte[]> parts, HashSet<string> dropped)
    {
        foreach (var name in parts.Keys.Where(key => key.Contains("drawing", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (dropped.Contains(name) || !TryParse(parts[name], out var drawing))
                continue;
            var relsName = RelsPath(name);
            if (!parts.TryGetValue(relsName, out var relsBytes) || !TryParse(relsBytes, out var rels))
                continue;
            var removedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rel in rels.Descendants().Where(element => element.Name.LocalName == "Relationship"))
            {
                var target = Attr(rel, "Target");
                var id = Attr(rel, "Id");
                var full = target is null ? null : FindPart(parts, relsName, target);
                if (id is not null && full is not null && dropped.Contains(full))
                    removedIds.Add(id);
            }

            if (removedIds.Count == 0)
                continue;
            foreach (var frame in drawing.Descendants().Where(element => element.Name.LocalName == "graphicFrame").ToList())
            {
                var chart = frame.Descendants().FirstOrDefault(element => element.Name.LocalName == "chart");
                var id = chart is null ? null : RelId(chart);
                if (id is not null && removedIds.Contains(id))
                    frame.Remove();
            }

            parts[name] = SaveXml(drawing);
        }
    }

    private static void DropTree(Dictionary<string, byte[]> parts, HashSet<string> dropped, string part)
    {
        if (!dropped.Add(part))
            return;
        var relsName = RelsPath(part);
        if (parts.ContainsKey(relsName))
            dropped.Add(relsName);
        if (!parts.TryGetValue(relsName, out var bytes) || !TryParse(bytes, out var rels))
            return;
        foreach (var rel in rels.Descendants().Where(element => element.Name.LocalName == "Relationship"))
        {
            var target = Attr(rel, "Target");
            if (target is null)
                continue;
            var full = FindPart(parts, relsName, target);
            if (full is null || IsSharedPart(full))
                continue;
            DropTree(parts, dropped, full);
        }
    }

    private static bool IsSharedPart(string name)
    {
        var lower = name.Replace('\\', '/').ToLowerInvariant();
        return lower.Contains("sharedstrings")
            || lower.EndsWith("styles.xml")
            || lower.Contains("/theme/")
            || lower.Contains("slidelayout")
            || lower.Contains("slidemaster")
            || lower.Contains("notesmaster");
    }

    private static bool ReferencesSheet(string formula, string sheet)
    {
        if (string.IsNullOrEmpty(formula) || string.IsNullOrEmpty(sheet))
            return false;
        var quoted = "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'!";
        if (formula.Contains(quoted, StringComparison.OrdinalIgnoreCase))
            return true;
        var needle = sheet + "!";
        var index = 0;
        while ((index = formula.IndexOf(needle, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            if (index == 0 || !IsSheetNameChar(formula[index - 1]))
                return true;
            index += needle.Length;
        }

        return false;
    }

    private static bool IsSheetNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';

    private static string? Residue(Dictionary<string, byte[]> parts, List<string> removed, List<string> sheetNames)
    {
        foreach (var label in removed)
        {
            if (label == "Nothing to remove")
                continue;
            if (StillPresent(label, parts, sheetNames))
                return TextCatalog.Format("cleanStillThere", ("item", ReasonText.Item(label)));
        }

        return null;
    }

    private static bool StillPresent(string label, Dictionary<string, byte[]> parts, List<string> sheetNames)
    {
        bool Part(Func<string, bool> match) => parts.Keys.Any(match);
        switch (label)
        {
            case "Hidden sheet":
                return parts.Keys.Where(key => key.EndsWith("/workbook.xml", StringComparison.OrdinalIgnoreCase) || key.Equals("workbook.xml", StringComparison.OrdinalIgnoreCase))
                        .Any(key => TryParse(parts[key], out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "sheet" && IsSheetHidden(element)))
                    || parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var xml) && sheetNames.Any(sheet =>
                        xml.Descendants().Any(element =>
                            (element.Name.LocalName is "definedName" or "f" && ReferencesSheet(element.Value, sheet))
                            || (element.Name.LocalName == "worksheetSource" && SheetAttr(element, sheet))
                            || (pair.Key.Contains("chart", StringComparison.OrdinalIgnoreCase) && ReferencesSheet(xml.ToString(), sheet)))));
            case "Hidden slide":
                return Part(key => key.Contains("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase))
                    && parts.Where(pair => pair.Key.EndsWith("ppt/presentation.xml", StringComparison.OrdinalIgnoreCase))
                        .Any(pair => TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "sldId" && IsSlideHidden(element)));
            case "Comments":
                return Part(key => Path.GetFileName(key).Contains("comment", StringComparison.OrdinalIgnoreCase) || key.Contains("/comments", StringComparison.OrdinalIgnoreCase) || key.EndsWith("/people.xml", StringComparison.OrdinalIgnoreCase))
                    || parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName is "commentReference" or "commentRangeStart" or "commentRangeEnd"));
            case "Custom XML":
                return Part(key => key.Contains("customxml/", StringComparison.OrdinalIgnoreCase));
            case "Macros":
                return Part(key => key.Contains("vba", StringComparison.OrdinalIgnoreCase));
            case "Thumbnail":
                return Part(key => key.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) && key.StartsWith("docProps/", StringComparison.OrdinalIgnoreCase));
            case "Embedded objects":
                return Part(key => key.Contains("/embeddings/", StringComparison.OrdinalIgnoreCase));
            case "Speaker notes":
                return Part(key => key.Contains("/notesSlides/", StringComparison.OrdinalIgnoreCase));
            case "External links":
                return Part(key => key.Contains("/externalLinks/", StringComparison.OrdinalIgnoreCase));
            case "Data connections":
                return Part(key => key.EndsWith("/connections.xml", StringComparison.OrdinalIgnoreCase));
            case "Hidden text":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName is "vanish" or "specVanish"));
            case "Tracked changes (accepted)":
            case "Tracked changes (rejected)":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName is "ins" or "del"));
            case "Hidden rows":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "row" && IsHidden(element)));
            case "Hidden columns":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "col" && IsHidden(element)));
            case "Document template":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "attachedTemplate"));
            case "Document properties":
                return NamedTextRemains(parts, "creator", "lastModifiedBy", "title", "subject", "description", "keywords", "category", "revision");
            case "Application properties":
                return NamedTextRemains(parts, "Company", "Manager", "HyperlinkBase", "Template");
            case "Custom properties":
                return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => element.Name.LocalName == "property"));
            case "Off-slide shapes":
                return OffSlideRemains(parts);
            default:
                return false;
        }
    }

    private static bool NamedTextRemains(Dictionary<string, byte[]> parts, params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return parts.Any(pair => LooksXml(pair.Key) && TryParse(pair.Value, out var doc) && doc.Descendants().Any(element => set.Contains(element.Name.LocalName) && !string.IsNullOrWhiteSpace(element.Value)));
    }

    private static bool OffSlideRemains(Dictionary<string, byte[]> parts)
    {
        var width = 12_192_000;
        var height = 6_858_000;
        if (parts.TryGetValue("ppt/presentation.xml", out var presentation))
            (width, height) = SlideSize(presentation, width, height);
        foreach (var name in parts.Keys.Where(key => key.Contains("/slides/slide", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryParse(parts[name], out var doc))
                continue;
            foreach (var shape in doc.Descendants().Where(element => element.Name.LocalName is "sp" or "pic" or "cxnSp"))
            {
                var off = shape.Descendants().FirstOrDefault(element => element.Name.LocalName == "off");
                if (off is null)
                    continue;
                var x = LongAttr(off, "x") ?? 0;
                var y = LongAttr(off, "y") ?? 0;
                if (x < 0 || y < 0 || x >= width || y >= height)
                    return true;
            }
        }

        return false;
    }

    private static bool TryParse(byte[] bytes, out XDocument doc)
    {
        var xml = Encoding.UTF8.GetString(bytes);
        if (xml.Length > 0 && xml[0] == '\uFEFF')
            xml = xml[1..];
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            return true;
        }
        catch (System.Xml.XmlException)
        {
            doc = new XDocument();
            return false;
        }
    }

    private static byte[] SaveXml(XDocument doc) =>
        Encoding.UTF8.GetBytes(doc.Declaration is null ? doc.ToString() : doc.Declaration + "\n" + doc.ToString());

    private static string? Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

    private static string? RelId(XElement element)
    {
        var related = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == "id" && attribute.Name.NamespaceName.Contains("relationships", StringComparison.OrdinalIgnoreCase));
        return related?.Value ?? Attr(element, "id");
    }

    private static void SetAttr(XElement element, string name, string value)
    {
        var attribute = element.Attributes().FirstOrDefault(item => item.Name.LocalName == name);
        if (attribute is null)
            element.SetAttributeValue(name, value);
        else
            attribute.Value = value;
    }

    private static string RelsPath(string part)
    {
        var slash = part.LastIndexOf('/');
        var dir = slash < 0 ? "" : part[..(slash + 1)];
        return dir + "_rels/" + Path.GetFileName(part) + ".rels";
    }

    private static bool IsSheetHidden(XElement sheet)
    {
        var state = sheet.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "state")?.Value ?? "";
        return state.Equals("hidden", StringComparison.OrdinalIgnoreCase) || state.Equals("veryHidden", StringComparison.OrdinalIgnoreCase);
    }

    private static string MacroFree(string contentType) => contentType switch
    {
        "application/vnd.ms-word.document.macroEnabled.main+xml" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
        "application/vnd.ms-excel.sheet.macroEnabled.main+xml" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
        "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml" => "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml",
        _ => contentType
    };

    private static bool HideRowsAndColumns(XDocument doc, List<string> removed)
    {
        var changed = false;
        var rows = doc.Descendants().Where(element => element.Name.LocalName == "row" && IsHidden(element)).ToList();
        if (rows.Count > 0)
        {
            foreach (var row in rows)
                row.Remove();
            Note(removed, "Hidden rows");
            changed = true;
        }

        var hiddenCols = new HashSet<int>();
        foreach (var col in doc.Descendants().Where(element => element.Name.LocalName == "col" && IsHidden(element)).ToList())
        {
            var min = IntAttr(col, "min");
            var max = IntAttr(col, "max");
            if (min is null)
                continue;
            for (var i = min.Value; i <= (max ?? min.Value); i++)
                hiddenCols.Add(i);
            col.Remove();
            changed = true;
        }

        if (hiddenCols.Count > 0)
        {
            foreach (var cell in doc.Descendants().Where(element => element.Name.LocalName == "c").ToList())
            {
                var reference = cell.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "r")?.Value;
                if (reference is not null && hiddenCols.Contains(ColumnIndex(reference)))
                    cell.Remove();
            }

            Note(removed, "Hidden columns");
        }

        return changed;
    }

    private static bool OffSlide(XDocument doc, int width, int height, List<string> removed)
    {
        var shapes = doc.Descendants().Where(element => element.Name.LocalName is "sp" or "pic" or "cxnSp").ToList();
        var changed = false;
        foreach (var shape in shapes)
        {
            var off = shape.Descendants().FirstOrDefault(element => element.Name.LocalName == "off");
            if (off is null)
                continue;
            var x = LongAttr(off, "x") ?? 0;
            var y = LongAttr(off, "y") ?? 0;
            if (x >= 0 && y >= 0 && x < width && y < height)
                continue;
            shape.Remove();
            changed = true;
        }

        if (changed)
            Note(removed, "Off-slide shapes");
        return changed;
    }

    private static bool ContentTypes(XDocument doc, HashSet<string> dropped, List<string> removed)
    {
        var changed = false;
        foreach (var element in doc.Descendants().Where(item => item.Name.LocalName == "Override").ToList())
        {
            var part = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "PartName")?.Value ?? "";
            var normalized = part.TrimStart('/');
            if (dropped.Contains(normalized) || part.Contains("vba", StringComparison.OrdinalIgnoreCase))
            {
                element.Remove();
                changed = true;
            }
        }

        foreach (var attribute in doc.Descendants().SelectMany(element => element.Attributes()).ToList())
        {
            var next = MacroFree(attribute.Value);
            if (next != attribute.Value)
            {
                attribute.Value = next;
                Note(removed, "Macros");
                changed = true;
            }
        }

        return changed;
    }

    private static bool Relationships(XDocument doc, string relsName, HashSet<string> dropped, Dictionary<string, byte[]> parts)
    {
        var changed = false;
        foreach (var rel in doc.Descendants().Where(element => element.Name.LocalName == "Relationship").ToList())
        {
            var target = Attr(rel, "Target");
            if (target is null)
                continue;
            if (string.Equals(Attr(rel, "TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                continue;
            var full = FindPart(parts, relsName, target);
            if (target.Contains("vba", StringComparison.OrdinalIgnoreCase) || target.Contains("embeddings/", StringComparison.OrdinalIgnoreCase) || (full is not null && dropped.Contains(full)))
            {
                rel.Remove();
                changed = true;
                continue;
            }

            if (full is null)
                continue;
            var relative = RelativeTarget(relsName, full);
            if (!relative.Equals(target.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                SetAttr(rel, "Target", relative);
                changed = true;
            }
        }

        return changed;
    }

    private static bool ClearCore(XDocument doc)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "creator", "lastModifiedBy", "title", "subject", "description", "keywords", "category", "revision"
        };
        return ClearNamed(doc, names);
    }

    private static bool ClearApp(XDocument doc)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Company", "Manager", "HyperlinkBase", "Template"
        };
        return ClearNamed(doc, names);
    }

    private static bool ClearCustom(XDocument doc)
    {
        var props = doc.Descendants().Where(element => element.Name.LocalName == "property").ToList();
        if (props.Count == 0)
            return false;
        foreach (var prop in props)
            prop.Remove();
        return true;
    }

    private static bool ClearNamed(XDocument doc, HashSet<string> names)
    {
        var found = doc.Descendants().Where(element => names.Contains(element.Name.LocalName) && !string.IsNullOrWhiteSpace(element.Value)).ToList();
        if (found.Count == 0)
            return false;
        foreach (var element in found)
            element.Remove();
        return true;
    }

    private static string? RelationshipTarget(Dictionary<string, byte[]> parts, string workbookName, string id)
    {
        var rels = workbookName.Replace('\\', '/');
        var slash = rels.LastIndexOf('/');
        var dir = slash < 0 ? "" : rels[..(slash + 1)];
        var relsName = dir + "_rels/" + Path.GetFileName(rels) + ".rels";
        if (!parts.TryGetValue(relsName, out var bytes) || !TryParse(bytes, out var doc))
            return null;
        var rel = doc.Descendants().FirstOrDefault(element => element.Name.LocalName == "Relationship" && element.Attributes().Any(attribute => attribute.Name.LocalName == "Id" && attribute.Value == id));
        var target = rel?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Target")?.Value;
        return target is null ? null : FindPart(parts, relsName, target);
    }

    private static string? FindPart(Dictionary<string, byte[]> parts, string relsName, string target)
    {
        var normalized = target.Replace('\\', '/').Trim();
        var resolved = Resolve(relsName, normalized);
        if (parts.ContainsKey(resolved))
            return parts.Keys.First(key => key.Equals(resolved, StringComparison.OrdinalIgnoreCase));
        var trimmed = normalized.TrimStart('/');
        if (parts.ContainsKey(trimmed))
            return parts.Keys.First(key => key.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        var file = Path.GetFileName(trimmed);
        if (file.Length == 0)
            return null;
        var matches = parts.Keys.Where(key => key.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) || key.Equals(file, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static string RelativeTarget(string relsName, string part)
    {
        var slash = relsName.LastIndexOf('/');
        var folder = slash < 0 ? "" : relsName[..slash];
        if (folder.EndsWith("/_rels", StringComparison.OrdinalIgnoreCase))
            folder = folder[..^"/_rels".Length];
        var baseParts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var targetParts = part.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < baseParts.Length && common < targetParts.Length && baseParts[common].Equals(targetParts[common], StringComparison.OrdinalIgnoreCase))
            common++;
        var pieces = new List<string>();
        for (var i = 0; i < baseParts.Length - common; i++)
            pieces.Add("..");
        pieces.AddRange(targetParts.Skip(common));
        return string.Join('/', pieces);
    }

    private static string Resolve(string relsName, string target)
    {
        target = target.Replace('\\', '/');
        if (target.StartsWith('/'))
            return target.TrimStart('/');
        var slash = relsName.LastIndexOf('/');
        var folder = slash < 0 ? "" : relsName[..slash];
        if (folder.EndsWith("/_rels", StringComparison.OrdinalIgnoreCase))
            folder = folder[..^"/_rels".Length];
        var combined = string.IsNullOrEmpty(folder) ? target : folder.TrimEnd('/') + "/" + target;
        var parts = new List<string>();
        foreach (var piece in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (piece == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            if (piece != ".")
                parts.Add(piece);
        }

        return string.Join('/', parts);
    }

    private static (int Width, int Height) SlideSize(byte[] presentation, int width, int height)
    {
        try
        {
            var doc = XDocument.Parse(Encoding.UTF8.GetString(presentation));
            var size = doc.Descendants().FirstOrDefault(element => element.Name.LocalName == "sldSz");
            if (size is null)
                return (width, height);
            return ((int)(LongAttr(size, "cx") ?? width), (int)(LongAttr(size, "cy") ?? height));
        }
        catch (System.Xml.XmlException)
        {
            return (width, height);
        }
    }

    private static bool IsHidden(XElement element)
    {
        var value = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "hidden")?.Value;
        return value is "1" or "true";
    }

    private static int? IntAttr(XElement element, string name)
    {
        var value = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
        return int.TryParse(value, out var number) ? number : null;
    }

    private static long? LongAttr(XElement element, string name)
    {
        var value = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;
        return long.TryParse(value, out var number) ? number : null;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c))
                break;
            index = index * 26 + char.ToUpperInvariant(c) - 'A' + 1;
        }

        return index;
    }

    private static string StripMeta(string html)
    {
        var sb = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            if (html.AsSpan(i).StartsWith("<meta", StringComparison.OrdinalIgnoreCase))
            {
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }

            sb.Append(html[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool LooksXml(string name) =>
        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static void Note(List<string> removed, string item)
    {
        if (!removed.Contains(item, StringComparer.Ordinal))
            removed.Add(item);
    }

    private static ImageFormat ImageFormatFor(string extension)
    {
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Jpeg;
        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Bmp;
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Gif;
        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return ImageFormat.Tiff;
        return ImageFormat.Png;
    }
}
