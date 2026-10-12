using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HirRadar;

/// <summary>
/// Konzervatív, címalapú tájékoztató besorolás. Nem tényellenőrzés,
/// nem követeli meg a teljes cikk letöltését, és nem töröl találatot.
/// </summary>
public enum DomesticRelevance { Unknown, Hungarian, LikelyForeign }

public static class DomesticRelevanceClassifier
{
    // A teljes magyar vonatkozású címek és néhány ismert intézmény elsőbbséget kapnak.
    private static readonly Regex HungarianMarkers = Words(
        "magyar", "magyarorszag", "hazai", "budapest", "budapesti", "baja", "bajai",
        "bacs-kiskun", "kecskemet", "kalocsa", "szeged", "debrecen", "pecs", "gyor",
        "miskolc", "szolnok", "eger", "nyiregyhaza", "kaposvar", "sopron", "veszprem",
        "zalaegerszeg", "bekescsaba", "szekesfehervar", "szombathely", "dunaujvaros",
        "orbán", "orban", "fidesz", "tisza part", "orszaggyules", "allamfo",
        "nav", "mav", "bkk", "forint", "ft", "nemzeti bank", "magyar nemzeti bank",
        "ksh", "nav", "mtva", "orszagos mentoszolgalat");

    // Csak határozott nemzetközi utalás esetén jelölünk egy címet külföldinek.
    // Az EU vagy Brüsszel önmagában nem elegendő; magyar vonatkozás is lehet.
    private static readonly Regex ForeignMarkers = Words(
        "donald trump", "trump", "putyin", "putin", "zelenszkij", "biden", "washington",
        "moszkva", "moszkvai", "london", "londoni", "parizs", "peking",
        "egyesult allamok", "usa", "amerika", "amerikai", "oroszorszag", "orosz",
        "ukrajna", "ukran", "kina", "kinai", "izrael", "izraeli", "gaza",
        "iran", "irani", "franciaorszag", "francia", "nemetorszag", "nemet",
        "torokorszag", "torok", "japan", "japan", "indiaban", "india",
        "spanyolorszag", "olaszorszag", "ausztria", "szlovakia",
        "braziliaban", "brazilia", "del-korea", "eszakkorea");

    private static readonly Regex MixedDomesticSignals = Words(
        "onkormanyzat", "kozbeszerzes", "korhaz", "miniszterium", "kormanyhivatal",
        "rendorkapitanysag", "varmegye", "megyei", "alpolgarmester", "polgarmester");

    private static Regex Words(params string[] expressions) => new(
        @"(?<![a-z])(?:" + string.Join("|", expressions.Select(e =>
            Regex.Escape(Normalize(e)).Replace("\\ ", @"\s+"))) + @")[a-z]*(?![a-z])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(120));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var output = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                output.Append(char.ToLowerInvariant(c));
        return output.ToString().Normalize(NormalizationForm.FormC);
    }

    public static DomesticRelevance Assess(NewsItem item)
    {
        if (item.RelevanceCached &&
            string.Equals(item.CachedRelevanceTitle, item.Title, StringComparison.Ordinal) &&
            string.Equals(item.CachedRelevanceSource, item.SourceName, StringComparison.Ordinal) &&
            item.CachedRelevanceLocal == item.IsLocalMatch)
            return item.CachedRelevance;
        var result = Calculate(item);
        item.CachedRelevance = result;
        item.CachedRelevanceTitle = item.Title;
        item.CachedRelevanceSource = item.SourceName;
        item.CachedRelevanceLocal = item.IsLocalMatch;
        item.RelevanceCached = true;
        return result;
    }

    private static DomesticRelevance Calculate(NewsItem item)
    {
        string title = Normalize(item.Title);
        // Saját településfigyelés alapján érkezett találat szintén HU-releváns.
        if (item.IsLocalMatch || HungarianMarkers.IsMatch(title)) return DomesticRelevance.Hungarian;

        string source = Normalize(item.SourceName);
        if (source.Contains("belfold") || source.Contains("itthon") ||
            source.Contains("baja hangja") || source.Contains("kecsup") ||
            source.Contains("kormany.hu")) return DomesticRelevance.Hungarian;

        // A magyar közintézmények általános megnevezése önmagában csak akkor erős
        // magyar jel, ha egyértelmű külföldi hely/vezető nem szerepel a címben.
        bool foreign = ForeignMarkers.IsMatch(title);
        if (!foreign && MixedDomesticSignals.IsMatch(title)) return DomesticRelevance.Hungarian;
        if (foreign) return DomesticRelevance.LikelyForeign;
        return DomesticRelevance.Unknown;
    }

    public static string Label(NewsItem item) => Assess(item) switch
    {
        DomesticRelevance.Hungarian => "Magyar vonatkozás (címalapú)",
        DomesticRelevance.LikelyForeign => "Valószínűleg külföldi (címalapú)",
        _ => "Nem dönthető el a címből"
    };
}
