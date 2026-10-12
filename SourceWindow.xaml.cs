using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace HirRadar;

public partial class SourceWindow : Window
{
    private readonly ObservableCollection<NewsSource> _editable;
    private readonly Dictionary<string, NewsSource> _original;
    public List<NewsSource> Result { get; private set; } = [];

    public SourceWindow(IEnumerable<NewsSource> sources)
    {
        InitializeComponent();
        var originals = sources.ToList();
        _original = originals.ToDictionary(s => s.Id);
        _editable = new ObservableCollection<NewsSource>(originals.Select(s => s.Copy()));
        SourcesGrid.ItemsSource = _editable;
        if (_editable.Count > 0) SourcesGrid.SelectedIndex = 0;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var source = new NewsSource { Name = "Új forrás", FeedUrl = "https://", Enabled = true };
        _editable.Add(source);
        SourcesGrid.SelectedItem = source;
        SourcesGrid.ScrollIntoView(source);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesGrid.SelectedItem is NewsSource source) _editable.Remove(source);
    }

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        var window = new RefreshHistoryWindow { Owner = this };
        window.ShowDialog();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HirRadar", "logs");
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HírRadar – napló", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var save = new Microsoft.Win32.SaveFileDialog
        {
            Title = "HírRadar – forrásdiagnosztika mentése",
            Filter = "Szövegfájl (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"HirRadar_Diagnosztika_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (save.ShowDialog(this) != true) return;
        try
        {
            SourcesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            SourcesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            string report = DiagnosticsReport.Build(_editable);
            await File.WriteAllTextAsync(save.FileName, report, System.Text.Encoding.UTF8);
            AppLogger.Info("Forrásdiagnosztikai jelentés mentve.");
            MessageBox.Show(this, "A diagnosztikai jelentés mentése sikerült.",
                "HírRadar – diagnosztika", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Forrásdiagnosztika export", ex);
            MessageBox.Show(this, "Nem sikerült elmenteni a jelentést.\n\n" + ex.Message,
                "HírRadar – diagnosztika", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void SourcesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ErrorDetails is null) return;
        if (SourcesGrid.SelectedItem is not NewsSource source)
        {
            ErrorDetails.Text = "Válassz egy forrást a részletekhez.";
            return;
        }
        string state = source.LastCheckedUtc.HasValue ? source.StateDisplay : "Még nem ellenőrzött";
        string lastOk = source.LastSuccessUtc?.ToLocalTime().ToString("yyyy. MM. dd. HH:mm:ss") ?? "—";
        ErrorDetails.Text = $"{source.Name} · {state} · utolsó sikeres ellenőrzés: {lastOk}" +
            (source.ConsecutiveFailures > 0 ? $"\nEgymást követő hibák: {source.ConsecutiveFailures}" : "") +
            (source.HasAffiliation ? $"\nForrásjelölés: {source.AffiliationDetails}" : "") +
            (string.IsNullOrWhiteSpace(source.LastError) ? "" : $"\nHiba: {source.LastError}");
    }

    private bool _copyDiagnosticsBusy;

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (_copyDiagnosticsBusy || SourcesGrid.SelectedItem is not NewsSource ||
            string.IsNullOrWhiteSpace(ErrorDetails.Text)) return;

        _copyDiagnosticsBusy = true;
        Button? copyButton = sender as Button;
        if (copyButton is not null) copyButton.IsEnabled = false;
        try
        {
            string diagnostics = ErrorDetails.Text;
            bool copied = await ClipboardHelper.TryCopyPlainTextAsync(diagnostics);
            if (!copied)
            {
                AppLogger.Warning("Diagnostics clipboard: CLIPBRD_E_CANT_OPEN; a vágólap foglalt.");
                MessageBox.Show(this,
                    "A Windows vágólapja foglalt maradt az újrapróbálkozások után is. " +
                    "Próbáld újra néhány másodperc múlva.",
                    "HírRadar – diagnosztika", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Diagnostics clipboard", ex);
            MessageBox.Show(this, "A hibaüzenetet nem sikerült a vágólapra másolni.\n\n" + ex.Message,
                "HírRadar – diagnosztika", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _copyDiagnosticsBusy = false;
            if (copyButton is not null)
                copyButton.IsEnabled = SourcesGrid.SelectedItem is NewsSource;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SourcesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        SourcesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        foreach (var item in _editable)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || !Uri.TryCreate(item.FeedUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("https" or "http") || string.IsNullOrWhiteSpace(uri.Host))
            {
                MessageBox.Show(this, "Minden forráshoz név és teljes http(s) weboldal- vagy RSS-cím szükséges.",
                    "Hibás hírforrás", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            item.Name = item.Name.Trim();
            item.FeedUrl = item.FeedUrl.Trim();
            if (_original.TryGetValue(item.Id, out var original) &&
                !string.Equals(item.FeedUrl, original.FeedUrl, StringComparison.OrdinalIgnoreCase))
            {
                item.LastSuccessUtc = null;
                item.ConsecutiveFailures = 0;
                item.LastCheckedUtc = null;
                item.LastError = "";
                item.LastMethod = "";
                item.LastItemCount = 0;
                item.LastDurationMs = 0;
                item.LastRecentCount = 0;
                item.LastDatedCount = 0;
            }
        }
        if (_editable.GroupBy(s => s.FeedUrl.TrimEnd('/'), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            MessageBox.Show(this, "Ugyanaz az URL kétszer szerepel a forráslistában.",
                "Duplikált forrás", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = _editable.Select(s => s.Copy()).ToList();
        DialogResult = true;
    }
}
