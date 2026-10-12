using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace HirRadar;

public partial class MainWindow : Window
{
    private readonly ElasticWindowDrag _elasticWindowDrag;
    private readonly NewsStore _store = new();
    private readonly FeedReader _reader = new();
    private readonly ArticleReader _articleReader = new();
    private readonly ImageCacheManager _imageCache = new();
    private readonly RefreshHistoryStore _refreshHistory = new();
    private NewsItem? _expandedItem;
    private CancellationTokenSource? _articleRequest;
    private readonly ObservableCollection<NewsItem> _allItems = [];
    private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _fetchLimit = new(3, 3);
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private DateTimeOffset _nextHealthSample = DateTimeOffset.MinValue;
    private ICollectionView? _view;
    private List<NewsSource> _sources = [];
    private bool _checking;
    private bool _closing;
    private bool _exitRequested;
    private bool _initialized;
    private readonly bool _launchMinimized;
    private HirRadarSettings _settings = new();
    private WatchRules _watchRules = new();
    private TopicRules _topicRules = TopicRules.Defaults();
    private List<ViewPreset> _savedViews = [];
    private bool _applyingPreset;
    private bool _updatingProfileChoice;
    private WinForms.NotifyIcon? _trayIcon;
    private WinForms.ContextMenuStrip? _trayMenu;
    private WinForms.ToolStripMenuItem? _notificationsMenuItem;
    private DateTime _lastFilterDay = DateTime.Today;
    private DateTimeOffset _nextCheck = DateTimeOffset.MaxValue;
    private sealed record SourceChoice(string Id, string Name);
    private sealed record TopicChoice(string Id, string Name);
    private sealed record ProfileChoice(string Id, string Name);
    private sealed record SourceResult(NewsSource Source, FeedReadResult? Feed, string? Error,
        DateTimeOffset CheckedUtc, long DurationMs);
    // Az indítóképernyő csak a ténylegesen elindult műveleteket mutatja.
    public event Action<string>? StartupStatusChanged;
    public event Action? StartupAttentionRequired;

    private void ReportStartupStatus(string message) => StartupStatusChanged?.Invoke(message);
    private void ShowStartupWarning(string message, string title, MessageBoxImage image)
    {
        StartupAttentionRequired?.Invoke();
        MessageBox.Show(this, message, title, MessageBoxButton.OK, image);
    }

    public MainWindow()
    {
        InitializeComponent();
        _elasticWindowDrag = new ElasticWindowDrag(this, WindowContent);
        _launchMinimized = Environment.GetCommandLineArgs().Skip(1)
            .Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (_launchMinimized)
        {
            WindowState = WindowState.Minimized;
            ShowInTaskbar = false;
        }
        _clock.Tick += Clock_Tick;
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            if (!_closing) ApplyFilters();
        };
        Application.Current.SessionEnding += Application_SessionEnding;
    }

    private void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // A Windows leállítását/kijelentkezést ne akadályozza a "bezárás a tálcára" beállítás.
        _exitRequested = true;
    }

    private static Drawing.Icon LoadApplicationIcon()
    {
        try
        {
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                using var extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                    return (Drawing.Icon)extracted.Clone();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Az alkalmazásikon nem tölthető be: " + ex);
        }

        return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
    }

    private void InitializeTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        var showItem = new WinForms.ToolStripMenuItem("HírRadar megnyitása");
        showItem.Font = new Drawing.Font(showItem.Font, Drawing.FontStyle.Bold);
        showItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ShowFromTray));
        menu.Items.Add(showItem);

        var refreshItem = new WinForms.ToolStripMenuItem("Frissítés most");
        refreshItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => { _ = RefreshAsync(); }));
        menu.Items.Add(refreshItem);

        _notificationsMenuItem = new WinForms.ToolStripMenuItem("Általános hírértesítések")
        {
            Checked = _settings.NotifyOnNewArticles,
            CheckOnClick = false
        };
        _notificationsMenuItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(async () =>
        {
            _settings.NotifyOnNewArticles = !_settings.NotifyOnNewArticles;
            if (_notificationsMenuItem is not null)
                _notificationsMenuItem.Checked = _settings.NotifyOnNewArticles;
            try { await _store.SaveSettingsAsync(_settings); }
            catch (Exception ex) { StatusText.Text = "Értesítési beállítás mentése sikertelen: " + ex.Message; }
        }));
        menu.Items.Add(_notificationsMenuItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem("Kilépés");
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication));
        menu.Items.Add(exitItem);

        // WPF alatt a NotifyIcon automatikus ContextMenuStrip-felnyitása nem minden
        // környezetben megbízható. Nem rendeljük hozzá az ikonhoz; a jobb egérgomb
        // felengedésekor explicit nyitjuk meg a menüt, a főablak állapotától függetlenül.
        _trayMenu = menu;
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "HírRadar – Belföldi hírfigyelő",
            Visible = true
        };
        _trayIcon.MouseUp += TrayIcon_MouseUp;
        _trayIcon.MouseDoubleClick += TrayIcon_MouseDoubleClick;
    }

    private void TrayIcon_MouseUp(object? sender, WinForms.MouseEventArgs e)
    {
        if (_closing || e.Button != WinForms.MouseButtons.Right || _trayMenu is null)
            return;

        // A menüt pontosan ott jelenítjük meg, ahol a jobb kattintás történt.
        // Ez akkor is működhet, ha a főablak látható, minimalizált vagy rejtett.
        try
        {
            if (_trayMenu.Visible)
                _trayMenu.Close();
            _trayMenu.Show(WinForms.Cursor.Position);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("A HírRadar tálcamenüje nem nyitható meg: " + ex);
            AppLogger.Error("Tálcamenü megjelenítési hiba", ex);
        }
    }

    private void TrayIcon_MouseDoubleClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (!_closing && e.Button == WinForms.MouseButtons.Left)
            Dispatcher.BeginInvoke(new Action(ShowFromTray));
    }

    private void HideToTray(bool showHint)
    {
        if (_trayIcon is null) return;
        Hide();
        // Nem jelenítünk meg automatikus ballonértesítést elrejtéskor:
        // a ballon első kattintása elnyelheti a tálcaikon jobb klikkjét.
    }

    private void ShowFromTray()
    {
        if (_closing) return;
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Show();
        Activate();
        Focus();
    }

    private void ExitApplication()
    {
        if (_closing) return;
        _exitRequested = true;
        Close();
    }

    private void TryShowTrayMessage(string title, string description)
    {
        if (_trayIcon is null) return;
        try
        {
            _trayIcon.ShowBalloonTip(4500, title, description, WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            // Az operációs rendszer az értesítést letilthatja. A hírek feldolgozása ettől független.
            Debug.WriteLine("A tálcaértesítés nem jeleníthető meg: " + ex);
        }
    }

    private void NotifyNewArticles(IReadOnlyList<NewsItem> articles)
    {
        if (articles.Count == 0 || _trayIcon is null) return;
        // Egy ciklusban legfeljebb egy értesítés: a figyelt hírek elsőbbséget kapnak.
        var watched = articles.Where(x => x.IsWatched).ToList();
        if (_watchRules.NotifyOnMatches && watched.Count > 0)
        {
            string priority = watched.Any(x => x.IsLocalMatch) ? "helyi / figyelt" : "figyelt";
            string title = $"{watched.Count} új {priority} hír";
            string text = $"{watched[0].SourceName}: {watched[0].Title}";
            if (text.Length > 200) text = text[..197] + "...";
            TryShowTrayMessage(title, text);
            return;
        }
        if (!_settings.NotifyOnNewArticles) return;
        string normalTitle = articles.Count == 1 ? "1 új hír érkezett" : $"{articles.Count} új hír érkezett";
        string normalText = $"{articles[0].SourceName}: {articles[0].Title}";
        if (normalText.Length > 200) normalText = normalText[..197] + "...";
        TryShowTrayMessage(normalTitle, normalText);
    }

    public async Task InitializeForStartupAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            ReportStartupStatus("Beállítások betöltése...");
            try { _settings = await _store.LoadSettingsAsync(); }
            catch (Exception ex)
            {
                _settings = new HirRadarSettings();
                ShowStartupWarning("A háttérműködés beállításai nem olvashatók; az alapértékeket használjuk.\n\n" + ex.Message,
                    "HírRadar – beállítások", MessageBoxImage.Warning);
            }
            ReportStartupStatus("Értesítési terület előkészítése...");
            try { InitializeTrayIcon(); }
            catch (Exception ex)
            {
                WindowState = WindowState.Normal;
                ShowInTaskbar = true;
                Activate();
                ShowStartupWarning("A rendszertálca nem inicializálható. A HírRadar ablakos módban használható.\n\n" + ex.Message,
                    "HírRadar – rendszertálca", MessageBoxImage.Warning);
            }
            // Windows-autostart esetén már az első lekérdezés alatt rejtve futunk.
            if (_trayIcon is not null && _launchMinimized)
                HideToTray(false);

            ReportStartupStatus("Hírforrások betöltése...");
            _sources = await _store.LoadSourcesAsync();
            ReportStartupStatus("Kulcsszavak és mentett szűrők betöltése...");
            try { _watchRules = await _store.LoadWatchRulesAsync(); }
            catch (Exception ex)
            {
                _watchRules = new WatchRules();
                ShowStartupWarning("A figyelési szabályok nem olvashatók; a fájl változatlan marad, és ideiglenesen az alapértékeket használjuk.\n\n" + ex.Message,
                    "HírRadar – figyelések", MessageBoxImage.Warning);
            }
            try { _savedViews = await _store.LoadViewPresetsAsync(); }
            catch (Exception ex)
            {
                _savedViews = [];
                ShowStartupWarning("A mentett nézetek fájlja nem olvasható. A fájlt nem módosítjuk automatikusan.\n\n" + ex.Message,
                    "HírRadar – mentett nézetek", MessageBoxImage.Warning);
            }
            try
            {
                _topicRules = await _store.LoadTopicRulesAsync();
                TopicClassifier.Configure(_topicRules);
            }
            catch (Exception ex)
            {
                _topicRules = TopicRules.Defaults();
                TopicClassifier.Configure(_topicRules);
                AppLogger.Error("Témaszabályok betöltése", ex);
                ShowStartupWarning("A témaszabályok nem olvashatók. Az alapértékek ideiglenesen aktívak; a fájlt nem módosítjuk.\n\n" + ex.Message,
                    "HírRadar – témák", MessageBoxImage.Warning);
            }
            ReportStartupStatus("Hírarchívum beolvasása...");
            var items = await _store.LoadItemsAsync();
            // A betöltött NewsItem objektumok még nincsenek UI elemhez kötve:
            // deduplikálás, kulcsszavas/témás besorolás és csoportosítás háttérszálon.
            // A kész kollekciót csak ezután adjuk át a WPF-nek a UI-szálon.
            var prepared = await Task.Run(() =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var ordered = new List<NewsItem>(items.Count);
                foreach (var item in items.OrderByDescending(x => x.PublishedUtc))
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id)) continue;
                    item.IsNew = false;
                    WatchMatcher.Apply(item, _watchRules);
                    TopicClassifier.Apply(item);
                    ordered.Add(item);
                }
                EventGrouper.Apply(ordered);
                return ordered;
            }, _lifetime.Token);
            foreach (var item in prepared)
            {
                _knownIds.Add(item.Id);
                _allItems.Add(item);
            }
            _view = CollectionViewSource.GetDefaultView(_allItems);
            _view.Filter = AcceptItem;
            _view.SortDescriptions.Add(new SortDescription(nameof(NewsItem.PublishedUtc), ListSortDirection.Descending));
            NewsList.ItemsSource = _view;
            ReportStartupStatus($"{_allItems.Count} hír rendezése és kategorizálása...");
            RebuildSourceChoices();
            RebuildTopicChoices();
            RebuildProfileChoices();
            UpdateWatchSummary();
            ApplyFilters();
            _clock.Start();
            UpdateCacheSize();
            ReportStartupStatus("Friss hírek letöltésének indítása...");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _clock.Start();
            StatusText.Text = "Betöltési hiba: " + ex.Message;
            ShowStartupWarning("A helyi adatokat nem sikerült beolvasni. A fájlokat nem írtuk felül.\n\n" + ex.Message +
                "\n\nAdatmappa: " + _store.DataDirectory,
                "HírRadar – betöltési hiba", MessageBoxImage.Error);
        }
    }

    private void Clock_Tick(object? sender, EventArgs e)
    {
        if (_closing) return;
        if (!_checking && DateTimeOffset.UtcNow >= _nextCheck) _ = RefreshAsync();
        NextCheckText.Text = _checking ? "Források ellenőrzése folyamatban..." :
            _nextCheck == DateTimeOffset.MaxValue ? "Következő ellenőrzés: —" :
            "Következő ellenőrzés: " + _nextCheck.ToLocalTime().ToString("HH:mm:ss");
        if (DateTimeOffset.UtcNow >= _nextHealthSample)
        {
            _nextHealthSample = DateTimeOffset.UtcNow.AddMinutes(5);
            RuntimeHealth.Report(_allItems.Count, _checking);
        }
        if (_lastFilterDay != DateTime.Today)
        {
            _lastFilterDay = DateTime.Today;
            ApplyFilters();
        }
    }

    private async Task<SourceResult> ReadSourceAsync(NewsSource source, CancellationToken token)
    {
        // A várakozási sor az alkalmazás leállításakor megszakítható.
        await _fetchLimit.WaitAsync(token);
        var sw = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        try
        {
            // Egy átmeneti HTTP/hálózati hiba esetén legfeljebb egy második próba.
            // A két próbára összesen 55 mp jut, nem próbálunk végtelenül egy forrást.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    // A HTML/XML feldolgozás CPU-munkája a ThreadPoolon fusson,
                    // ne a WPF dispatcher szálán állítsa meg a görgetést.
                    FeedReadResult feed = await Task.Run(
                        () => _reader.ReadAsync(source, deadline.Token), deadline.Token);
                    return new SourceResult(source, feed, null, DateTimeOffset.UtcNow, sw.ElapsedMilliseconds);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    return new SourceResult(source, null, "Időtúllépés: a forrás 55 másodpercen belül nem válaszolt.",
                        DateTimeOffset.UtcNow, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    bool transient = ex is HttpRequestException http &&
                        (http.StatusCode is null || (int)http.StatusCode >= 500 ||
                         http.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        || ex is TaskCanceledException;
                    if (!transient || attempt > 0)
                        return new SourceResult(source, null, ex.Message, DateTimeOffset.UtcNow, sw.ElapsedMilliseconds);
                    try { await Task.Delay(TimeSpan.FromMilliseconds(850), deadline.Token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (OperationCanceledException)
                    {
                        return new SourceResult(source, null, "Időtúllépés a forrás újrapróbálásakor.",
                            DateTimeOffset.UtcNow, sw.ElapsedMilliseconds);
                    }
                }
            }
            return new SourceResult(source, null, "Ismeretlen hálózati hiba", DateTimeOffset.UtcNow, sw.ElapsedMilliseconds);
        }
        finally { _fetchLimit.Release(); }
    }

    private async Task RefreshAsync()
    {
        if (_checking || _closing) return;
        _checking = true;
        _nextCheck = DateTimeOffset.UtcNow.AddMinutes(5);
        var elapsed = Stopwatch.StartNew();
        CheckButton.IsEnabled = false;
        SourcesButton.IsEnabled = false;
        int processed = 0, successful = 0, added = 0;
        bool canceled = false;
        string cycleError = "";
        var notificationItems = new List<NewsItem>();
        var errors = new List<string>();
        // Ciklusonkénti forrásdiagnosztika (csak állapotadatok, cikktartalom nélkül).
        var sourceResults = new List<SourceRefreshResult>();
        var enabled = _sources.Where(s => s.Enabled).ToList();
        ReportStartupStatus($"Hírek ellenőrzése: 0/{enabled.Count} forrás...");
        try
        {
            var pending = enabled.Select(s => ReadSourceAsync(s, _lifetime.Token)).ToList();
            while (pending.Count > 0)
            {
                Task<SourceResult> finished = await Task.WhenAny(pending);
                pending.Remove(finished);
                SourceResult outcome = await finished;
                processed++;
                var source = outcome.Source;
                bool hadBaseline = source.LastSuccessUtc.HasValue;
                int articlesBefore = added;
                source.LastCheckedUtc = outcome.CheckedUtc;
                source.LastDurationMs = outcome.DurationMs;
                if (outcome.Feed is { } feed)
                {
                    successful++;
                    source.LastSuccessUtc = outcome.CheckedUtc;
                    source.LastError = "";
                    source.ConsecutiveFailures = 0;
                    source.LastMethod = feed.Method;
                    source.LastItemCount = feed.Items.Count;
                    source.LastDatedCount = feed.Items.Count(x => x.HasPublishedDate);
                    source.LastRecentCount = feed.Items.Count(x => x.HasPublishedDate && x.PublishedUtc > DateTimeOffset.UtcNow.AddHours(-24));
                    // WPF: a CollectionView.DeferRefresh() közbeni ObservableCollection.Add()
                    // CurrentItem/SelectionChanged ellenőrzést válthat ki az ItemsControlból,
                    // ami InvalidOperationException-t dobhat ("Cannot change or check ...").
                    // Élő ItemsSource mellett ezért NEM deferral alatt módosítjuk a listát.
                    foreach (var article in feed.Items)
                    {
                        if (_knownIds.Contains(article.Id)) continue;
                        // Első szinkron: nem jelölünk minden régi RSS bejegyzést friss hírnek.
                        article.IsNew = hadBaseline;
                        WatchMatcher.Apply(article, _watchRules);
                        TopicClassifier.Apply(article);
                        // Az ID csak sikeres listába helyezés után legyen ismert.
                        // Korábban kivételkor azonosító maradhatott bent cikk nélkül,
                        // emiatt a következő kör is kihagyhatta a hírt.
                        _allItems.Add(article);
                        _knownIds.Add(article.Id);
                        added++;
                        // Régi feed-archívum beolvasásakor ne árasszuk el a gépet értesítésekkel.
                        if (article.IsNew && (!article.HasPublishedDate ||
                            article.PublishedUtc >= DateTimeOffset.UtcNow.AddHours(-6)))
                            notificationItems.Add(article);
                    }
                }
                else
                {
                    source.LastError = outcome.Error ?? "Ismeretlen lekérési hiba";
                    source.ConsecutiveFailures = Math.Min(source.ConsecutiveFailures + 1, 9999);
                    AppLogger.Warning($"Forráshiba: {source.Name} · {source.LastError}");
                    source.LastItemCount = 0;
                    errors.Add(source.Name + ": " + source.LastError);
                }
                sourceResults.Add(new SourceRefreshResult
                {
                    SourceId = source.Id,
                    SourceName = source.Name,
                    Succeeded = outcome.Feed is not null,
                    DurationMs = outcome.DurationMs,
                    ArticlesFound = outcome.Feed?.Items.Count ?? 0,
                    NewArticles = added - articlesBefore,
                    Method = outcome.Feed?.Method ?? "",
                    // A diagnosztika méretének korlátozása. Nem tartalmaz cikkszöveget.
                    Error = string.IsNullOrEmpty(outcome.Error) ? "" :
                        outcome.Error.Length > 350 ? outcome.Error[..350] : outcome.Error
                });
                StatusText.Text = $"Ellenőrzés: {processed}/{enabled.Count} · {successful} sikeres · {added} újonnan talált cikk";
                ReportStartupStatus($"Hírek letöltése: {processed}/{enabled.Count} forrás · {added} új hír");
            }

            // A megjelölt cikkeket megőrizzük. A teljes lista méretét korlátozzuk.
            int trimmed = 0;
            if (_allItems.Count > 12000)
            {
                int surplus = _allItems.Count - 12000;
                // A csillagozott hírek maradjanak meg; a forrásokból újra felismert
                // régi azonosítókat továbbra is megjegyezzük a futó munkamenetben.
                var toTrim = _allItems.Where(x => !x.IsStarred)
                    .OrderBy(x => x.PublishedUtc).Take(surplus).ToList();
                // Ugyanaz a WPF CollectionView-szabály érvényes a Remove()-ra is.
                // A régi hírek kivételénél sem használunk DeferRefresh()-t.
                foreach (var old in toTrim) { _allItems.Remove(old); trimmed++; }
            }
            if (!_lifetime.IsCancellationRequested)
            {
                // Üres ciklusban ne írjuk újra a akár 12 000 elemes hírarchívumot,
                // és ne számoljuk újra a kapcsolatokat. A forrásstátusz viszont mentendő.
                if (added > 0 || trimmed > 0)
                {
                    await _store.SaveItemsAsync(_allItems);
                    EventGrouper.Apply(_allItems);
                    ApplyFilters();
                    UpdateWatchSummary();
                }
                await _store.SaveSourcesAsync(_sources);
                StatusText.Text = $"Utolsó ellenőrzés: {DateTime.Now:HH:mm:ss} · {successful}/{enabled.Count} sikeres · " +
                    $"{added} újonnan talált cikk" + (errors.Count > 0 ? $" · {errors.Count} hibás forrás (részletek: Források...)" : " · minden forrás rendben");
                StatusText.ToolTip = errors.Count == 0 ? StatusText.Text : string.Join("\n", errors);
                NotifyNewArticles(notificationItems);
                AppLogger.Info($"Forrásellenőrzés: {enabled.Count} forrás, {successful} sikeres, " +
                    $"{errors.Count} hiba, {added} új cím, {elapsed.ElapsedMilliseconds} ms");
                ReportStartupStatus($"Hírfigyelés kész: {successful}/{enabled.Count} forrás · {added} új hír");
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { canceled = true; }
        catch (Exception ex)
        {
            cycleError = ex.GetType().Name + ": " + ex.Message;
            AppLogger.Error("Refresh", ex);
            StatusText.Text = "Feldolgozási / mentési hiba: " + ex.Message;
            StatusText.ToolTip = StatusText.Text;
        }
        finally
        {
            elapsed.Stop();
            if (!canceled && !_lifetime.IsCancellationRequested)
            {
                try
                {
                    await _refreshHistory.AppendAsync(new RefreshHistoryEntry
                    {
                        FinishedAtUtc = DateTimeOffset.UtcNow,
                        CheckedSources = processed,
                        SuccessfulSources = successful,
                        FailedSources = errors.Count + (cycleError.Length > 0 ? 1 : 0),
                        NewArticles = added,
                        DurationMs = elapsed.ElapsedMilliseconds,
                        SourceResults = sourceResults,
                        Summary = cycleError.Length > 0 ? "Feldolgozási hiba" :
                            errors.Count > 0 ? "Forráshibák voltak" : "Rendben"
                    });
                }
                catch (Exception ex) { AppLogger.Error("Refresh history persistence", ex); }
            }
            _checking = false;
            // A következő ellenőrzés az aktuális ciklus végétől indul:
            // lassú hálózat esetén sem sűrűsödnek össze a lekérések.
            _nextCheck = DateTimeOffset.UtcNow.AddMinutes(5);
            if (!_closing)
            {
                CheckButton.IsEnabled = true;
                SourcesButton.IsEnabled = true;
                // A forrásellenőrzés nem módosította a szűrőket.
                // Elkerüljük a második teljes újraszűrést ugyanabban a ciklusban.
                UpdateCacheSize();
            }
        }
    }

    private bool AcceptItem(object obj)
    {
        if (obj is not NewsItem item || !PassNonGroupFilters(item)) return false;
        if (GroupCompactCheck?.IsChecked == true && item.IsGroupSecondary &&
            item.RelatedItems.Any(r => !r.IsGroupSecondary && PassNonGroupFilters(r))) return false;
        return true;
    }

    private bool PassNonGroupFilters(NewsItem item)
    {
        string search = SearchBox?.Text?.Trim() ?? "";
        if (search.Length > 0 && !item.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            && !item.SourceName.Contains(search, StringComparison.CurrentCultureIgnoreCase)) return false;
        if (UnreadOnly?.IsChecked == true && item.IsRead) return false;
        if (StarredOnly?.IsChecked == true && !item.IsStarred) return false;
        int focus = FocusMode?.SelectedIndex ?? 0;
        if (focus == 1 && !item.IsWatched) return false;
        if (focus == 2 && !item.IsLocalMatch) return false;
        if (focus == 3 && !item.IsKeywordMatch) return false;
        var relevance = DomesticRelevanceClassifier.Assess(item);
        int domestic = DomesticMode?.SelectedIndex ?? 1;
        if (domestic == 1 && relevance == DomesticRelevance.LikelyForeign) return false;
        if (domestic == 2 && relevance != DomesticRelevance.Hungarian) return false;
        if (domestic == 3 && relevance != DomesticRelevance.LikelyForeign) return false;
        if (domestic == 4 && relevance != DomesticRelevance.Unknown) return false;
        if (SourceMode?.SelectedItem is SourceChoice choice && choice.Id.Length > 0 && item.SourceId != choice.Id) return false;
        if (TopicMode?.SelectedItem is TopicChoice topic && topic.Id.Length > 0 && !item.TopicIds.Contains(topic.Id)) return false;

        var local = (item.HasPublishedDate ? item.PublishedUtc : item.FirstSeenUtc).ToLocalTime();
        int mode = DateMode?.SelectedIndex ?? 0;
        if (mode == 0 && local.Date != DateTimeOffset.Now.Date) return false;
        if (mode == 1 && local < DateTimeOffset.Now.AddHours(-24)) return false;
        return true;
    }

    private void ApplyFilters()
    {
        if (_view is null) return;
        _view.Refresh();
        int visible = _view.Cast<object>().Count();
        CountText.Text = $"{visible} / {_allItems.Count} hír";
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (_applyingPreset) return;
        // Ha a betöltött profil bármely szűrőjét szerkesztik, az aktuális nézet egyéni lesz.
        if (ProfileMode is not null && ProfileMode.SelectedIndex > 0 && !_updatingProfileChoice)
        {
            _updatingProfileChoice = true;
            try { ProfileMode.SelectedIndex = 0; DeleteViewButton.IsEnabled = false; }
            finally { _updatingProfileChoice = false; }
        }
        // Gépelés közben ne szűrjük újra akár minden karakter után a teljes archívumot.
        // A legördülő szűrők és jelölőnégyzetek továbbra is azonnal frissülnek.
        if (e is TextChangedEventArgs && ReferenceEquals(sender, SearchBox))
        {
            _filterDebounce.Stop();
            _filterDebounce.Start();
        }
        else
        {
            _filterDebounce.Stop();
            ApplyFilters();
        }
    }

    private void RebuildSourceChoices()
    {
        string previous = (SourceMode.SelectedItem as SourceChoice)?.Id ?? "";
        var choices = new List<SourceChoice> { new("", "Minden forrás") };
        choices.AddRange(_sources.Select(s => new SourceChoice(s.Id, s.Name)));
        SourceMode.ItemsSource = choices;
        SourceMode.SelectedItem = choices.FirstOrDefault(s => s.Id == previous) ?? choices[0];
    }

    private void RebuildTopicChoices()
    {
        string previous = (TopicMode.SelectedItem as TopicChoice)?.Id ?? "";
        var choices = new List<TopicChoice> { new("", "Minden téma") };
        choices.AddRange(TopicClassifier.Categories.Select(t => new TopicChoice(t.Id, t.Name)));
        choices.Add(new TopicChoice(TopicClassifier.OtherId, TopicClassifier.OtherName));
        TopicMode.ItemsSource = choices;
        TopicMode.SelectedItem = choices.FirstOrDefault(c => c.Id == previous) ?? choices[0];
    }

    private void RebuildProfileChoices(string selectedName = "")
    {
        _updatingProfileChoice = true;
        try
        {
            var choices = new List<ProfileChoice> { new("", "— Egyéni szűrés —") };
            choices.AddRange(_savedViews.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(v => new ProfileChoice(v.Name, v.Name)));
            ProfileMode.ItemsSource = choices;
            ProfileMode.SelectedItem = choices.FirstOrDefault(c =>
                string.Equals(c.Id, selectedName, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
            DeleteViewButton.IsEnabled = (ProfileMode.SelectedItem as ProfileChoice)?.Id.Length > 0;
        }
        finally { _updatingProfileChoice = false; }
    }

    private void ProfileMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfileChoice || ProfileMode is null) return;
        var id = (ProfileMode.SelectedItem as ProfileChoice)?.Id ?? "";
        DeleteViewButton.IsEnabled = id.Length > 0;
        if (id.Length == 0) return;
        var preset = _savedViews.FirstOrDefault(p => string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase));
        if (preset is null) return;
        _applyingPreset = true;
        try
        {
            SearchBox.Text = preset.SearchText;
            DateMode.SelectedIndex = preset.DateIndex;
            FocusMode.SelectedIndex = preset.FocusIndex;
            UnreadOnly.IsChecked = preset.UnreadOnly;
            StarredOnly.IsChecked = preset.StarredOnly;
            GroupCompactCheck.IsChecked = preset.CompactGroups;
            DomesticMode.SelectedIndex = preset.DomesticIndex;
            SourceMode.SelectedItem = SourceMode.Items.OfType<SourceChoice>()
                .FirstOrDefault(c => c.Id == preset.SourceId) ?? SourceMode.Items[0];
            TopicMode.SelectedItem = TopicMode.Items.OfType<TopicChoice>()
                .FirstOrDefault(c => c.Id == preset.TopicId) ?? TopicMode.Items[0];
        }
        finally { _applyingPreset = false; }
        ApplyFilters();
        StatusText.Text = "Mentett nézet betöltve: " + preset.Name;
    }

    private ViewPreset CaptureCurrentView(string name) => new()
    {
        Name = name,
        SearchText = SearchBox.Text.Trim(),
        SourceId = (SourceMode.SelectedItem as SourceChoice)?.Id ?? "",
        TopicId = (TopicMode.SelectedItem as TopicChoice)?.Id ?? "",
        DateIndex = Math.Clamp(DateMode.SelectedIndex, 0, 2),
        FocusIndex = Math.Clamp(FocusMode.SelectedIndex, 0, 3),
        DomesticIndex = Math.Clamp(DomesticMode.SelectedIndex, 0, 4),
        UnreadOnly = UnreadOnly.IsChecked == true,
        StarredOnly = StarredOnly.IsChecked == true,
        CompactGroups = GroupCompactCheck.IsChecked == true
    };

    private async void SaveView_Click(object sender, RoutedEventArgs e)
    {
        string previous = (ProfileMode.SelectedItem as ProfileChoice)?.Id ?? "";
        var dialog = new ViewNameWindow(previous) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        string name = dialog.Result;
        var updated = _savedViews.Select(p => p.Copy()).ToList();
        int existing = updated.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0 && MessageBox.Show(this, $"Már van ilyen nevű nézet: {name}. Felülírjuk?",
                "HírRadar – mentett nézet", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (existing < 0 && updated.Count >= 40)
        {
            MessageBox.Show(this, "Legfeljebb 40 mentett nézet hozható létre.",
                "HírRadar – mentett nézet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var captured = CaptureCurrentView(name);
        if (existing >= 0) updated[existing] = captured;
        else updated.Add(captured);
        try
        {
            await _store.SaveViewPresetsAsync(updated);
            _savedViews = updated;
            RebuildProfileChoices(name);
            StatusText.Text = "Nézet elmentve: " + name;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A nézet mentése sikertelen.\n\n" + ex.Message,
                "HírRadar – mentett nézet", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteView_Click(object sender, RoutedEventArgs e)
    {
        string name = (ProfileMode.SelectedItem as ProfileChoice)?.Id ?? "";
        if (name.Length == 0) return;
        if (MessageBox.Show(this, $"Biztosan törlöd ezt a mentett nézetet?\n\n{name}",
                "HírRadar – mentett nézet", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var updated = _savedViews.Where(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        try
        {
            await _store.SaveViewPresetsAsync(updated);
            _savedViews = updated;
            RebuildProfileChoices();
            StatusText.Text = "Mentett nézet törölve: " + name + " (a jelenlegi szűrés nem változott).";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A nézet törlése sikertelen.\n\n" + ex.Message,
                "HírRadar – mentett nézet", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "HírRadar – hírek exportálása",
            FileName = "HirRadar_" + DateTime.Now.ToString("yyyyMMdd_HHmm"),
            DefaultExt = ".csv",
            Filter = "CSV (Excel kompatibilis)|*.csv|JSON|*.json"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var rows = _view.Cast<NewsItem>().ToList();
            if (dialog.FilterIndex == 2)
            {
                var entries = rows.Select(x => new
                {
                    x.Title, x.SourceName, x.Url, x.PublishedUtc,
                    x.HasPublishedDate, x.IsRead, x.IsStarred,
                    x.IsLocalMatch, x.IsKeywordMatch, x.MatchedTerms,
                    x.TopicLabels, DomesticRelevance = DomesticRelevanceClassifier.Label(x),
                    x.AffiliationDisplay, x.AffiliationDetails, Related = x.RelatedItems.Select(r => r.Url).ToArray()
                });
                string json = System.Text.Json.JsonSerializer.Serialize(entries,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(dialog.FileName, json, new System.Text.UTF8Encoding(true));
            }
            else
            {
                static string Cell(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                var lines = new List<string> { "Időpont;Forrás;Kiadói jelölés;Magyar vonatkozás;Cím;URL;Témák;Helyi;Figyelt;Kapcsolódó URL-ek;Olvasott;Csillagozott" };
                lines.AddRange(rows.Select(x => string.Join(";", new[]
                {
                    Cell(x.HasPublishedDate ? x.PublishedUtc.ToLocalTime().ToString("yyyy.MM.dd HH:mm") : "ismeretlen"),
                    Cell(x.SourceName), Cell(x.AffiliationDisplay), Cell(DomesticRelevanceClassifier.Label(x)),
                    Cell(x.Title), Cell(x.Url), Cell(x.TopicLabels),
                    Cell(x.IsLocalMatch ? "igen" : "nem"), Cell(x.IsKeywordMatch ? "igen" : "nem"),
                    Cell(string.Join(" | ", x.RelatedItems.Select(r => r.Url))),
                    Cell(x.IsRead ? "igen" : "nem"), Cell(x.IsStarred ? "igen" : "nem")
                })));
                await File.WriteAllLinesAsync(dialog.FileName, lines, new System.Text.UTF8Encoding(true));
            }
            StatusText.Text = $"Export elkészült: {rows.Count} hír · {dialog.FileName}";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Export", ex);
            MessageBox.Show(this, "Exportálási hiba:\n" + ex.Message, "HírRadar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetFilters_Click(object sender, RoutedEventArgs e)
    {
        _applyingPreset = true;
        try
        {
            SearchBox.Text = "";
            DateMode.SelectedIndex = 0;
            SourceMode.SelectedIndex = 0;
            UnreadOnly.IsChecked = false;
            StarredOnly.IsChecked = false;
            FocusMode.SelectedIndex = 0;
            DomesticMode.SelectedIndex = 1;
            TopicMode.SelectedIndex = 0;
            GroupCompactCheck.IsChecked = false;
        }
        finally { _applyingPreset = false; }
        RebuildProfileChoices();
        ApplyFilters();
        StatusText.Text = "Alapszűrők visszaállítva.";
    }

    private void UpdateWatchSummary()
    {
        int local = _allItems.Count(x => x.IsLocalMatch);
        int keyword = _allItems.Count(x => x.IsKeywordMatch);
        WatchSummaryText.Text = $"Helyi: {local} · Kulcsszó: {keyword} · " +
            $"Beállítva: {_watchRules.Locations.Count} hely / {_watchRules.Keywords.Count} kulcsszó";
    }

    private async void Topics_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TopicWindow(_topicRules) { Owner = this };
        if (editor.ShowDialog() != true) return;
        try
        {
            await _store.SaveTopicRulesAsync(editor.Result);
            _topicRules = editor.Result;
            TopicClassifier.Configure(_topicRules);
            foreach (var item in _allItems) TopicClassifier.Apply(item);
            ApplyFilters();
            StatusText.Text = "Témaszabályok mentve; a meglévő hírek újrabesorolva.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Témák mentése", ex);
            MessageBox.Show(this, ex.Message, "HírRadar – témák", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void WatchButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WatchWindow(_watchRules) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // A mentés véglegesítése előtt nem cseréljük le az aktív szabályokat.
            await _store.SaveWatchRulesAsync(dialog.Result);
            _watchRules = dialog.Result;
            foreach (var item in _allItems) WatchMatcher.Apply(item, _watchRules);
            UpdateWatchSummary();
            ApplyFilters();
            StatusText.Text = "Figyelt kifejezések mentve; a korábbi hírek találatai újraszámítva.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A figyelési szabályokat nem sikerült menteni.\n\n" + ex.Message,
                "HírRadar – figyelések", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void SourcesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checking) return;
        var dialog = new SourceWindow(_sources) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _sources = dialog.Result;
        try
        {
            await _store.SaveSourcesAsync(_sources);
            RebuildSourceChoices();
            await RefreshAsync();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Források mentése", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void CloseArticle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is NewsItem item && ReferenceEquals(item, _expandedItem))
            CollapseArticle();
    }

    private void CollapseArticle()
    {
        _articleRequest?.Cancel();
        _articleRequest = null; // A folyamatban levő feladat a saját finally ágában Dispose-ol.
        if (_expandedItem is not null)
        {
            // Az átmenetileg betöltött szövegrészleteket elengedjük.
            // Ez csak az olvasónézetben tárolt, átmeneti adatokat érinti.
            _expandedItem.IsExpanded = false;
            _expandedItem.ArticleBlocks = [];
            _expandedItem.ArticleBody = "";
        }
        _expandedItem = null;
    }

    private async void ToggleArticle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not NewsItem item) return;
        if (ReferenceEquals(_expandedItem, item))
        {
            CollapseArticle();
            return;
        }

        CollapseArticle();
        _expandedItem = item;
        item.IsExpanded = true;
        item.IsRead = true;
        item.IsNew = false;
        if (string.IsNullOrWhiteSpace(item.ArticleBody))
        {
            item.ArticleStatus = "Cikk letöltése és a törzsszöveg felismerése...";
            item.IsArticleLoading = true;
        }
        // Nem frissítjük azonnal az 'Olvasatlan' szűrőt, mert ezzel a megnyitott
        // cikk azonnal eltűnne a lista alól. Következő szűrésnél érvényesül.
        try { await _store.SaveItemsAsync(_allItems); }
        catch (Exception ex) { StatusText.Text = "Olvasottság mentési hiba: " + ex.Message; }

        if (!ReferenceEquals(_expandedItem, item)) return;
        if (!string.IsNullOrWhiteSpace(item.ArticleBody)) return;

        item.ArticleStatus = "Cikk letöltése és a törzsszöveg felismerése...";
        item.IsArticleLoading = true;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _articleRequest = request;
        try
        {
            // A Google Hírek RSS tartalék köztes URL-t adhat. Még a HTML
            // letöltése előtt feloldjuk a valódi kiadói hivatkozásra, máskülönben
            // a Google süti- / hozzájárulási oldalát olvasnánk be cikként.
            string articleUrl = await ResolveArticleLinkAsync(item, request.Token);
            if (!ReferenceEquals(_expandedItem, item) || request.IsCancellationRequested) return;
            ArticleReadResult article = await _articleReader.ReadAsync(articleUrl, request.Token);
            if (!ReferenceEquals(_expandedItem, item) || request.IsCancellationRequested) return;
            item.ArticleBody = article.Body;
            item.ArticleBlocks = article.Blocks;
            item.ArticleStatus = article.Information;

        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_expandedItem, item))
            {
                AppLogger.Error("Article reader", ex);
                item.ArticleBody = "";
                item.ArticleStatus = "A cikk nem tölthető be az alkalmazáson belül: " + ex.Message +
                                     "  Használd az Eredeti cikk gombot.";
            }
        }
        finally
        {
            item.IsArticleLoading = false;
            if (ReferenceEquals(_articleRequest, request)) _articleRequest = null;
            request.Dispose();
        }
    }

    /// <summary>
    /// A korábbi kiadások news.json állományában is lehetnek Google RSS linkek.
    /// Csak kattintáskor oldjuk fel őket; a valódi URL-t mentjük az elemhez,
    /// de az ID és a korábbi olvasottság/csillagozás érintetlen marad.
    /// </summary>
    private async Task<string> ResolveArticleLinkAsync(NewsItem item, CancellationToken token)
    {
        if (!GoogleNewsLinkResolver.IsGoogleNewsArticle(item.Url)) return item.Url;
        string source = item.SourceName.Contains("Kormány.hu", StringComparison.OrdinalIgnoreCase)
            || item.SourceName.Contains("Kormany.hu", StringComparison.OrdinalIgnoreCase)
            ? "kormany.hu" : "";
        if (source.Length == 0)
            throw new FormatException(
                "Google Hírek köztes URL érkezett, de az eredeti portál nem azonosítható. " +
                "A Google sütioldalát nem használjuk cikkszövegként. Nyisd meg az eredeti linket böngészőben.");
        item.ArticleStatus = "Eredeti " + source + " cikkhivatkozás keresése...";
        string url = await GoogleNewsLinkResolver.ResolveOriginalAsync(item.Url, source, token);
        token.ThrowIfCancellationRequested();
        item.Url = url;
        try { await _store.SaveItemsAsync(_allItems); }
        catch (Exception ex) { AppLogger.Error("Resolved original URL persistence", ex); }
        return url;
    }

    private void UpdateCacheSize()
    {
        try
        {
            var (_, total) = _imageCache.GetSizes();
            CacheButton.Content = "Régi cache: " + ImageCacheManager.FormatBytes(total) + " · Törlés";
        }
        catch (Exception ex) { AppLogger.Error("Cache size", ex); }
    }

    private void CacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CacheWindow(_imageCache, () =>
        {
            // Régi cache ürítésekor a megnyitott szövegpanelt is alaphelyzetbe állítjuk.
            CollapseArticle();
            foreach (var item in _allItems)
            {
                item.ArticleBlocks = [];
                item.ArticleBody = "";
            }
            UpdateCacheSize();
            StatusText.Text = "A választott gyorsítótár törölve. A hírarchívum megmaradt.";
        }) { Owner = this };
        dialog.ShowDialog();
        UpdateCacheSize();
    }

    private void ArticleScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner) return;
        double step = Math.Clamp(e.Delta / 120.0 * 68.0, -180.0, 180.0);
        double target = Math.Clamp(inner.VerticalOffset - step, 0, inner.ScrollableHeight);
        if (Math.Abs(target - inner.VerticalOffset) > 0.1)
        {
            inner.ScrollToVerticalOffset(target);
        }
        else
        {
            // Ha a cikk tetején / alján vagyunk, a külső lista is pixelalapon mozog.
            var outer = FindScrollViewer(NewsList);
            if (outer is not null)
                outer.ScrollToVerticalOffset(Math.Clamp(outer.VerticalOffset - step, 0, outer.ScrollableHeight));
        }
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    private bool _copyArticleBusy;

    private async void CopyArticle_Click(object sender, RoutedEventArgs e)
    {
        if (_copyArticleBusy || (sender as Button)?.Tag is not NewsItem item ||
            !ReferenceEquals(_expandedItem, item) || !item.CanCopyArticle)
            return;

        // A teljes felismert törzsszöveget másoljuk (nem a képeket vagy a HTML-t).
        // A cím a másolat elején szerepel; a bekezdéstöréseket megőrizzük.
        string title = item.Title.Trim();
        string body = item.ArticleBody.Trim();
        string text = body;
        bool startsWithTitle = title.Length > 0 &&
            body.StartsWith(title, StringComparison.CurrentCultureIgnoreCase) &&
            (body.Length == title.Length || char.IsWhiteSpace(body[title.Length]));
        if (title.Length > 0 && !startsWithTitle)
            text = title + "\n\n" + body;
        text = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("\n", Environment.NewLine);

        _copyArticleBusy = true;
        // Az XAML IsEnabled Bindinget nem írjuk felül: különben a cikk
        // újranyitásakor a gomb beragadhatna letiltott állapotba.
        StatusText.Text = "Cikk másolása a vágólapra...";
        try
        {
            bool copied = await ClipboardHelper.TryCopyPlainTextAsync(text);
            if (copied)
            {
                StatusText.Text = "A cikk szövege a vágólapra másolva.";
            }
            else
            {
                AppLogger.Warning("Article clipboard: CLIPBRD_E_CANT_OPEN; a vágólap az újrapróbálkozások után is foglalt.");
                StatusText.Text = "A Windows vágólapja foglalt; a másolás nem sikerült.";
                MessageBox.Show(this,
                    "A Windows vágólapját jelenleg egy másik program használja. " +
                    "A HírRadar többször megpróbálta a másolást, de a vágólap továbbra is foglalt.\n\n" +
                    "Próbáld újra néhány másodperc múlva. A cikk szövege továbbra is nyitva marad.",
                    "HírRadar – cikk másolása", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Article clipboard", ex);
            StatusText.Text = "Cikkmásolási hiba: " + ex.Message;
            MessageBox.Show(this, "Nem sikerült a cikket a vágólapra másolni.\n\n" + ex.Message,
                "HírRadar – cikk másolása", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _copyArticleBusy = false;
        }
    }

    private async void OpenOriginal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not NewsItem item) return;
        try
        {
            // Sikertelen automatikus feloldás esetén a böngésző továbbra is
            // megpróbálhatja a Google Hírek eredeti köztes linkjét megnyitni.
            if (GoogleNewsLinkResolver.IsGoogleNewsArticle(item.Url))
            {
                try { await ResolveArticleLinkAsync(item, _lifetime.Token); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLogger.Error("Open original Google News link", ex);
                }
            }
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Érvénytelen HTTP(S) hivatkozás.");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            item.IsRead = true;
            item.IsNew = false;
            await _store.SaveItemsAsync(_allItems);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nem sikerült megnyitni a cikket.\n\n" + ex.Message,
                "Böngésző megnyitása", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not NewsItem item) return;
        item.IsStarred = !item.IsStarred;
        ApplyFilters();
        try { await _store.SaveItemsAsync(_allItems); }
        catch (Exception ex) { StatusText.Text = "A jelölés mentése nem sikerült: " + ex.Message; }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5) { if (!_checking) _ = RefreshAsync(); e.Handled = true; }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        bool startup = _settings.StartWithWindows;
        try { startup = WindowsStartup.IsEnabled(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nem sikerült kiolvasni a Windows automatikus indítási beállítását.\n\n" + ex.Message,
                "Windows-indítás", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        var dialog = new SettingsWindow(_settings, startup) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            // Először a Windows beállítását módosítjuk. Hibája esetén a mentett opciók változatlanok maradnak.
            WindowsStartup.SetEnabled(dialog.Result.StartWithWindows);
            await _store.SaveSettingsAsync(dialog.Result);
            _settings = dialog.Result;
            if (_notificationsMenuItem is not null)
                _notificationsMenuItem.Checked = _settings.NotifyOnNewArticles;
            StatusText.Text = "Háttérműködés és értesítések beállításai mentve.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nem sikerült minden beállítást menteni.\n\n" + ex.Message,
                "HírRadar – beállítások mentése", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized || _closing || !_settings.MinimizeToTray || _trayIcon is null) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_closing && WindowState == WindowState.Minimized && _settings.MinimizeToTray)
                HideToTray(false);
        }), DispatcherPriority.Background);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _settings.CloseToTray && _trayIcon is not null)
        {
            e.Cancel = true;
            HideToTray(true);
            return;
        }

        _closing = true;
        _elasticWindowDrag.Dispose();
        _clock.Stop();
        _filterDebounce.Stop();
        CollapseArticle();
        _lifetime.Cancel();
        Application.Current.SessionEnding -= Application_SessionEnding;
        if (_trayIcon is not null)
        {
            _trayIcon.MouseUp -= TrayIcon_MouseUp;
            _trayIcon.MouseDoubleClick -= TrayIcon_MouseDoubleClick;
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
    }
}
