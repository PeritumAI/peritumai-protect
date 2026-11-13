using System.Globalization;
using Peritum.Protect.Core.Links;

namespace Peritum.Protect.Core.Tests;

public class LinkCorpusTests
{
    private static readonly BrandCatalog Catalog = BrandCatalog.LoadAll();

    [Fact]
    public void Ordinary_links_produce_no_warning()
    {
        var links = Ordinary().ToList();
        Assert.True(links.Count >= 300, "corpus has " + links.Count);
        var warnings = new List<string>();
        foreach (var text in links)
        {
            foreach (var link in LinkInspector.Inspect(text, Catalog))
            {
                if (link.Warns)
                    warnings.Add(text + " → " + string.Join(",", link.Flags.Select(flag => flag.Code)));
            }
        }

        Assert.True(warnings.Count == 0, string.Join("\n", warnings.Take(12)));
    }

    [Fact]
    public void Trick_links_all_warn_and_cover_every_reason()
    {
        var links = Tricks().ToList();
        Assert.True(links.Count >= 100, "corpus has " + links.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in links)
        {
            var found = LinkInspector.Inspect(text, Catalog);
            Assert.Contains(found, link => link.Warns);
            foreach (var link in found)
            {
                foreach (var flag in link.Flags)
                    seen.Add(flag.Code);
            }
        }

        foreach (var code in new[]
        {
            LinkCodes.Lookalike,
            LinkCodes.BrandElsewhere,
            LinkCodes.UserinfoTrick,
            LinkCodes.DangerousScheme,
            LinkCodes.IpHost,
            LinkCodes.RiskyDownload,
            LinkCodes.Shortener,
            LinkCodes.OddPort
        })
            Assert.Contains(code, seen);
    }

    private static IEnumerable<string> Ordinary()
    {
        yield return "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        yield return "https://accounts.google.com/ServiceLogin";
        yield return "https://docs.google.com/document/d/abc123/edit";
        yield return "https://www.paypal.com/signin";
        yield return "https://www.amazon.co.uk/gp/cart";
        yield return "https://www.amazon.de/";
        yield return "https://contoso.sharepoint.com/sites/team/Shared%20Documents";
        yield return "https://github.com/peritum/protect/blob/main/README.md";
        yield return "https://t.co/abc";
        yield return "https://nam12.safelinks.protection.outlook.com/?url=https%3A%2F%2Fwww.paypal.com%2Fsignin&data=05%7C01";
        yield return "https://münchen.de/";
        yield return "https://пример.рф/";
        yield return "https://" + new IdnMapping().GetAscii("münchen.de") + "/";
        yield return "https://news.example/story?utm_source=newsletter&utm_medium=email&utm_campaign=spring&utm_content=button&mc_eid=abc&mc_cid=def";
        yield return "mailto:ada@example.com";
        yield return "tel:+1-202-555-0100";
        yield return "http://localhost:8080/app";
        yield return "http://127.0.0.1:5000/";
        yield return "http://[::1]/";
        yield return "http://10.1.2.3/";
        yield return "http://192.168.0.20/status";
        yield return "https://en.wikipedia.org/wiki/PayPal";
        yield return "https://www.bbc.co.uk/news/technology-paypal-outage";
        yield return "https://www.theverge.com/apple";
        yield return "https://stackoverflow.com/questions/tagged/microsoft-graph";
        yield return "https://medium.com/@someone/google-cloud-tips-123";
        yield return "https://www.nytimes.com/2026/10/01/business/amazon-strike.html";
        yield return "https://recepti.rs/ananas-torta";
        yield return "https://www.kupujemprodajem.com/posta-oglas";
        yield return "https://www.blic.rs/biznis/posta-srbije-poskupljenja";
        yield return "https://apps.apple.com/app/paypal/id283646709";
        yield return "https://play.google.com/store/apps/details?id=com.paypal.android.p2pmobile";
        yield return "https://www.facebook.com/PayPal";
        yield return "https://www.instagram.com/paypal/";
        yield return "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        yield return "https://www.paypal-communication.com/";
        yield return "https://www.nflxext.com/site.css";
        yield return "https://www.dhlparcel.de/en/private-customers.html";
        yield return "http://localhost:3000/";
        yield return "https://example.com:443/health";
        yield return "http://example.com:80/health";
        yield return "https://www.google.com/url?q=https%3A%2F%2Fexample.com%2Fdocs%2Fguide&sa=D&source=editors&ust=1";
        yield return "https://urldefense.proofpoint.com/v2/url?u=https-3A__example.com_docs&d=DwICAg&c=example&r=example&m=example&s=example&e=";

        var words = new[]
        {
            "atlas", "birch", "cinder", "delta", "ember", "fjord", "grove", "harbor", "ion", "juniper",
            "kelp", "lumen", "maple", "north", "otter", "pebble", "quartz", "river", "spruce", "timber"
        };
        var tlds = new[] { "example", "test", "invalid" };
        var n = 0;
        foreach (var word in words)
        {
            foreach (var tld in tlds)
            {
                n++;
                yield return "https://" + word + "." + tld + "/page/" + n;
                yield return "https://www." + word + "." + tld + "/?id=" + n + "&ref=newsletter";
            }
        }

        foreach (var brand in Catalog.Brands)
        {
            foreach (var domain in brand.Domains)
                yield return "https://" + domain + "/";
        }
    }

    private static IEnumerable<string> Tricks()
    {
        string[] lookalikes =
        [
            "paypa1", "paypall", "paypl", "payapl", "paypax", "micros0ft", "arnazon", "app1e", "netfl1x",
            "g00gle", "microsfot", "facebok", "linkdin", "dropbx", "adobee", "spotfy", "coinbas", "binanc",
            "whatsap", "instagran", "youtub", "chasee", "wellsfarg"
        ];
        foreach (var label in lookalikes)
            yield return "https://" + label + ".example/login";

        yield return "https://paypal.com.secure-login.example/signin";
        yield return "https://login.paypal.com.secure-login.example/signin";
        yield return "https://secure-login.example/paypal.com/signin";
        yield return "https://paypal-reset.example/account";
        yield return "https://microsoft-365-login.example/";
        yield return "https://apple-id-verify.example/unlock";
        yield return "https://dhl-delivery.example/track";
        yield return "https://ups.com.track-parcel.example/status";
        yield return "https://visa-secure.example/3ds";
        yield return "paypal-login.example/verify";
        yield return "https://evil.example/amazon.com/order";

        yield return "https://paypal.com@evil.example/login";
        yield return "https://accounts.google.com@evil.example/signin";
        yield return "http://microsoft.com@203.0.113.8/";

        yield return "javascript:alert(1)";
        yield return "data:text/html,hello";
        yield return "vbscript:msgbox(1)";
        yield return "file://evil.example/share/invoice";
        yield return "search-ms:query=invoice&crumb=location:%5C%5Cevil.example";
        yield return "ms-msdt:id=PCWDiagnostic";
        yield return "ms-appinstaller:?source=https://evil.example/setup";
        yield return "ms-officecmd:ofe|u|https://evil.example/sheet";

        yield return "http://203.0.113.10/login";
        yield return "http://198.51.100.20/login";
        yield return "http://[2001:db8::5]/admin";

        foreach (var ext in new[] { "exe", "scr", "js", "vbs", "hta", "lnk", "iso", "img", "msi", "bat", "cmd", "ps1" })
            yield return "https://files.example/invoice." + ext;
        yield return "https://files.example/invoice.pdf.exe";
        yield return "https://files.example/invoice.pdf.js?download=1";

        yield return "https://bit.ly/invoice.exe";
        yield return "https://micros0ft-support.example/help";
        yield return "https://steamcommunity-gift.example/claim";
        yield return "https://tinyurl.com/invoice.msi";
        yield return "https://paypa1.example:8443/signin";
        yield return "https://secure-login.example:8080/paypal.com/signin";

        var idn = new IdnMapping();
        yield return "https://" + idn.GetAscii("p\u0430ypal.com") + "/signin";
        yield return "https://p\u0430ypal.example/signin";
        yield return "https://dhI.com/track";

        for (var i = 0; i < 30; i++)
            yield return "https://paypal-secure-" + i + ".example/verify";
    }
}
