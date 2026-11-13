using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap7bConfidenceTests
{
    [Fact]
    public void Random_numbers_without_a_label_stay_under_half_a_percent()
    {
        var rules = CountryRules();
        var random = new Random(7);
        foreach (var length in new[] { 9, 10, 11 })
        {
            var counts = rules.ToDictionary(rule => rule.Id, _ => 0);
            for (var n = 0; n < 2000; n++)
            {
                var digits = string.Create(length, random, static (span, rng) =>
                {
                    for (var i = 0; i < span.Length; i++)
                        span[i] = (char)('0' + rng.Next(10));
                });
                var text = "Invoice " + digits + " paid.";
                foreach (var finding in new RuleScanner().Find(text, rules))
                    counts[finding.RuleId]++;
            }

            foreach (var rule in rules)
            {
                var rate = counts[rule.Id] / 2000d;
                // A valid PESEL is a date plus a check digit, so a random 11-digit
                // number still passes about 1 to 2 percent of the time. That check
                // stays on without a label. Every other rule must stay under 0.5 percent.
                var cap = rule.Id == "pl.pesel" ? 0.025 : 0.005;
                Assert.True(rate < cap, rule.Id + " length " + length + " was " + counts[rule.Id] + "/2000");
            }
        }
    }

    [Theory]
    [InlineData("us.ssn", "SSN 211111111")]
    [InlineData("us.ssn", "Social Security 211111111")]
    [InlineData("us.itin", "ITIN 900700001")]
    [InlineData("rs.pib", "PIB 301423672")]
    [InlineData("rs.pib", "poreski identifikacioni broj 100000016")]
    [InlineData("nl.bsn", "BSN 111222333")]
    [InlineData("nl.bsn", "burgerservicenummer 123456782")]
    [InlineData("hr.oib", "OIB 75143090474")]
    [InlineData("gb.nhs", "NHS 9434765919")]
    [InlineData("de.steuerid", "Steuer-ID 12345678911")]
    [InlineData("de.steuerid", "Steueridentifikationsnummer 12345678911")]
    [InlineData("de.steuerid", "IdNr 12345678911")]
    [InlineData("br.cpf", "CPF 52998224725")]
    [InlineData("br.cnpj", "CNPJ 11222333000181")]
    [InlineData("gb.nino", "NI number AB123456C")]
    [InlineData("gb.nino", "National Insurance AB123456C")]
    [InlineData("in.aadhaar", "Aadhaar 234123412346")]
    public void A_nearby_label_finds_an_unformatted_number(string ruleId, string text)
    {
        Assert.Equal(ruleId, Hit(text, ruleId));
    }

    [Theory]
    [InlineData("us.ssn", "211-11-1111")]
    [InlineData("us.itin", "900-70-0001")]
    [InlineData("br.cpf", "529.982.247-25")]
    [InlineData("br.cnpj", "11.222.333/0001-81")]
    [InlineData("in.aadhaar", "2341 2341 2346")]
    [InlineData("gb.nhs", "943 476 5919")]
    [InlineData("gb.nhs", "943-476-5919")]
    [InlineData("de.steuerid", "12 345 678 911")]
    [InlineData("nl.bsn", "1112.22.333")]
    [InlineData("gb.nino", "AB 12 34 56 C")]
    [InlineData("pl.pesel", "44051401359")]
    [InlineData("es.dni", "12345678Z")]
    [InlineData("es.nie", "Y1234567X")]
    [InlineData("it.codice", "RSSMRA85T10A562S")]
    public void Printed_shape_or_a_strong_check_needs_no_label(string ruleId, string text)
    {
        Assert.Equal(ruleId, Hit(text, ruleId));
    }

    [Theory]
    [InlineData("Order 100234567 shipped")]
    [InlineData("Account 123456789")]
    [InlineData("Invoice 211111111 paid.")]
    public void Ordinary_numbers_are_not_flagged(string text)
    {
        var findings = new RuleScanner().Find(text, CountryRules());
        Assert.Empty(findings);
    }

    [Fact]
    public void A_label_on_another_line_or_too_far_away_does_not_count()
    {
        Assert.Null(Hit("SSN\n211111111", "us.ssn"));
        Assert.Null(Hit("SSN " + new string('x', 45) + "211111111", "us.ssn"));
    }

    [Fact]
    public void A_spreadsheet_column_header_is_a_label()
    {
        var rules = new[] { Rule("us.ssn") };
        var csv = "Name,SSN\nAda,211111111\n";
        var segment = new TextSegment { Start = 0, Text = csv, Place = SegmentPlace.Csv() };
        var findings = new RuleScanner().FindSegments(csv, [segment], rules);
        Assert.Contains(findings, finding => finding.RuleId == "us.ssn");

        var sheet = Path.Combine(Path.GetTempPath(), "peritum-ssn-" + Guid.NewGuid().ToString("n") + ".xlsx");
        try
        {
            WriteSheet(sheet, "Social Security", "211111111");
            var scanned = new RuleScanner().Scan(ScanRequest.ForFile(sheet), rules);
            Assert.Contains(scanned.Findings, finding => finding.RuleId == "us.ssn");
        }
        finally
        {
            if (File.Exists(sheet))
                File.Delete(sheet);
        }
    }

    [Fact]
    public void Packs_keep_the_local_label_words()
    {
        var root = RepoRoot();
        AssertWords(root, "us.json", "Social Security");
        AssertWords(root, "rs.json", "poreski identifikacioni broj");
        AssertWords(root, "nl.json", "burgerservicenummer");
        AssertWords(root, "de.json", "Steueridentifikationsnummer", "IdNr");
        AssertWords(root, "gb.json", "NI number");
        AssertWords(root, "br.json", "CPF", "CNPJ");
    }

    private static void AssertWords(string root, string file, params string[] words)
    {
        var json = File.ReadAllText(Path.Combine(root, "rules", "country", file));
        Assert.Contains("\"keywords\"", json, StringComparison.Ordinal);
        foreach (var word in words)
            Assert.Contains("\"" + word + "\"", json, StringComparison.Ordinal);
    }

    private static string? Hit(string text, string ruleId)
    {
        var findings = new RuleScanner().Find(text, [Rule(ruleId)]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }

    private static Rule Rule(string ruleId) =>
        BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == ruleId);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PeritumProtect.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root");
    }

    private static void WriteSheet(string path, string header, string value)
    {
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
        var sheets = workbook.Workbook.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheets());
        var part = workbook.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
        var data = new DocumentFormat.OpenXml.Spreadsheet.SheetData();
        data.Append(new DocumentFormat.OpenXml.Spreadsheet.Row(Cell("A1", header)) { RowIndex = 1 });
        data.Append(new DocumentFormat.OpenXml.Spreadsheet.Row(Cell("A2", value)) { RowIndex = 2 });
        part.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(data);
        part.Worksheet.Save();
        sheets.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = 1,
            Name = "Sheet1"
        });
        workbook.Workbook.Save();
    }

    private static DocumentFormat.OpenXml.Spreadsheet.Cell Cell(string reference, string text) =>
        new()
        {
            CellReference = reference,
            DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.String,
            CellValue = new DocumentFormat.OpenXml.Spreadsheet.CellValue(text)
        };

    private static List<Rule> CountryRules() =>
        BuiltinRules.LoadEmbedded()
            .Where(pack => pack.Id.StartsWith("country.", StringComparison.Ordinal))
            .SelectMany(pack => pack.Rules)
            .ToList();
}
