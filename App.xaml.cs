using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HirRadar;

public partial class App : Application
{
    private Mutex? _singleton;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _singleton = new Mutex(true, @"Local\HirRadar.Desktop.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            _singleton.Dispose();
            _singleton = null;
            MessageBox.Show("A HírRadar már fut. Ha nem látható az ablaka, keresd a HírRadar ikont a rendszertálcán.",
                "HírRadar", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppLogger.Info("Application start · 1.0.2");
        DispatcherUnhandledException += HandleDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += HandleFatalError;
        TaskScheduler.UnobservedTaskException += HandleTaskError;

        SplashWindow? splash = null;
        MainWindow? mainWindow = null;
        Action? releaseSplashForWarning = null;
        try
        {
            bool minimized = e.Args.Any(arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (!minimized)
            {
                splash = new SplashWindow();
                splash.Show();
                splash.SetStatus("Alkalmazás előkészítése...");
            }

            mainWindow = new MainWindow();
            MainWindow = mainWindow;
            var currentMain = mainWindow;
            var currentSplash = splash;
            bool mainClosed = false;
            currentMain.Closed += (_, _) => mainClosed = true;

            // Az ablak NINCS megjelenítve a splash alatt. Az Opacity=0 módszer
            // mellett a Windows még kirajzolhatta az ablak keretét/címsorát.
            // A XAML vezérlők már példányosítva vannak, ezért a helyi betöltés
            // és az első forrásellenőrzés tényleges ablak nélkül is elkezdhető.
            void RevealMainWindow()
            {
                if (currentSplash?.IsVisible == true) currentSplash.Close();
                if (mainClosed) return;
                if (!currentMain.IsVisible)
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose;
                    currentMain.Show();
                }
                if (!minimized)
                {
                    currentMain.ShowInTaskbar = true;
                    currentMain.Activate();
                }
            }

            if (currentSplash is not null)
            {
                currentMain.StartupStatusChanged += currentSplash.SetStatus;
                releaseSplashForWarning = RevealMainWindow;
                // Ha tényleg modális hibajelzés kell, előbb szabadítsuk fel a képernyőt.
                currentMain.StartupAttentionRequired += releaseSplashForWarning;
            }

            if (minimized)
            {
                // Az automatikus rejtett indulás ne mutasson splash képet.
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                currentMain.Show();
            }

            // A hírfrissítés és az archivált adatok olvasása már a splash alatt
            // elkezdődik; a teljes hálózati kör NEM blokkolja a főablakot.
            Task initialization = currentMain.InitializeForStartupAsync();
            _ = ObserveStartupAsync(initialization);

            if (currentSplash is not null)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                currentMain.StartupStatusChanged -= currentSplash.SetStatus;
                if (releaseSplashForWarning is not null)
                    currentMain.StartupAttentionRequired -= releaseSplashForWarning;
                RevealMainWindow();
            }
        }
        catch (Exception ex)
        {
            if (splash?.IsVisible == true) splash.Close();
            AppLogger.Error("Startup", ex);
            MessageBox.Show("Indítási hiba: " + ex.Message, "HírRadar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
        finally
        {
            if (splash?.IsVisible == true) splash.Close();
        }
    }

    private static async Task ObserveStartupAsync(Task initialization)
    {
        try { await initialization; }
        catch (Exception ex) { AppLogger.Error("Startup background initialization", ex); }
    }

    private static void HandleDispatcherError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLogger.Error("UI", e.Exception);
        MessageBox.Show("Nem kezelt hiba történt. A részletek a helyi naplóban találhatók.\n\n" +
            e.Exception.Message, "HírRadar", MessageBoxButton.OK, MessageBoxImage.Error);
        // Do not swallow unexpected UI exceptions: corrupted state must not continue silently.
    }

    private static void HandleFatalError(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) AppLogger.Error("Fatal", ex);
        else AppLogger.Warning("Unhandled exception object");
    }

    private static void HandleTaskError(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLogger.Error("Unobserved task", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("Application exit");
        try { _singleton?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleton?.Dispose();
        _singleton = null;
        base.OnExit(e);
    }
}
