using System.Windows;
using System.Windows.Controls;

namespace HirRadar;

public partial class TopicWindow : Window
{
    private readonly TopicRules _editing;
    private bool _switching;
    private string? _selectedId;
    public TopicRules Result { get; private set; }

    public TopicWindow(TopicRules current)
    {
        InitializeComponent();
        _editing = TopicRules.Sanitize(current);
        Result = _editing;
        CategoryList.ItemsSource = TopicClassifier.DefaultCategories.Select(c => c.Name).ToList();
        CategoryList.SelectedIndex = 0;
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_switching && _selectedId is not null) StoreSelected();
        _switching = true;
        try
        {
            int index = CategoryList.SelectedIndex;
            _selectedId = index < 0 ? null : TopicClassifier.DefaultCategories[index].Id;
            TermsBox.Text = _selectedId is null ? "" : string.Join(Environment.NewLine, _editing.Terms[_selectedId]);
        }
        finally { _switching = false; }
    }

    private void Terms_Changed(object sender, TextChangedEventArgs e) { }

    private void StoreSelected()
    {
        if (_selectedId is null) return;
        _editing.Terms[_selectedId] = TermsBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Visszaállítsuk az összes témakategória alapértelmezett szavait?", "HírRadar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _editing.Terms = TopicRules.Defaults().Terms;
        _switching = true;
        try { if (_selectedId is not null) TermsBox.Text = string.Join(Environment.NewLine, _editing.Terms[_selectedId]); }
        finally { _switching = false; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        StoreSelected();
        Result = TopicRules.Sanitize(_editing);
        DialogResult = true;
    }
}
