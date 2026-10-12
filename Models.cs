using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace HirRadar;

public sealed class NewsSource : INotifyPropertyChanged
{
    private string _name = "";
    private string _feedUrl = "";
    private bool _enabled = true;
    private DateTimeOffset? _lastCheckedUtc;
    private DateTimeOffset? _lastSuccessUtc;
    private string _lastError = "";
    private string _lastMethod = "";
    private int _lastItemCount;
    private long _lastDurationMs;
    private int _lastRecentCount;
    private int _lastDatedCount;
    private int _consecutiveFailures;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { _name = value; Notify(); Notify(nameof(AffiliationDisplay)); Notify(nameof(AffiliationDetails)); Notify(nameof(HasAffiliation)); } }
    public string FeedUrl { get => _feedUrl; set { _feedUrl = value; Notify(); Notify(nameof(AffiliationDisplay)); Notify(nameof(AffiliationDetails)); Notify(nameof(HasAffiliation)); } }
    public bool Enabled { get => _enabled; set { _enabled = value; Notify(); Notify(nameof(StateDisplay)); } }
    public DateTimeOffset? LastCheckedUtc { get => _lastCheckedUtc; set { _lastCheckedUtc = value; Notify(); Notify(nameof(LastCheckedDisplay)); } }
    public DateTimeOffset? LastSuccessUtc { get => _lastSuccessUtc; set { _lastSuccessUtc = value; Notify(); Notify(nameof(StateDisplay)); } }
    public string LastError { get => _lastError; set { _lastError = value; Notify(); Notify(nameof(StateDisplay)); } }
    public string LastMethod { get => _lastMethod; set { _lastMethod = value; Notify(); Notify(nameof(MethodDisplay)); } }
    public int LastItemCount { get => _lastItemCount; set { _lastItemCount = value; Notify(); Notify(nameof(ResultDisplay)); Notify(nameof(StateDisplay)); } }
    public long LastDurationMs { get => _lastDurationMs; set { _lastDurationMs = value; Notify(); Notify(nameof(ResultDisplay)); } }

    public int LastRecentCount { get => _lastRecentCount; set { _lastRecentCount = value; Notify(); Notify(nameof(QualityDisplay)); } }
    public int LastDatedCount { get => _lastDatedCount; set { _lastDatedCount = value; Notify(); Notify(nameof(QualityDisplay)); } }
    public int ConsecutiveFailures { get => _consecutiveFailures; set { _consecutiveFailures = Math.Clamp(value, 0, 9999); Notify(); Notify(nameof(FailureStreakDisplay)); } }
    [JsonIgnore] public string FailureStreakDisplay => ConsecutiveFailures == 0 ? "—" : ConsecutiveFailures.ToString(CultureInfo.InvariantCulture);
    [JsonIgnore] public string AffiliationDisplay => SourceAttribution.Get(FeedUrl, Name).Badge;
    [JsonIgnore] public string AffiliationDetails => SourceAttribution.Get(FeedUrl, Name).Tooltip;
    [JsonIgnore] public bool HasAffiliation => SourceAttribution.Get(FeedUrl, Name).HasBadge;
    [JsonIgnore] public string QualityDisplay => LastCheckedUtc is null ? "—" :
        $"24h: {LastRecentCount} · dátum: {LastDatedCount}/{LastItemCount}";
    [JsonIgnore] public string StateDisplay => !Enabled ? "Szünetel" :
        !string.IsNullOrEmpty(LastError) ? "Hiba" :
        LastSuccessUtc.HasValue && LastItemCount == 0 ? "Üres" :
        LastSuccessUtc.HasValue ? "Rendben" : "Még nem ellenőrzött";
    [JsonIgnore] public string LastCheckedDisplay => LastCheckedUtc?.ToLocalTime().ToString("yyyy. MM. dd. HH:mm", CultureInfo.GetCultureInfo("hu-HU")) ?? "—";
    [JsonIgnore] public string MethodDisplay => string.IsNullOrWhiteSpace(LastMethod) ? "—" : LastMethod;
    [JsonIgnore] public string ResultDisplay => LastCheckedUtc.HasValue ? $"{LastItemCount} cikk · {LastDurationMs} ms" : "—";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public NewsSource Copy() => new()
    {
        Id = Id, Name = Name, FeedUrl = FeedUrl, Enabled = Enabled,
        LastCheckedUtc = LastCheckedUtc, LastSuccessUtc = LastSuccessUtc,
        LastError = LastError, LastMethod = LastMethod,
        LastItemCount = LastItemCount, LastDurationMs = LastDurationMs,
        LastRecentCount = LastRecentCount, LastDatedCount = LastDatedCount,
        ConsecutiveFailures = ConsecutiveFailures
    };
}

public sealed class NewsItem : INotifyPropertyChanged
{
    private bool _isRead;
    private bool _isStarred;
    private bool _isNew;
    private bool _isExpanded;
    private bool _isLocalMatch;
    private bool _isKeywordMatch;
    private string _matchedTerms = "";
    private bool _isArticleLoading;
    private string _articleBody = "";
    private string _articleStatus = "";
    private IReadOnlyList<ArticleBlock> _articleBlocks = [];
    private IReadOnlyList<NewsItem> _relatedItems = [];
    private bool _isGroupSecondary;
    // A nagy listát minden szűréskor nem kell újra regexekkel besorolni.
    // A cache csak a futó munkamenetben él és az eredeti cím/forrás/helyi jel mellett érvényes.
    [JsonIgnore] internal bool RelevanceCached { get; set; }
    [JsonIgnore] internal DomesticRelevance CachedRelevance { get; set; }
    [JsonIgnore] internal string? CachedRelevanceTitle { get; set; }
    [JsonIgnore] internal string? CachedRelevanceSource { get; set; }
    [JsonIgnore] internal bool CachedRelevanceLocal { get; set; }
    public string Id { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset PublishedUtc { get; set; }
    public bool HasPublishedDate { get; set; } = true;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public bool IsRead { get => _isRead; set { if (_isRead == value) return; _isRead = value; Notify(); } }
    public bool IsStarred { get => _isStarred; set { if (_isStarred == value) return; _isStarred = value; Notify(); Notify(nameof(StarSymbol)); } }
    [JsonIgnore] public bool IsNew { get => _isNew; set { _isNew = value; Notify(); } }
    // A figyelési találatok minden indításkor újraszámíthatók; nem módosítják a news.json sémáját.
    [JsonIgnore] public bool IsLocalMatch { get => _isLocalMatch; set { if (_isLocalMatch == value) return; _isLocalMatch = value; Notify(); Notify(nameof(IsWatched)); } }
    [JsonIgnore] public bool IsKeywordMatch { get => _isKeywordMatch; set { if (_isKeywordMatch == value) return; _isKeywordMatch = value; Notify(); Notify(nameof(IsWatched)); } }
    [JsonIgnore] public string MatchedTerms { get => _matchedTerms; set { _matchedTerms = value; Notify(); } }
    // Automatikus, címalapú témák: NEM mentjük a news.json-ba, indításkor újraszámítjuk.
    [JsonIgnore] public IReadOnlyList<string> TopicIds { get; private set; } = [];
    [JsonIgnore] public string TopicLabels { get; private set; } = "";
    public void SetTopics(IReadOnlyList<string> ids, string labels)
    {
        TopicIds = ids;
        TopicLabels = labels;
        Notify(nameof(TopicIds));
        Notify(nameof(TopicLabels));
    }
    [JsonIgnore] public IReadOnlyList<NewsItem> RelatedItems => _relatedItems;
    [JsonIgnore] public int RelatedCount => _relatedItems.Count;
    [JsonIgnore] public bool HasRelated => _relatedItems.Count != 0;
    [JsonIgnore] public string RelatedDisplay => $"Kapcsolódó tudósítások ({_relatedItems.Count}) · automatikus javaslat";
    [JsonIgnore] public bool IsGroupSecondary { get => _isGroupSecondary; set { if (_isGroupSecondary == value) return; _isGroupSecondary = value; Notify(); } }
    public void SetRelated(IReadOnlyList<NewsItem> items)
    {
        if (_relatedItems.SequenceEqual(items)) return;
        _relatedItems = items;
        Notify(nameof(RelatedItems)); Notify(nameof(RelatedCount));
        Notify(nameof(HasRelated)); Notify(nameof(RelatedDisplay));
    }
    [JsonIgnore] public string AffiliationDisplay => SourceAttribution.Get(Url, SourceName).Badge;
    [JsonIgnore] public string AffiliationDetails => SourceAttribution.Get(Url, SourceName).Tooltip;
    [JsonIgnore] public bool HasAffiliation => SourceAttribution.Get(Url, SourceName).HasBadge;
    [JsonIgnore] public bool IsWatched => IsLocalMatch || IsKeywordMatch;
    // Csak a futó alkalmazás memóriájában élnek; nem kerülnek a news.json fájlba.
    [JsonIgnore] public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; Notify(); Notify(nameof(ExpandSymbol)); } }
    [JsonIgnore] public bool IsArticleLoading
    {
        get => _isArticleLoading;
        set { if (_isArticleLoading == value) return; _isArticleLoading = value; Notify(); Notify(nameof(CanCopyArticle)); }
    }
    [JsonIgnore] public string ArticleBody
    {
        get => _articleBody;
        set
        {
            if (_articleBody == value) return;
            _articleBody = value;
            Notify();
            Notify(nameof(CanCopyArticle));
        }
    }
    [JsonIgnore] public bool CanCopyArticle => !string.IsNullOrWhiteSpace(ArticleBody);
    [JsonIgnore] public IReadOnlyList<ArticleBlock> ArticleBlocks { get => _articleBlocks; set { _articleBlocks = value; Notify(); } }
    [JsonIgnore] public string ArticleStatus { get => _articleStatus; set { _articleStatus = value; Notify(); } }
    [JsonIgnore] public string ExpandSymbol => IsExpanded ? "▴" : "▾";
    [JsonIgnore] public string StarSymbol => IsStarred ? "★" : "☆";
    [JsonIgnore] public string PublishedDisplay => HasPublishedDate
        ? PublishedUtc.ToLocalTime().ToString("HH:mm", CultureInfo.GetCultureInfo("hu-HU"))
        : "észlelve " + FirstSeenUtc.ToLocalTime().ToString("HH:mm", CultureInfo.GetCultureInfo("hu-HU"));
    [JsonIgnore] public string DayDisplay => (HasPublishedDate ? PublishedUtc : FirstSeenUtc)
        .ToLocalTime().ToString("yyyy. MM. dd.", CultureInfo.GetCultureInfo("hu-HU"));
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string BuildId(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri) { Fragment = "" };
            var parameters = builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            builder.Query = string.Join("&", parameters.Where(p =>
            {
                var key = Uri.UnescapeDataString(p.Split('=')[0]);
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("gclid", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("mc_cid", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("mc_eid", StringComparison.OrdinalIgnoreCase);
            }));
            url = builder.Uri.AbsoluteUri.TrimEnd('/');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24];
    }
}
