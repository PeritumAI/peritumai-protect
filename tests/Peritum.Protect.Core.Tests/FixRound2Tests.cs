using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Clipboard;
using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Events;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace Peritum.Protect.Core.Tests;

public class FixRound2Tests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Theory]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    [InlineData(".pptx")]
    public void Structural_words_are_removed_from_text_and_the_copy_validates(string extension)
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-struct-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "sample" + extension);
            WriteOffice(original, "Grid Table Style Font Body");
            var rules = Terms("Grid", "Table", "Style", "Font", "Body");
            var copy = new ProtectService().MakeProtectedCopy(original, rules, "{name}_protect{ext}");
            Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
            Assert.True(File.Exists(copy.OutputPath));
            var package = File.ReadAllBytes(copy.OutputPath!);
            Assert.Null(OfficePackageValidator.Check(package, extension, File.ReadAllBytes(original)));
            var xml = ReadZipText(package);
            if (extension == ".docx")
                Assert.Contains("tblGrid", xml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Grid<", xml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Table<", xml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Style<", xml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Font<", xml, StringComparison.Ordinal);
            Assert.DoesNotContain(">Body<", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Namespaced_core_properties_and_thumbnail_are_cleared()
    {
        var package = ZipOf(
            ("docProps/core.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:creator>Ada Lovelace</dc:creator>
                  <cp:lastModifiedBy>Ada Lovelace</cp:lastModifiedBy>
                  <dc:title>Secret Title</dc:title>
                  <dc:subject>Secret Subject</dc:subject>
                  <cp:keywords>Secret Keywords</cp:keywords>
                  <dc:description>Secret Description</dc:description>
                  <cp:category>Secret Category</cp:category>
                </cp:coreProperties>
                """),
            ("docProps/app.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties">
                  <Company>SecretCo</Company>
                  <Manager>SecretManager</Manager>
                </Properties>
                """),
            ("docProps/custom.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/custom-properties">
                  <property name="Client"><vt:lpwstr xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">Hidden Name</vt:lpwstr></property>
                </Properties>
                """),
            ("docProps/thumbnail.jpeg", "not-a-real-image"),
            ("package/services/metadata/core-properties/core.psmdcp", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:creator>Ada Lovelace</dc:creator>
                </cp:coreProperties>
                """));
        var cleaned = OfficePackageSanitizer.Apply(package, []);
        var text = ReadZipText(cleaned);
        Assert.DoesNotContain("Ada Lovelace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret Title", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretCo", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden Name", text, StringComparison.Ordinal);
        using var zip = new ZipArchive(new MemoryStream(cleaned), ZipArchiveMode.Read);
        Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("thumbnail", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Package_properties_round_trip_clears_the_author()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-meta-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "meta.docx");
            WriteOffice(original, "Hello");
            using (var doc = WordprocessingDocument.Open(original, true))
            {
                doc.PackageProperties.Creator = "Ada Lovelace";
                doc.PackageProperties.LastModifiedBy = "Ada Lovelace";
                doc.PackageProperties.Title = "Secret Title";
            }
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
            var xml = ReadZipText(File.ReadAllBytes(copy.OutputPath!));
            Assert.DoesNotContain("Ada Lovelace", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("Secret Title", xml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("DB_PASSWORD=hunter2hunter2", "hunter2hunter2")]
    [InlineData("API_KEY=abcdEFGH1234", "abcdEFGH1234")]
    [InlineData("SECRET_TOKEN: purple-rabbit", "purple-rabbit")]
    [InlineData("Server=db;Password=hunter2hunter2;User=sa", "hunter2hunter2")]
    [InlineData("Pwd=hunter2hunter2;", "hunter2hunter2")]
    public void Env_and_ado_secrets_redact_only_the_value(string text, string value)
    {
        var findings = new RuleScanner().Find(text, Rules);
        var hit = Assert.Single(findings, finding => finding.RuleId == "secret.password_assignment");
        Assert.Equal(value, text.Substring(hit.Start, hit.Length));
    }

    [Theory]
    [InlineData("AIzaSyA1234567890abcdeHIJKLMN1234567890", "secret.google_api")]
    [InlineData("ASIAIOSFODNN7EXAMPLE", "secret.aws_temp")]
    [InlineData("aws_secret_access_key = wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "secret.aws_secret")]
    [InlineData("glpat-abcdefghijklmnopqrst", "secret.gitlab")]
    [InlineData("hf_abcdefghijklmnopqrstuvwxyz123456", "secret.huggingface")]
    [InlineData("SG.abcdefghijklmnopqrstuv.abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ", "secret.sendgrid")]
    [InlineData("npm_1234567890abcdefghijklmnopqrstuvwxyz", "secret.npm")]
    [InlineData("sk-ant-api03-abcdefghijklmnopqrstuvwxyz", "secret.anthropic")]
    [InlineData("SK0123456789abcdef0123456789abcdef", "secret.twilio")]
    [InlineData("AccountKey=abcdEFGHijklMNOP1234567890+/==", "secret.azure")]
    [InlineData("SharedAccessSignature=sv=2024&sig=abcd", "secret.azure")]
    [InlineData("postgres://user:pass@localhost/db", "secret.connection")]
    [InlineData("MTxxxxxxxxxxxxxxxxxxxxxx.xxxxxx.xxxxxxxxxxxxxxxxxxxxxxxxxxx", "secret.discord")]
    public void Each_new_secret_pattern_hits(string text, string ruleId)
    {
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, finding => finding.RuleId == ruleId);
    }

    [Fact]
    public void Pem_markers_for_dsa_encrypted_and_pgp_hit()
    {
        foreach (var marker in new[] { "BEGIN DSA PRIVATE KEY", "BEGIN ENCRYPTED PRIVATE KEY", "BEGIN PGP PRIVATE KEY BLOCK" })
        {
            var text = "-----" + marker + "-----\nabc\n-----" + marker.Replace("BEGIN", "END") + "-----";
            var findings = new RuleScanner().Find(text, Rules);
            Assert.Contains(findings, finding => finding.RuleId == "secret.pem");
        }
    }

    [Fact]
    public void Jmbg_accepts_a_valid_checksum_and_rejects_noise()
    {
        var valid = ValidJmbg("010199071000");
        var findings = new RuleScanner().Find("ID " + valid + " end", Rules);
        Assert.Contains(findings, finding => finding.RuleId == "pii.jmbg" && finding.Snippet == valid);
        Assert.DoesNotContain(new RuleScanner().Find("0101990710000", Rules), finding => finding.RuleId == "pii.jmbg");
        Assert.DoesNotContain(new RuleScanner().Find("3201990710006", Rules), finding => finding.RuleId == "pii.jmbg");
    }

    [Fact]
    public void Keyword_lookaround_matches_symbols_and_regex_is_stable()
    {
        var rules = new List<Rule>
        {
            Term("C++"),
            Term("#matter")
        };
        var scanner = new RuleScanner();
        var first = scanner.Find("Use C++ and #matter today.", rules);
        var second = scanner.Find("Use C++ and #matter today.", rules);
        Assert.Equal(2, first.Count);
        Assert.Equal(first.Select(f => f.Start), second.Select(f => f.Start));
        Assert.Empty(scanner.Find("Cplusplus", rules));
    }

    [Fact]
    public void Data_uri_and_pure_hex_are_not_entropy_hits()
    {
        var payload = Convert.ToBase64String(Enumerable.Range(0, 80).Select(i => (byte)(i * 13)).ToArray());
        var text = "data:image/png;base64," + payload + " " + new string('a', 40) + " " + new string('b', 40);
        var findings = new RuleScanner().Find(text, Rules);
        Assert.DoesNotContain(findings, finding => finding.RuleId == "secret.entropy" && finding.Snippet.Contains("data:", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, finding => finding.Snippet == new string('a', 40));
    }

    [Fact]
    public void Entropy_skip_on_a_large_blob_still_allows_a_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-entropy-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var blob = Convert.ToBase64String(Enumerable.Repeat((byte)7, 800_000).ToArray());
            var original = Path.Combine(dir, "blob.txt");
            File.WriteAllText(original, blob + "\nada@example.com\nbob@example.com\n");
            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");
            Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
            Assert.Contains(ScanCoverage.EntropySkipNote, copy.Note, StringComparison.Ordinal);
            var output = File.ReadAllText(copy.OutputPath!);
            Assert.DoesNotContain("ada@example.com", output, StringComparison.Ordinal);
            Assert.Contains("[EMAIL]", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Pdf_round_trips_serbian_cyrillic_and_cjk()
    {
        var text = "Šećer i đak\nСрбија\n汉字";
        var pdf = SimplePdfWriter.FromText(text, out var note);
        Assert.Null(note);
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var extracted = string.Join("\n", document.GetPages().Select(page => page.Text));
        Assert.Contains("Šećer", extracted, StringComparison.Ordinal);
        Assert.Contains("Србија", extracted, StringComparison.Ordinal);
        Assert.Contains("汉字", extracted, StringComparison.Ordinal);
    }

    [Fact]
    public void Allow_list_file_has_no_raw_value()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-allow-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "allowlist.json");
            var store = new AllowListStore(path, Enumerable.Repeat((byte)3, 32).ToArray());
            store.Add("sk-testFAKEkey1234567890");
            var raw = File.ReadAllText(path);
            Assert.DoesNotContain("sk-test", raw, StringComparison.Ordinal);
            Assert.Contains("hints", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Rotated_logs_keep_six_files_and_stats_span_them()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-rotate-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var log = new EventLog(Path.Combine(dir, "events.jsonl"), new byte[32]);
            for (var i = 0; i < 8; i++)
            {
                var month = Path.Combine(dir, "events-2026-0" + (i + 1) + ".jsonl");
                File.WriteAllText(month, "{\"ts\":\"2026-0" + (i + 1) + "-01T00:00:00Z\",\"source\":\"manual\",\"action\":\"warn\",\"hits\":[{\"ruleId\":\"pii.email\",\"count\":2}],\"durationMs\":12,\"incomplete\":true}\n");
            }
            log.Append(new ProtectEvent
            {
                Ts = DateTimeOffset.UtcNow,
                Source = "manual",
                Action = EventActions.CopyMade,
                Hits = []
            });
            log.Append(log.FalsePositive("pii.email"));
            Assert.True(Directory.GetFiles(dir, "events*.jsonl").Length <= EventLog.FilesToKeep + 1);
            var stats = ActivityStats.Summarize(log.ReadAll());
            Assert.True(stats.Scans >= 1);
            Assert.True(stats.CopiesMade >= 1);
            Assert.Contains(stats.Rules, rule => rule.RuleId == "pii.email" && rule.FalsePositives >= 1);
            Assert.Contains("incomplete_scans", stats.ToCsv(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Error_log_records_the_exception_and_not_an_extra_body()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-err-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "errors.log");
            ErrorLog.Write(new InvalidOperationException("disk full"), "1.2.3", path);
            var text = File.ReadAllText(path);
            Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
            Assert.Contains("disk full", text, StringComparison.Ordinal);
            Assert.Contains("1.2.3", text, StringComparison.Ordinal);
            Assert.DoesNotContain("clipboard", text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Clipboard_gate_warns_on_foreground_and_not_twice()
    {
        var gate = new ClipboardAlertGate();
        var session = new ProtectService().Scan(
            ScanRequest.ForText("ada@example.com", ScanSource.Clipboard),
            Rules);
        gate.Store(4, session);
        Assert.False(gate.ShouldWarn(4, targetInFront: false, minHits: 1));
        Assert.True(gate.ShouldWarn(4, targetInFront: true, minHits: 1));
        gate.MarkShown(4);
        Assert.False(gate.ShouldWarn(4, targetInFront: true, minHits: 1));
        gate.MarkDecided(4);
        Assert.False(gate.ShouldWarn(4, targetInFront: true, minHits: 1));
    }

    [Fact]
    public void Saved_settings_gain_the_new_target_processes_once()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-schema-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, """{"schemaVersion":2,"targetProcesses":["chrome.exe"]}""");
            var loaded = new SettingsStore(path).Load();
            Assert.Equal(3, loaded.SchemaVersion);
            Assert.Contains("LM Studio.exe", loaded.TargetProcesses);
            Assert.Contains("ollama app.exe", loaded.TargetProcesses);
            Assert.True(TargetProcessList.Matches("LM Studio", TargetProcessList.Default));
            Assert.True(TargetProcessList.Matches("ollama app", TargetProcessList.Default));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static List<Rule> Terms(params string[] words) => words.Select(Term).ToList();

    private static Rule Term(string word) => new()
    {
        Id = "user." + word,
        Label = word,
        Kind = "keyword",
        Pattern = word,
        Placeholder = "TERM",
        Severity = Severity.High,
        IgnoreCase = false
    };

    private static string ValidJmbg(string first12)
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

    private static void WriteOffice(string path, string text)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            WriteDocx(path, text);
        else if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            WriteXlsx(path, text);
        else
            WritePptx(path, text);
    }

    private static void WriteDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
            var body = new Body(
            new Paragraph(new Run(new Text(text))),
            new Table(
                new TableProperties(new TableWidth { Width = "0", Type = TableWidthUnitValues.Auto }),
                new TableGrid(new GridColumn { Width = "1000" }, new GridColumn { Width = "1000" }),
                new TableRow(new TableCell(new Paragraph(new Run(new Text("cell")))))));
        main.Document = new Document(body);
        main.Document.Save();
    }

    private static void WriteXlsx(string path, string text)
    {
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = doc.AddWorkbookPart();
        workbook.Workbook = new X.Workbook(new X.Sheets(new X.Sheet
        {
            Name = "Sheet1",
            SheetId = 1,
            Id = "rId1"
        }));
        var sheet = workbook.AddNewPart<WorksheetPart>("rId1");
        sheet.Worksheet = new X.Worksheet(new X.SheetData(
            new X.Row(new X.Cell
            {
                CellReference = "A1",
                DataType = X.CellValues.InlineString,
                InlineString = new X.InlineString(new X.Text(text))
            })));
        workbook.Workbook.Save();
    }

    private static void WritePptx(string path, string text)
    {
        using var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentation = doc.AddPresentationPart();
        presentation.Presentation = new P.Presentation(new P.SlideIdList());
        var slidePart = presentation.AddNewPart<SlidePart>("rId1");
        slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup()),
            new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = 2, Name = "Title" },
                    new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(),
                new P.TextBody(
                    new A.BodyProperties(),
                    new A.ListStyle(),
                    new A.Paragraph(new A.Run(new A.Text(text))))))));
        presentation.Presentation.SlideIdList!.Append(new P.SlideId { Id = 256, RelationshipId = "rId1" });
        presentation.Presentation.Save();
    }

    private static byte[] ZipOf(params (string Name, string Text)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        return stream.ToArray();
    }

    private static string ReadZipText(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var builder = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            builder.Append(reader.ReadToEnd());
        }
        return builder.ToString();
    }
}
