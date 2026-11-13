using System.IO.Compression;
using System.Text;
using Peritum.Protect.Core;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class EmailFileTests
{
    private const string Email = "ada@example.com";
    private const string Other = "bob@example.com";
    private const string Key = "AKIAIOSFODNN7EXAMPLE";

    [Fact]
    public void Multipart_scans_headers_body_and_attachment_and_drops_unsupported()
    {
        var raw = """
            From: Ada Lovelace <ada@example.com>
            To: Bob <bob@example.com>
            Cc: Cara <cara@example.com>
            Subject: Notes
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="BOUND"

            --BOUND
            Content-Type: text/plain; charset=utf-8

            Please review the note.
            --BOUND
            Content-Type: text/plain; charset=utf-8
            Content-Disposition: attachment; filename="note.txt"

            See bob@example.com
            --BOUND
            Content-Type: application/msword
            Content-Disposition: attachment; filename="old.doc"

            secret AKIAIOSFODNN7EXAMPLE
            --BOUND--
            """;
        using var dir = new WorkDir();
        var path = dir.Write("mail.eml", Normalize(raw));
        var before = File.ReadAllBytes(path);
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.False(session.Result.Incomplete);
        Assert.Contains(Email, session.Text, StringComparison.Ordinal);
        Assert.Contains(Other, session.Text, StringComparison.Ordinal);
        Assert.Contains("cara@example.com", session.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, session.Text, StringComparison.Ordinal);
        Assert.Contains("old.doc", session.Result.Note ?? "", StringComparison.Ordinal);

        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        Assert.NotNull(copy.OutputPath);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Other, text, StringComparison.Ordinal);
        Assert.DoesNotContain("cara@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("old.doc", text, StringComparison.Ordinal);
        Assert.Contains(EmlFileAdapter.RemovedInside, text, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Html_only_message_is_scanned_and_the_copy_is_text()
    {
        var raw = """
            From: Notice
            To: Desk
            Subject: Hello
            Content-Type: text/html; charset=utf-8

            <html><body><p>Write to <b>ada@example.com</b> today.</p></body></html>
            """;
        using var dir = new WorkDir();
        var path = dir.Write("html.eml", Normalize(raw));
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.Contains(Email, session.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", session.Text, StringComparison.Ordinal);
        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>", text, StringComparison.Ordinal);
        Assert.Contains("[EMAIL]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Base64_attachment_key_is_found_and_removed()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("token " + Key + "\n"));
        var raw = "From: Notice\r\nSubject: File\r\nMIME-Version: 1.0\r\n" +
                  "Content-Type: multipart/mixed; boundary=\"BOUND\"\r\n\r\n" +
                  "--BOUND\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nSee the file.\r\n" +
                  "--BOUND\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n" +
                  "Content-Disposition: attachment; filename=\"key.txt\"\r\n\r\n" +
                  payload + "\r\n--BOUND--\r\n";
        using var dir = new WorkDir();
        var path = dir.Write("key.eml", raw);
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.Contains(Key, session.Text, StringComparison.Ordinal);
        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        var text = File.ReadAllText(copy.OutputPath!);
        Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
        Assert.Contains("[AWS_KEY]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Attachment_over_the_package_limit_is_incomplete()
    {
        var payload = Convert.ToBase64String(HighRatioPackage());
        var raw = "From: Notice\r\nSubject: Bomb\r\nMIME-Version: 1.0\r\n" +
                  "Content-Type: multipart/mixed; boundary=\"BOUND\"\r\n\r\n" +
                  "--BOUND\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nHello\r\n" +
                  "--BOUND\r\nContent-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document\r\n" +
                  "Content-Transfer-Encoding: base64\r\nContent-Disposition: attachment; filename=\"bomb.docx\"\r\n\r\n" +
                  payload + "\r\n--BOUND--\r\n";
        using var dir = new WorkDir();
        var path = dir.Write("bomb.eml", raw);
        var before = File.ReadAllBytes(path);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var session = Scan(path);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Took " + clock.Elapsed);
        Assert.True(session.Result.Incomplete);
        Assert.Contains("100 to 1", session.Result.Note ?? "", StringComparison.Ordinal);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}");
        Assert.Null(copy.OutputPath);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Malformed_message_is_not_checked()
    {
        using var dir = new WorkDir();
        var path = dir.Write("broken.eml", "legacy");
        var session = Scan(path);
        Assert.Equal(UncheckedFiles.Corrupt, session.Result.Error);
        Assert.DoesNotContain("Nothing found", session.Result.Error ?? "", StringComparison.Ordinal);
        var copy = new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}");
        Assert.Null(copy.OutputPath);
    }

    [Fact]
    public void Nested_multipart_quoted_printable_and_encoded_subject_are_read()
    {
        var raw = """
            From: Desk
            Subject: =?utf-8?Q?ada=40example.com?=
            MIME-Version: 1.0
            Content-Type: multipart/mixed; boundary="OUT"

            --OUT
            Content-Type: multipart/alternative; boundary="IN"

            --IN
            Content-Type: text/plain; charset=iso-8859-1
            Content-Transfer-Encoding: quoted-printable

            Caf=E9 for ada@example.com
            --IN--
            --OUT--
            """;
        using var dir = new WorkDir();
        var path = dir.Write("nested.eml", Normalize(raw));
        var session = Scan(path);
        Assert.Null(session.Result.Error);
        Assert.Contains("Café", session.Text, StringComparison.Ordinal);
        Assert.Contains(Email, session.Text, StringComparison.Ordinal);
        Assert.True(session.Result.HitCount >= 1);
        var copy = Copy(path);
        Assert.True(copy.OriginalUnchanged, copy.Error);
        Assert.DoesNotContain(Email, File.ReadAllText(copy.OutputPath!), StringComparison.Ordinal);
    }

    private static ScanSession Scan(string path) =>
        new ProtectService().Scan(ScanRequest.ForFile(path), Rules());

    private static CopyResult Copy(string path) =>
        new ProtectService().MakeProtectedCopy(path, Rules(), "{name}_protect{ext}");

    private static IReadOnlyList<Rule> Rules() =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    private static string Normalize(string raw)
    {
        var lines = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim().Split('\n');
        return string.Join("\r\n", lines.Select(line => line.Trim())) + "\r\n";
    }

    private static byte[] HighRatioPackage()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var writer = entry.Open();
            var chunk = Encoding.ASCII.GetBytes(new string('A', 8192));
            for (var i = 0; i < 40; i++)
                writer.Write(chunk);
        }

        return ms.ToArray();
    }

    private sealed class WorkDir : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "peritum-eml-" + Guid.NewGuid().ToString("n"));

        public WorkDir() => Directory.CreateDirectory(_path);

        public string Write(string name, string text)
        {
            var path = Path.Combine(_path, name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(_path, true);
    }
}
