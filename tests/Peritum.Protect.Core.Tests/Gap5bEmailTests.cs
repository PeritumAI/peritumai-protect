using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace Peritum.Protect.Core.Tests;

public class Gap5bEmailTests
{
    private const string Address = "ada@example.com";

    private static IReadOnlyList<Rule> Active =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Theory]
    [InlineData("Contact ada@example.com.", "ada@example.com")]
    [InlineData("Mail: ada@example.com. Thanks", "ada@example.com")]
    [InlineData("Pisite na ada@firma.rs.", "ada@firma.rs")]
    [InlineData("Send to ada.lovelace+tag@mail.example.co.uk.", "ada.lovelace+tag@mail.example.co.uk")]
    [InlineData("Value: ada@example.com.\nNext", "ada@example.com")]
    public void An_address_at_the_end_of_a_sentence_is_found(string text, string address)
    {
        var findings = new RuleScanner().Find(text, Active);
        var hit = Assert.Single(findings, finding => finding.RuleId == "pii.email");
        Assert.Equal(address, text.Substring(hit.Start, hit.Length));
    }

    [Fact]
    public void Both_copies_are_found_when_only_the_second_ends_with_a_stop()
    {
        const string text = "ada@example.com then ada@example.com.";
        var findings = new RuleScanner().Find(text, Active).Where(finding => finding.RuleId == "pii.email").ToList();
        Assert.Equal(2, findings.Count);
        Assert.All(findings, hit => Assert.Equal(Address, text.Substring(hit.Start, hit.Length)));
    }

    [Theory]
    [InlineData("ada@example.com.")]
    [InlineData("ada@example.com-")]
    [InlineData("ada@example.com...")]
    [InlineData("See ada@example.com.- next")]
    public void A_trailing_dot_or_hyphen_is_not_part_of_the_address(string text)
    {
        var hit = Assert.Single(new RuleScanner().Find(text, Active), finding => finding.RuleId == "pii.email");
        Assert.Equal(Address, text.Substring(hit.Start, hit.Length));
    }

    [Theory]
    [InlineData("ada@bad..example.com")]
    [InlineData("ada@-example.com")]
    [InlineData("ada@example-.com")]
    [InlineData("ada@.example.com")]
    [InlineData("ada@example.c")]
    public void A_broken_domain_is_not_an_address(string text)
    {
        Assert.DoesNotContain(new RuleScanner().Find(text, Active), finding => finding.RuleId == "pii.email");
    }

    [Fact]
    public void A_hyphen_inside_a_label_is_still_an_address()
    {
        const string text = "Write ada@my-host.co.uk today";
        var hit = Assert.Single(new RuleScanner().Find(text, Active), finding => finding.RuleId == "pii.email");
        Assert.Equal("ada@my-host.co.uk", text.Substring(hit.Start, hit.Length));
    }

    [Fact]
    public void Punctuation_around_every_builtin_rule_still_covers_the_value()
    {
        var rules = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).ToList();
        var samples = Samples();
        Assert.Equal(rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            samples.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());

        var frames = new[]
        {
            "X {v} Y",
            "Send to {v}.",
            "Send to {v}, thanks",
            "({v})",
            "\"{v}\"",
            "'{v}'",
            "[{v}]",
            "<{v}>",
            "{v};",
            "{v}!",
            "{v}?",
            "{v}:",
            "{v}…",
            "Value: {v}.\nNext",
            "{v}",
            "{v}。",
            "«{v}»"
        };

        var scanner = new RuleScanner();
        foreach (var rule in rules)
        {
            var (cue, value) = samples[rule.Id];
            foreach (var frame in frames)
            {
                var text = frame.Replace("{v}", cue + value, StringComparison.Ordinal);
                var findings = scanner.Find(text, [rule]);
                var at = text.IndexOf(value, StringComparison.Ordinal);
                Assert.True(at >= 0, rule.Id);
                var covered = findings.Any(finding => finding.Start <= at && finding.Start + finding.Length >= at + value.Length);
                Assert.True(covered, rule.Id + " in " + frame.Replace("\n", "\\n", StringComparison.Ordinal));
                if (rule.Id == "pii.email")
                {
                    var hit = findings.Single(finding => finding.Start <= at && finding.Start + finding.Length >= at + value.Length);
                    Assert.Equal(value, text.Substring(hit.Start, hit.Length));
                }
            }
        }
    }

    [Theory]
    [InlineData("Contact ada@example.com.", 1)]
    [InlineData("ada@example.com then ada@example.com.", 2)]
    public void Sentence_ending_email_is_absent_from_copies_bundle_and_clipboard(string text, int tokens)
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-gap5b-email-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rules = Active;
            var service = new ProtectService();
            var maps = Path.Combine(root, "maps");
            foreach (var (name, write) in Writers(text))
            {
                var source = Path.Combine(root, name);
                write(source);
                var before = Hashing.Sha256File(source);

                var protect = service.MakeProtectedCopy(source, rules, "{name}_protect{ext}");
                Assert.True(string.IsNullOrEmpty(protect.Error), name + " " + protect.Error);
                Assert.Equal(before, Hashing.Sha256File(source));
                Assert.False(Contains(protect.OutputPath!, Address), name + " protected");

                var reversible = service.MakeProtectedCopy(source, rules, "{name}_reversible{ext}", reversible: true, mapsDirectory: maps);
                Assert.True(string.IsNullOrEmpty(reversible.Error), name + " " + reversible.Error);
                Assert.Equal(before, Hashing.Sha256File(source));
                Assert.False(Contains(reversible.OutputPath!, Address), name + " reversible");

                var exported = AiBundle.ExportFile(source, rules, _ => false, new MapStore(maps));
                Assert.True(exported.Succeeded, name + " " + exported.Message);
                var bundle = File.ReadAllText(exported.OutputPath!);
                Assert.DoesNotContain(Address, bundle, StringComparison.Ordinal);
                Assert.Equal(tokens, Count(bundle, "[[EMAIL_1]]"));
                Assert.DoesNotContain("[[EMAIL_2]]", bundle, StringComparison.Ordinal);
            }

            var session = RichClipboard.Inspect(new ClipboardScanInput { Text = text }, rules, ocr: null);
            Assert.Null(session.Result.Error);
            var permanent = RichClipboard.Redact(session, session.Result.Findings, reversible: false);
            var reversibleClip = RichClipboard.Redact(session, session.Result.Findings, reversible: true);
            Assert.Null(permanent.Error);
            Assert.Null(reversibleClip.Error);
            Assert.DoesNotContain(Address, permanent.PlainText, StringComparison.Ordinal);
            Assert.DoesNotContain(Address, reversibleClip.PlainText, StringComparison.Ordinal);
            Assert.Equal(tokens, Count(reversibleClip.PlainText, "[[EMAIL_1]]"));

            var (redacted, map) = ReversibleRedactor.Apply(text, session.Result.Findings, reversible: true);
            var clipBundle = AiBundle.Export(redacted, map.Id);
            Assert.DoesNotContain(Address, clipBundle, StringComparison.Ordinal);
            Assert.Equal(tokens, Count(clipBundle, "[[EMAIL_1]]"));
            Assert.DoesNotContain("[[EMAIL_2]]", clipBundle, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Dictionary<string, (string Cue, string Value)> Samples()
    {
        const string pem = """
            -----BEGIN RSA PRIVATE KEY-----
            MIIEowIBAAKCAQEA7example
            -----END RSA PRIVATE KEY-----
            """;
        return new Dictionary<string, (string Cue, string Value)>(StringComparer.Ordinal)
        {
            ["pii.email"] = ("", Address),
            ["pii.phone"] = ("", "+381 64 123 4567"),
            ["pii.jmbg"] = ("", Jmbg("010199071000")),
            ["rs.pib"] = ("PIB ", "100000016"),
            ["rs.phone"] = ("", "+381640000001"),
            ["hr.oib"] = ("OIB ", "10000000000"),
            ["si.emso"] = ("", Jmbg("010199050000")),
            ["ba.jmbg"] = ("", Jmbg("020200017500")),
            ["me.jmbg"] = ("", Jmbg("150698021001")),
            ["mk.embg"] = ("", Jmbg("010199041000")),
            ["de.steuerid"] = ("Steuer-ID ", "12345678911"),
            ["de.phone"] = ("", "+4915100000000"),
            ["fr.nir"] = ("", "190017505600195"),
            ["es.dni"] = ("", "12345678Z"),
            ["es.nie"] = ("", "Y1234567X"),
            ["it.codice"] = ("", "RSSMRA85T10A562S"),
            ["nl.bsn"] = ("BSN ", "123456782"),
            ["pl.pesel"] = ("", "14051401352"),
            ["gb.nino"] = ("NI number ", "AB123456C"),
            ["gb.nhs"] = ("NHS ", "1000000028"),
            ["us.ssn"] = ("", "211-11-1111"),
            ["us.itin"] = ("", "900-70-0001"),
            ["br.cpf"] = ("CPF ", NationalId.WithCpf("100000001")),
            ["br.cnpj"] = ("CNPJ ", NationalId.WithCnpj("100000000001")),
            ["in.aadhaar"] = ("Aadhaar ", NationalId.WithAadhaar("23456789012")),
            ["in.pan"] = ("", "ABCPE1234F"),
            ["pii.ipv4"] = ("", "8.8.8.8"),
            ["pii.dob"] = ("", "date of birth: 04/05/1990"),
            ["pii.health_id"] = ("", "MRN: AB123456"),
            ["pii.matter"] = ("", "MAT-2024-00123"),
            ["secret.aws_access_key"] = ("", "AKIAIOSFODNN7EXAMPLE"),
            ["secret.github_pat"] = ("", "ghp_1234567890abcdefghij"),
            ["secret.stripe"] = ("", "sk_live_FAKE1234567890"),
            ["secret.slack"] = ("", "xoxb-123456789012345678901234"),
            ["secret.openai_sk"] = ("", "sk-testFAKEkey1234567890"),
            ["secret.jwt"] = ("", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U"),
            ["secret.pem"] = ("", pem),
            ["secret.entropy"] = ("", "q7W9mK2pL8xN4vB6cZ1rT5yH0sD3fG7aQ2"),
            ["secret.google_api"] = ("", "AIzaSyA1234567890abcdeHIJKLMN1234567890"),
            ["secret.aws_temp"] = ("", "ASIAIOSFODNN7EXAMPLE"),
            ["secret.aws_secret"] = ("", "aws_secret_access_key=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"),
            ["secret.gitlab"] = ("", "glpat-abcdefghijklmnopqrst"),
            ["secret.huggingface"] = ("", "hf_abcdefghijklmnopqrstuvwxyz123456"),
            ["secret.sendgrid"] = ("", "SG.abcdefghijklmnopqrstuv.abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ"),
            ["secret.npm"] = ("", "npm_1234567890abcdefghijklmnopqrstuvwxyz"),
            ["secret.anthropic"] = ("", "sk-ant-api03-abcdefghijklmnopqrstuvwxyz"),
            ["secret.twilio"] = ("", "SK0123456789abcdef0123456789abcdef"),
            ["secret.azure"] = ("", "AccountKey=abcdEFGHijklMNOP1234567890+/=="),
            ["secret.connection"] = ("", "postgres://user:s3cret@"),
            ["secret.password_assignment"] = ("DB_PASSWORD=", "hunter2hunter2"),
            ["secret.discord"] = ("", "MTxxxxxxxxxxxxxxxxxxxxxx.xxxxxx.xxxxxxxxxxxxxxxxxxxxxxxxxxx"),
            ["fin.card"] = ("", "4111111111111111"),
            ["fin.iban"] = ("", "GB82WEST12345698765432"),
            ["fin.salary"] = ("", "salary: $85,000"),
            ["court.us.federal"] = ("", "1:05-cv-06794"),
            ["court.eu.cjeu"] = ("", "C-250/25"),
            ["ref.labels"] = ("Claim no. ", "KB-2024-000123"),
            ["ref.finance"] = ("Invoice no. ", "2024-0142")
        };
    }

    private static string Jmbg(string first12)
    {
        for (var digit = 0; digit <= 9; digit++)
        {
            var candidate = first12 + digit;
            var sum = 0;
            for (var i = 0; i < 12; i++)
                sum += (candidate[i] - '0') * (7 - (i % 6));
            var remainder = sum % 11;
            if (remainder == 1)
                continue;
            var check = remainder == 0 ? 0 : 11 - remainder;
            if (check == digit)
                return candidate;
        }

        throw new InvalidOperationException("no checksum");
    }

    private static IEnumerable<(string Name, Action<string> Write)> Writers(string text)
    {
        yield return ("notes.txt", path => File.WriteAllText(path, text));
        yield return ("notes.docx", path => WriteDocx(path, text));
        yield return ("notes.xlsx", path => WriteXlsx(path, text));
        yield return ("notes.eml", path => File.WriteAllText(path, Eml(text)));
    }

    private static string Eml(string body)
    {
        var text = "From: Desk\r\nTo: Ada\r\nSubject: Note\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n" +
                   body.Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n";
        return text;
    }

    private static void WriteDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
        main.Document.Save();
    }

    private static void WriteXlsx(string path, string text)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new X.Workbook();
        var sheets = workbook.Workbook.AppendChild(new X.Sheets());
        var sheetPart = workbook.AddNewPart<WorksheetPart>();
        var row = new X.Row { RowIndex = 1 };
        row.Append(new X.Cell
        {
            CellReference = "A1",
            DataType = X.CellValues.InlineString,
            InlineString = new X.InlineString(new X.Text(text))
        });
        sheetPart.Worksheet = new X.Worksheet(new X.SheetData(row));
        sheets.Append(new X.Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "Clients" });
        workbook.Workbook.Save();
    }

    private static bool Contains(string path, string value)
    {
        var hay = File.ReadAllBytes(path);
        var needle = Encoding.ASCII.GetBytes(value);
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (hay[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
