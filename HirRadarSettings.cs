using System.IO;
using Microsoft.Win32;

namespace HirRadar;

public sealed class HirRadarSettings
{
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool NotifyOnNewArticles { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
}

/// <summary>
/// Per-user startup, without administrator rights. Windows uses this Run key
/// for interactive logons. The registered command starts the window minimized.
/// </summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HirRadar";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("A Windows automatikus indításának beállítása nem elérhető.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        string? path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A futtatható EXE útvonala nem határozható meg.");

        key.SetValue(ValueName, $"\"{path}\" --minimized", RegistryValueKind.String);
    }
}
