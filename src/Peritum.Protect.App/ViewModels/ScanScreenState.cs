using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.App.ViewModels;

public enum ScanScreenKind
{
    Idle,
    Scanning,
    Safe,
    Sensitive,
    Success,
    Error
}

public sealed record ScanScreenState(
    ScanScreenKind Kind,
    string Title,
    string Body,
    bool ResultVisible,
    bool IsBusy,
    bool CanMakeCopy,
    bool CanCopyText)
{
    public static ScanScreenState Idle { get; } =
        new(ScanScreenKind.Idle, "", "", false, false, false, false);

    public static ScanScreenState Scanning(string fileName) =>
        new(
            ScanScreenKind.Scanning,
            TextCatalog.Format("checkingFile", ("file", fileName)),
            TextCatalog.Get("scanningLocally"),
            true,
            true,
            false,
            false);

    public static ScanScreenState CopyCreated(string fileName) =>
        new(
            ScanScreenKind.Success,
            TextCatalog.Get("copySaved"),
            fileName,
            true,
            false,
            false,
            false);

    public static ScanScreenState FromSession(ScanSession session)
    {
        var name = session.Result.DisplayName ?? "clipboard";
        if (!string.IsNullOrEmpty(session.Result.Error))
        {
            var body = session.Result.ReasonCode is { Length: > 0 } code
                ? TextCatalog.Get(BatchScan.BodyKey(code))
                : session.Result.Error;
            return new(
                ScanScreenKind.Error,
                name,
                body,
                true,
                false,
                false,
                false);
        }

        var entropyOnly = !session.Result.Truncated &&
            ScanCoverage.OnlyEntropyIncomplete(session.Result.Incomplete, session.Result.FailedRuleIds);
        if ((session.Result.Incomplete || session.Result.Truncated) && !entropyOnly)
        {
            return new(
                ScanScreenKind.Error,
                name,
                TextCatalog.Get("notFullyCheckedSlow"),
                true,
                false,
                false,
                false);
        }

        if (session.Result.HitCount == 0 && session.Result.LinkFindings.Count == 0)
        {
            return new(
                ScanScreenKind.Safe,
                name,
                ResultGroups.FileSummary(session.Result.Findings, session.Result.LinkCount, 0),
                true,
                false,
                session.Request.FilePath is not null,
                false);
        }

        return new(
            ScanScreenKind.Sensitive,
            name,
            ResultGroups.FileSummary(session.Result.Findings, session.Result.LinkCount, session.Result.LinkFindings.Count),
            true,
            false,
            session.Request.FilePath is not null,
            session.Text.Length > 0);
    }
}
