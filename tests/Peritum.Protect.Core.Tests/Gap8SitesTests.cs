namespace Peritum.Protect.Core.Tests;

public class Gap8SitesTests
{
    [Fact]
    public void A_bookmarks_file_imports_each_site_once()
    {
        var html = """
            <!DOCTYPE NETSCAPE-Bookmark-file-1>
            <DL><p>
              <DT><A HREF="https://www.example-bank.com/login">Bank</A>
              <DT><A HREF="https://news.example/story">News</A>
              <DT><A HREF="https://www.example-bank.com/pay">Again</A>
              <DT><A HREF="javascript:alert(1)">Skip</A>
            </DL>
            """;
        var sites = Peritum.Protect.Core.Links.BookmarkDomains.FromHtml(html);
        Assert.Equal(new[] { "example-bank.com", "news.example" }, sites);
    }

    [Fact]
    public void A_typed_site_becomes_its_registrable_domain()
    {
        Assert.Equal("example-bank.com", Peritum.Protect.Core.Links.BookmarkDomains.Normalize("https://www.example-bank.com/login"));
        Assert.Equal("news.example", Peritum.Protect.Core.Links.BookmarkDomains.Normalize("news.example"));
        Assert.Null(Peritum.Protect.Core.Links.BookmarkDomains.Normalize("javascript:alert(1)"));
    }

    [Fact]
    public void A_policy_can_add_organization_domains_and_lock_the_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "peritum-gap8-sites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "policy.json");
            File.WriteAllText(path, """{"organizationDomains":["https://www.bank.example/login"],"lockSites":true}""");
            var policy = Peritum.Protect.Core.Config.PolicyFile.Load(path);
            Assert.NotNull(policy);
            Assert.Equal("bank.example", Assert.Single(policy!.OrganizationDomains));
            Assert.True(policy.Locks("lockSites"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
