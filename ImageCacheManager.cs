using System.IO;

namespace HirRadar;

/// <summary>
/// Korábbi verziók cikkképeinek maradványcache-kezelője.
/// Ez a verzió SOHA nem tölt le, nem dekódol és nem ment cikkképet.
/// Kizárólag az Alpha 0.7.12.1 és korábbi kiadások régi cache-ét lehet kiüríteni.
/// </summary>
public sealed class ImageCacheManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string CacheDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HirRadar", "cache");
    private string ImageDirectory => Path.Combine(CacheDirectory, "images");

    public ImageCacheManager()
    {
        // Az új, kizárólag szöveges kiadásban a régi képek automatikusan
        // kikerülnek a korábbi saját cache-ből. Hírarchívumot nem érintünk.
        try
        {
            if (Directory.Exists(ImageDirectory)) Directory.Delete(ImageDirectory, recursive: true);
        }
        catch (IOException ex) { AppLogger.Warning("Régi képcache eltávolítása: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { AppLogger.Warning("Régi képcache jogosultság: " + ex.Message); }
    }

    public (long Images, long Total) GetSizes() =>
        (DirectorySize(ImageDirectory), DirectorySize(CacheDirectory));

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }

    private static long DirectorySize(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        long sum = 0;
        foreach (var filename in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try { sum += new FileInfo(filename).Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return sum;
    }

    public Task ClearImagesAsync() => ClearAsync(true);
    public Task ClearAllAsync() => ClearAsync(false);

    private async Task ClearAsync(bool imagesOnly)
    {
        await _gate.WaitAsync();
        try
        {
            string target = imagesOnly ? ImageDirectory : CacheDirectory;
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            // Törlés után nem hozunk létre új képcache mappát.
        }
        finally { _gate.Release(); }
    }
}
