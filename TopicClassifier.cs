using System.Globalization;
using System.Text;

namespace HirRadar;

/// <summary>
/// Konzervatív, átlátható címalapú címkézés. Nem tényellenőrzés és nem teljes cikkelemzés.
/// Egy cím több témát is kaphat; nulla egyezésnél Egyéb.
/// </summary>
public static class TopicClassifier
{
    public sealed record Topic(string Id, string Name, string[] Terms);

    public static IReadOnlyList<Topic> DefaultCategories { get; } =
    [
        new("politika", "Politika / közélet", ["kormány", "miniszter", "parlament", "országgyűlés", "ellenzék", "párt", "választás", "politikus", "politikai", "köztársasági elnök", "kormánypárt", "tüntetés"]),
        new("onkormanyzat", "Önkormányzat", ["önkormányzat", "polgármester", "alpolgármester", "képviselő-testület", "közgyűlés", "városháza", "közbeszerzés", "helyi képviselő", "városi költségvetés"]),
        new("gazdasag", "Gazdaság", ["gazdaság", "infláció", "forint", "árfolyam", "beruházás", "tőzsde", "vállalat", "munkabér", "munkanélkül", "költségvetés", "adóemelés", "jegybank", "kamat", "üzemanyagár", "ingatlanár"]),
        new("bunugy", "Bűnügy / igazságszolgáltatás", ["rendőrség", "rendőr", "nyomozás", "letartóztat", "őrizetbe", "bíróság", "ügyészség", "vádirat", "bűncselekmény", "rablás", "lopás", "csalás", "korrupció", "ítélet"]),
        new("egeszseg", "Egészségügy", ["kórház", "egészségügy", "orvos", "ápoló", "beteg", "mentő", "gyógyszer", "járvány", "rendelő", "egészségbiztosítás"]),
        new("oktatas", "Oktatás", ["oktatás", "iskola", "egyetem", "óvoda", "tanár", "pedagógus", "diák", "érettségi", "tanterv", "felsőoktatás", "hallgató"]),
        new("kozlekedes", "Közlekedés", ["közlekedés", "vasút", "vonat", "máv", "volán", "autópálya", "útlezárás", "baleset", "menetrend", "forgalomkorlátozás", "busz", "repülőtér"]),
        new("kornyezet", "Környezet / időjárás", ["időjárás", "vihar", "árvíz", "aszály", "környezet", "hőhullám", "fagy", "esőzés", "természetvédelem", "vízállás", "szennyezés"])
    ];

    private static IReadOnlyList<Topic> _categories = DefaultCategories;
    public static IReadOnlyList<Topic> Categories => _categories;

    public static void Configure(TopicRules rules)
    {
        rules = TopicRules.Sanitize(rules);
        _categories = DefaultCategories.Select(c =>
            new Topic(c.Id, c.Name, rules.Terms[c.Id].ToArray())).ToList();
    }

    public const string OtherId = "egyeb";
    public static string OtherName => "Egyéb / besorolatlan";

    public static void Apply(NewsItem item)
    {
        string title = Normalize(item.Title);
        var hits = Categories.Where(category => category.Terms.Any(term => MatchesPrefix(title, Normalize(term)))).ToList();
        item.SetTopics(
            hits.Count == 0 ? [OtherId] : hits.Select(x => x.Id).ToList(),
            hits.Count == 0 ? OtherName : string.Join(" · ", hits.Select(x => x.Name)));
    }

    private static bool MatchesPrefix(string text, string term)
    {
        if (text.Length == 0 || term.Length == 0) return false;
        return (" " + text + " ").Contains(" " + term, StringComparison.Ordinal);
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        bool spaced = true;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                result.Append(char.ToLowerInvariant(c));
                spaced = false;
            }
            else if (!spaced)
            {
                result.Append(' ');
                spaced = true;
            }
        }
        return result.ToString().TrimEnd();
    }
}
