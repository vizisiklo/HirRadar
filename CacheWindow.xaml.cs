using System.Windows;

namespace HirRadar;

public partial class CacheWindow : Window
{
    private readonly ImageCacheManager _cache;
    private readonly Action _afterClear;

    public CacheWindow(ImageCacheManager cache, Action afterClear)
    {
        InitializeComponent();
        _cache = cache;
        _afterClear = afterClear;
        UpdateSizes();
    }

    private void UpdateSizes()
    {
        var (images, total) = _cache.GetSizes();
        TotalSizeText.Text = "Teljes cache: " + ImageCacheManager.FormatBytes(total);
        ImageSizeText.Text = "Korábbi képek: " + ImageCacheManager.FormatBytes(images);
    }

    private async void ClearImages_Click(object sender, RoutedEventArgs e) => await ClearAsync(all: false);
    private async void ClearAll_Click(object sender, RoutedEventArgs e) => await ClearAsync(all: true);

    private async Task ClearAsync(bool all)
    {
        string what = all ? "a teljes gyorsítótárat" : "a korábbi cikkek képcache-ét";
        if (MessageBox.Show(this, $"Biztosan törlöd {what}? A mentett hírek és beállítások megmaradnak.",
            "HírRadar – cache törlése", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        ImagesButton.IsEnabled = false;
        AllButton.IsEnabled = false;
        try
        {
            if (all) await _cache.ClearAllAsync();
            else await _cache.ClearImagesAsync();
            _afterClear();
            UpdateSizes();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "A gyorsítótár nem törölhető teljesen: " + ex.Message,
                "HírRadar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImagesButton.IsEnabled = true;
            AllButton.IsEnabled = true;
        }
    }
}
