using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Peritum.Protect.Core.Files;

public static class OfficePackageSanitizer
{
    private static readonly HashSet<string> CoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "creator", "lastModifiedBy", "title", "subject", "keywords", "description", "category"
    };

    private static readonly HashSet<string> AppNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Company", "Manager"
    };

    public static byte[] Apply(byte[] package, IReadOnlyCollection<string> values) =>
        Apply(package, values, new PackageLimits.Budget(), 1);

    private static byte[] Apply(byte[] package, IReadOnlyCollection<string> values, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            throw new PackageLimitException(PackageLimits.NestedTooDeep);
        if (package.Length < 4 || package[0] != (byte)'P' || package[1] != (byte)'K')
            return package;
        var needles = values
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToList();
        using var input = new MemoryStream(package);
        using var output = new MemoryStream();
        using (var source = new ZipArchive(input, ZipArchiveMode.Read))
        using (var dest = new ZipArchive(output, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var data = Read(entry, budget);
                if (IsThumbnail(entry.FullName))
                    continue;
                if (PackageLimits.IsZip(data))
                    data = Apply(data, values, budget, depth + 1);
                else if (IsText(entry.FullName))
                    data = RewriteTextPart(entry.FullName, data, needles);

                var created = dest.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var writer = created.Open();
                writer.Write(data);
            }
        }
        return output.ToArray();
    }

    public static string? FindLeak(byte[] package, IReadOnlyCollection<string> values) =>
        FindLeak(package, values, new PackageLimits.Budget(), 1);

    private static string? FindLeak(byte[] package, IReadOnlyCollection<string> values, PackageLimits.Budget budget, int depth)
    {
        if (depth > PackageLimits.MaxNestingDepth)
            throw new PackageLimitException(PackageLimits.NestedTooDeep);
        if (package.Length < 4 || package[0] != (byte)'P' || package[1] != (byte)'K')
            return FindInText(Encoding.UTF8.GetString(package), values, "the copy");
        using var input = new MemoryStream(package);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            var data = Read(entry, budget);
            if (PackageLimits.IsZip(data))
            {
                var nested = FindLeak(data, values, budget, depth + 1);
                if (nested is not null)
                    return nested;
                continue;
            }
            if (!IsText(entry.FullName))
                continue;
            var partText = Encoding.UTF8.GetString(data);
            if (partText.Length > 0 && partText[0] == '\uFEFF')
                partText = partText[1..];
            var leak = LooksLikeXml(partText)
                ? FindInXml(partText, values, PartName(entry.FullName))
                : FindInText(partText, values, PartName(entry.FullName));
            if (leak is not null)
                return leak;
        }
        return null;
    }

    private static string? FindInXml(string text, IReadOnlyCollection<string> values, string part)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.HasAttributes)
                {
                    while (reader.MoveToNextAttribute())
                    {
                        if (!IsStructuralAttribute(reader) && FindInText(reader.Value, values, part) is not null)
                            return part;
                    }
                    reader.MoveToElement();
                }
                else if ((reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA) &&
                         FindInText(reader.Value, values, part) is not null)
                {
                    return part;
                }
            }
            return null;
        }
        catch (XmlException)
        {
            return FindInText(text, values, part);
        }
    }

    public static string? FindInText(string text, IReadOnlyCollection<string> values, string part)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value))
                continue;
            if (text.Contains(value, StringComparison.Ordinal) || text.Contains(XmlEscape(value), StringComparison.Ordinal))
                return part;
        }
        return null;
    }

    private static byte[] RewriteTextPart(string name, byte[] data, IReadOnlyList<string> needles)
    {
        var text = Encoding.UTF8.GetString(data);
        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];
        byte[] result;
        if (!LooksLikeXml(text))
            result = Encoding.UTF8.GetBytes(ReplaceValues(text, needles));
        else
        {
            try
            {
                result = Encoding.UTF8.GetBytes(RewriteXml(name, text, needles));
            }
            catch (XmlException)
            {
                result = data;
            }
        }
        return result;
    }

    private static string RewriteXml(string name, string text, IReadOnlyList<string> needles)
    {
        var kind = PartKindOf(name);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = false,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var output = new StringBuilder();
        string? declaration = null;
        var writerSettings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
            ConformanceLevel = ConformanceLevel.Fragment,
            Encoding = new UTF8Encoding(false)
        };
        using var writer = XmlWriter.Create(output, writerSettings);
        var stack = new Stack<string>();
        var skipDepth = -1;
        while (reader.Read())
        {
            if (skipDepth >= 0)
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == skipDepth)
                    skipDepth = -1;
                continue;
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.XmlDeclaration:
                    declaration = reader.GetAttribute("standalone") == "yes"
                        ? "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                        : "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
                    break;
                case XmlNodeType.Element:
                    if (ShouldDropElement(kind, reader))
                    {
                        if (!reader.IsEmptyElement)
                            skipDepth = reader.Depth;
                        break;
                    }
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                            WriteAttribute(writer, reader, needles);
                        reader.MoveToElement();
                    }
                    if (reader.IsEmptyElement)
                        writer.WriteEndElement();
                    else
                        stack.Push(reader.LocalName);
                    break;
                case XmlNodeType.EndElement:
                    if (stack.Count > 0)
                        stack.Pop();
                    writer.WriteFullEndElement();
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    var parent = stack.Count > 0 ? stack.Peek() : "";
                    var value = ClearText(kind, parent) ? "" : ReplaceValues(reader.Value, needles);
                    if (reader.NodeType == XmlNodeType.CDATA)
                        writer.WriteCData(value);
                    else
                        writer.WriteString(value);
                    break;
                case XmlNodeType.SignificantWhitespace:
                case XmlNodeType.Whitespace:
                    writer.WriteWhitespace(reader.Value);
                    break;
                case XmlNodeType.Comment:
                    writer.WriteComment(reader.Value);
                    break;
                case XmlNodeType.ProcessingInstruction:
                    writer.WriteProcessingInstruction(reader.Name, reader.Value);
                    break;
            }
        }
        writer.Flush();
        if (declaration is null)
            return output.ToString();
        return declaration + output;
    }

    private static void WriteAttribute(XmlWriter writer, XmlReader reader, IReadOnlyList<string> needles)
    {
        var value = reader.Value;
        if (!IsStructuralAttribute(reader))
            value = ReplaceValues(value, needles);
        if (reader.Prefix == "xmlns")
            writer.WriteAttributeString("xmlns", reader.LocalName, null, value);
        else if (reader.LocalName == "xmlns")
            writer.WriteAttributeString("xmlns", value);
        else
            writer.WriteAttributeString(reader.Prefix, reader.LocalName, reader.NamespaceURI, value);
    }

    private static bool ShouldDropElement(PartKind kind, XmlReader reader)
    {
        var local = reader.LocalName;
        if (kind == PartKind.Custom && local.Equals("property", StringComparison.OrdinalIgnoreCase))
            return true;
        if (local.Equals("Override", StringComparison.OrdinalIgnoreCase) ||
            local.Equals("Relationship", StringComparison.OrdinalIgnoreCase))
        {
            var target = reader.GetAttribute("PartName") ?? reader.GetAttribute("Target") ?? "";
            var type = reader.GetAttribute("Type") ?? reader.GetAttribute("ContentType") ?? "";
            if (target.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
                type.Contains("thumbnail", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool ClearText(PartKind kind, string parent)
    {
        if (kind == PartKind.Core && CoreNames.Contains(parent))
            return true;
        if (kind == PartKind.App && AppNames.Contains(parent))
            return true;
        return false;
    }

    private static bool IsStructuralAttribute(XmlReader reader)
    {
        if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns")
            return true;
        var local = reader.LocalName;
        if (local.Equals("id", StringComparison.OrdinalIgnoreCase))
            return true;
        if (local.StartsWith("rsid", StringComparison.OrdinalIgnoreCase))
            return true;
        if (local.Equals("styleId", StringComparison.OrdinalIgnoreCase))
            return true;
        if (local.Equals("val", StringComparison.OrdinalIgnoreCase))
            return true;
        if (local is "cx" or "cy" or "x" or "y" or "w" or "h" or "l" or "t" or "b" or "r" or "ind" or "sz")
            return reader.Value.Length > 0 && reader.Value.All(c => char.IsDigit(c) || c == '-');
        return false;
    }

    private static string ReplaceValues(string text, IReadOnlyList<string> needles)
    {
        foreach (var value in needles)
        {
            text = text.Replace(value, "[REDACTED]", StringComparison.Ordinal);
            text = text.Replace(XmlEscape(value), "[REDACTED]", StringComparison.Ordinal);
        }
        return text;
    }

    private static string XmlEscape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static string PartName(string path)
    {
        var name = path.Replace('\\', '/');
        if (name.Contains("comment", StringComparison.OrdinalIgnoreCase))
            return "comments";
        if (name.Contains("sharedStrings", StringComparison.OrdinalIgnoreCase))
            return "shared strings";
        if (name.Contains("footnote", StringComparison.OrdinalIgnoreCase))
            return "footnotes";
        if (name.Contains("endnote", StringComparison.OrdinalIgnoreCase))
            return "endnotes";
        if (name.Contains("core.xml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            return "document properties";
        if (name.Contains("hyperlink", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return "links";
        return "the document";
    }

    private static bool IsText(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".xml") || lower.EndsWith(".rels") || lower.EndsWith(".txt") ||
               lower.EndsWith(".vml") || lower.EndsWith(".psmdcp");
    }

    private static bool IsThumbnail(string name) =>
        name.Contains("thumbnail", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeXml(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        return trimmed.StartsWith("<");
    }

    private static PartKind PartKindOf(string name)
    {
        var file = name.Replace('\\', '/');
        if (file.EndsWith("core.xml", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
            return PartKind.Core;
        if (file.EndsWith("app.xml", StringComparison.OrdinalIgnoreCase))
            return PartKind.App;
        if (file.EndsWith("custom.xml", StringComparison.OrdinalIgnoreCase))
            return PartKind.Custom;
        return PartKind.Other;
    }

    private static byte[] Read(ZipArchiveEntry entry, PackageLimits.Budget budget) =>
        PackageLimits.ReadEntry(entry, budget);

    private enum PartKind
    {
        Other,
        Core,
        App,
        Custom
    }
}
