using System.IO;
using System.Text.Json;

namespace HirRadar;

public sealed class NewsStore
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HirRadar");

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly JsonSerializerOptions _compactJson = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NewsStore() => Directory.CreateDirectory(DataDirectory);

    public async Task<List<NewsSource>> LoadSourcesAsync()
    {
        string path = Path.Combine(DataDirectory, "sources.json");
        if (!File.Exists(path))
        {
            string freshMarker = Path.Combine(DataDirectory, "alpha_0_7_regional_applied.txt");
            if (!File.Exists(freshMarker))
                await File.WriteAllTextAsync(freshMarker, "Regional defaults were included at first start.");
            string expansionMarker = Path.Combine(DataDirectory, "alpha_0_7_3_sources_applied.txt");
            if (!File.Exists(expansionMarker))
                await File.WriteAllTextAsync(expansionMarker, "Expanded sources included on fresh install.");
            // Az Alpha 0.7.9 alapforrásai BAON-t és Qubitet sem tartalmaznak.
            return DefaultSources();
        }
        List<NewsSource> sources;
        await using (var stream = File.OpenRead(path))
            sources = await JsonSerializer.DeserializeAsync<List<NewsSource>>(stream, _json) ?? [];

        // Egyszeri, visszafelé kompatibilis bővítés. Ha a felhasználó később törli
        // a két új forrást, nem jelennek meg újra az alkalmazás indításakor.
        string migration = Path.Combine(DataDirectory, "alpha_0_2_sources_applied.txt");
        if (!File.Exists(migration))
        {
            string backup = path + ".before-alpha0.2.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            var extras = AdditionalSources();
            foreach (var extra in extras)
            {
                if (!sources.Any(s => string.Equals(s.FeedUrl.TrimEnd('/'), extra.FeedUrl.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase))) sources.Add(extra);
            }
            await SaveSourcesAsync(sources);
            await File.WriteAllTextAsync(migration, "HírRadar Alpha 0.2 – új források egyszeri hozzáadása.");
        }
        await AddRegionalSourcesOnceAsync(sources, path);
        await EnableUntestedRegionalSourcesOnceAsync(sources, path);
        await AddExtendedSourcesOnceAsync(sources, path);
        await RemoveAutoAddedQubitOnceAsync(sources, path);
        await RemoveBaonSourcesOnceAsync(sources, path);
        return sources;
    }

    private async Task AddRegionalSourcesOnceAsync(List<NewsSource> sources, string path)
    {
        // Only once: later user deletions remain deleted; defaults are disabled until tested.
        string marker = Path.Combine(DataDirectory, "alpha_0_7_regional_applied.txt");
        if (File.Exists(marker)) return;
        var additional = RegionalSources();
        var candidates = additional.Where(extra => !sources.Any(s =>
            string.Equals(s.FeedUrl.TrimEnd('/'), extra.FeedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))).ToList();
        if (candidates.Count > 0)
        {
            string backup = path + ".before-alpha0.7.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            sources.AddRange(candidates);
            await SaveSourcesAsync(sources);
        }
        await File.WriteAllTextAsync(marker, "HírRadar regional sources – once. User changes preserved.");
    }

    private async Task EnableUntestedRegionalSourcesOnceAsync(List<NewsSource> sources, string path)
    {
        // A 0.7.0 verzió a regionális portálokat alapból inaktívan adta hozzá.
        // A 0.7.1 csak azokat aktiválja egyszer, amelyeket még soha nem ellenőriztek.
        // A ténylegesen használt vagy már ellenőrzött tiltott forrásokat tiszteletben tartjuk.
        string marker = Path.Combine(DataDirectory, "alpha_0_7_1_regional_enabled.txt");
        if (File.Exists(marker)) return;
        var regionalHosts = new[] { "bajahangja.hu", "kecsup.hu" };
        var missing = RegionalSources().Where(extra => !sources.Any(source =>
            Uri.TryCreate(source.FeedUrl, UriKind.Absolute, out var url) &&
            Uri.TryCreate(extra.FeedUrl, UriKind.Absolute, out var expected) &&
            url.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase))).ToList();
        var targets = sources.Where(source =>
            regionalHosts.Any(host => Uri.TryCreate(source.FeedUrl, UriKind.Absolute, out var url) &&
                (url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                 url.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))) &&
            !source.Enabled && !source.LastCheckedUtc.HasValue && !source.LastSuccessUtc.HasValue).ToList();
        if (targets.Count > 0 || missing.Count > 0)
        {
            string backup = path + ".before-alpha0.7.1.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            foreach (var source in targets) source.Enabled = true;
            sources.AddRange(missing);
            await SaveSourcesAsync(sources);
        }
        await File.WriteAllTextAsync(marker, "Previously untested regional sources enabled once; user choices otherwise retained.");
    }


    private async Task AddExtendedSourcesOnceAsync(List<NewsSource> sources, string path)
    {
        // Egyszeri, adatmegőrző bővítés; később eltávolított forrást nem teszünk vissza.
        const string markerName = "alpha_0_7_3_sources_applied.txt";
        string marker = Path.Combine(DataDirectory, markerName);
        if (File.Exists(marker)) return;
        var additions = ExpandedSources().Where(candidate =>
        {
            if (!Uri.TryCreate(candidate.FeedUrl, UriKind.Absolute, out var wanted)) return false;
            return !sources.Any(existing =>
                Uri.TryCreate(existing.FeedUrl, UriKind.Absolute, out var current) &&
                string.Equals(RemoveWww(current.Host), RemoveWww(wanted.Host),
                    StringComparison.OrdinalIgnoreCase));
        }).ToList();
        if (additions.Count > 0)
        {
            string backup = path + ".before-alpha0.7.3.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            sources.AddRange(additions);
            await SaveSourcesAsync(sources);
        }
        await File.WriteAllTextAsync(marker,
            $"Expanded source catalog applied once: {DateTimeOffset.UtcNow:O}; added {additions.Count}; user changes preserved.");
    }

    private async Task RemoveAutoAddedQubitOnceAsync(List<NewsSource> sources, string path)
    {
        // Alpha 0.7.4: csak a 0.7.3-ban automatikusan hozzáadott, változatlan
        // nevű Qubit-forrást távolítjuk el. Más, felhasználó által szerkesztett
        // forrásokat nem módosítunk. A korábbi lista visszaállítható a mentésből.
        string marker = Path.Combine(DataDirectory, "alpha_0_7_4_qubit_source_removed.txt");
        if (File.Exists(marker)) return;
        var autoAdded = sources.Where(source =>
            string.Equals(source.Name.Trim(), "Qubit", StringComparison.OrdinalIgnoreCase) &&
            IsHost(source.FeedUrl, "qubit.hu")).ToList();
        if (autoAdded.Count > 0)
        {
            string backup = path + ".before-alpha0.7.4.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            foreach (var source in autoAdded) sources.Remove(source);
            await SaveSourcesAsync(sources);
        }
        await File.WriteAllTextAsync(marker,
            $"Auto-added Qubit removed: {autoAdded.Count}; {DateTimeOffset.UtcNow:O}. Existing user sources kept.");
    }

    // 0.7.9: a BAON forrás megszüntetése a felhasználó kérésére, egyszeri migrációval.
    // Az eredeti teljes sources.json és news.json megmarad külön biztonsági másolatban.
    private async Task RemoveBaonSourcesOnceAsync(List<NewsSource> sources, string path)
    {
        string marker = Path.Combine(DataDirectory, "alpha_0_7_9_baon_removed.txt");
        if (File.Exists(marker)) return;
        var unwanted = sources.Where(s => IsBaonSource(s.Name, s.FeedUrl)).ToList();
        if (unwanted.Count > 0)
        {
            string backup = path + ".before-alpha0.7.9.bak";
            if (!File.Exists(backup)) File.Copy(path, backup);
            string idsPath = Path.Combine(DataDirectory, "alpha_0_7_9_baon_source_ids.json");
            await File.WriteAllTextAsync(idsPath, JsonSerializer.Serialize(unwanted.Select(s => s.Id).ToList()));
            foreach (var source in unwanted) sources.Remove(source);
            await SaveSourcesAsync(sources);
        }
        await File.WriteAllTextAsync(marker,
            $"BAON sources removed: {unwanted.Count}; {DateTimeOffset.UtcNow:O}");
    }

    private static bool IsBaonSource(string? name, string? feedUrl) =>
        (name?.Trim().StartsWith("BAON", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (feedUrl is not null && IsHost(feedUrl, "baon.hu"));

    private static bool IsHost(string candidateUrl, string expectedHost) =>
        Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith("." + expectedHost, StringComparison.OrdinalIgnoreCase));

    private static string RemoveWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    public async Task<List<NewsItem>> LoadItemsAsync()
    {
        string path = Path.Combine(DataDirectory, "news.json");
        if (!File.Exists(path)) return [];

        List<NewsItem> items;
        await using (var stream = File.OpenRead(path))
            items = await JsonSerializer.DeserializeAsync<List<NewsItem>>(stream, _json) ?? [];

        // Egyszeri takarítás az előző verzióban automatikusan beszedett Qubit-hírekre.
        // A teljes eredeti hírtár megmarad egy .bak fájlban, a csillagozásokkal együtt.
        // A felhasználó később manuálisan ismét felveheti a Qubitet: az ilyen új híreket
        // ez az egyszeri migráció már nem távolítja el.
        string marker = Path.Combine(DataDirectory, "alpha_0_7_4_qubit_articles_removed.txt");
        if (!File.Exists(marker))
        {
            var unwanted = items.Where(item =>
                string.Equals(item.SourceName.Trim(), "Qubit", StringComparison.OrdinalIgnoreCase)).ToList();
            if (unwanted.Count > 0)
            {
                string backup = path + ".before-alpha0.7.4.bak";
                if (!File.Exists(backup)) File.Copy(path, backup);
                items.RemoveAll(item => unwanted.Contains(item));
                await SaveItemsAsync(items);
            }
            await File.WriteAllTextAsync(marker,
                $"Auto-imported Qubit articles archived: {unwanted.Count}; {DateTimeOffset.UtcNow:O}");
        }
        // A BAON forrásból beolvasott régi Google RSS-híreket is eltávolítjuk.
        // A hírtár teljes egészéről mentés készül még bármilyen módosítás előtt.
        string baonMarker = Path.Combine(DataDirectory, "alpha_0_7_9_baon_articles_removed.txt");
        if (!File.Exists(baonMarker))
        {
            string idsPath = Path.Combine(DataDirectory, "alpha_0_7_9_baon_source_ids.json");
            HashSet<string> removedIds = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(idsPath))
            {
                var storedIds = JsonSerializer.Deserialize<List<string>>(await File.ReadAllTextAsync(idsPath)) ?? [];
                removedIds = new HashSet<string>(storedIds, StringComparer.OrdinalIgnoreCase);
            }
            var removed = items.Where(item =>
                removedIds.Contains(item.SourceId) ||
                IsBaonSource(item.SourceName, item.Url)).ToList();
            if (removed.Count > 0)
            {
                string backup = path + ".before-alpha0.7.9.bak";
                if (!File.Exists(backup)) File.Copy(path, backup);
                items.RemoveAll(item => removed.Contains(item));
                await SaveItemsAsync(items);
            }
            await File.WriteAllTextAsync(baonMarker,
                $"BAON articles archived: {removed.Count}; {DateTimeOffset.UtcNow:O}");
        }
        return items;
    }

    public async Task<HirRadarSettings> LoadSettingsAsync()
    {
        string path = Path.Combine(DataDirectory, "settings.json");
        if (!File.Exists(path)) return new HirRadarSettings();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<HirRadarSettings>(stream, _json) ?? new HirRadarSettings();
    }

    public Task SaveSettingsAsync(HirRadarSettings settings) => SaveAtomicAsync("settings.json", settings);

    public async Task<WatchRules> LoadWatchRulesAsync()
    {
        string path = Path.Combine(DataDirectory, "watch_rules.json");
        if (!File.Exists(path)) return new WatchRules();
        await using var stream = File.OpenRead(path);
        var saved = await JsonSerializer.DeserializeAsync<WatchRules>(stream, _json) ?? new WatchRules();
        saved.Keywords = WatchRules.CleanLines(saved.Keywords);
        saved.Locations = WatchRules.CleanLines(saved.Locations);
        return saved;
    }

    public Task SaveWatchRulesAsync(WatchRules rules) => SaveAtomicAsync("watch_rules.json", rules);

    public async Task<TopicRules> LoadTopicRulesAsync()
    {
        string path = Path.Combine(DataDirectory, "topic_rules.json");
        if (!File.Exists(path)) return TopicRules.Defaults();
        await using var stream = File.OpenRead(path);
        return TopicRules.Sanitize(await JsonSerializer.DeserializeAsync<TopicRules>(stream, _json));
    }

    public Task SaveTopicRulesAsync(TopicRules rules) => SaveAtomicAsync("topic_rules.json", TopicRules.Sanitize(rules));


    public async Task<List<ViewPreset>> LoadViewPresetsAsync()
    {
        string path = Path.Combine(DataDirectory, "saved_views.json");
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        var entries = await JsonSerializer.DeserializeAsync<List<ViewPreset>>(stream, _json);
        return ViewPreset.Sanitize(entries);
    }

    public Task SaveViewPresetsAsync(IEnumerable<ViewPreset> presets) =>
        SaveAtomicAsync("saved_views.json", ViewPreset.Sanitize(presets));

    public Task SaveSourcesAsync(IEnumerable<NewsSource> sources) => SaveAtomicAsync("sources.json", sources.ToList());
    public Task SaveItemsAsync(IEnumerable<NewsItem> items) => SaveAtomicAsync("news.json", items.ToList());

    private async Task SaveAtomicAsync<T>(string filename, T value)
    {
        await _saveGate.WaitAsync();
        try
        {
            string path = Path.Combine(DataDirectory, filename);
            string temp = path + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, value,
                    filename == "news.json" ? _compactJson : _json);
            File.Move(temp, path, true);
        }
        finally { _saveGate.Release(); }
    }

    private static List<NewsSource> AdditionalSources() =>
    [
        new() { Name = "Infostart – Belföld", FeedUrl = "https://infostart.hu/belfold/rss/" },
        new() { Name = "Népszava", FeedUrl = "https://nepszava.hu/feed/" }
    ];

    private static List<NewsSource> DefaultSources() =>
    [
        new() { Name = "Telex – Belföld", FeedUrl = "https://telex.hu/rss/mstag/belfold" },
        new() { Name = "HVG – Itthon", FeedUrl = "https://hvg.hu/rss/itthon" },
        new() { Name = "24.hu – Belföld", FeedUrl = "https://24.hu/belfold/feed/" },
        new() { Name = "444", FeedUrl = "https://444.hu/feed" },
        new() { Name = "Portfolio – Gazdaság", FeedUrl = "https://www.portfolio.hu/rss/gazdasag.xml" },
        .. AdditionalSources(),
        .. RegionalSources(),
        .. ExpandedSources()
    ];

    // A nyilvános RSS-címeket használjuk, ahol megtalálhatóak; egyébként HTML-lista.
    // RSS nélküli kormany.hu/hirek minden esetben saját HTML feldolgozóval indul.
    // Az oldalak működése változhat, ezért a forrásdiagnosztikát a felhasználó teszteli.
    private static List<NewsSource> ExpandedSources() =>
    [
        new() { Name = "Kormány.hu – hivatalos hírek", FeedUrl = "https://kormany.hu/hirek" },
        new() { Name = "Magyar Hang – Belföld", FeedUrl = "https://hang.hu/belfold" },
        new() { Name = "Index – 24 óra", FeedUrl = "https://index.hu/24ora/rss/" },
        new() { Name = "Origo", FeedUrl = "https://www.origo.hu/publicapi/hu/rss/origo/articles" },
        new() { Name = "Magyar Nemzet", FeedUrl = "https://magyarnemzet.hu/" },
        new() { Name = "Híradó.hu – közmédia", FeedUrl = "https://hirado.hu/" },
        new() { Name = "Válasz Online", FeedUrl = "https://www.valaszonline.hu/" },
        new() { Name = "Átlátszó", FeedUrl = "https://atlatszo.hu/" },
        new() { Name = "Direkt36", FeedUrl = "https://www.direkt36.hu/" },
        new() { Name = "Magyar Narancs", FeedUrl = "https://magyarnarancs.hu/" },
        new() { Name = "Mérce", FeedUrl = "https://merce.hu/" },
        new() { Name = "G7", FeedUrl = "https://g7.hu/" }
    ];

    private static List<NewsSource> RegionalSources() =>
    [
        new() { Name = "Baja Hangja – Baja", FeedUrl = "https://bajahangja.hu/", Enabled = true },
        new() { Name = "KecsUP – Kecskemét", FeedUrl = "https://kecsup.hu/", Enabled = true }
    ];
}
