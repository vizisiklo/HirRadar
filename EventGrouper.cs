using System.Globalization;
using System.Text;

namespace HirRadar;

/// <summary>
/// Conservative headline similarity grouping; a suggested editorial grouping, not fact-checking.
/// Processes newest 900 records only to keep UI responsive with large archives.
/// </summary>
public static class EventGrouper
{
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "hogy", "mint", "miatt", "után", "alatt", "ebben", "azért", "most", "akkor", "ilyen", "olyan",
        "lesz", "lett", "volt", "vannak", "erről", "ennek", "ezzel", "még", "egyre", "már", "nem", "aki",
        "ami", "amit", "közben", "nagy", "újabb", "új", "magyar", "magyarország", "magyarorszagon",
        "kiderült", "mutatjuk", "fotók", "videó", "történt", "tudni", "lehet", "szerint", "viszont", "csak", "egyik"
    };

    public static void Apply(IEnumerable<NewsItem> all)
    {
        var selected = all.OrderByDescending(x => x.HasPublishedDate ? x.PublishedUtc : x.FirstSeenUtc)
            .Take(900).ToList();
        foreach (var item in all)
        {
            if (item.HasRelated) item.SetRelated([]);
            if (item.IsGroupSecondary) item.IsGroupSecondary = false;
        }
        var tokens = selected.Select(x => Tokenize(x.Title)).ToList();
        var grouped = new bool[selected.Count];
        for (int i = 0; i < selected.Count; i++)
        {
            if (grouped[i] || tokens[i].Count < 3) continue;
            var group = new List<int> { i };
            var currentDate = selected[i].HasPublishedDate ? selected[i].PublishedUtc : selected[i].FirstSeenUtc;
            for (int j = i + 1; j < selected.Count; j++)
            {
                if (grouped[j] || tokens[j].Count < 3) continue;
                var otherDate = selected[j].HasPublishedDate ? selected[j].PublishedUtc : selected[j].FirstSeenUtc;
                // A jelöltek időrendben csökkenő sorrendűek: 72 órán túl
                // minden további jelölt is régebbi, nem szükséges összevetni.
                if ((currentDate - otherDate).TotalHours > 72) break;
                if (selected[i].SourceId == selected[j].SourceId) continue;
                int common = tokens[i].Count(t => tokens[j].Contains(t));
                int union = tokens[i].Count + tokens[j].Count - common;
                if (common >= 3 && union > 0 && common / (double)union >= 0.54)
                    group.Add(j);
            }
            if (group.Count < 2) continue;
            // Newest report is lead. The rest remain visible unless the user activates compact grouping.
            foreach (int index in group.Skip(1)) { grouped[index] = true; selected[index].IsGroupSecondary = true; }
            foreach (int index in group)
                selected[index].SetRelated(group.Where(other => other != index).Select(other => selected[other]).ToList());
        }
    }

    private static HashSet<string> Tokenize(string title)
    {
        var decomposed = title.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length >= 4 && !Stop.Contains(x)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
    }
}
