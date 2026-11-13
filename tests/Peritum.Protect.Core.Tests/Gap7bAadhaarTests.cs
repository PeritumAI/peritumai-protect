using System.Reflection;
using Peritum.Protect.Core.Rules;
using Peritum.Protect.Core.Scanning;

namespace Peritum.Protect.Core.Tests;

public class Gap7bAadhaarTests
{
    private const string Published = "234123412346";

    [Fact]
    public void Published_verhoeff_values_are_aadhaar_and_errors_are_rejected()
    {
        Assert.Equal(3, IndependentCheckDigit("236"));
        Assert.Equal("2363", "236" + IndependentCheckDigit("236"));
        Assert.True(InvokeVerhoeff("2363"));
        Assert.True(IndependentValid("2363"));
        Assert.True(IndependentValid(Published));

        Assert.True(NationalId.ValidAadhaar(Published));
        Assert.Equal("in.aadhaar", Hit("Aadhaar " + Published));

        for (var i = 0; i < Published.Length; i++)
        {
            for (var digit = '0'; digit <= '9'; digit++)
            {
                if (digit == Published[i])
                    continue;
                var altered = Published[..i] + digit + Published[(i + 1)..];
                Assert.False(NationalId.ValidAadhaar(altered), altered);
                Assert.Null(Hit(altered));
            }
        }

        for (var i = 0; i < Published.Length - 1; i++)
        {
            if (Published[i] == Published[i + 1])
                continue;
            var swapped = Published[..i] + Published[i + 1] + Published[i] + Published[(i + 2)..];
            Assert.False(NationalId.ValidAadhaar(swapped), swapped);
            Assert.Null(Hit(swapped));
        }
    }

    private static string? Hit(string text)
    {
        var rule = BuiltinRules.LoadEmbedded().SelectMany(pack => pack.Rules).Single(item => item.Id == "in.aadhaar");
        var findings = new RuleScanner().Find("ID " + text + " end", [rule]);
        return findings.Count == 0 ? null : findings[0].RuleId;
    }

    private static bool InvokeVerhoeff(string value)
    {
        var method = typeof(NationalId).GetMethod("Verhoeff", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (bool)method!.Invoke(null, [value])!;
    }

    /// <summary>
    /// Published Verhoeff tables. Permutation rows 6 and 7 are the standard rows.
    /// Does not call <see cref="NationalId"/>.
    /// </summary>
    private static bool IndependentValid(string value)
    {
        int[,] d =
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 1, 2, 3, 4, 0, 6, 7, 8, 9, 5 },
            { 2, 3, 4, 0, 1, 7, 8, 9, 5, 6 },
            { 3, 4, 0, 1, 2, 8, 9, 5, 6, 7 },
            { 4, 0, 1, 2, 3, 9, 5, 6, 7, 8 },
            { 5, 9, 8, 7, 6, 0, 4, 3, 2, 1 },
            { 6, 5, 9, 8, 7, 1, 0, 4, 3, 2 },
            { 7, 6, 5, 9, 8, 2, 1, 0, 4, 3 },
            { 8, 7, 6, 5, 9, 3, 2, 1, 0, 4 },
            { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 }
        };
        int[,] p =
        {
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            { 1, 5, 7, 6, 2, 8, 3, 0, 9, 4 },
            { 5, 8, 0, 3, 7, 9, 6, 1, 4, 2 },
            { 8, 9, 1, 6, 0, 4, 3, 5, 2, 7 },
            { 9, 4, 5, 3, 1, 2, 6, 8, 7, 0 },
            { 4, 2, 8, 6, 5, 7, 3, 9, 0, 1 },
            { 2, 7, 9, 3, 8, 0, 6, 4, 1, 5 },
            { 7, 0, 4, 6, 9, 1, 3, 2, 5, 8 }
        };
        var checksum = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var digit = value[value.Length - 1 - i] - '0';
            checksum = d[checksum, p[i % 8, digit]];
        }
        return checksum == 0;
    }

    private static int IndependentCheckDigit(string body)
    {
        for (var digit = 0; digit <= 9; digit++)
        {
            if (IndependentValid(body + digit))
                return digit;
        }
        throw new InvalidOperationException("no check digit");
    }
}
