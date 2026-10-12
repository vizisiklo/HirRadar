using System.Windows;

namespace HirRadar;

public partial class SettingsWindow : Window
{
    public HirRadarSettings Result { get; private set; }

    public SettingsWindow(HirRadarSettings settings, bool startWithWindows)
    {
        InitializeComponent();
        Result = settings;
        MinimizeCheck.IsChecked = settings.MinimizeToTray;
        CloseCheck.IsChecked = settings.CloseToTray;
        NotificationsCheck.IsChecked = settings.NotifyOnNewArticles;
        StartupCheck.IsChecked = startWithWindows;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = new HirRadarSettings
        {
            MinimizeToTray = MinimizeCheck.IsChecked == true,
            CloseToTray = CloseCheck.IsChecked == true,
            NotifyOnNewArticles = NotificationsCheck.IsChecked == true,
            StartWithWindows = StartupCheck.IsChecked == true
        };
        DialogResult = true;
    }
}
