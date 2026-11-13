using System.Text.RegularExpressions;
using Peritum.Protect.Core.Files;
using Peritum.Protect.Core.Links;
using Peritum.Protect.Core.Rules;

namespace Peritum.Protect.Core.Scanning;

public sealed class RuleScanner : IScanner
{
    public const int MaxFindings = 2000;
    private readonly FileAdapterRouter _files;
    private readonly string _region;

    public RuleScanner() : this(new FileAdapterRouter(), null)
    {
    }

    public RuleScanner(string region) : this(new FileAdapterRouter(), region)
    {
    }

    public RuleScanner(FileAdapterRouter files) : this(files, null)
    {
    }

    public RuleScanner(FileAdapterRouter files, string? region)
    {
        _files = files;
        _region = CountryRegions.Canonical(string.IsNullOrEmpty(region)
            ? System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName
            : region);
    }

    public ScanResult Scan(ScanRequest request, IReadOnlyList<Rule> rules)
    {
        if (!string.IsNullOrEmpty(request.FilePath))
        {
            var read = _files.Read(request.FilePath);
            var name = Path.GetFileName(request.FilePath);
            if (read.Error is not null)
                return ScanResult.Failed(request.Source, name, read.Error, read.ReasonCode);
            var text = Trim(read.Text, out var truncated);
            var match = MatchSegments(text, read.Segments, rules, limit: true);
            return new ScanResult
            {
                Findings = match.Findings,
                ContentHash = Hashing.Sha256Text(text),
                Source = request.Source,
                DisplayName = name,
                Truncated = truncated || read.Text.Length > text.Length,
                Incomplete = truncated || read.Text.Length > text.Length || match.Incomplete,
                FailedRuleIds = match.FailedRuleIds
            };
        }

        var body = request.Text ?? "";
        var scanned = Trim(body, out var cut);
        var textMatch = Match(scanned, rules, limit: true, header: "", csv: false);
        var place = request.Source == ScanSource.Clipboard ? SegmentPlace.Clipboard() : SegmentPlace.Text();
        var located = FindingPlaces.Apply(scanned, [new TextSegment { Start = 0, Text = scanned, Place = place }], textMatch.Findings);
        return new ScanResult
        {
            Findings = located,
            ContentHash = Hashing.Sha256Text(scanned),
            Source = request.Source,
            DisplayName = request.Source == ScanSource.Clipboard ? "clipboard" : null,
            Truncated = cut,
            Incomplete = cut || textMatch.Incomplete,
            FailedRuleIds = textMatch.FailedRuleIds
        };
    }

    public IReadOnlyList<Finding> Find(string text, IReadOnlyList<Rule> rules) =>
        Match(text, rules, limit: true, header: "", csv: false).Findings;

    public IReadOnlyList<Finding> FindAll(string text, IReadOnlyList<Rule> rules) =>
        Match(text, rules, limit: false, header: "", csv: false).Findings;

    public MatchResult Match(string text, IReadOnlyList<Rule> rules, bool limit) =>
        Match(text, rules, limit, header: "", csv: false);

    private MatchResult Match(string text, IReadOnlyList<Rule> rules, bool limit, string header, bool csv)
    {
        if (string.IsNullOrEmpty(text) || rules.Count == 0)
            return new MatchResult([], [], false);
        var raw = new List<RawHit>();
        var failed = new List<string>();
        foreach (var rule in rules)
        {
            try
            {
                Collect(text, rule, raw, limit, header, csv);
            }
            catch (RegexMatchTimeoutException)
            {
                failed.Add(rule.Id);
            }
            catch (ArgumentException)
            {
                failed.Add(rule.Id);
            }
            if (limit && raw.Count > MaxFindings * 2)
                break;
        }

        AdjustUrls(text, raw, rules);
        var findings = Resolve(text, raw, limit ? MaxFindings : null, header, csv, _region);
        var capped = limit && findings.Count >= MaxFindings && raw.Count > findings.Count;
        return new MatchResult(findings, failed, failed.Count > 0 || capped);
    }

    public IReadOnlyList<Finding> FindSegments(string joined, IReadOnlyList<TextSegment> segments, IReadOnlyList<Rule> rules) =>
        MatchSegments(joined, segments, rules, limit: true).Findings;

    public IReadOnlyList<Finding> FindSegmentsAll(string joined, IReadOnlyList<TextSegment> segments, IReadOnlyList<Rule> rules) =>
        MatchSegments(joined, segments, rules, limit: false).Findings;

    public MatchResult MatchSegments(string joined, IReadOnlyList<TextSegment> segments, IReadOnlyList<Rule> rules, bool limit)
    {
        if (segments.Count == 0)
        {
            var plain = Match(joined, rules, limit, header: "", csv: false);
            var placed = FindingPlaces.Apply(
                joined,
                [new TextSegment { Start = 0, Text = joined, Place = SegmentPlace.Text() }],
                plain.Findings);
            return new MatchResult(placed, plain.FailedRuleIds, plain.Incomplete);
        }

        var all = new List<Finding>();
        var failed = new List<string>();
        var incomplete = false;
        foreach (var segment in segments)
        {
            if (segment.Text.Length == 0)
                continue;
            var csv = string.Equals(segment.Place?.Kind, "csv", StringComparison.Ordinal);
            var match = Match(segment.Text, rules, limit: false, segment.Header, csv);
            foreach (var finding in match.Findings)
                all.Add(finding.Shift(segment.Start));
            foreach (var id in match.FailedRuleIds)
            {
                if (!failed.Contains(id, StringComparer.Ordinal))
                    failed.Add(id);
            }
            incomplete |= match.Incomplete;
            if (limit && all.Count >= MaxFindings)
            {
                incomplete = true;
                break;
            }
        }
        if (limit && all.Count > MaxFindings)
        {
            all = all.Take(MaxFindings).ToList();
            incomplete = true;
        }
        all = FindingPlaces.Apply(joined, segments, all).ToList();
        return new MatchResult(all, failed, incomplete || failed.Count > 0);
    }

    public static IReadOnlyList<Finding> ExceptValues(string text, IReadOnlyList<Finding> findings, IReadOnlyCollection<string>? blocked)
    {
        if (blocked is null || blocked.Count == 0)
            return findings;
        var set = blocked as IReadOnlySet<string> ?? new HashSet<string>(blocked, StringComparer.Ordinal);
        var kept = new List<Finding>(findings.Count);
        foreach (var finding in findings)
        {
            if (finding.Start < 0 || finding.Length <= 0 || finding.Start + finding.Length > text.Length)
                continue;
            var value = text.Substring(finding.Start, finding.Length);
            if (!set.Contains(value))
                kept.Add(finding);
        }
        return kept;
    }

    private static string Trim(string text, out bool truncated)
    {
        const int max = 2_000_000;
        if (text.Length <= max)
        {
            truncated = false;
            return text;
        }
        truncated = true;
        return text[..max];
    }

    private static void Collect(string text, Rule rule, List<RawHit> raw, bool limit, string header, bool csv)
    {
        switch (rule.Kind)
        {
            case "regex" when rule.Id == "pii.email":
                CollectEmail(text, rule, raw);
                break;
            case "regex":
                if (!string.IsNullOrEmpty(rule.Pattern))
                    CollectRegex(text, rule, rule.Pattern, raw, literal: false, limit);
                break;
            case "phone":
                CollectPhone(text, rule, raw);
                break;
            case "jmbg":
            case "checksum":
                CollectNational(text, rule, raw, header, csv);
                break;
            case "keyword":
                foreach (var word in PatternsOf(rule))
                    CollectRegex(text, rule, @"(?<![\w])" + Regex.Escape(word) + @"(?![\w])", raw, literal: true, limit);
                break;
            case "exact":
                CollectExact(text, rule, raw);
                break;
            case "prefix":
                CollectPrefix(text, rule, raw);
                break;
            case "luhn":
                CollectLuhn(text, rule, raw);
                break;
            case "iban":
                CollectIban(text, rule, raw);
                break;
            case "entropy":
                if (text.Length > ScanCoverage.EntropyGiveUpLength)
                    throw new RegexMatchTimeoutException();
                CollectEntropy(text, rule, raw, limit);
                break;
            case "pem-block":
                CollectPem(text, rule, raw);
                break;
        }
    }

    private static IEnumerable<string> PatternsOf(Rule rule)
    {
        if (rule.Patterns.Count > 0)
            return rule.Patterns;
        if (!string.IsNullOrEmpty(rule.Pattern))
            return [rule.Pattern];
        return [];
    }

    private static Regex CreateRegex(string pattern, RegexOptions options)
    {
        try
        {
            return new Regex(pattern, options | RegexOptions.NonBacktracking);
        }
        catch (NotSupportedException)
        {
            return new Regex(pattern, options | RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> RegexCache = new();

    private static void CollectRegex(string text, Rule rule, string pattern, List<RawHit> raw, bool literal, bool limit)
    {
        var options = RegexOptions.CultureInvariant;
        if (rule.IgnoreCase)
            options |= RegexOptions.IgnoreCase;
        var key = pattern + "|" + options;
        var regex = RegexCache.GetOrAdd(key, _ => CreateRegex(pattern, options));
        CollectMatches(regex, text, 0, rule, raw, limit);
    }

    private static void CollectMatches(Regex regex, string text, int offset, Rule rule, List<RawHit> raw, bool limit)
    {
        foreach (Match match in regex.Matches(text))
        {
            if (!match.Success || match.Length == 0)
                continue;
            var hit = match.Groups["value"].Success ? match.Groups["value"] : match;
            if (hit.Length == 0)
                continue;
            if (rule.MinLength is int min && hit.Length < min)
                continue;
            raw.Add(new RawHit(offset + hit.Index, hit.Length, rule));
            if (limit && raw.Count > MaxFindings * 2)
                return;
        }
    }

    private static void CollectExact(string text, Rule rule, List<RawHit> raw)
    {
        var comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var needle in PatternsOf(rule))
        {
            if (string.IsNullOrEmpty(needle))
                continue;
            var idx = 0;
            while (idx <= text.Length - needle.Length)
            {
                var found = text.IndexOf(needle, idx, comparison);
                if (found < 0)
                    break;
                raw.Add(new RawHit(found, needle.Length, rule));
                idx = found + Math.Max(1, needle.Length);
                if (raw.Count > MaxFindings * 2)
                    return;
            }
        }
    }

    private static void CollectPrefix(string text, Rule rule, List<RawHit> raw)
    {
        var comparison = rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var min = rule.MinLength ?? 1;
        foreach (var prefix in PatternsOf(rule))
        {
            if (string.IsNullOrEmpty(prefix))
                continue;
            var idx = 0;
            while (idx < text.Length)
            {
                var found = text.IndexOf(prefix, idx, comparison);
                if (found < 0)
                    break;
                if (found > 0 && IsKeyChar(text[found - 1]))
                {
                    idx = found + 1;
                    continue;
                }
                var end = found + prefix.Length;
                while (end < text.Length && IsKeyChar(text[end]))
                    end++;
                if (end - found >= min)
                    raw.Add(new RawHit(found, end - found, rule));
                idx = Math.Max(end, found + 1);
                if (raw.Count > MaxFindings * 2)
                    return;
            }
        }
    }

    private static void CollectLuhn(string text, Rule rule, List<RawHit> raw)
    {
        var regex = RegexCache.GetOrAdd(
            "card",
            _ => new Regex(@"\b(?:\d[ -]?){12,18}\d\b", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(750)));
        foreach (Match match in regex.Matches(text))
        {
            if (!ConsistentCardSeparators(match.Value))
                continue;
            var digits = new string(match.Value.Where(char.IsDigit).ToArray());
            if (digits.Length < (rule.MinLength ?? 13) || digits.Length > 19)
                continue;
            if (digits.Distinct().Count() < 2 || !KnownCardPrefix(digits) || !Luhn(digits))
                continue;
            raw.Add(new RawHit(match.Index, match.Length, rule));
        }
    }

    private static void CollectIban(string text, Rule rule, List<RawHit> raw)
    {
        for (var i = 0; i < text.Length - 14; i++)
        {
            if (!char.IsLetter(text[i]) || !char.IsLetter(text[i + 1]) || !char.IsDigit(text[i + 2]) || !char.IsDigit(text[i + 3]))
                continue;
            if (i > 0 && char.IsLetterOrDigit(text[i - 1]))
                continue;

            var compact = new System.Text.StringBuilder();
            var j = i;
            while (j < text.Length && compact.Length < 34)
            {
                var c = text[j];
                if (c == ' ')
                {
                    if (compact.Length >= 15 && IbanMod97(compact.ToString()))
                        break;
                    var k = j + 1;
                    while (k < text.Length && text[k] == ' ')
                        k++;
                    if (k >= text.Length || !char.IsLetterOrDigit(text[k]))
                        break;
                    var t = k;
                    while (t < text.Length && char.IsLetter(text[t]))
                        t++;
                    if (t - k > 4 && compact.Length >= 15)
                        break;
                    j = k;
                    continue;
                }
                if (!char.IsLetterOrDigit(c))
                    break;
                compact.Append(char.ToUpperInvariant(c));
                j++;
            }

            if (compact.Length >= 15 && IbanMod97(compact.ToString()))
            {
                raw.Add(new RawHit(i, j - i, rule));
                i = j - 1;
            }
        }
    }

    private static void CollectEntropy(string text, Rule rule, List<RawHit> raw, bool limit)
    {
        var min = rule.MinLength ?? 32;
        var bits = rule.EntropyBits ?? 4.0;
        foreach (var (start, length) in EntropyScanner.Find(text, min, bits, limit ? 40 : int.MaxValue))
            raw.Add(new RawHit(start, length, rule));
    }

    private static void CollectPem(string text, Rule rule, List<RawHit> raw)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        foreach (var marker in PatternsOf(rule))
        {
            if (string.IsNullOrEmpty(marker))
                continue;
            var endMarker = marker.Replace("BEGIN", "END", StringComparison.OrdinalIgnoreCase);
            var idx = 0;
            while (idx < text.Length)
            {
                var found = text.IndexOf(marker, idx, comparison);
                if (found < 0)
                    break;
                var start = found;
                while (start > 0 && text[start - 1] == '-')
                    start--;
                var end = found + marker.Length;
                var endAt = text.IndexOf(endMarker, end, comparison);
                if (endAt >= 0)
                {
                    end = endAt + endMarker.Length;
                    while (end < text.Length && text[end] == '-')
                        end++;
                }
                else
                {
                    end = Math.Min(text.Length, end + 400);
                }
                raw.Add(new RawHit(start, Math.Max(1, end - start), rule));
                idx = end;
            }
        }
    }

    private static readonly HashSet<string> SecretQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "access_token", "id_token", "refresh_token", "key", "api_key", "apikey",
        "secret", "client_secret", "password", "pwd", "pass", "sig", "signature",
        "x-amz-signature", "x-amz-credential", "x-amz-security-token", "sv", "auth", "session", "sid"
    };

    private readonly record struct QueryValue(int Start, int Length, bool Secret);

    private static void AdjustUrls(string text, List<RawHit> raw, IReadOnlyList<Rule> rules)
    {
        var urls = LinkInspector.Locate(text);
        if (urls.Count == 0)
            return;
        var values = QueryValues(text, urls);
        for (var i = 0; i < raw.Count; i++)
        {
            var hit = raw[i];
            if (!TryClamp(values, hit.Start, hit.Length, out var length) || length <= 0 || length == hit.Length)
                continue;
            raw[i] = hit with { Length = length };
        }

        raw.RemoveAll(hit => hit.Length <= 0 || (hit.Rule.Id == "secret.entropy" && OverlapsUrl(urls, hit.Start, hit.Length)));

        var entropy = rules.FirstOrDefault(rule => rule.Id == "secret.entropy" && rule.Kind == "entropy");
        if (entropy is null)
            return;
        var min = entropy.MinLength ?? 32;
        foreach (var value in values)
        {
            if (!value.Secret || value.Length < min)
                continue;
            if (EntropyScanner.Shannon(text.AsSpan(value.Start, value.Length)) < 3.0)
                continue;
            if (raw.Any(hit => hit.Start < value.Start + value.Length && value.Start < hit.Start + hit.Length))
                continue;
            raw.Add(new RawHit(value.Start, value.Length, entropy));
        }
    }

    private static List<QueryValue> QueryValues(string text, IReadOnlyList<LinkInspector.UrlRange> urls)
    {
        var list = new List<QueryValue>();
        foreach (var url in urls)
        {
            if (url.Start < 0 || url.Length <= 0 || url.Start + url.Length > text.Length)
                continue;
            var slice = text.AsSpan(url.Start, url.Length);
            var question = slice.IndexOf('?');
            if (question < 0)
                continue;
            var query = slice[(question + 1)..];
            var hash = query.IndexOf('#');
            if (hash >= 0)
                query = query[..hash];
            var parts = new List<(string Name, int Start, int Length)>();
            var index = 0;
            var queryStart = url.Start + question + 1;
            while (index <= query.Length)
            {
                var rest = query[index..];
                var amp = rest.IndexOf('&');
                var part = amp < 0 ? rest : rest[..amp];
                var eq = part.IndexOf('=');
                if (eq > 0 && eq < part.Length - 1)
                {
                    parts.Add((part[..eq].ToString(), queryStart + index + eq + 1, part.Length - eq - 1));
                }

                if (amp < 0)
                    break;
                index += amp + 1;
            }

            var hasState = false;
            foreach (var part in parts)
            {
                if (part.Name.Equals("state", StringComparison.OrdinalIgnoreCase))
                    hasState = true;
            }

            foreach (var part in parts)
                list.Add(new QueryValue(part.Start, part.Length, IsSecretQueryName(part.Name, hasState)));
        }

        return list;
    }

    private static bool IsSecretQueryName(string name, bool hasState)
    {
        if (name.Equals("code", StringComparison.OrdinalIgnoreCase))
            return hasState;
        return SecretQueryNames.Contains(name);
    }

    private static bool TryClamp(List<QueryValue> values, int start, int length, out int clamped)
    {
        clamped = length;
        foreach (var value in values)
        {
            if (start < value.Start || start >= value.Start + value.Length)
                continue;
            var end = Math.Min(start + length, value.Start + value.Length);
            clamped = Math.Max(0, end - start);
            return true;
        }

        return false;
    }

    private static bool OverlapsUrl(IReadOnlyList<LinkInspector.UrlRange> urls, int start, int length)
    {
        var end = start + length;
        foreach (var url in urls)
        {
            if (start < url.Start + url.Length && end > url.Start)
                return true;
        }

        return false;
    }

    private static List<Finding> Resolve(string text, List<RawHit> raw, int? maxFindings, string header, bool csv, string region)
    {
        raw.Sort((a, b) =>
        {
            var severity = b.Rule.Severity.CompareTo(a.Rule.Severity);
            if (severity != 0)
                return severity;
            var length = b.Length.CompareTo(a.Length);
            if (length != 0)
                return length;
            if (IsNational(a.Rule) && IsNational(b.Rule))
            {
                var label = LabelScore(text, b, header, csv).CompareTo(LabelScore(text, a, header, csv));
                if (label != 0)
                    return label;
            }
            var specific = Specificity(b.Rule).CompareTo(Specificity(a.Rule));
            if (specific != 0)
                return specific;
            if (IsNational(a.Rule) && IsNational(b.Rule))
            {
                var regionScore = RegionScore(b.Rule, region).CompareTo(RegionScore(a.Rule, region));
                if (regionScore != 0)
                    return regionScore;
                var strength = Strength(b.Rule).CompareTo(Strength(a.Rule));
                if (strength != 0)
                    return strength;
            }
            return a.Start.CompareTo(b.Start);
        });

        var accepted = new List<RawHit>();
        foreach (var hit in raw)
        {
            var overlaps = false;
            foreach (var kept in accepted)
            {
                if (hit.Start < kept.Start + kept.Length && kept.Start < hit.Start + hit.Length)
                {
                    overlaps = true;
                    break;
                }
            }
            if (overlaps)
                continue;
            accepted.Add(hit);
            if (maxFindings is int max && accepted.Count >= max)
                break;
        }

        accepted.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        var findings = new List<Finding>(accepted.Count);
        foreach (var hit in accepted)
        {
            findings.Add(new Finding
            {
                RuleId = hit.Rule.Id,
                Label = hit.Rule.Label,
                DisplayLabel = hit.Rule.DisplayLabel,
                PluralLabel = hit.Rule.PluralLabel,
                Severity = hit.Rule.Severity,
                Start = hit.Start,
                Length = hit.Length,
                Placeholder = hit.Rule.Placeholder,
                Category = hit.Rule.Category,
                Snippet = Snip(text, hit.Start, hit.Length)
            });
        }
        return findings;
    }

    private static string Snip(string text, int start, int length)
    {
        var slice = text.Substring(start, length).Replace('\r', ' ').Replace('\n', ' ');
        const int max = 48;
        return slice.Length <= max ? slice : slice[..max] + "…";
    }

    private static bool IsKeyChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    private static bool Luhn(string digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alt)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }
            sum += n;
            alt = !alt;
        }
        return sum % 10 == 0;
    }

    private static bool IbanMod97(string compact)
    {
        if (compact.Length is < 15 or > 34)
            return false;
        var rearranged = compact[4..] + compact[..4];
        var mod = 0;
        foreach (var c in rearranged)
        {
            var digits = c is >= '0' and <= '9'
                ? ((int)(c - '0')).ToString()
                : c is >= 'A' and <= 'Z'
                    ? (c - 'A' + 10).ToString()
                    : null;
            if (digits is null)
                return false;
            foreach (var d in digits)
                mod = (mod * 10 + (d - '0')) % 97;
        }
        return mod == 1;
    }

    private static void CollectEmail(string text, Rule rule, List<RawHit> raw)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '@')
                continue;
            var left = i - 1;
            var local = 0;
            while (left >= 0 && local < 64 && IsEmailLocal(text[left]))
            {
                left--;
                local++;
            }
            left++;
            if (local == 0 || (left > 0 && IsEmailLocal(text[left - 1])))
                continue;
            var start = i + 1;
            var right = start;
            var domain = 0;
            while (right < text.Length && domain < 255 && IsEmailDomain(text[right]))
            {
                right++;
                domain++;
            }
            var capped = domain >= 255 && right < text.Length && IsEmailDomain(text[right]);
            while (right > start && (text[right - 1] == '.' || text[right - 1] == '-'))
                right--;
            if (capped || right <= start || !EmailDomainOk(text, start, right))
                continue;
            raw.Add(new RawHit(left, right - left, rule));
            i = right - 1;
        }
    }

    private static bool EmailDomainOk(string text, int start, int end)
    {
        var dot = -1;
        var labelStart = start;
        for (var i = start; i < end; i++)
        {
            if (text[i] != '.')
                continue;
            if (!EmailLabelOk(text, labelStart, i))
                return false;
            dot = i;
            labelStart = i + 1;
        }

        return dot >= start && EmailLabelOk(text, labelStart, end) && end - dot >= 3;
    }

    private static bool EmailLabelOk(string text, int start, int end) =>
        end > start && text[start] != '-' && text[end - 1] != '-';

    private static void CollectPhone(string text, Rule rule, List<RawHit> raw)
    {
        var i = 0;
        var guard = 0;
        while (i < text.Length && guard++ < text.Length + 2)
        {
            if (!IsPhoneStart(text, i))
            {
                i++;
                continue;
            }
            var start = i;
            var digits = 0;
            var separators = 0;
            var paren = false;
            while (i < text.Length && digits < 16 && IsPhoneChar(text[i]))
            {
                if (char.IsDigit(text[i]))
                    digits++;
                else
                {
                    separators++;
                    paren |= text[i] is '(' or ')';
                }
                i++;
            }
            while (i > start && !char.IsDigit(text[i - 1]))
            {
                i--;
                separators--;
            }
            var value = text[start..i];
            var compact = new string(value.Where(char.IsDigit).ToArray());
            var plus = value.StartsWith('+');
            var zeroZero = compact.StartsWith("00", StringComparison.Ordinal);
            var shaped = plus || zeroZero || paren || value.Contains('/') || (value.StartsWith('0') && separators > 0 && digits >= 9);
            if (i == start)
                i++;
            if (digits is >= 8 and <= 15 && shaped && !LooksLikeDate(compact) && (separators > 0 || plus || zeroZero) &&
                MatchesPhonePattern(rule, value, compact))
                raw.Add(new RawHit(start, i - start, rule));
        }
    }

    private static void CollectNational(string text, Rule rule, List<RawHit> raw, string header, bool csv)
    {
        var hits = new List<(int Start, int Length)>();
        NationalId.Collect(text, rule, hits);
        foreach (var hit in hits)
        {
            var value = text.Substring(hit.Start, hit.Length);
            if (NeedsLabel(rule, value) && !LabelNearby(text, hit.Start, hit.Length, rule, header, csv))
                continue;
            raw.Add(new RawHit(hit.Start, hit.Length, rule));
        }
    }

    private static bool NeedsLabel(Rule rule, string value)
    {
        if (rule.Algorithm is not ("ssn" or "itin" or "pib" or "bsn" or "oib" or "nhs" or "steuerid" or "nino" or "cpf" or "cnpj" or "aadhaar"))
            return false;
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c))
                return false;
        }
        return value.Length > 0;
    }

    private static bool LabelNearby(string text, int start, int length, Rule rule, string header, bool csv)
    {
        if (KeywordAt(header, rule))
            return true;
        if (csv && KeywordAt(CsvColumnHeader(text, start), rule))
            return true;
        var lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r'))
            lineStart--;
        var lineEnd = start;
        while (lineEnd < text.Length && text[lineEnd] is not ('\n' or '\r'))
            lineEnd++;
        var windowStart = Math.Max(lineStart, start - 40);
        var windowEnd = Math.Min(lineEnd, start + length + 40);
        if (windowEnd <= windowStart)
            return false;
        return KeywordAt(text.Substring(windowStart, windowEnd - windowStart), rule);
    }

    private static bool KeywordAt(string text, Rule rule)
    {
        if (string.IsNullOrEmpty(text) || rule.Keywords.Count == 0)
            return false;
        foreach (var keyword in rule.Keywords)
        {
            if (keyword.Length == 0)
                continue;
            var pattern = @"\b" + Regex.Escape(keyword) + @"\b";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
                return true;
        }
        return false;
    }

    private static string CsvColumnHeader(string text, int start)
    {
        var firstBreak = text.IndexOfAny(['\r', '\n']);
        if (firstBreak < 0 || start <= firstBreak)
            return "";
        var headerLine = text[..firstBreak];
        var lineStart = 0;
        for (var i = 0; i < start; i++)
        {
            if (text[i] is '\n' or '\r')
                lineStart = i + 1;
        }
        var delimiter = headerLine.Count(ch => ch == '\t') > headerLine.Count(ch => ch == ',') ? '\t' : ',';
        var column = 0;
        for (var i = lineStart; i < start; i++)
        {
            if (text[i] == delimiter)
                column++;
        }
        var headers = headerLine.Split(delimiter);
        return column < headers.Length ? headers[column].Trim().Trim('"') : "";
    }

    private static int Specificity(Rule rule)
    {
        if (rule.Id.Equals("pii.jmbg", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (!string.IsNullOrEmpty(rule.Algorithm) || rule.Patterns.Count > 0)
            return 2;
        return 0;
    }

    private static bool IsNational(Rule rule) => rule.Kind is "jmbg" or "checksum";

    private static int LabelScore(string text, RawHit hit, string header, bool csv) =>
        LabelNearby(text, hit.Start, hit.Length, hit.Rule, header, csv) ? 1 : 0;

    private static int RegionScore(Rule rule, string region) =>
        region.Length > 0 && rule.CountryCode.Equals(region, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static int Strength(Rule rule) => rule.Algorithm switch
    {
        "jmbg-ba" or "jmbg-me" or "jmbg-si" or "jmbg-mk" => 60,
        "jmbg" or "pesel" => 50,
        "codice" or "dni" or "nie" => 40,
        "steuerid" => 30,
        "cpf" or "cnpj" or "aadhaar" => 25,
        "nir" => 22,
        "oib" or "pib" or "bsn" or "nhs" => 20,
        _ => 10
    };

    private static bool MatchesPhonePattern(Rule rule, string raw, string compact)
    {
        if (rule.Patterns.Count == 0 && string.IsNullOrEmpty(rule.Pattern))
            return true;
        var plus = raw.Contains('+') ? "+" + compact : compact;
        var forms = new[] { raw, plus, compact };
        var patterns = rule.Patterns.Count > 0 ? rule.Patterns : [rule.Pattern!];
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern))
                continue;
            foreach (var form in forms)
            {
                if (Regex.IsMatch(form, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)))
                    return true;
            }
        }
        return false;
    }

    private static bool KnownCardPrefix(string digits)
    {
        if (digits.StartsWith('4'))
            return digits.Length is 13 or 16 or 19;
        if (InRange(digits, 4, 2221, 2720) || InRange(digits, 2, 51, 55))
            return digits.Length == 16;
        if (digits.StartsWith("34") || digits.StartsWith("37"))
            return digits.Length == 15;
        if (digits.StartsWith("6011") || digits.StartsWith("65") || InRange(digits, 3, 644, 649))
            return digits.Length == 16;
        if (digits.StartsWith("36") || digits.StartsWith("38") || InRange(digits, 3, 300, 305))
            return digits.Length is 14 or 16;
        if (InRange(digits, 4, 3528, 3589))
            return digits.Length is >= 16 and <= 19;
        return digits.StartsWith("50") || InRange(digits, 2, 56, 69);
    }

    private static bool InRange(string digits, int width, int min, int max)
    {
        if (digits.Length < width || !int.TryParse(digits[..width], out var value))
            return false;
        return value >= min && value <= max;
    }

    private static bool ConsistentCardSeparators(string value)
    {
        var spaces = value.Count(c => c == ' ');
        var dashes = value.Count(c => c == '-');
        return spaces == 0 || dashes == 0;
    }

    private static bool IsEmailLocal(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '%' or '+' or '-';

    private static bool IsEmailDomain(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '-';

    private static bool IsPhoneStart(string text, int index)
    {
        var c = text[index];
        if (c == '+' || c == '(')
            return true;
        if (c != '0' || index > 0 && char.IsDigit(text[index - 1]))
            return false;
        return index + 1 < text.Length && (char.IsDigit(text[index + 1]) || text[index + 1] == '0');
    }

    private static bool IsPhoneChar(char c) => char.IsDigit(c) || c is '+' or ' ' or '-' or '.' or '/' or '(' or ')';

    private static bool LooksLikeDate(string digits)
    {
        if (digits.Length == 8 &&
            int.TryParse(digits[..4], out var year) &&
            int.TryParse(digits.Substring(4, 2), out var month) &&
            int.TryParse(digits.Substring(6, 2), out var day))
        {
            return year is >= 1900 and <= 2100 && month is >= 1 and <= 12 && day is >= 1 and <= 31;
        }
        return false;
    }

    private readonly record struct RawHit(int Start, int Length, Rule Rule);
}

public readonly record struct MatchResult(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> FailedRuleIds,
    bool Incomplete);
