using System.Globalization;
using Peritum.Protect.Core.Links;

namespace Peritum.Protect.Core.Tests;

public class LinkInspectorTests
{
    private static readonly BrandCatalog Catalog = BrandCatalog.LoadAll();

    [Fact]
    public void Registrable_domain_is_the_registered_name()
    {
        Assert.Equal("secure-login.xyz", Domain("https://paypal.com.secure-login.xyz/signin"));
        Assert.Equal("secure-login.example", Domain("https://paypal.com.secure-login.example/signin"));
        Assert.Equal("amazon.co.uk", Domain("https://www.amazon.co.uk/gp/cart"));
        Assert.Equal("amazon.com.au", Domain("https://www.amazon.com.au/"));
        Assert.Equal("microsoftonline.com", Domain("https://login.microsoftonline.com/common"));
        Assert.Equal("example.co.rs", Domain("https://shop.example.co.rs/"));
        Assert.Equal("example.com.br", Domain("https://www.example.com.br/"));
        Assert.Equal("example.co.in", Domain("https://www.example.co.in/"));
    }

    [Theory]
    [InlineData("https://paypa1.example/signin")]
    [InlineData("https://paypall.example/signin")]
    [InlineData("https://paypl.example/signin")]
    [InlineData("https://payapl.example/signin")]
    [InlineData("https://paypax.example/signin")]
    [InlineData("https://micros0ft.example/login")]
    [InlineData("https://arnazon.example/order")]
    [InlineData("https://paypa1.com/signin")]
    public void Typo_of_a_long_brand_is_lookalike(string url)
    {
        var link = One(url);
        Assert.True(link.Has(LinkCodes.Lookalike));
        Assert.Equal(LinkSeverity.High, link.Flag(LinkCodes.Lookalike)!.Severity);
        Assert.True(link.Warns);
        Assert.False(string.IsNullOrWhiteSpace(link.BrandName));
    }

    [Fact]
    public void Punycode_and_mixed_script_lookalikes_are_flagged()
    {
        var idn = new IdnMapping();
        var cyrillicA = "\u0430";
        var host = "p" + cyrillicA + "ypal.com";
        var puny = "https://" + idn.GetAscii(host) + "/";
        var encoded = One(puny);
        Assert.True(encoded.Has(LinkCodes.Lookalike));
        Assert.True(encoded.Warns);

        var mixed = One("https://p" + cyrillicA + "ypal.example/");
        Assert.True(mixed.Has(LinkCodes.Lookalike));
        Assert.StartsWith("xn--", idn.GetAscii(mixed.Host), StringComparison.Ordinal);
    }

    [Fact]
    public void Capital_i_and_cyrillic_confusable_match_a_short_brand()
    {
        var eye = One("https://dhI.com/");
        Assert.True(eye.Has(LinkCodes.Lookalike));
        Assert.Equal("DHL", eye.BrandName);

        var idn = new IdnMapping();
        var host = "dh\u04cf.com";
        var cyrillic = One("https://" + idn.GetAscii(host) + "/");
        Assert.True(cyrillic.Has(LinkCodes.Lookalike));
    }

    [Theory]
    [InlineData("https://ing.example/")]
    [InlineData("https://visaapplication.gov.example/form")]
    [InlineData("https://upstairs.com/")]
    [InlineData("https://dh1.com/")]
    public void Short_brand_names_do_not_use_edit_distance(string url)
    {
        var link = One(url);
        Assert.False(link.Warns);
        Assert.False(link.Has(LinkCodes.Lookalike));
        Assert.False(link.Has(LinkCodes.BrandElsewhere));
    }

    [Theory]
    [InlineData("https://paypal.com.secure-login.example/signin", "secure-login.example")]
    [InlineData("https://microsoft-365-login.example/", "microsoft-365-login.example")]
    [InlineData("https://dhl-delivery.example/track", "dhl-delivery.example")]
    [InlineData("https://ups.com.track-parcel.example/status", "track-parcel.example")]
    [InlineData("paypal-login.example/verify", "paypal-login.example")]
    public void Brand_in_the_wrong_place_is_brand_elsewhere(string text, string domain)
    {
        var link = One(text);
        Assert.Equal(domain, link.RegistrableDomain);
        Assert.True(link.Has(LinkCodes.BrandElsewhere));
        Assert.Equal(LinkSeverity.High, link.Flag(LinkCodes.BrandElsewhere)!.Severity);
        Assert.True(link.Warns);
    }

    [Fact]
    public void Text_before_at_is_ignored()
    {
        var link = One("https://paypal.com@evil.example/login");
        Assert.Equal("evil.example", link.Host);
        Assert.Equal("evil.example", link.RegistrableDomain);
        Assert.Equal("paypal.com", link.UserInfo);
        Assert.True(link.Has(LinkCodes.UserinfoTrick));
        Assert.Equal(LinkSeverity.High, link.Flag(LinkCodes.UserinfoTrick)!.Severity);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file://evil.example/share/invoice")]
    [InlineData("search-ms:query=invoice")]
    [InlineData("ms-msdt:id=PCWDiagnostic")]
    [InlineData("ms-appinstaller:?source=https://evil.example/app")]
    [InlineData("ms-officecmd:ofe|u|https://evil.example/doc")]
    public void Abused_schemes_are_dangerous(string url)
    {
        var link = One(url);
        Assert.True(link.Has(LinkCodes.DangerousScheme));
        Assert.Equal(LinkSeverity.High, link.Flag(LinkCodes.DangerousScheme)!.Severity);
        Assert.True(link.Warns);
    }

    [Fact]
    public void A_local_file_link_is_not_a_remote_share()
    {
        var link = One("file:///C:/Windows/notepad.exe");
        Assert.False(link.Has(LinkCodes.DangerousScheme));
        Assert.False(link.Warns);
    }

    [Theory]
    [InlineData("http://203.0.113.10/login")]
    [InlineData("http://[2001:db8::5]/")]
    public void An_address_instead_of_a_name_is_an_ip_host(string url)
    {
        var link = One(url);
        Assert.True(link.Has(LinkCodes.IpHost));
        Assert.Equal(LinkSeverity.Medium, link.Flag(LinkCodes.IpHost)!.Severity);
        Assert.True(link.Warns);
    }

    [Theory]
    [InlineData("https://files.example/invoice.exe")]
    [InlineData("https://files.example/screen.scr")]
    [InlineData("https://files.example/run.js")]
    [InlineData("https://files.example/run.vbs")]
    [InlineData("https://files.example/run.hta")]
    [InlineData("https://files.example/open.lnk")]
    [InlineData("https://files.example/disk.iso")]
    [InlineData("https://files.example/disk.img")]
    [InlineData("https://files.example/setup.msi")]
    [InlineData("https://files.example/go.bat")]
    [InlineData("https://files.example/go.cmd")]
    [InlineData("https://files.example/go.ps1")]
    public void Risky_downloads_warn(string url)
    {
        var link = One(url);
        Assert.True(link.Has(LinkCodes.RiskyDownload));
        Assert.Equal(LinkSeverity.Medium, link.Flag(LinkCodes.RiskyDownload)!.Severity);
        Assert.True(link.Warns);
    }

    [Fact]
    public void A_shortener_or_odd_port_alone_does_not_warn()
    {
        var shortener = One("https://t.co/abc");
        Assert.True(shortener.Has(LinkCodes.Shortener));
        Assert.Equal(LinkSeverity.Low, shortener.Flag(LinkCodes.Shortener)!.Severity);
        Assert.False(shortener.Warns);

        var port = One("https://example.com:8443/status");
        Assert.True(port.Has(LinkCodes.OddPort));
        Assert.Equal(LinkSeverity.Low, port.Flag(LinkCodes.OddPort)!.Severity);
        Assert.False(port.Warns);

        var both = One("https://bit.ly/invoice.exe");
        Assert.True(both.Has(LinkCodes.Shortener));
        Assert.True(both.Has(LinkCodes.RiskyDownload));
        Assert.True(both.Warns);

        var local = One("http://localhost:8080/app");
        Assert.True(local.Has(LinkCodes.OddPort));
        Assert.False(local.Warns);
        Assert.False(local.Has(LinkCodes.IpHost));
    }

    [Fact]
    public void Safety_wrappers_are_unwrapped_before_the_check()
    {
        var safe = One("https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpaypal.com%2Fsignin&data=05");
        Assert.Equal("paypal.com", safe.RegistrableDomain);
        Assert.False(safe.Warns);
        Assert.Empty(safe.Flags);

        var safeTrick = One("https://eu.safelinks.protection.outlook.com/?url=https%3A%2F%2Fpaypa1.example%2Fsignin&data=05");
        Assert.Equal("paypa1.example", safeTrick.RegistrableDomain);
        Assert.True(safeTrick.Has(LinkCodes.Lookalike));

        var proof = One("https://urldefense.proofpoint.com/v2/url?u=https-3A__paypal.com_signin&d=DwICAg&c=example&r=example&m=example&s=example&e=");
        Assert.Equal("paypal.com", proof.RegistrableDomain);
        Assert.False(proof.Warns);

        var proof3 = One("https://urldefense.com/v3/__https://paypa1.example/signin__;Iw!!fake");
        Assert.Equal("paypa1.example", proof3.RegistrableDomain);
        Assert.True(proof3.Has(LinkCodes.Lookalike));

        var mime = One("https://protect-eu.mimecast.com/s/AbCdEfGh?url=https%3A%2F%2Fpaypal.com%2Fsignin");
        Assert.Equal("paypal.com", mime.RegistrableDomain);
        Assert.False(mime.Warns);

        var google = One("https://www.google.com/url?q=https%3A%2F%2Faccounts.google.com%2FServiceLogin&sa=D&source=editors");
        Assert.Equal("google.com", google.RegistrableDomain);
        Assert.False(google.Warns);
        Assert.Empty(google.Flags);
    }

    [Fact]
    public void Html_and_markdown_links_keep_the_visible_text()
    {
        var html = One("<p>See <a href=\"https://secure-login.example/paypal.com/signin\">www.paypal.com</a> now.</p>");
        Assert.Equal("secure-login.example", html.RegistrableDomain);
        Assert.Equal("www.paypal.com", html.VisibleText);
        Assert.True(html.Has(LinkCodes.BrandElsewhere));

        var markdown = One("Open [your account](https://paypa1.example/signin) today.");
        Assert.Equal("paypa1.example", markdown.RegistrableDomain);
        Assert.Equal("your account", markdown.VisibleText);
        Assert.True(markdown.Has(LinkCodes.Lookalike));
    }

    [Fact]
    public void An_email_address_is_not_a_link()
    {
        Assert.Empty(LinkInspector.Inspect("Write to ada@example.com today.", Catalog));
    }

    [Fact]
    public void Ordinary_reserved_and_idn_names_do_not_warn()
    {
        Assert.False(One("https://example.com/welcome").Warns);
        Assert.False(One("mailto:ada@example.com").Warns);
        Assert.False(One("tel:+1-202-555-0100").Warns);
        Assert.False(One("https://münchen.de/").Warns);
        var idn = new IdnMapping();
        Assert.False(One("https://" + idn.GetAscii("münchen.de") + "/").Warns);
        Assert.False(One("https://пример.рф/").Warns);
        Assert.False(One("https://example.com:443/path").Warns);
        Assert.False(One("http://example.com:80/path").Warns);
    }

    [Fact]
    public void A_site_the_user_added_is_official()
    {
        var catalog = BrandCatalog.LoadAll(extraDomains: ["my-employer.example"]);
        var link = LinkInspector.Inspect("https://my-employer.example/pay", catalog).Single();
        Assert.False(link.Warns);
        Assert.Empty(link.Flags);
        Assert.True(catalog.IsOfficial("my-employer.example"));
    }

    [Fact]
    public void The_suffix_list_records_its_date()
    {
        Assert.False(string.IsNullOrWhiteSpace(PublicSuffixList.AsOf));
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", PublicSuffixList.AsOf);
    }

    private static string? Domain(string url) => One(url).RegistrableDomain;

    private static InspectedLink One(string text)
    {
        var links = LinkInspector.Inspect(text, Catalog);
        Assert.Single(links);
        return links[0];
    }
}
