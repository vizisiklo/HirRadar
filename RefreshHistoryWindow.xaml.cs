using System.IO;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace HirRadar;

public partial class RefreshHistoryWindow : Window
{
    private IReadOnlyList<RefreshHistoryEntry> _history = [];

    public RefreshHistoryWindow()
    {
        InitializeComponent();
        Loaded += RefreshHistoryWindow_Loaded;
    }

    private async void RefreshHistoryWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _history = await new RefreshHistoryStore().ReadAsync();
            HistoryGrid.ItemsSource = _history;
            HistoryStatus.Text = _history.Count == 0
                ? "Még nincs lezárt ellenőrzési ciklus."
                : $"{_history.Count} ellenőrzési ciklus betöltve.";

            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var lastDay = _history.Where(x => x.FinishedAtUtc >= cutoff).ToList();
            int withDetails = lastDay.Count(x => x.SourceResults.Count > 0);
            int ok = lastDay.Sum(x => x.SuccessfulSources);
            int fail = lastDay.Sum(x => x.FailedSources);
            double successRate = ok + fail > 0 ? 100.0 * ok / (ok + fail) : 0;
            SummaryText.Text = lastDay.Count == 0
                ? "Elmúlt 24 óra: nincs mentett ellenőrzés."
                : $"Elmúlt 24 óra: {lastDay.Count} ciklus · {ok} sikeres és {fail} hibás forrásellenőrzés · " +
                  $"{successRate:0.0}% sikerarány · {withDetails} ciklushoz rendelkezésre állnak forrásrészletek.";
            ExportButton.IsEnabled = _history.Count > 0;
            if (_history.Count > 0) HistoryGrid.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Refresh history UI", ex);
            HistoryStatus.Text = "Az ellenőrzési előzmények nem olvashatók.";
            SummaryText.Text = "A jelentés betöltése nem sikerült. Az eredeti előzményfájlt nem módosítottuk.";
            ExportButton.IsEnabled = false;
        }
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceResultsGrid is null || DetailStatus is null) return;
        if (HistoryGrid.SelectedItem is not RefreshHistoryEntry selected)
        {
            SourceResultsGrid.ItemsSource = null;
            DetailStatus.Text = "Válassz egy ellenőrzési ciklust.";
            return;
        }

        SourceResultsGrid.ItemsSource = selected.SourceResults;
        DetailStatus.Text = selected.SourceResults.Count == 0
            ? "Ehhez a régebbi ciklushoz nincs forrásonkénti előzmény."
            : $"{selected.SourceResults.Count} forrás · {selected.FinishedDisplay}";
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0) return;
        var save = new SaveFileDialog
        {
            Title = "HírRadar – frissítési előzmények exportja",
            Filter = "CSV-fájl (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"HirRadar_Ellenorzesek_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (save.ShowDialog(this) != true) return;

        ExportButton.IsEnabled = false;
        try
        {
            string csv = BuildCsv(_history);
            // UTF-8 BOM: magyar Excelben az ékezetek megőrzéséhez.
            await File.WriteAllTextAsync(save.FileName, csv, new UTF8Encoding(true));
            HistoryStatus.Text = $"CSV mentve: {Path.GetFileName(save.FileName)}";
            AppLogger.Info("Frissítési előzmények CSV exportja sikeres.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Refresh history CSV export", ex);
            MessageBox.Show(this, "Nem sikerült menteni az előzményeket.\n\n" + ex.Message,
                "HírRadar – CSV export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { ExportButton.IsEnabled = true; }
    }

    private static string BuildCsv(IEnumerable<RefreshHistoryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ciklus befejezése;Ellenőrzött forrás;Sikeres;Hibás;Új cím összesen;Ciklus idő (ms);Ciklus állapota;Forrás;Forrás állapota;Forrás idő (ms);Találat;Új cím a forrásból;Módszer;Hiba");
        foreach (var item in entries)
        {
            // A régi verziók összesítő rekordjai forrásspecifikus adatok nélkül is exportálhatók.
            IEnumerable<SourceRefreshResult?> sources = item.SourceResults.Count == 0
                ? new SourceRefreshResult?[] { null }
                : item.SourceResults.Cast<SourceRefreshResult?>();
            foreach (var source in sources)
            {
                string[] fields =
                [
                    item.FinishedAtUtc.ToLocalTime().ToString("yyyy.MM.dd. HH:mm:ss", CultureInfo.InvariantCulture),
                    item.CheckedSources.ToString(CultureInfo.InvariantCulture),
                    item.SuccessfulSources.ToString(CultureInfo.InvariantCulture),
                    item.FailedSources.ToString(CultureInfo.InvariantCulture),
                    item.NewArticles.ToString(CultureInfo.InvariantCulture),
                    item.DurationMs.ToString(CultureInfo.InvariantCulture),
                    item.Summary,
                    source?.SourceName ?? "(régi ciklus – nincs részlet)",
                    source?.StateDisplay ?? "",
                    source?.DurationMs.ToString(CultureInfo.InvariantCulture) ?? "",
                    source?.ArticlesFound.ToString(CultureInfo.InvariantCulture) ?? "",
                    source?.NewArticles.ToString(CultureInfo.InvariantCulture) ?? "",
                    source?.Method ?? "",
                    source?.Error ?? ""
                ];
                sb.AppendLine(string.Join(";", fields.Select(EscapeCsv)));
            }
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        // Szövegmezők Excel-formulaként történő értelmezésének megelőzése.
        value = value.Replace("\r", " ").Replace("\n", " ").Trim();
        if (value.Length > 0 && "=+@-".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
