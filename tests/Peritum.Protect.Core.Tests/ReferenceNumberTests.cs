using Peritum.Protect.Core.Config;
using Peritum.Protect.Core.Profiles;
using Peritum.Protect.Core.Redaction;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class ReferenceNumberTests
{
    private static readonly string[] Labels =
    [
        "Case No.", "Case number", "Matter", "Matter no.", "Our ref", "Your ref", "Ref.",
        "File no.", "Docket", "Claim no.", "Account no.", "Client no.", "Patient ID", "MRN",
        "Employee ID", "Staff no."
    ];

    [Theory]
    [InlineData("Case No. 1:24-cv-01234", "1:24-cv-01234")]
    [InlineData("Case number C-123/24", "C-123/24")]
    [InlineData("Matter no. ACME/LIT/17", "ACME/LIT/17")]
    [InlineData("Our ref: 2024-0142", "2024-0142")]
    public void A_label_flags_only_the_identifier(string text, string identifier)
    {
        var rule = LabelAnchor.RuleFor("ref.labels", "reference number", "reference numbers", "REF", Labels);
        var hits = new RuleScanner().Find(text, [rule]);
        var hit = Assert.Single(hits);
        Assert.Equal(identifier, text.Substring(hit.Start, hit.Length));
        Assert.DoesNotContain("Case", text.Substring(hit.Start, hit.Length), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Case No. 1:24-cv-01234 was filed.", "1:24-cv-01234")]
    [InlineData("Your ref C-123/24 refers.", "C-123/24")]
    [InlineData("Matter: 2024-0142 (billing)", "2024-0142")]
    [InlineData("Docket #23-5678", "23-5678")]
    [InlineData("Claim no. KB-2024-000123", "KB-2024-000123")]
    [InlineData("Our ref: 2024-0142, next step.", "2024-0142")]
    [InlineData("See (Docket #23-5678) today.", "23-5678")]
    public void A_label_matches_one_identifier_token(string text, string identifier)
    {
        var rule = LabelAnchor.RuleFor("ref.labels", "reference number", "reference numbers", "REF", Labels);
        var hit = Assert.Single(new RuleScanner().Find(text, [rule]));
        Assert.Equal(identifier, text.Substring(hit.Start, hit.Length));
        var redacted = TextRedactor.Apply(text, [hit]);
        Assert.Contains("[REF]", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(identifier, redacted, StringComparison.Ordinal);
        Assert.Contains(text.Replace(identifier, "[REF]", StringComparison.Ordinal), redacted, StringComparison.Ordinal);
        var (reversible, map) = ReversibleRedactor.Apply(text, [hit], reversible: true);
        Assert.Equal(identifier, map.Tokens.Values.Single());
        Assert.DoesNotContain(identifier, reversible, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("The matter was settled in 2024.")]
    [InlineData("Case No. pending")]
    [InlineData("Our ref is below")]
    [InlineData("Docket 2 O 123/24")]
    public void Plain_words_after_a_label_do_not_match(string text)
    {
        var rule = LabelAnchor.RuleFor("ref.labels", "reference number", "reference numbers", "REF", Labels);
        Assert.Empty(new RuleScanner().Find(text, [rule]));
    }

    [Fact]
    public void A_label_without_an_identifier_does_not_match()
    {
        var rule = LabelAnchor.RuleFor("ref.labels", "reference number", "reference numbers", "REF", Labels);
        Assert.Empty(new RuleScanner().Find("Our ref is below", [rule]));
        Assert.Empty(new RuleScanner().Find("1:24-cv-01234 is on the page", [rule]));
    }

    [Fact]
    public void Invoice_and_policy_labels_are_separate_from_the_general_list()
    {
        var general = LabelAnchor.RuleFor("ref.labels", "reference number", "reference numbers", "REF", Labels);
        var finance = LabelAnchor.RuleFor("ref.finance", "invoice or policy number", "invoice or policy numbers", "REF", ["Invoice no.", "Policy no."]);
        Assert.Empty(new RuleScanner().Find("Invoice no. 2024-0142", [general]));
        var hit = Assert.Single(new RuleScanner().Find("Invoice no. 2024-0142", [finance]));
        Assert.Equal("2024-0142", "Invoice no. 2024-0142".Substring(hit.Start, hit.Length));
        var accountant = ProfileCatalog.LoadEmbedded().Single(profile => profile.Id == "accountant");
        Assert.Contains("ref.finance", accountant.EnabledRuleIds);
        Assert.DoesNotContain("ref.finance", ProfileCatalog.LoadEmbedded().Single(profile => profile.Id == "lawyer").EnabledRuleIds);
    }

    [Fact]
    public void Examples_become_a_pattern_in_plain_words()
    {
        var derived = NumberPattern.FromExamples(["ACME-2024-0142", "ACME-2025-0007"]);
        Assert.Null(derived.Error);
        Assert.Equal(@"\bACME-\d{4}-\d{4}\b", derived.Pattern);
        Assert.Equal("ACME, dash, 4 digits, dash, 4 digits", derived.Words);
        Assert.False(derived.TooBroad);
        Assert.Contains("ACME-2024-0142", derived.Hits("See ACME-2024-0142 and ACME-2025-0007."));
    }

    [Fact]
    public void Variable_lengths_and_mixed_separators_stay_in_the_pattern()
    {
        var derived = NumberPattern.FromExamples(["AB-12/7", "ABC-9/88"]);
        Assert.Null(derived.Error);
        Assert.Equal(@"\b\p{L}{2,3}-\d{1,2}/\d{1,2}\b", derived.Pattern);
        Assert.Contains("slash", derived.Words, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pattern_that_matches_ordinary_numbers_is_refused()
    {
        var derived = NumberPattern.FromExamples(["2024", "1999"]);
        Assert.True(derived.TooBroad);
        Assert.True(derived.Refused);
        Assert.Equal("This pattern matches too much ordinary text.", derived.Error);
    }

    [Theory]
    [InlineData(new[] { "1", "2" }, "1 digit")]
    [InlineData(new[] { "7", "42" }, "1 to 2 digits")]
    [InlineData(new[] { "A1", "B2" }, "1 letter, 1 digit")]
    public void A_pattern_without_a_fixed_shape_is_refused(string[] examples, string words)
    {
        var derived = NumberPattern.FromExamples(examples);
        Assert.Equal(words, derived.Words);
        Assert.True(derived.Refused);
        Assert.Equal("This pattern matches too much ordinary text.", derived.Error);
    }

    [Fact]
    public void A_fixed_format_is_accepted_and_reports_its_hits()
    {
        var matter = NumberPattern.FromExamples(["ACME-2024-0142", "ACME-2025-0007"]);
        Assert.False(matter.Refused);
        Assert.Null(matter.Error);
        Assert.Equal("ACME, dash, 4 digits, dash, 4 digits", matter.Words);
        var matterHits = matter.Hits("See ACME-2024-0142 and ACME-2025-0007.");
        Assert.Contains("ACME-2024-0142", matterHits);
        Assert.Contains("ACME-2025-0007", matterHits);

        var file = NumberPattern.FromExamples(["LIT/17", "LIT/1234"]);
        Assert.False(file.Refused);
        Assert.Null(file.Error);
        Assert.Equal("LIT, slash, 2 to 4 digits", file.Words);
        var fileHits = file.Hits("See LIT/17 and LIT/1234.");
        Assert.Contains("LIT/17", fileHits);
        Assert.Contains("LIT/1234", fileHits);
    }

    [Fact]
    public void The_neutral_sample_includes_ordinary_numbers_times_and_prices()
    {
        var sample = NumberPattern.NeutralSample;
        Assert.Contains(" 1 ", " " + sample + " ", StringComparison.Ordinal);
        Assert.Contains("14:30", sample, StringComparison.Ordinal);
        Assert.Contains("12.50", sample, StringComparison.Ordinal);
        var tokens = sample.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        var hits = new System.Text.RegularExpressions.Regex(@"\b\d{1}\b").Matches(sample).Count;
        Assert.True(hits / (double)tokens > 0.05);
    }

    [Fact]
    public void An_edited_pattern_is_rejected_when_it_is_not_valid()
    {
        var derived = NumberPattern.FromRegex("(");
        Assert.Equal("That pattern is not valid.", derived.Error);
    }

    [Fact]
    public void A_taught_format_round_trips_through_my_terms_and_export()
    {
        var derived = NumberPattern.FromExamples(["MAT-2024-0142", "MAT-2025-0007"]);
        var dir = Path.Combine(Path.GetTempPath(), "peritum-ref-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "terms.json");
            UserDictionary.Save(path, [new UserTerm { Label = "Matter", Kind = "regex", Text = derived.Pattern, Category = "personal", Reversible = true }]);
            var loaded = UserDictionary.Load(path);
            Assert.Equal(derived.Pattern, loaded[0].Text);
            Assert.Equal("personal", loaded[0].Category);
            var json = RuleExchange.Export([], loaded);
            var read = RuleExchange.Read(json);
            Assert.True(read.Accepted, read.Message);
            Assert.Equal(derived.Pattern, read.Terms[0].Text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void A_policy_can_provide_a_number_format()
    {
        var dir = Path.Combine(Path.GetTempPath(), "peritum-policy-ref-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "policy.json");
            File.WriteAllText(path, """{"numberFormats":[{"name":"Matter","pattern":"\\bACME-\\d{4}-\\d{4}\\b","category":"personal","reversible":true}]}""");
            var result = PolicyFile.Read(path, registry: null);
            Assert.Null(result.Warning);
            var format = Assert.Single(result.Policy.NumberFormats);
            var hit = Assert.Single(new RuleScanner().Find("File ACME-2024-0142", [format.ToRule()]));
            Assert.Equal("ACME-2024-0142", "File ACME-2024-0142".Substring(hit.Start, hit.Length));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Court_formats_are_off_until_turned_on_and_match_the_documented_shape()
    {
        var packs = BuiltinRules.LoadEmbedded();
        var active = RulePackLoader.SelectActive(packs, [], []);
        Assert.DoesNotContain(active, rule => rule.Id.StartsWith("court.", StringComparison.Ordinal));
        var federal = packs.SelectMany(pack => pack.Rules).Single(rule => rule.Id == "court.us.federal");
        var europe = packs.SelectMany(pack => pack.Rules).Single(rule => rule.Id == "court.eu.cjeu");
        Assert.Contains("pacer.uscourts.gov", federal.Notes, StringComparison.Ordinal);
        Assert.Contains("curia.europa.eu", europe.Notes, StringComparison.Ordinal);
        var on = RulePackLoader.SelectActive(packs, [federal.Id, europe.Id], []);
        Assert.Single(new RuleScanner().Find("See 1:05-cv-06794 today", on), hit => hit.RuleId == federal.Id);
        Assert.Single(new RuleScanner().Find("Judgment in C-250/25", on), hit => hit.RuleId == europe.Id);
        Assert.Empty(new RuleScanner().Find("Room 1234", on.Where(rule => rule.Id == federal.Id).ToList()));
    }
}
