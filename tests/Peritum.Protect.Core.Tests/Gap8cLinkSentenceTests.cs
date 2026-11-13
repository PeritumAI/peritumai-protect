using Peritum.Protect.Core;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8cLinkSentenceTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    [Fact]
    public void A_mismatched_link_is_described_in_plain_words()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-gap8c-sentence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "link-phish.eml");
            var source = Path.Combine(FindRepo(), "fixtures", "link-phish.eml");
            File.Copy(source, path);
            var session = new ProtectService().Scan(ScanRequest.ForFile(path), Rules);
            Assert.True(string.IsNullOrEmpty(session.Result.Error), session.Result.Error);
            var finding = Assert.Single(session.Result.LinkFindings, item => item.RuleId == "link.text_mismatch");
            Assert.Equal("Shows www.paypal.com but goes to account-verify.example", finding.Snippet);
            Assert.False(string.IsNullOrWhiteSpace(finding.Location));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "fixtures", "link-phish.eml")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("fixtures/link-phish.eml was not found.");
    }
}
