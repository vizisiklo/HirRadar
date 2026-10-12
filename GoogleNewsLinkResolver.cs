using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HirRadar;

/// <summary>
/// Google Hírek RSS esetén a link sokszor egy Google-oldalra mutató,
/// JS-sel feloldandó köztes cím. A linket csak felhasználói kattintáskor
/// próbáljuk a kiadó eredeti URL-jére feloldani; a hírek ciklikus
/// ellenőrzése közben nem kérjük le őket külön.
///
/// Az RPC nem dokumentált Google-végpont, ezért a feloldás sikertelen is lehet.
/// Sikertelen eredmény esetén NEM adjuk vissza a Google-linket cikkolvasásra.
/// </summary>
public static class GoogleNewsLinkResolver
{
    private static readonly HttpClient Http = CreateClient();
    private const string DecodeEndpoint = "https://news.google.com/_/DotsSplashUi/data/batchexecute";

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        handler.CookieContainer.Add(new Uri("https://news.google.com"), new Cookie("CONSENT", "PENDING+987", "/", "news.google.com"));
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("hu-HU,hu;q=0.9,en-US;q=0.8,en;q=0.7");
        return http;
    }

    public static bool IsGoogleNewsArticle(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Host.Equals("news.google.com", StringComparison.OrdinalIgnoreCase) &&
        (uri.AbsolutePath.Contains("/articles/", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.StartsWith("/read/", StringComparison.OrdinalIgnoreCase));

    public static bool IsBaonArticle(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("baon.hu", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".baon.hu", StringComparison.OrdinalIgnoreCase));

    public static bool IsGovernmentArticle(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("kormany.hu", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".kormany.hu", StringComparison.OrdinalIgnoreCase));

    public static bool IsGoogleHost(Uri? uri) => uri is not null &&
        (uri.Host.Equals("google.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("google.hu", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".google.hu", StringComparison.OrdinalIgnoreCase));

    /// <summary>Csak a megadott kiadó tényleges URL-jét adja vissza.</summary>
    public static async Task<string> ResolveOriginalAsync(string googleUrl, string expectedPublisher, CancellationToken token)
    {
        if (!IsGoogleNewsArticle(googleUrl)) throw new FormatException("Ez nem Google Hírek RSS cikkhivatkozás.");
        if (!Uri.TryCreate(googleUrl, UriKind.Absolute, out var googleUri))
            throw new FormatException("Érvénytelen Google Hírek hivatkozás.");
        string id = googleUri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? "";
        if (id.Length is < 20 or > 4096 || !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$"))
            throw new FormatException("Nem felismerhető Google Hírek cikkazonosító.");

        string? resolved = TryDecodeLegacy(id);
        if (resolved is null)
            resolved = await DecodeModernAsync(id, token);

        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var publisherUri) ||
            publisherUri.Scheme is not ("http" or "https"))
            throw new FormatException("A Google nem adott vissza használható cikkhivatkozást.");

        // A forrás lekérdezése a BAON-ra (illetve a Kormány.hu-ra) szűrt RSS,
        // így ne engedjük, hogy egy Google találat más portál cikkjére vezessen.
        bool valid = expectedPublisher switch
        {
            "baon.hu" => IsBaonArticle(resolved),
            "kormany.hu" => IsGovernmentArticle(resolved),
            _ => false
        };
        if (!valid)
            throw new FormatException("A Google Hírek hivatkozása nem a várt kiadóra mutat (" +
                publisherUri.Host + "). Cikkszöveget nem jelenítünk meg más oldalról.");
        return publisherUri.AbsoluteUri;
    }

    private static string? TryDecodeLegacy(string id)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(id.Replace('-', '+').Replace('_', '/')
                .PadRight((id.Length + 3) / 4 * 4, '='));
            if (bytes.Length < 9 || bytes[0] != 0x08 || bytes[1] != 0x13 || bytes[2] != 0x22)
                return null;
            int position = 3;
            int len = 0;
            int shift = 0;
            while (position < bytes.Length && shift < 35)
            {
                int b = bytes[position++];
                len |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            if (len <= 0 || len > bytes.Length - position) return null;
            string candidate = System.Text.Encoding.UTF8.GetString(bytes, position, len);
            return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                uri.Scheme is "https" or "http" ? uri.AbsoluteUri : null;
        }
        catch (Exception) { return null; }
    }

    private static async Task<string> DecodeModernAsync(string id, CancellationToken token)
    {
        string document;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://news.google.com/rss/articles/" + id);
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            document = await response.Content.ReadAsStringAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new FormatException("A Google Hírek köztes URL-jének lekérése sikertelen: " + ex.Message, ex);
        }

        var signature = Regex.Match(document, "data-n-a-sg\\s*=\\s*[\\\"'](?<value>[^\\\"']+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        var timestamp = Regex.Match(document, "data-n-a-ts\\s*=\\s*[\\\"'](?<value>\\d+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        if (!signature.Success || !timestamp.Success ||
            !long.TryParse(timestamp.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ts))
            throw new FormatException(
                "A Google Hírek nem adta vissza a hivatkozás feloldásához szükséges adatokat " +
                "(sütihozzájárulási oldal, blokkolás vagy változó Google-felület). " +
                "Próbáld az Eredeti cikk gombot.");

        // Az Fbv4je Google RPC nem nyilvános API. Hibája nem akadályozhatja
        // sem a hírlistát, sem a más portálok olvasását.
        object?[] parameters =
        [
            "garturlreq",
            new object?[]
            {
                new object?[] { "X", "X", new[] { "X", "X" }, null, null, 1, 1, "HU:hu", null, 1,
                    null, null, null, null, null, 0, 1 },
                "X", "X", 1, new[] { 1, 1, 1 }, 1, 1, null, 0, 0, null, 0
            },
            id, ts, WebUtility.HtmlDecode(signature.Groups["value"].Value)
        ];
        string inner = JsonSerializer.Serialize(parameters);
        string outer = JsonSerializer.Serialize(new object?[] { new object?[] { new object?[] { "Fbv4je", inner } } });
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("f.req", outer)
        });
        using var post = new HttpRequestMessage(HttpMethod.Post, DecodeEndpoint) { Content = content };
        post.Headers.Referrer = new Uri("https://news.google.com/");
        post.Headers.TryAddWithoutValidation("Origin", "https://news.google.com");
        post.Headers.TryAddWithoutValidation("X-Same-Domain", "1");
        string raw;
        try
        {
            using var response = await Http.SendAsync(post, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            raw = await response.Content.ReadAsStringAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new FormatException("A Google Hírek eredeti URL-feloldása nem érhető el: " + ex.Message, ex);
        }
        try
        {
            int arrayStart = raw.IndexOf('[');
            if (arrayStart < 0) throw new FormatException("A Google válasza nem tartalmaz JSON adatot.");
            using var frames = JsonDocument.Parse(raw[arrayStart..]);
            foreach (var frame in frames.RootElement.EnumerateArray())
            {
                if (frame.ValueKind != JsonValueKind.Array || frame.GetArrayLength() < 3 ||
                    frame[0].ValueKind != JsonValueKind.String || frame[0].GetString() != "wrb.fr" ||
                    frame[1].GetString() != "Fbv4je" || frame[2].ValueKind != JsonValueKind.String)
                    continue;
                using var decoded = JsonDocument.Parse(frame[2].GetString()!);
                var values = decoded.RootElement;
                if (values.ValueKind == JsonValueKind.Array && values.GetArrayLength() >= 2 &&
                    values[1].ValueKind == JsonValueKind.String)
                    return values[1].GetString()!;
            }
        }
        catch (JsonException ex)
        {
            throw new FormatException("A Google Hírek URL-feloldó válasza nem értelmezhető.", ex);
        }
        throw new FormatException("A Google Hírek nem oldotta fel az eredeti cikk URL-jét. Próbáld az Eredeti cikk gombot.");
    }
}
