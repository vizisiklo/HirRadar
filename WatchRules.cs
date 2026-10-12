using System.Globalization;
using System.Text;

namespace HirRadar;

/// <summary>
/// Szerkeszthető megfigyelési kifejezések. Minden kifejezés önálló alternatíva (VAGY).
/// Csak a hír címét vizsgáljuk, a cikktörzset nem töltjük le háttérben.
/// </summary>
public sealed class WatchRules
{
    public List<string> Keywords { get; set; } = [];
    public List<string> Locations { get; set; } = ["Baja", "Bács-Kiskun"];
    public bool NotifyOnMatches { get; set; } = true;

    public static List<string> CleanLines(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();
    }
}

public static class WatchMatcher
{
    /// <summary>
    /// Az ékezetektől, kis-/nagybetűktől és írásjelektől független keresés.
    /// Szókezdő egyezést használ, így pl. "Baja" és "bajai" is találat.
    /// Nem tesz tényállítást a cikk témájáról: ez cím-alapú jelzés.
    /// </summary>
    public static void Apply(NewsItem item, WatchRules rules)
    {
        string headline = Normalize(item.Title);
        var locations = FindMatches(headline, rules.Locations, true);
        var keywords = FindMatches(headline, rules.Keywords, false);
        item.IsLocalMatch = locations.Count > 0;
        item.IsKeywordMatch = keywords.Count > 0;
        item.MatchedTerms = string.Join(", ", locations.Concat(keywords).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static readonly string[] LocativeSuffixes =
        ["i", "n", "en", "on", "an", "ban", "ben", "ra", "re", "rol", "tol", "hoz", "hez", "nek", "nak", "bol", "be", "ba", "on", "ot", "et", "at", "ert"];

    private static List<string> FindMatches(string normalizedTitle, IEnumerable<string>? rules, bool locations)
    {
        var result = new List<string>();
        var titleWords = normalizedTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string term in WatchRules.CleanLines(rules))
        {
            // Location aliases: canonical name | optional inflected/alternate names.
            string[] aliases = locations ? term.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [term];
            string label = aliases.Length > 0 ? aliases[0] : term;
            foreach (string alias in aliases)
            {
                string normalizedTerm = Normalize(alias);
                if (normalizedTerm.Length == 0) continue;
                if (normalizedTerm.Contains(' '))
                {
                    var phraseWords = normalizedTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 0; i <= titleWords.Length - phraseWords.Length; i++)
                    {
                        bool preceding = true;
                        for (int j = 0; j < phraseWords.Length - 1; j++)
                            if (titleWords[i + j] != phraseWords[j]) { preceding = false; break; }
                        if (!preceding) continue;
                        string last = titleWords[i + phraseWords.Length - 1];
                        string expected = phraseWords[^1];
                        if (last == expected || (last.StartsWith(expected, StringComparison.Ordinal) &&
                            LocativeSuffixes.Contains(last[expected.Length..], StringComparer.Ordinal)))
                        {
                            result.Add(label);
                            break;
                        }
                    }
                    if (result.Contains(label, StringComparer.OrdinalIgnoreCase)) break;
                    continue;
                }
                if (titleWords.Any(word => word == normalizedTerm ||
                    (locations && word.StartsWith(normalizedTerm, StringComparison.Ordinal) &&
                     LocativeSuffixes.Contains(word[normalizedTerm.Length..], StringComparer.Ordinal))))
                { result.Add(label); break; }
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool lastSpace = true;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().TrimEnd();
    }
}
