using System.Windows;

namespace HirRadar;

public partial class ViewNameWindow : Window
{
    public string Result { get; private set; } = "";

    public ViewNameWindow(string suggestedName = "")
    {
        InitializeComponent();
        NameBox.Text = suggestedName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Adj nevet a nézetnek.", "HírRadar – mentett nézet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Result = name;
        DialogResult = true;
    }
}
