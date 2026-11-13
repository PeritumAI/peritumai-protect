using Peritum.Protect.Core.Links;

namespace Peritum.Protect.Core.Tests;

public class Gap8bLinkTests
{
    private static readonly BrandCatalog Catalog = BrandCatalog.LoadAll();

    public static IEnumerable<object[]> Ordinary()
    {
        yield return ["https://en.wikipedia.org/wiki/PayPal"];
        yield return ["https://www.bbc.co.uk/news/technology-paypal-outage"];
        yield return ["https://www.theverge.com/apple"];
        yield return ["https://stackoverflow.com/questions/tagged/microsoft-graph"];
        yield return ["https://medium.com/@someone/google-cloud-tips-123"];
        yield return ["https://www.nytimes.com/2026/10/01/business/amazon-strike.html"];
        yield return ["https://recepti.rs/ananas-torta"];
        yield return ["https://www.kupujemprodajem.com/posta-oglas"];
        yield return ["https://www.blic.rs/biznis/posta-srbije-poskupljenja"];
        yield return ["https://www.paypal-communication.com/"];
        yield return ["https://www.nflxext.com/"];
        yield return ["https://www.docusign.net/"];
        yield return ["https://www.dhlparcel.de/"];
        yield return ["https://www.bancaintesa.rs/"];
        yield return ["https://www.unicreditbank.rs/"];
        yield return ["https://www.raiffeisenbank.rs/"];
        yield return ["https://www.otpbanka.rs/"];
        yield return ["http://127.0.0.1:5000/"];
        yield return ["http://localhost/"];
        yield return ["http://[::1]/"];
        yield return ["http://10.1.2.3/"];
        yield return ["http://192.168.0.20/status"];
        yield return ["https://www.python.org/ftp/python/3.13.0/python-3.13.0-amd64.exe"];
        yield return ["https://github.com/cli/cli/releases/download/v2.40.0/gh_2.40.0_windows_amd64.exe"];
        yield return ["https://aadcdn.msftauth.net/shared/1.0/content/js/convergedsa_v2_3.js"];
    }

    [Theory]
    [MemberData(nameof(Ordinary))]
    public void An_ordinary_link_does_not_warn(string url)
    {
        var scope = url.Contains(".exe", StringComparison.OrdinalIgnoreCase) || url.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            ? LinkScope.Clipboard
            : LinkScope.Document;
        var link = LinkInspector.Inspect(url, Catalog, scope).Single();
        Assert.False(link.Warns, string.Join(",", link.Flags.Select(flag => flag.Code + ":" + flag.Severity)));
    }

    [Fact]
    public void A_brand_domain_in_the_path_is_medium()
    {
        var link = LinkInspector.Inspect("https://secure-login.example/paypal.com/signin", Catalog).Single();
        Assert.True(link.Has(LinkCodes.BrandElsewhere));
        Assert.Equal(LinkSeverity.Medium, link.Flag(LinkCodes.BrandElsewhere)!.Severity);
        Assert.True(link.Warns);
    }

    [Fact]
    public void A_brand_in_the_host_stays_high()
    {
        var keyword = LinkInspector.Inspect("https://microsoft-365-login.example/", Catalog).Single();
        Assert.Equal(LinkSeverity.High, keyword.Flag(LinkCodes.BrandElsewhere)!.Severity);
        var nested = LinkInspector.Inspect("https://paypal.com.account-verify.example/signin", Catalog).Single();
        Assert.Equal(LinkSeverity.High, nested.Flag(LinkCodes.BrandElsewhere)!.Severity);
    }

    [Theory]
    [InlineData("https://micros0ft-support.com/login")]
    [InlineData("https://steamcommunity-gift.ru/claim")]
    public void A_hyphenated_host_trick_warns(string url)
    {
        var link = LinkInspector.Inspect(url, Catalog).Single();
        Assert.True(link.Warns);
        Assert.True(link.Has(LinkCodes.Lookalike) || link.Has(LinkCodes.BrandElsewhere));
    }

    [Fact]
    public void A_double_extension_stays_high_and_a_single_download_is_quiet_on_the_clipboard()
    {
        var doubled = LinkInspector.Inspect("https://files.example/invoice.pdf.exe", Catalog, LinkScope.Clipboard).Single();
        Assert.Equal(LinkSeverity.High, doubled.Flag(LinkCodes.RiskyDownload)!.Severity);
        Assert.True(doubled.Warns);

        var single = LinkInspector.Inspect("https://files.example/setup.exe", Catalog, LinkScope.Clipboard).Single();
        Assert.False(single.Warns);

        var script = LinkInspector.Inspect("https://files.example/app.js", Catalog, LinkScope.Clipboard).Single();
        Assert.False(script.Has(LinkCodes.RiskyDownload));

        var document = LinkInspector.Inspect("https://files.example/setup.exe", Catalog, LinkScope.Document).Single();
        Assert.Equal(LinkSeverity.Medium, document.Flag(LinkCodes.RiskyDownload)!.Severity);
        Assert.True(document.Warns);
    }

    [Fact]
    public void An_ip_host_has_no_registrable_domain_and_a_public_address_warns()
    {
        var loopback = LinkInspector.Inspect("http://127.0.0.1:5000/", Catalog).Single();
        Assert.Null(loopback.RegistrableDomain);
        Assert.False(loopback.Warns);

        var address = LinkInspector.Inspect("http://203.0.113.10/login", Catalog).Single();
        Assert.Null(address.RegistrableDomain);
        Assert.Equal("203.0.113.10", address.Host);
        Assert.Equal(LinkSeverity.Medium, address.Flag(LinkCodes.IpHost)!.Severity);

        var local = LinkInspector.Inspect("http://192.168.1.20/", Catalog).Single();
        Assert.Null(local.RegistrableDomain);
        Assert.Equal(LinkSeverity.Low, local.Flag(LinkCodes.IpHost)!.Severity);
        Assert.False(local.Warns);
    }

    [Fact]
    public void The_link_card_does_not_pad_the_domain()
    {
        var root = Repo();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Peritum.Protect.App", "Views", "LinkWarningWindow.xaml"));
        var runs = xaml.IndexOf("<Run Text=\"{Binding Before", StringComparison.Ordinal);
        Assert.True(runs > 0);
        var next = xaml.IndexOf("<Run Text=\"{Binding Domain", runs, StringComparison.Ordinal);
        Assert.True(next > runs);
        var between = xaml[runs..next];
        Assert.DoesNotContain('\n', between);
        Assert.DoesNotContain(' ', between[(between.IndexOf("/>", StringComparison.Ordinal) + 2)..]);
    }

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lang", "en.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root was not found.");
    }
}
