using Peritum.Protect.Core.Links;

namespace Peritum.Protect.Core.Tests;

public class BrandCatalogTests
{
    [Fact]
    public void The_global_list_cites_its_sources_and_holds_150_to_200_brands()
    {
        var catalog = BrandCatalog.LoadGlobal();
        Assert.InRange(catalog.Brands.Count, 150, 200);
        Assert.False(string.IsNullOrWhiteSpace(catalog.AsOf));
        Assert.Contains(catalog.Sources, source => source.Contains("Check Point", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Sources, source => source.Contains("APWG", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            catalog.Brands,
            brand => brand.Domains.Contains("microsoftonline.com") && brand.Domains.Contains("sharepoint.com"));
    }

    [Fact]
    public void Every_brand_has_a_domain_and_no_domain_is_shared()
    {
        var all = BrandCatalog.LoadAll();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Assert.True(all.Brands.Count > 150);
        foreach (var brand in all.Brands)
        {
            Assert.NotEmpty(brand.Domains);
            Assert.False(string.IsNullOrWhiteSpace(brand.Name));
            foreach (var domain in brand.Domains)
            {
                Assert.DoesNotContain("://", domain, StringComparison.Ordinal);
                Assert.True(
                    seen.TryAdd(domain, brand.Name),
                    domain + " is listed for " + seen[domain] + " and " + brand.Name);
            }
        }
    }

    [Fact]
    public void Every_official_domain_passes_with_no_flag()
    {
        var catalog = BrandCatalog.LoadAll();
        foreach (var brand in catalog.Brands)
        {
            foreach (var domain in brand.Domains)
            {
                var links = LinkInspector.Inspect("https://" + domain + "/", catalog);
                Assert.Single(links);
                Assert.True(
                    links[0].Flags.Count == 0,
                    domain + " flagged " + string.Join(",", links[0].Flags.Select(flag => flag.Code)));
                Assert.Equal(domain, links[0].RegistrableDomain);
            }
        }
    }

    [Fact]
    public void A_country_pack_follows_the_region()
    {
        foreach (var code in new[] { "RS", "HR", "SI", "BA", "ME", "MK", "DE", "FR", "ES", "IT", "NL", "PL", "GB", "US", "BR", "IN" })
        {
            var home = BrandCatalog.ForRegion(code);
            var foreign = BrandCatalog.ForRegion(code == "US" ? "DE" : "US");
            var local = home.Brands.Where(brand => brand.Country == code).ToList();
            Assert.InRange(local.Count, 10, 20);
            var sample = local[0].Domains[0];
            Assert.True(home.IsOfficial(sample));
            Assert.False(foreign.IsOfficial(sample));
            var withProfile = BrandCatalog.ForRegion(code == "US" ? "DE" : "US", extraCountries: [code]);
            Assert.True(withProfile.IsOfficial(sample));
        }
    }
}
