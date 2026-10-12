using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HirRadar;

/// <summary>
/// Csak lokális, felhasználó által explicit exportált hibakeresési összesítés.
/// Sem teljes cikket, sem olvasási előzményt, sem gépfelhasználónevet nem gyűjt.
/// </summary>
public static class DiagnosticsReport
{
    public static string Build(IEnumerable<NewsSource> sources)
    {
        var all = sources.ToList();
        var active = all.Where(s => s.Enabled).ToList();
        int ok = active.Count(s => s.LastCheckedUtc.HasValue && string.IsNullOrWhiteSpace(s.LastError) && s.LastItemCount > 0);
        int empty = active.Count(s => s.LastCheckedUtc.HasValue && string.IsNullOrWhiteSpace(s.LastError) && s.LastItemCount == 0);
        int failed = active.Count(s => !string.IsNullOrWhiteSpace(s.LastError));
        int untested = active.Count(s => !s.LastCheckedUtc.HasValue);
        int persistentFailures = active.Count(s => s.ConsecutiveFailures >= 3);

        var sb = new StringBuilder();
        sb.AppendLine("HÍRRADAR – FORRÁSDIAGNOSZTIKA");
        sb.AppendLine("Verzió: 1.0.2");
        sb.AppendLine($"Export ideje: {DateTimeOffset.Now:yyyy.MM.dd. HH:mm:ss zzz}");
        sb.AppendLine($"Operációs rendszer: {Environment.OSVersion.VersionString}");
        sb.AppendLine($".NET verzió: {Environment.Version}");
        sb.AppendLine("Frissítési ütemezés: 5 perc a megelőző ciklus befejezésétől");
        sb.AppendLine("Utolsó 5 perces helyi CPU/RAM mérés: " + RuntimeHealth.Latest);
        sb.AppendLine($"Források: {all.Count}, aktív: {active.Count}, sikeres és nem üres: {ok}, üres: {empty}, hibás: {failed}, nem ellenőrzött: {untested}, 3+ egymás utáni hiba: {persistentFailures}");
        sb.AppendLine();

        // A cache-méret hasznos QA adat; fájlrendszer-hozzáférési hiba ne blokkolja a jelentést.
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HirRadar", "cache");
            long bytes = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
                : 0;
            sb.AppendLine($"Cache mérete: {ImageCacheManager.FormatBytes(bytes)}");
        }
        catch (IOException) { sb.AppendLine("Cache mérete: nem olvasható (I/O hiba)"); }
        catch (UnauthorizedAccessException) { sb.AppendLine("Cache mérete: nem olvasható (jogosultság)"); }

        try
        {
            using var process = Process.GetCurrentProcess();
            sb.AppendLine($"Memória (private bytes): {ImageCacheManager.FormatBytes(process.PrivateMemorySize64)}");
        }
        catch (Exception) { sb.AppendLine("Memória: nem hozzáférhető"); }

        sb.AppendLine();
        sb.AppendLine("FORRÁSONKÉNTI ÁLLAPOT");
        sb.AppendLine("------------------------------------------");
        foreach (var source in all.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            sb.AppendLine($"Forrás: {source.Name}");
            sb.AppendLine($"  Engedélyezve: {(source.Enabled ? "igen" : "nem")}");
            sb.AppendLine($"  Állapot: {source.StateDisplay}");
            sb.AppendLine($"  Cím: {SafeUrl(source.FeedUrl)}");
            sb.AppendLine($"  Utolsó ellenőrzés: {Stamp(source.LastCheckedUtc)}");
            sb.AppendLine($"  Utolsó siker: {Stamp(source.LastSuccessUtc)}");
            sb.AppendLine($"  Feldolgozás: {source.MethodDisplay}");
            sb.AppendLine($"  Egymást követő sikertelen ellenőrzések: {source.ConsecutiveFailures}");
            sb.AppendLine($"  Találatok: {source.LastItemCount}, dátumozott: {source.LastDatedCount}, 24 órán belül: {source.LastRecentCount}, idő: {source.LastDurationMs} ms");
            if (!string.IsNullOrWhiteSpace(source.LastError))
                sb.AppendLine("  Utolsó hiba: " + source.LastError.Replace('\r', ' ').Replace('\n', ' '));
            sb.AppendLine();
        }
        sb.AppendLine("A jelentés nem tartalmaz cikkek szövegét, olvasási előzményeket vagy a napló teljes tartalmát.");
        return sb.ToString();
    }

    private static string Stamp(DateTimeOffset? timestamp) =>
        timestamp?.ToLocalTime().ToString("yyyy.MM.dd. HH:mm:ss zzz", CultureInfo.GetCultureInfo("hu-HU")) ?? "—";

    private static string SafeUrl(string url)
    {
        // A lekérdezési paraméterek hozzáférési tokeneket tartalmazhatnak: ne exportáljuk őket.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.GetLeftPart(UriPartial.Path);
        return "(érvénytelen URL)";
    }
}
