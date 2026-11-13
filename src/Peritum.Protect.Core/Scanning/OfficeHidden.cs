using System.Xml;

namespace Peritum.Protect.Core.Scanning;

public static class OfficeHidden
{
    public static IReadOnlyList<HiddenHit> ScanXml(string xml)
    {
        var hits = new List<HiddenHit>();
        if (string.IsNullOrWhiteSpace(xml))
            return hits;
        if (xml.Contains("w:vanish", StringComparison.OrdinalIgnoreCase) || xml.Contains("specVanish", StringComparison.OrdinalIgnoreCase))
            hits.Add(new HiddenHit { Reason = "Hidden text", Text = "A run is marked hidden." });
        if (xml.Contains("hidden=\"1\"", StringComparison.OrdinalIgnoreCase) || xml.Contains("state=\"hidden\"", StringComparison.OrdinalIgnoreCase))
            hits.Add(new HiddenHit { Reason = "Hidden sheet or row", Text = "A sheet, row, or slide is hidden." });
        if (xml.Contains("display:none", StringComparison.OrdinalIgnoreCase) || xml.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase))
            hits.Add(new HiddenHit { Reason = "Hidden HTML", Text = "Styled text is not visible." });
        if (xml.Contains("<!--", StringComparison.Ordinal))
            hits.Add(new HiddenHit { Reason = "HTML comment", Text = Inner(xml, "<!--", "-->") });
        try
        {
            var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "t" or "instrText" && reader.GetAttribute("val") == "000000")
                    hits.Add(new HiddenHit { Reason = "White text", Text = "Text color is white." });
            }
        }
        catch (XmlException)
        {
            // Not XML. The string checks above still apply.
        }
        return hits;
    }

    private static string Inner(string text, string start, string end)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        if (a < 0)
            return "";
        a += start.Length;
        var b = text.IndexOf(end, a, StringComparison.Ordinal);
        if (b < 0)
            return text[a..];
        return text[a..b].Trim();
    }
}
