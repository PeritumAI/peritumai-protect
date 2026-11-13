using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class UncheckedFileTests
{
    [Fact]
    public void Encrypted_office_pdf_corrupt_and_legacy_files_are_not_checked()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-unchecked-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var service = new ProtectService();
            AssertNotChecked(service, EncryptedOffice(dir), UncheckedFiles.PasswordProtected);
            AssertNotChecked(service, EncryptedPdf(dir), UncheckedFiles.PasswordProtected);
            AssertNotChecked(service, Corrupt(dir, "broken.docx", "this is not a document"u8.ToArray()), UncheckedFiles.Corrupt);
            AssertNotChecked(service, Corrupt(dir, "broken.pdf", "not a pdf"u8.ToArray()), UncheckedFiles.Corrupt);
            foreach (var extension in new[] { ".doc", ".xls", ".ppt", ".rtf", ".odt", ".msg" })
                AssertNotChecked(service, Corrupt(dir, "legacy" + extension, "legacy"u8.ToArray()), UncheckedFiles.LegacyType);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static void AssertNotChecked(ProtectService service, string path, string reason)
    {
        var session = service.Scan(ScanRequest.ForFile(path), []);
        Assert.Equal(reason, session.Result.Error);
        Assert.DoesNotContain("Nothing found", session.Result.Error ?? "", StringComparison.Ordinal);
        Assert.Contains("Not checked", session.Result.Error ?? "", StringComparison.Ordinal);
        var copy = service.MakeProtectedCopy(path, [], "{name}_protect{ext}");
        Assert.Null(copy.OutputPath);
    }

    private static string EncryptedOffice(string dir)
    {
        var path = Path.Combine(dir, "secret.docx");
        var body = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var name = System.Text.Encoding.Unicode.GetBytes("EncryptionInfo");
        var bytes = new byte[body.Length + name.Length];
        body.CopyTo(bytes, 0);
        name.CopyTo(bytes, body.Length);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string EncryptedPdf(string dir)
    {
        var path = Path.Combine(dir, "secret.pdf");
        File.WriteAllText(path, "%PDF-1.4\n1 0 obj << /Encrypt 2 0 R >> endobj\n%%EOF\n");
        return path;
    }

    private static string Corrupt(string dir, string name, byte[] bytes)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
