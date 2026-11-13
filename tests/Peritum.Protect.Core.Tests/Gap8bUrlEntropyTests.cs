using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap8bUrlEntropyTests
{
    private static IReadOnlyList<Rule> Rules =>
        RulePackLoader.SelectActive(BuiltinRules.LoadEmbedded(), [], []);

    public static IEnumerable<object[]> OrdinaryLinks()
    {
        yield return ["https://grok.com/share/a8f3c1e04b2d4e6f9a1c3d5e7b9f0a2c4d6e8f0"];
        yield return ["https://grok.com/chat/3f2504e0-4f89-41d3-9a0c-0305e82c3301"];
        yield return ["https://x.com/i/grok/share/7kQm9pL2nX4vB8cD1eF6gH3jK5mN8pQ"];
        yield return ["https://chatgpt.com/share/6ba7b810-9dad-11d1-80b4-00c04fd430c8"];
        yield return ["https://claude.ai/share/550e8400-e29b-41d4-a716-446655440000"];
        yield return ["https://docs.google.com/document/d/1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms/edit?usp=sharing"];
        yield return ["https://drive.google.com/file/d/1a2B3c4D5e6F7g8H9i0JklMnOpQrStUvWxYz0123/view"];
        yield return ["https://contoso.sharepoint.com/:w:/g/personal/ada_contoso_com/EWqabcDEF123ghiJKL456mnoPQR789stuVWX?e=Ab12Cd"];
        yield return ["https://www.amazon.com/dp/B0EXAMPLE1?ref_=pd_rd_w&pd_rd_w=AbCdEfGhIjKlMnOpQrStUvWxYz012345&pf_rd_p=1a2b3c4d5e6f7g8h9i0jklmnopqrst"];
        yield return ["https://www.linkedin.com/posts/ada-lovelace_activity-7123456789012345678-AbCdEfGhIj"];
        yield return ["https://open.spotify.com/track/4cOdK2wGLETKBW3PvgPWqT?si=a1B2c3D4e5F6g7H8i9J0kLmN"];
        yield return ["https://github.com/dotnet/runtime/commit/a1b2c3d4e5f678901234567890abcdef12345678"];
        yield return ["https://www.google.com/search?q=weather&gs_lcrp=EgZjaHJvbWUyBggAEEUYOTIHCAEQABiABDIHCAIQABiP2dqMGoxajSoAgCwAgA"];
        yield return ["https://mail.google.com/mail/u/0/#inbox/FMfcgzQcpkL8vN3rT2wXyZ1aB4cD5eF6"];
        yield return ["https://teams.microsoft.com/l/meetup-join/19%3ameeting_AbC123dEf456GhI789%40thread.v2/0?context=%7b%22Tid%22%3a%22aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee%22%7d"];
    }

    [Theory]
    [MemberData(nameof(OrdinaryLinks))]
    public void An_everyday_link_is_not_a_secret(string url)
    {
        var findings = new RuleScanner().Find("See " + url + " today.", Rules);
        Assert.Empty(findings);
    }

    [Fact]
    public void A_hundred_ordinary_links_have_no_findings()
    {
        var urls = OrdinaryCorpus().ToList();
        Assert.True(urls.Count >= 100, "corpus has " + urls.Count);
        var scanner = new RuleScanner();
        var flagged = new List<string>();
        foreach (var url in urls)
        {
            var findings = scanner.Find(url, Rules);
            if (findings.Count > 0)
                flagged.Add(url + " -> " + string.Join(", ", findings.Select(finding => finding.RuleId)));
        }

        Assert.True(flagged.Count == 0, string.Join(Environment.NewLine, flagged));
    }

    [Fact]
    public void A_zoom_password_is_only_the_parameter_value()
    {
        const string password = "Q3n8mK2pL9xR4vB7cZ1tY6wH0sD5fA8b";
        const string url = "https://us05web.zoom.us/j/5551234567?pwd=" + password;
        var text = "Join " + url + " at noon.";
        var finding = Assert.Single(new RuleScanner().Find(text, Rules));
        Assert.StartsWith("secret.", finding.RuleId, StringComparison.Ordinal);
        Assert.Equal(password, text.Substring(finding.Start, finding.Length));
        var redacted = TextRedactor.Apply(text, [finding]);
        Assert.Contains("https://us05web.zoom.us/j/5551234567?pwd=", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(password, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_presigned_url_flags_the_key_and_the_signature_only()
    {
        const string key = "AKIAIOSFODNN7EXAMPLE";
        const string signature = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const string url =
            "https://bucket.s3.amazonaws.com/doc.pdf?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential="
            + key
            + "%2F20261001%2Fus-east-1%2Fs3%2Faws4_request&X-Amz-Date=20261001T120000Z&X-Amz-Signature="
            + signature;
        var text = "Download " + url;
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, finding => text.Substring(finding.Start, finding.Length) == key);
        Assert.Contains(findings, finding => text.Substring(finding.Start, finding.Length) == signature);
        Assert.All(findings, finding =>
        {
            var value = text.Substring(finding.Start, finding.Length);
            Assert.DoesNotContain("amazonaws.com", value, StringComparison.Ordinal);
            Assert.DoesNotContain("X-Amz-", value, StringComparison.Ordinal);
            Assert.True(value == key || value == signature, value);
        });
        var redacted = TextRedactor.Apply(text, findings);
        Assert.Contains("https://bucket.s3.amazonaws.com/doc.pdf?", redacted, StringComparison.Ordinal);
        Assert.Contains("X-Amz-Credential=", redacted, StringComparison.Ordinal);
        Assert.Contains("X-Amz-Signature=", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(key, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(signature, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reset_link_redacts_only_the_token_value()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";
        const string url = "https://example.com/reset?token=" + jwt + "&next=/home";
        var text = "Reset " + url;
        var findings = new RuleScanner().Find(text, Rules);
        Assert.Contains(findings, finding => text.Substring(finding.Start, finding.Length) == jwt);
        Assert.All(findings, finding =>
            Assert.Equal(jwt, text.Substring(finding.Start, finding.Length)));
        var (redacted, _) = ReversibleRedactor.Apply(text, findings, reversible: true);
        Assert.Contains("https://example.com/reset?token=", redacted, StringComparison.Ordinal);
        Assert.Contains("&next=/home", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(jwt, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_api_key_query_value_is_the_only_finding()
    {
        const string key = "sk-proj-abcdefghijklmnopqrstuvwxyz012345";
        const string url = "https://api.example.com/v1/items?api_key=" + key + "&limit=10";
        var text = "Call " + url;
        var finding = Assert.Single(new RuleScanner().Find(text, Rules));
        Assert.Equal(key, text.Substring(finding.Start, finding.Length));
        Assert.StartsWith("secret.", finding.RuleId, StringComparison.Ordinal);
        var redacted = TextRedactor.Apply(text, [finding]);
        Assert.Contains("https://api.example.com/v1/items?api_key=", redacted, StringComparison.Ordinal);
        Assert.Contains("&limit=10", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(key, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_outside_a_link_is_still_flagged()
    {
        const string secret = "q7W9mK2pL8xN4vB6cZ1rT5yH0sD3fG7aQ2";
        var findings = new RuleScanner().Find("token " + secret + " end", Rules);
        Assert.Contains(findings, finding => finding.RuleId == "secret.entropy");
    }

    private static IEnumerable<string> OrdinaryCorpus()
    {
        var n = 0;
        string Id() => Mix(n++);
        string[] hosts =
        [
            "https://grok.com/share/",
            "https://grok.com/chat/",
            "https://x.com/i/grok/share/",
            "https://chatgpt.com/share/",
            "https://claude.ai/share/",
            "https://docs.google.com/document/d/",
            "https://drive.google.com/file/d/",
            "https://contoso.sharepoint.com/:w:/g/personal/ada_contoso_com/",
            "https://contoso-my.sharepoint.com/:b:/g/personal/ada_contoso_com/",
            "https://outlook.office365.com/mail/id/",
            "https://www.dropbox.com/s/",
            "https://www.dropbox.com/scl/fi/",
            "https://app.box.com/s/",
            "https://www.notion.so/acme/",
            "https://www.figma.com/file/",
            "https://www.canva.com/design/",
            "https://www.youtube.com/watch?v=",
            "https://youtu.be/",
            "https://www.bbc.com/news/",
            "https://www.reuters.com/world/",
            "https://www.theguardian.com/technology/",
            "https://www.amazon.com/dp/",
            "https://www.amazon.co.uk/dp/",
            "https://www.ebay.com/itm/",
            "https://www.etsy.com/listing/",
            "https://www.google.com/maps/place/",
            "https://maps.apple.com/?address=",
            "https://github.com/dotnet/runtime/commit/",
            "https://gitlab.com/example/app/-/commit/",
            "https://open.spotify.com/track/",
            "https://www.linkedin.com/posts/ada_activity-",
            "https://teams.microsoft.com/l/meetup-join/",
            "https://calendar.google.com/calendar/event?eid=",
            "https://mail.google.com/mail/u/0/#inbox/"
        ];
        foreach (var host in hosts)
        {
            yield return host + Id();
            yield return host + Id() + "?utm_source=newsletter&utm_medium=email&utm_campaign=" + Id();
            yield return host + Id() + "?fbclid=" + Id() + "&gclid=" + Id() + "&msclkid=" + Id();
        }

        yield return "https://news.example.com/2026/10/01/city-council-notes?utm_source=share&utm_content=" + Id();
        yield return "https://shop.example.com/products/linen-shirt?variant=" + Id() + "&utm_source=ig";
        yield return "https://example.com/landing?code=" + Id();
    }

    private static string Mix(int seed)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var chars = new char[36];
        var state = seed * 1103515245 + 12345;
        for (var i = 0; i < chars.Length; i++)
        {
            state = state * 1664525 + 1013904223;
            chars[i] = alphabet[(state >>> 16) & 61];
        }

        return new string(chars);
    }
}
