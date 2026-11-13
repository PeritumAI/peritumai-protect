using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Redaction;
using UglyToad.PdfPig;

namespace Peritum.Protect.Core.Tests;

public class MonthlyReportTests
{
    [Fact]
    public void Report_counts_match_the_log_and_omit_values_and_file_names()
    {
        var inMonth = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Scan(inMonth, EventActions.Warn, [("pii.email", 2), ("secret.openai_sk", 1), ("hidden.content", 1)], incomplete: false, file: "invoice.pdf", path: @"C:\secret\ada@example.com"),
            Scan(inMonth, EventActions.Warn, [], incomplete: true, file: "big.zip"),
            Scan(inMonth, EventActions.Allow, [("pii.email", 1)], incomplete: false),
            new ProtectEvent { Ts = inMonth, Action = EventActions.Stop },
            new ProtectEvent { Ts = inMonth, Action = EventActions.Redact, Mode = "permanent" },
            new ProtectEvent { Ts = inMonth, Action = EventActions.CopyMade, Mode = "reversible", FileName = "notes.txt" },
            new ProtectEvent { Ts = inMonth, Action = EventActions.ClickFix },
            new ProtectEvent { Ts = inMonth, Action = EventActions.FalsePositive, Hits = [new HitCount { RuleId = "pii.email", Count = 1 }] },
            Scan(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), EventActions.Warn, [("pii.email", 9)], incomplete: false, file: "august.txt")
        };

        var report = SafetyReport.Build(events, 2026, 9, false, new Dictionary<string, string> { ["pii.email"] = "е-пошта Šećer" });
        Assert.Equal(3, report.Scans);
        Assert.Equal(2, report.Warnings);
        Assert.Equal(1, report.Redactions);
        Assert.Equal(1, report.ReversibleCopies);
        Assert.Equal(1, report.Stops);
        Assert.Equal(1, report.HiddenFindings);
        Assert.Equal(1, report.ClickFixWarnings);
        Assert.Equal(1, report.IncompleteScans);
        var email = report.Rules.Single(rule => rule.RuleId == "pii.email");
        Assert.Equal(3, email.Hits);
        Assert.Equal(1, email.FalsePositives);
        Assert.Equal(1d / 3d, email.FalsePositiveRate, 3);
        Assert.InRange(report.Actions.Count, 3, 5);
        Assert.Contains(report.Actions, action => action.Contains("Emails were among the findings", StringComparison.Ordinal));

        var text = SafetyReport.Text(report, false);
        var csv = SafetyReport.Csv(report, false);
        Assert.Contains("2026-09", text, StringComparison.Ordinal);
        Assert.Contains("not a compliance certificate", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("peritumai.com", text, StringComparison.Ordinal);
        Assert.Contains("scans,3", csv, StringComparison.Ordinal);
        Assert.Contains("warnings,2", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice.pdf", text, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice.pdf", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("august.txt", text, StringComparison.Ordinal);

        var namedReport = SafetyReport.Build(events, 2026, 9, true);
        var named = SafetyReport.Text(namedReport, true);
        Assert.Contains("File: invoice.pdf", named, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", named, StringComparison.Ordinal);
        Assert.Contains("invoice.pdf", SafetyReport.Csv(namedReport, true), StringComparison.Ordinal);

        var pdf = SafetyReport.Pdf(report, false);
        using var document = PdfDocument.Open(pdf);
        var extracted = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.Contains("е-пошта", extracted, StringComparison.Ordinal);
        Assert.Contains("Šećer", extracted, StringComparison.Ordinal);
        Assert.Contains("Scans", extracted, StringComparison.Ordinal);
        Assert.DoesNotContain("invoice.pdf", extracted, StringComparison.Ordinal);
        Assert.DoesNotContain("ada@example.com", extracted, StringComparison.Ordinal);
    }

    private static ProtectEvent Scan(
        DateTimeOffset when,
        string action,
        (string Id, int Count)[] hits,
        bool incomplete,
        string? file = null,
        string? path = null) => new()
    {
        Ts = when,
        Action = action,
        Incomplete = incomplete ? true : null,
        FileName = file,
        FilePath = path,
        Hits = hits.Select(hit => new HitCount { RuleId = hit.Id, Count = hit.Count }).ToList()
    };
}
