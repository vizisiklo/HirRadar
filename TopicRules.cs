namespace HirRadar;

/// <summary>Editable, locally stored keyword lists for the eight fixed topical categories.</summary>
public sealed class TopicRules
{
    public Dictionary<string, List<string>> Terms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static TopicRules Defaults() => new()
    {
        Terms = TopicClassifier.DefaultCategories.ToDictionary(t => t.Id,
            t => t.Terms.ToList(), StringComparer.OrdinalIgnoreCase)
    };

    public static TopicRules Sanitize(TopicRules? input)
    {
        var cleaned = new TopicRules();
        foreach (var category in TopicClassifier.DefaultCategories)
        {
            List<string>? terms = null;
            if (input?.Terms is not null) input.Terms.TryGetValue(category.Id, out terms);
            cleaned.Terms[category.Id] = (terms ?? category.Terms.ToList())
                .Select(t => (t ?? "").Trim()).Where(t => t.Length is > 1 and <= 100)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
        }
        return cleaned;
    }
}
