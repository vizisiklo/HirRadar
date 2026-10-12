using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HirRadar;

/// <summary>
/// Az ellenőrzési ciklusok visszanézhető helyi előzményei.
/// Nem ment cikkcímet, URL-t, teljes szöveget vagy képet.
/// </summary>
/// <summary>
/// Egy forrás eredménye egy befejezett ellenőrzési ciklusban.
/// Nem tartalmaz cikkcímet, cikklinket vagy cikkszöveget.
/// </summary>
public sealed record SourceRefreshResult
{
    public string SourceId { get; init; } = "";
    public string SourceName { get; init; } = "";
    public bool Succeeded { get; init; }
    public long DurationMs { get; init; }
    public int ArticlesFound { get; init; }
    public int NewArticles { get; init; }
    public string Method { get; init; } = "";
    public string Error { get; init; } = "";
    [JsonIgnore] public string StateDisplay => Succeeded ? "Sikeres" : "Hiba";
    [JsonIgnore] public string DurationDisplay => $"{DurationMs / 1000.0:0.00} mp";
    [JsonIgnore] public string DetailDisplay => Succeeded ? Method : Error;
}

public sealed record RefreshHistoryEntry
{
    public DateTimeOffset FinishedAtUtc { get; init; }
    public int CheckedSources { get; init; }
    public int SuccessfulSources { get; init; }
    public int FailedSources { get; init; }
    public int NewArticles { get; init; }
    public long DurationMs { get; init; }
    public string Summary { get; init; } = "";
    // Régebbi (0.7.13-as) előzményekben nincs ilyen mező. Az üres alapérték megőrzi az olvashatóságot.
    public List<SourceRefreshResult> SourceResults { get; init; } = [];
    [JsonIgnore] public string FinishedDisplay => FinishedAtUtc.ToLocalTime().ToString("yyyy.MM.dd. HH:mm:ss");
    [JsonIgnore] public string DurationDisplay => $"{DurationMs / 1000.0:0.0} mp";
}

public sealed class RefreshHistoryStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RefreshHistoryEntry>? _cache;
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HirRadar", "refresh_history.json");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<RefreshHistoryEntry>> ReadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var entries = await LoadAsync();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
            return entries.Where(x => x.FinishedAtUtc >= cutoff)
                .OrderByDescending(x => x.FinishedAtUtc).Take(200).ToList();
        }
        finally { _gate.Release(); }
    }

    public async Task AppendAsync(RefreshHistoryEntry item)
    {
        await _gate.WaitAsync();
        try
        {
            var current = _cache ??= await LoadAsync();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
            var retained = current.Where(x => x.FinishedAtUtc >= cutoff)
                .Append(item)
                .OrderByDescending(x => x.FinishedAtUtc)
                .Take(200)
                .ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Az előző ép fájl megmarad, amíg az új teljesen ki nem íródott.
            string temp = _path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(retained, Options));
                File.Move(temp, _path, true);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } }
            _cache = retained; // Csak sikeres atomikus fájlcsere után.
        }
        finally { _gate.Release(); }
    }

    private async Task<List<RefreshHistoryEntry>> LoadAsync()
    {
        if (!File.Exists(_path)) return [];
        string json = await File.ReadAllTextAsync(_path);
        return JsonSerializer.Deserialize<List<RefreshHistoryEntry>>(json, Options) ?? [];
    }
}
