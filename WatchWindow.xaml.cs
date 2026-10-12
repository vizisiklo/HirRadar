using System.Windows;

namespace HirRadar;

public partial class WatchWindow : Window
{
    public WatchRules Result { get; private set; }

    public WatchWindow(WatchRules rules)
    {
        InitializeComponent();
        Result = rules;
        KeywordsBox.Text = string.Join(Environment.NewLine, rules.Keywords ?? []);
        LocationsBox.Text = string.Join(Environment.NewLine, rules.Locations ?? []);
        NotifyMatchesCheck.IsChecked = rules.NotifyOnMatches;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var keywords = WatchRules.CleanLines(KeywordsBox.Text.Split('\n').Select(x => x.TrimEnd('\r')));
        var locations = WatchRules.CleanLines(LocationsBox.Text.Split('\n').Select(x => x.TrimEnd('\r')));
        if (keywords.Any(x => x.Length > 80) || locations.Any(x => x.Length > 80))
        {
            MessageBox.Show(this, "Egy figyelt kifejezés legfeljebb 80 karakter lehet.",
                "HírRadar – figyelések", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (KeywordsBox.Text.Split('\n').Count(x => !string.IsNullOrWhiteSpace(x)) > 100 ||
            LocationsBox.Text.Split('\n').Count(x => !string.IsNullOrWhiteSpace(x)) > 100)
        {
            MessageBox.Show(this, "Legfeljebb 100 kulcsszó és 100 helyi kifejezés adható meg.",
                "HírRadar – figyelések", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = new WatchRules
        {
            Keywords = keywords,
            Locations = locations,
            NotifyOnMatches = NotifyMatchesCheck.IsChecked == true
        };
        DialogResult = true;
    }
}
