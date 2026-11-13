using Peritum.Protect.Core;

namespace Peritum.Protect.Core.Files;

/// <summary>Turns a stable removal token into the sentence shown in the result.</summary>
public static class ReasonText
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["Hidden sheet"] = "clean.hiddenSheet",
        ["Hidden slide"] = "clean.hiddenSlide",
        ["Hidden rows"] = "clean.hiddenRows",
        ["Hidden columns"] = "clean.hiddenColumns",
        ["Hidden text"] = "clean.hiddenText",
        ["Comments"] = "clean.comments",
        ["Macros"] = "clean.macros",
        ["Thumbnail"] = "clean.thumbnail",
        ["Embedded objects"] = "clean.embedded",
        ["Speaker notes"] = "clean.speakerNotes",
        ["External links"] = "clean.externalLinks",
        ["Data connections"] = "clean.dataConnections",
        ["Tracked changes (accepted)"] = "clean.trackedAccepted",
        ["Tracked changes (rejected)"] = "clean.trackedRejected",
        ["Document template"] = "clean.template",
        ["Document properties"] = "clean.documentProperties",
        ["Application properties"] = "clean.applicationProperties",
        ["Custom properties"] = "clean.customProperties",
        ["Off-slide shapes"] = "clean.offSlide",
        ["Custom XML"] = "clean.customXml",
        ["PDF document info"] = "clean.pdfInfo",
        ["Image metadata"] = "clean.imageMetadata",
        ["GPS location"] = "clean.gps",
        ["HTML comments"] = "clean.htmlComments",
        ["Hidden HTML"] = "clean.hiddenHtml",
        ["HTML metadata"] = "clean.htmlMetadata",
        ["Nothing to remove"] = "clean.nothing"
    };

    public static string Item(string token)
    {
        if (Keys.TryGetValue(token, out var key))
            return TextCatalog.Get(key);
        if (token.StartsWith("clean.", StringComparison.Ordinal))
            return TextCatalog.Get(token);
        return token;
    }

    public static string RemovedLine(string token) =>
        TextCatalog.Format("removedLead", ("item", Item(token)));
}
