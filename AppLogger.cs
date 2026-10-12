using System.Diagnostics;
using System.IO;
using System.Text;

namespace HirRadar;

/// <summary>Best-effort local diagnostics. No article body or credentials in logs.</summary>
public static class AppLogger
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HirRadar", "logs");
    private static DateTime _lastCleanupDate = DateTime.MinValue;

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string context, Exception error) => Write("ERROR", context + ": " + error);
    public static void Warning(string message) => Write("WARN", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                if (_lastCleanupDate != DateTime.UtcNow.Date)
                {
                    _lastCleanupDate = DateTime.UtcNow.Date;
                    foreach (var file in Directory.EnumerateFiles(Folder, "HirRadar-*.log"))
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                            try { File.Delete(file); } catch (IOException) { }
                }
                string path = Path.Combine(Folder, $"HirRadar-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception ex) { Debug.WriteLine("Logging unavailable: " + ex.Message); }
    }
}
