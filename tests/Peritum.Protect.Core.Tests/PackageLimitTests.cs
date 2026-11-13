using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class PackageLimitTests
{
    [Fact]
    public void High_ratio_zip_is_incomplete_and_not_copied()
    {
        var path = NewDocx();
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("word/document.xml", CompressionLevel.SmallestSize);
            using var writer = entry.Open();
            var chunk = Encoding.ASCII.GetBytes(new string('A', 8192));
            for (var i = 0; i < 40; i++)
                writer.Write(chunk);
        }
        AssertBlocked(path, "100 to 1");
    }

    [Fact]
    public void Too_many_zip_entries_are_incomplete_without_expanding_them()
    {
        var path = NewDocx();
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            for (var i = 0; i < PackageLimits.MaxEntries + 1; i++)
            {
                var entry = zip.CreateEntry("part" + i + ".xml", CompressionLevel.NoCompression);
                using var writer = entry.Open();
                writer.WriteByte((byte)'x');
            }
        }
        AssertBlocked(path, "10,000");
    }

    [Fact]
    public void A_lying_uncompressed_size_is_rejected_before_the_bytes_are_read()
    {
        var path = NewDocx();
        using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("word/document.xml", CompressionLevel.NoCompression);
            using var writer = entry.Open();
            writer.Write(Encoding.ASCII.GetBytes("hello"));
        }
        PatchUncompressedSize(path, 80u * 1024u * 1024u);
        AssertBlocked(path, "64 MB");
    }

    [Fact]
    public void Five_nested_packages_are_incomplete()
    {
        var path = NewDocx();
        File.WriteAllBytes(path, Nest(5));
        AssertBlocked(path, "3 deep");
    }

    [Fact]
    public void A_pdf_with_too_many_pages_is_incomplete()
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-pages-" + Guid.NewGuid().ToString("n") + ".pdf");
        try
        {
            File.WriteAllBytes(path, PdfWithPages(PackageLimits.MaxPdfPages + 1));
            AssertBlocked(path, "2,000 pages");
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void A_pdf_past_the_time_budget_is_incomplete()
    {
        var path = Path.Combine(Path.GetTempPath(), "peritum-slow-" + Guid.NewGuid().ToString("n") + ".pdf");
        try
        {
            File.WriteAllBytes(path, SimplePdfWriter.FromText("Hello from this page."));
            var read = new PdfFileAdapter { Elapsed = () => TimeSpan.FromMinutes(1) }.Read(path);
            Assert.True(read.Incomplete);
            Assert.Equal(PackageLimits.PdfTooSlow, read.Note);
            Assert.Null(read.Error);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AssertBlocked(string path, string reason)
    {
        try
        {
            var service = new ProtectService();
            var clock = Stopwatch.StartNew();
            ScanSession session;
            try
            {
                session = service.Scan(ScanRequest.ForFile(path), []);
            }
            catch (Exception ex)
            {
                Assert.Fail("Exception escaped: " + ex);
                return;
            }
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), "Took " + clock.Elapsed);
            Assert.True(session.Result.Incomplete);
            Assert.Contains(reason, session.Result.Note ?? "");
            var before = File.ReadAllBytes(path);
            var copy = service.MakeProtectedCopy(path, [], "{name}_protect{ext}");
            Assert.Null(copy.OutputPath);
            Assert.False(string.IsNullOrEmpty(copy.Error));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*_protect*"));
        }
        finally
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null && Path.GetFileName(dir).StartsWith("peritum-bomb-", StringComparison.Ordinal))
                Directory.Delete(dir, true);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string NewDocx()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-bomb-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "bomb.docx");
    }

    private static void PatchUncompressedSize(string path, uint value)
    {
        var bytes = File.ReadAllBytes(path);
        Patch(bytes, [0x50, 0x4B, 0x03, 0x04], 22, value);
        Patch(bytes, [0x50, 0x4B, 0x01, 0x02], 24, value);
        File.WriteAllBytes(path, bytes);
    }

    private static void Patch(byte[] bytes, byte[] signature, int field, uint value)
    {
        for (var i = 0; i <= bytes.Length - signature.Length - field - 4; i++)
        {
            var match = true;
            for (var j = 0; j < signature.Length; j++)
            {
                if (bytes[i + j] != signature[j])
                {
                    match = false;
                    break;
                }
            }
            if (!match)
                continue;
            bytes[i + field] = (byte)value;
            bytes[i + field + 1] = (byte)(value >> 8);
            bytes[i + field + 2] = (byte)(value >> 16);
            bytes[i + field + 3] = (byte)(value >> 24);
        }
    }

    private static byte[] Nest(int depth)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(depth == 1 ? "leaf.txt" : "nested.bin", CompressionLevel.NoCompression);
            using var writer = entry.Open();
            if (depth <= 1)
                writer.Write(Encoding.ASCII.GetBytes("ok"));
            else
                writer.Write(Nest(depth - 1));
        }
        return ms.ToArray();
    }

    private static byte[] PdfWithPages(int pages)
    {
        var ascii = Encoding.ASCII;
        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        void Add(string body)
        {
            offsets.Add(ascii.GetByteCount(builder.ToString()));
            builder.Append(body);
        }

        Add("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var kids = new StringBuilder();
        for (var i = 0; i < pages; i++)
        {
            if (i > 0)
                kids.Append(' ');
            kids.Append(i + 3);
            kids.Append(" 0 R");
        }
        Add("2 0 obj\n<< /Type /Pages /Count " + pages + " /Kids [" + kids + "] >>\nendobj\n");
        for (var i = 0; i < pages; i++)
            Add((i + 3) + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 20 20] >>\nendobj\n");

        var xref = ascii.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ");
        builder.Append(offsets.Count);
        builder.Append("\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            builder.Append(offsets[i].ToString("D10") + " 00000 n \n");
        builder.Append("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n");
        builder.Append(xref);
        builder.Append("\n%%EOF\n");
        return ascii.GetBytes(builder.ToString());
    }
}
