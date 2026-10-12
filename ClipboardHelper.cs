using System.Runtime.InteropServices;
using System.Windows;

namespace HirRadar;

/// <summary>
/// A Windows vágólapja osztott erőforrás: egy másik alkalmazás rövid időre
/// kizárólagos hozzáféréssel megnyithatja. Csak a foglaltságot jelző
/// CLIPBRD_E_CANT_OPEN hibánál próbálkozunk újra. Minden kísérlet az eredeti
/// WPF STA szálon fut, az aszinkron várakozás alatt pedig a felület használható.
/// </summary>
internal static class ClipboardHelper
{
    private const int ClipboardBusyHResult = unchecked((int)0x800401D0);

    // 8 kísérlet, összesen körülbelül 2,1 másodpercnyi várakozás.
    private static readonly int[] RetryDelaysMs = [90, 140, 200, 260, 350, 430, 600];

    /// <returns>True: sikeres másolás. False: az összes kísérletnél foglalt maradt a vágólap.</returns>
    public static async Task<bool> TryCopyPlainTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Nincs másolható szöveg.", nameof(text));

        if (Application.Current is null || !Application.Current.Dispatcher.CheckAccess())
            throw new InvalidOperationException("A vágólap másolását a WPF UI-szálon kell elvégezni.");

        for (int attempt = 0; attempt <= RetryDelaysMs.Length; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException ex) when (ex.HResult == ClipboardBusyHResult)
            {
                if (attempt == RetryDelaysMs.Length)
                    return false;

                // Ne blokkoljuk a WPF üzenetfeldolgozó szálát Thread.Sleep-pel.
                await Task.Delay(RetryDelaysMs[attempt]);
            }
        }
        return false;
    }
}
