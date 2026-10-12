namespace HirRadar;

/// <summary>
/// A szűrés összes felhasználói beállítása, a lekért hírektől függetlenül.
/// A dátum dinamikus (pl. „mai”), ezért egy mentett nézet minden nap aktuális.
/// </summary>
public sealed class ViewPreset
{
    public string Name { get; set; } = "";
    public string SearchText { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string TopicId { get; set; } = "";
    public int DateIndex { get; set; }
    public int FocusIndex { get; set; }
    public int DomesticIndex { get; set; } = 1;
    public bool UnreadOnly { get; set; }
    public bool StarredOnly { get; set; }
    public bool CompactGroups { get; set; }

    public ViewPreset Copy() => new()
    {
        Name = Name, SearchText = SearchText, SourceId = SourceId,
        TopicId = TopicId, DateIndex = DateIndex, FocusIndex = FocusIndex, DomesticIndex = DomesticIndex,
        UnreadOnly = UnreadOnly, StarredOnly = StarredOnly, CompactGroups = CompactGroups
    };

    public static List<ViewPreset> Sanitize(IEnumerable<ViewPreset>? presets) => (presets ?? [])
        .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
        .Select(p => new ViewPreset
        {
            Name = p.Name.Trim()[..Math.Min(p.Name.Trim().Length, 40)],
            SearchText = (p.SearchText ?? "").Trim()[..Math.Min((p.SearchText ?? "").Trim().Length, 200)],
            SourceId = p.SourceId ?? "", TopicId = p.TopicId ?? "",
            DateIndex = Math.Clamp(p.DateIndex, 0, 2), FocusIndex = Math.Clamp(p.FocusIndex, 0, 3),
            DomesticIndex = Math.Clamp(p.DomesticIndex, 0, 4),
            UnreadOnly = p.UnreadOnly, StarredOnly = p.StarredOnly, CompactGroups = p.CompactGroups
        })
        .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
        .Take(40)
        .ToList();
}
