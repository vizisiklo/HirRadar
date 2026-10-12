using System.Windows;

namespace HirRadar;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetStatus(status)));
            return;
        }
        LoadingStatus.Text = string.IsNullOrWhiteSpace(status) ? "HírRadar indítása..." : status;
    }
}
