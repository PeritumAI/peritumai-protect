using System.IO.Compression;
using System.Text;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class OutputVerificationTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void Verifier_refuses_a_package_that_still_contains_the_value()
    {
        var email = "ada@example.com";
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("word/comments.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("<w:t>" + email + "</w:t>");
        }

        var leak = OfficePackageSanitizer.FindLeak(stream.ToArray(), [email]);
        Assert.Equal("comments", leak);
    }

    [Fact]
    public void Five_thousand_emails_are_removed_from_the_csv_copy()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-bulk-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var original = Path.Combine(dir, "people.csv");
            var builder = new StringBuilder();
            for (var i = 0; i < 5000; i++)
                builder.Append("user").Append(i).Append("@example.com\n");
            File.WriteAllText(original, builder.ToString());

            var copy = new ProtectService().MakeProtectedCopy(original, Rules, "{name}_protect{ext}");

            Assert.True(string.IsNullOrEmpty(copy.Error), copy.Error);
            var output = File.ReadAllText(copy.OutputPath!);
            Assert.DoesNotContain("@example.com", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class DetectionQualityTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Theory]
    [InlineData("20260928")]
    [InlineData("Order #12345678")]
    [InlineData("Ref 2026-0912-4411")]
    [InlineData("PIB 101234567")]
    public void Ordinary_numbers_are_not_phones(string text)
    {
        var findings = new RuleScanner().Find(text, Rules);
        Assert.DoesNotContain(findings, finding => finding.RuleId == "pii.phone");
    }

    [Theory]
    [InlineData("+381 64 123 4567")]
    [InlineData("064/123-4567")]
    public void Serbian_phone_shapes_are_flagged(string text)
    {
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, finding => finding.RuleId == "pii.phone");
    }

    [Fact]
    public void Base64_blob_does_not_hide_nearby_emails()
    {
        var blob = Convert.ToBase64String(Enumerable.Repeat((byte)7, 40_000).ToArray());
        var text = blob + " ada@example.com " + blob + " bob@example.com " + blob + " cy@example.com";
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Equal(3, findings.Count(finding => finding.RuleId == "pii.email"));
    }
}
