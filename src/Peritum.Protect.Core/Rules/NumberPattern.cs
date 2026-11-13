using System.Text.RegularExpressions;

namespace Peritum.Protect.Core.Rules;

public sealed class NumberPattern
{
    public const string NeutralSample =
        "The meeting is on 12/05/2024 and again in 2025. Call 2024. Room 14. See page 12. " +
        "Invoice 100. Year 1999 was quiet. The code 1234 appears. Date 01.02.2024. " +
        "A plain year is 2019. Another date is 2024-01-02. " +
        "Single digits are 1 2 3 4 5 6 7 8 9. Short numbers are 7 14 42 and 100. " +
        "The clock shows 14:30 and 09:05. Prices are 12.50 and 9.99. Nothing else is numbered.";

    public string Pattern { get; init; } = "";
    public string Words { get; init; } = "";
    public bool TooBroad { get; init; }
    public bool Refused { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Samples { get; init; } = [];

    public static NumberPattern FromExamples(IReadOnlyList<string> examples)
    {
        var rows = examples.Select(example => example.Trim()).Where(example => example.Length > 0).ToList();
        if (rows.Count is < 2 or > 5)
            return Fail("Type 2 to 5 examples.");
        var tokenized = rows.Select(Tokenize).ToList();
        if (tokenized.Any(row => row.Count == 0) || tokenized.Select(row => row.Count).Distinct().Count() != 1)
            return Fail("These examples do not share one shape.");

        var parts = new List<string>();
        var words = new List<string>();
        var fixedLength = 0;
        var digitSlots = 0;
        var fixedLetters = false;
        var separators = false;
        var width = tokenized[0].Count;
        for (var column = 0; column < width; column++)
        {
            var cells = tokenized.Select(row => row[column]).ToList();
            if (cells.Select(cell => cell.Kind).Distinct().Count() != 1)
                return Fail("These examples do not share one shape.");
            var kind = cells[0].Kind;
            if (kind == "sep")
            {
                if (cells.Select(cell => cell.Text).Distinct().Count() != 1)
                    return Fail("These examples do not share one shape.");
                parts.Add(Regex.Escape(cells[0].Text));
                words.Add(WordFor(cells[0].Text));
                separators = true;
                fixedLength += cells[0].Text.Length;
                continue;
            }

            var lengths = cells.Select(cell => cell.Text.Length).ToList();
            var min = lengths.Min();
            var max = lengths.Max();
            var same = cells.Select(cell => cell.Text).Distinct(StringComparer.Ordinal).Count() == 1;
            if (same && kind == "letters")
            {
                parts.Add(Regex.Escape(cells[0].Text));
                words.Add(cells[0].Text);
                fixedLetters = true;
                fixedLength += cells[0].Text.Length;
                continue;
            }

            var atom = kind == "digits" ? "\\d" : "\\p{L}";
            parts.Add(min == max ? atom + "{" + min + "}" : atom + "{" + min + "," + max + "}");
            words.Add(CountWords(min, max, kind == "digits" ? "digit" : "letter", kind == "digits" ? "digits" : "letters"));
            fixedLength += min;
            if (kind == "digits")
                digitSlots += max;
        }

        var pattern = "\\b" + string.Join("", parts) + "\\b";
        var broad = fixedLength < 3 || (!fixedLetters && !separators && digitSlots < 6);
        return Finish(pattern, string.Join(", ", words), rows, broad);
    }

    public static NumberPattern FromRegex(string pattern)
    {
        var text = pattern.Trim();
        if (text.Length == 0)
            return Fail("Type a pattern.");
        return Finish(text, "Custom pattern", [], structurallyBroad: false);
    }

    public IReadOnlyList<string> Hits(string sample)
    {
        if (Refused || Error is not null || string.IsNullOrEmpty(Pattern))
            return [];
        try
        {
            return new Regex(Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200))
                .Matches(sample)
                .Select(match => match.Value)
                .ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    private static NumberPattern Finish(string pattern, string words, IReadOnlyList<string> examples, bool structurallyBroad)
    {
        Regex compiled;
        try
        {
            compiled = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return Fail("That pattern is not valid.");
        }

        var probes = new[] { "2024", "1999", "1234", "12/05/2024", "01.02.2024", "2024-01-02" };
        var tooBroad = probes.Any(probe => compiled.IsMatch(probe));
        var hits = 0;
        try
        {
            hits = compiled.Matches(NeutralSample).Count;
        }
        catch (RegexMatchTimeoutException)
        {
            return Fail("That pattern took too long.");
        }

        var tokens = NeutralSample.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        var refused = structurallyBroad || (tokens > 0 && hits / (double)tokens > 0.05);
        return new NumberPattern
        {
            Pattern = pattern,
            Words = words,
            TooBroad = tooBroad || refused,
            Refused = refused,
            Samples = examples,
            Error = refused ? "This pattern matches too much ordinary text." : null
        };
    }

    private static NumberPattern Fail(string error) => new() { Error = error, Refused = true };

    private static List<Piece> Tokenize(string example)
    {
        var pieces = new List<Piece>();
        var i = 0;
        while (i < example.Length)
        {
            var kind = KindOf(example[i]);
            var start = i;
            while (i < example.Length && KindOf(example[i]) == kind)
                i++;
            pieces.Add(new Piece(kind, example[start..i]));
        }

        return pieces;
    }

    private static string KindOf(char c)
    {
        if (char.IsDigit(c))
            return "digits";
        if (char.IsLetter(c))
            return "letters";
        return "sep";
    }

    private static string CountWords(int min, int max, string singular, string plural)
    {
        if (min == max)
            return min + " " + (min == 1 ? singular : plural);
        return min + " to " + max + " " + plural;
    }

    private static string WordFor(string separator) => separator switch
    {
        "-" => "dash",
        "/" => "slash",
        "." => "dot",
        "_" => "underscore",
        ":" => "colon",
        _ => separator
    };

    private readonly record struct Piece(string Kind, string Text);
}
