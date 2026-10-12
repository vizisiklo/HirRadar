namespace HirRadar;

/// <summary>Csak megjelenítési szövegelemek; nem készülnek képek vagy képfoglaló területek.</summary>
public sealed class ArticleBlock
{
    public string Text { get; init; } = "";
    public bool IsHeading { get; init; }
    public bool IsQuote { get; init; }
}
