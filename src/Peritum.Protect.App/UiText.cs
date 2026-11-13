using Peritum.Protect.Core;

namespace Peritum.Protect.App;

/// <summary>User-visible strings loaded from the language catalog.</summary>
public static class UiText
{
    public static string Tagline => TextCatalog.Get("tagline");
    public static string Positioning => TextCatalog.Get("positioning");
    public static string Privacy => TextCatalog.Get("privacy");
    public static string NothingFound => TextCatalog.Get("nothingFound");
    public static string NotFullyChecked => TextCatalog.Get("notFullyChecked");
    public static string DropTitle => TextCatalog.Get("dropTitle");
    public static string Browse => TextCatalog.Get("browse");
    public static string DropTypes => TextCatalog.Get("dropTypes");
    public static string ClipboardOn => TextCatalog.Get("watchTarget");
    public static string Checking => TextCatalog.Get("checking");
    public static string MakeCopy => TextCatalog.Get("makeCopy");
    public static string MakeReversibleCopy => TextCatalog.Get("makeReversibleCopy");
    public static string ReversibleCopy => TextCatalog.Get("reversibleHint");
    public static string Close => TextCatalog.Get("close");
    public static string LeaveAsIs => TextCatalog.Get("leaveAsIs");
    public static string Redact => TextCatalog.Get("redact");
    public static string RedactedImage => TextCatalog.Get("redactedImage");
    public static string UndoCopy => TextCatalog.Get("undoCopy");
    public static string ClearClipboard => TextCatalog.Get("clearClipboard");
    public static string DontWarn => TextCatalog.Get("dontWarn");
    public static string Details => TextCatalog.Get("details");
    public static string NotSensitive => TextCatalog.Get("notSensitive");
    public static string ShowFinding => TextCatalog.Get("show");
    public static string ShowAllFindings => TextCatalog.Get("showAllFindings");
    public static string CopyLocation => TextCatalog.Get("copyLocation");
    public static string Preview => TextCatalog.Get("preview");
    public static string PreviousFinding => TextCatalog.Get("previous");
    public static string NextFinding => TextCatalog.Get("next");
    public static string PopupTitle => TextCatalog.Get("popupTitle");
    public static string CopySaved => TextCatalog.Get("copySaved");
    public static string ProtectionOn => TextCatalog.Get("protectionOn");
    public static string Paused => TextCatalog.Get("paused");
    public static string WhatIsChecked => TextCatalog.Get("whatIsChecked");
    public static string EmptyActivity => TextCatalog.Get("emptyActivity");
    public static string Start => TextCatalog.Get("start");
    public static string Quit => TextCatalog.Get("quit");
    public static string SetByOrganization => TextCatalog.Get("setByOrganization");
    public static string CommandWarning => TextCatalog.Get("commandWarning");
    public static string CryptoWarning => TextCatalog.Get("cryptoWarning");
    public static string Redaction => TextCatalog.Get("redaction");
    public static string RedactionHint => TextCatalog.Get("redactionHint");
    public static string OrganizationPolicy => TextCatalog.Get("organizationPolicy");
    public static string RulesExchange => TextCatalog.Get("rulesExchange");

    public static IEnumerable<string> All()
    {
        yield return Tagline;
        yield return Positioning;
        yield return Privacy;
        yield return NothingFound;
        yield return NotFullyChecked;
        yield return DropTitle;
        yield return Browse;
        yield return DropTypes;
        yield return ClipboardOn;
        yield return Checking;
        yield return MakeCopy;
        yield return MakeReversibleCopy;
        yield return ReversibleCopy;
        yield return Close;
        yield return LeaveAsIs;
        yield return Redact;
        yield return RedactedImage;
        yield return UndoCopy;
        yield return ClearClipboard;
        yield return DontWarn;
        yield return Details;
        yield return NotSensitive;
        yield return ShowFinding;
        yield return ShowAllFindings;
        yield return CopyLocation;
        yield return Preview;
        yield return PreviousFinding;
        yield return NextFinding;
        yield return PopupTitle;
        yield return CopySaved;
        yield return ProtectionOn;
        yield return Paused;
        yield return WhatIsChecked;
        yield return EmptyActivity;
        yield return Start;
        yield return Quit;
        yield return SetByOrganization;
        yield return CommandWarning;
        yield return CryptoWarning;
        yield return Redaction;
        yield return RedactionHint;
        yield return OrganizationPolicy;
        yield return RulesExchange;
    }
}
