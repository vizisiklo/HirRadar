using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace HirRadar;

public sealed record ArticleReadResult(string Body, string Information, IReadOnlyList<ArticleBlock> Blocks);

/// <summary>
/// A címre kattintáskor futó helyi olvasónézet. A gyűjtő továbbra is kizárólag
/// címet/linket rögzít. A teljes cikk HTML-je nem kerül lemezre vagy publikálásra.
/// Csak a ténylegesen elérhető szerveroldali HTML-ből olvas, fizetőfalat nem kerül meg.
/// </summary>
public sealed class ArticleReader
{
    private const int MaxChars = 6_000_000;
    private const int MaxArticleChars = 150_000;
    private static readonly HttpClient Http = CreateClient();
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly string[] NoiseWords =
    [
        "sidebar", "related", "recommended", "newsletter", "subscribe", "sharing",
        "social", "comment", "advert", "banner", "cookie", "promo", "footer",
        "breadcrumb", "navigation", "widget", "paywall", "readmore", "more-from",
        "consent", "onetrust", "didomi", "quantcast", "gdpr", "privacy", "cmp-", "fc-dialog", "ot-sdk", "cookiebot",
        "kapcsolodo", "ajanlo", "ajanlott", "hirdetes", "hirlevel", "cimke", "megosztas"
    ];
    private static readonly HashSet<string> ContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h2", "h3", "h4", "li", "blockquote"
    };

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(23) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml;q=0.9,*/*;q=0.5");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("hu-HU,hu;q=0.9,en;q=0.5");
        return client;
    }

    public async Task<ArticleReadResult> ReadAsync(string url, CancellationToken token)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new FormatException("Az eredeti cikk hivatkozása nem HTTP(S) URL.");

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxChars)
            throw new FormatException("A weboldal mérete meghaladja a 6 MB-os biztonsági korlátot.");
        string html = await response.Content.ReadAsStringAsync(token);
        if (html.Length > MaxChars)
            throw new FormatException("A weboldal mérete meghaladja a 6 MB-os biztonsági korlátot.");

        // A Google RSS linkje nem a kiadó cikke. Korábban a Google sütiablakát
        // a generikus algoritmus sok, hosszú bekezdésként tévesen beolvasta.
        // Az effektív URL-t is vizsgáljuk (HTTP redirect után is).
        Uri finalUri = response.RequestMessage?.RequestUri ?? uri;
        if (GoogleNewsLinkResolver.IsGoogleHost(finalUri) ||
            GoogleNewsLinkResolver.IsGoogleHost(uri) ||
            html.Contains("A következőkre használjuk a cookie-kat és az adatokat", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("Before you continue to Google", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                "Google Hírek köztes vagy sütikezelési oldal érkezett, nem az eredeti cikk. " +
                "A beépített olvasó ezt nem jeleníti meg cikkszövegként.");
        if (IsBaon(uri) && !IsBaon(finalUri))
            throw new FormatException(
                "A BAON-cikk kérése másik webhelyre irányított: " + finalUri.Host +
                ". A tényleges cikk nem érhető el közvetlenül.");

        if (IsBaon(uri))
            return ExtractBaonArticle(html, finalUri);

        string body = ExtractArticleText(html);
        if (body.Length < 140)
            throw new FormatException("Nem azonosítható a teljes cikk törzsszövege. Lehet, hogy a portál JavaScriptből tölti be, vagy hozzáféréshez kötött.");
        var blocks = ExtractBlocks(html, uri, body);
        return new ArticleReadResult(body,
            "Olvasónézet · csak szöveg, bekezdések, alcímek és idézetek. Hiányzó részletnél nyisd meg az eredetit.", blocks);
    }

    private static bool IsBaon(Uri uri) =>
        uri.Host.Equals("baon.hu", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".baon.hu", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// BAON: csak egy igazolható cikk-törzskonténert fogadunk el.
    /// A generikus, teljes DOM-on végzett pontozás a portál CMP/süti párbeszédablakát
    /// választhatja ki, ezért BAON-on szándékosan NINCS dokumentum-szintű fallback.
    /// </summary>
    private static ArticleReadResult ExtractBaonArticle(string html, Uri uri)
    {
        var doc = new HtmlDocument { OptionFixNestedTags = true };
        doc.LoadHtml(html);
        var root = doc.DocumentNode;
        var focused = FindBaonArticleRoot(root);
        string extracted = focused is null ? "" : Assemble(focused);
        string jsonBody = ExtractJsonLd(root);

        // A szabványos articleBody megbízhatóbb lehet, de csak valódi, hosszú szövegként.
        // A kiolvasott elemeket ugyanebből a tartalomból készítjük.
        bool useJson = jsonBody.Length >= 180 && !LooksLikeConsent(jsonBody) &&
            (extracted.Length < 180 || jsonBody.Length > extracted.Length * 1.20);
        string body = useJson ? jsonBody : extracted;
        if (body.Length < 140 || LooksLikeConsent(body))
            throw new FormatException(
                "BAON: nem sikerült azonosítani a valódi cikk törzsszövegét. " +
                "A sütihozzájárulási szöveget nem jelenítjük meg cikként; használd az Eredeti cikk gombot.");

        IReadOnlyList<ArticleBlock> blocks;
        if (useJson || focused is null)
            blocks = FallbackBlocks(body);
        else
            blocks = ExtractBlocksFromNode(focused, body);

        return new ArticleReadResult(body,
            "BAON olvasónézet · csak az azonosított cikk törzsszövege, sütikezelési és ajánló szövegek nélkül.", blocks);
    }

    private static HtmlNode? FindBaonArticleRoot(HtmlNode root)
    {
        // A cikk címe (H1) felől keressük a legkisebb olyan tartalmi őst,
        // amelyben tényleges bekezdések is találhatók. Így az oldal másik felében
        // megjelenő cookie dialog nem nyerhet pusztán a szöveghossza alapján.
        var headings = root.SelectNodes("//h1") ?? Enumerable.Empty<HtmlNode>();
        foreach (var heading in headings.Where(h => !IsInNoise(h)))
        {
            foreach (var node in heading.AncestorsAndSelf().Take(9))
            {
                if (node.Name is not ("article" or "main" or "section" or "div")) continue;
                if (IsInNoise(node)) continue;
                if (BaonParagraphs(node).Count < 2) continue;
                if (BaonParagraphs(node).Sum(x => x.Length) < 240) continue;
                return node;
            }
        }

        string[] specificXpaths =
        [
            "//*[@itemprop='articleBody']",
            "//*[@data-testid='article-body']",
            "//*[contains(@class,'article-body') or contains(@class,'article__body') or contains(@class,'article-content') or contains(@class,'article__content') or contains(@class,'article-text') or contains(@class,'post-content') or contains(@class,'story-body')]"
        ];
        foreach (string xpath in specificXpaths)
        {
            var candidate = root.SelectNodes(xpath)?.Where(n => !IsInNoise(n))
                .Where(n => BaonParagraphs(n).Count >= 2)
                .OrderByDescending(n => BaonParagraphs(n).Sum(p => p.Length)).FirstOrDefault();
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private static List<string> BaonParagraphs(HtmlNode node) =>
        node.Descendants("p")
            .Where(p => !IsInNoise(p))
            .Select(p => Normalize(p.InnerText))
            .Where(s => s.Length >= 35 && !LooksLikeConsent(s))
            .Take(150).ToList();

    private static bool LooksLikeConsent(string text)
    {
        string s = text.ToLowerInvariant();
        if (s.Contains("cookie-k beállítása") || s.Contains("cookie beállítások") ||
            s.Contains("sütihozzájárulás") || s.Contains("adatkezelési beállítások")) return true;
        int hits = 0;
        foreach (string phrase in new[] { "hozzájárul", "adatkezelés", "szolgáltatásaink", "partnereink", "cookie", "süti", "böngészési adatok" })
            if (s.Contains(phrase, StringComparison.Ordinal)) hits++;
        return hits >= 3;
    }

    public static string ExtractArticleText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var doc = new HtmlDocument();
        doc.OptionFixNestedTags = true;
        doc.LoadHtml(html);
        var root = doc.DocumentNode;

        // Egyes hírportálok a teljes szöveget szabványos schema.org Article JSON-LD-ben is közlik.
        string structuredBody = ExtractJsonLd(root);

        // A teljes dokumentum saját híroldali fejlécét/ajánlóit eltávolítjuk.
        foreach (var node in root.Descendants().Where(n =>
            n.Name is "script" or "style" or "nav" or "footer" or "aside" or "form" or "svg" or "iframe" or "noscript" or "button" or "input" or "template" ||
            (n.Name is "div" or "section" or "ul" && IsNoise(n))).ToList())
            node.Remove();

        var candidates = root.Descendants().Where(n => n.Name is "article" or "main" or "div" or "section")
            .Where(n => n.Descendants("p").Take(2).Count() >= 2)
            .Select(n => (Node: n, Score: Score(n)))
            .OrderByDescending(x => x.Score)
            .Take(12)
            .ToList();

        string selectedBody = "";
        int bestScore = int.MinValue;
        foreach (var (node, score) in candidates)
        {
            var body = Assemble(node);
            if (body.Length < 100) continue;
            int effectiveScore = score + Math.Min(body.Length, 18000) / 8;
            if (effectiveScore > bestScore)
            {
                bestScore = effectiveScore;
                selectedBody = body;
            }
        }

        if (structuredBody.Length > Math.Max(240, (int)(selectedBody.Length * 1.35)))
            return structuredBody[..Math.Min(structuredBody.Length, MaxArticleChars)];
        return selectedBody[..Math.Min(selectedBody.Length, MaxArticleChars)];
    }

    private static IReadOnlyList<ArticleBlock> ExtractBlocks(string html, Uri articleUri, string fallbackText)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var root = doc.DocumentNode;
        // Choose actual article-body containers first; a selector is a hint, not a guarantee.
        string[] hints = articleUri.Host.ToLowerInvariant() switch
        {
            var h when h.Contains("telex.hu") => ["article", "//*[contains(@class,'article-body')]"],
            var h when h.Contains("24.hu") => ["//*[contains(@class,'entry-content')]", "article"],
            var h when h.Contains("hvg.hu") => ["//*[contains(@class,'article-content')]", "article"],
            var h when h.Contains("444.hu") => ["article", "//*[contains(@class,'article-body')]"],
            var h when h.Contains("portfolio.hu") => ["//*[contains(@class,'article-content')]", "article"],
            var h when h.Contains("baon.hu") => ["article", "//*[contains(@class,'article-content')]"],
            _ => ["article", "//*[contains(@itemprop,'articleBody')]", "//*[contains(@class,'post-content')]", "main"]
        };
        var candidates = new List<HtmlNode>();
        foreach (string xpath in hints)
            candidates.AddRange(root.SelectNodes(xpath == "article" ? "//article" : xpath == "main" ? "//main" : xpath)
                ?? Enumerable.Empty<HtmlNode>());
        candidates.AddRange(root.Descendants().Where(n => n.Name is "article" or "main" or "section" or "div")
            .Where(n => n.Descendants("p").Take(2).Count() >= 2).Take(200));
        var selected = candidates.Distinct().Where(n => !IsInNoise(n))
            .OrderByDescending(Score).FirstOrDefault();
        if (selected is null) return FallbackBlocks(fallbackText);
        return ExtractBlocksFromNode(selected, fallbackText);
    }

    private static IReadOnlyList<ArticleBlock> ExtractBlocksFromNode(HtmlNode selected, string fallbackText)
    {
        var result = new List<ArticleBlock>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in selected.DescendantsAndSelf())
        {
            if (IsInNoise(node)) continue;
            if (node.Name is "script" or "style") continue;
            if (!ContentTags.Contains(node.Name)) continue;
            if (node.Name is "blockquote" or "li" && node.Descendants("p").Any()) continue;
            if (node.Name == "p" && node.Ancestors("p").Any()) continue;
            var value = Normalize(node.InnerText);
            if (LooksLikeConsent(value)) continue;
            if (value.Length < (node.Name is "p" or "blockquote" ? 28 : 8) || !seen.Add(value)) continue;
            result.Add(new ArticleBlock { Text = value, IsHeading = node.Name is "h2" or "h3" or "h4", IsQuote = node.Name == "blockquote" });
            if (result.Sum(b => b.Text.Length) > MaxArticleChars) break;
        }
        int extractedLength = result.Sum(b => b.Text.Length);
        if (result.Count < 2 ||
            (fallbackText.Length > 500 && extractedLength < fallbackText.Length / 3))
            return FallbackBlocks(fallbackText);
        return result;
    }

    private static IReadOnlyList<ArticleBlock> FallbackBlocks(string text) =>
        text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Take(200)
            .Select(x => new ArticleBlock { Text = x }).ToList();

    private static int Score(HtmlNode node)
    {
        var paragraphs = node.Descendants("p")
            .Where(n => !IsInNoise(n))
            .Select(n => Normalize(n.InnerText))
            .Where(s => s.Length >= 35).Take(250).ToList();
        if (paragraphs.Count < 2) return int.MinValue / 2;
        int length = Math.Min(paragraphs.Sum(p => p.Length), 28000);
        int linked = Math.Min(node.Descendants("a").Sum(a => Normalize(a.InnerText).Length), 25000);
        var hints = (node.GetAttributeValue("id", "") + " " + node.GetAttributeValue("class", "")).ToLowerInvariant();
        int bonus = node.Name == "article" ? 2400 : node.Name == "main" ? 650 : 0;
        if (new[] { "article-body", "article-content", "post-content", "entry-content", "story-body",
                    "cikk-szoveg", "cikk-tartalom", "article-text", "post-body", "content-body", "article__body" }
            .Any(hints.Contains)) bonus += 3000;
        if (IsNoise(node)) bonus -= 15000;
        // A menüből és hírajánlókból összerakott hosszú oldal ne győzze le az article régiót.
        return length + paragraphs.Count * 22 + bonus - linked / 2;
    }

    private static string Assemble(HtmlNode article)
    {
        var paragraphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in article.DescendantsAndSelf().Where(n => ContentTags.Contains(n.Name)))
        {
            if (IsInNoise(node)) continue;
            // Ne vegyük fel a blockquote/li teljes szövegét a benne lévő p mellett másodszor.
            if (node.Name is "blockquote" or "li" && node.Descendants("p").Any()) continue;
            if (node.Name == "p" && node.Ancestors("p").Any()) continue;
            string text = Normalize(node.InnerText);
            if (LooksLikeConsent(text)) continue;
            if (text.Length < (node.Name is "p" or "blockquote" ? 28 : 8)) continue;
            if (!seen.Add(text)) continue;
            if (node.Name.StartsWith('h')) text = text.ToUpper(CultureInfo.GetCultureInfo("hu-HU"));
            paragraphs.Add(text);
            if (paragraphs.Sum(s => s.Length) >= MaxArticleChars) break;
        }
        return string.Join("\n\n", paragraphs);
    }

    private static bool IsInNoise(HtmlNode node) => node.AncestorsAndSelf().Any(IsNoise);

    private static bool IsNoise(HtmlNode node)
    {
        var marker = (" " + node.GetAttributeValue("class", "") + " " + node.GetAttributeValue("id", "") + " ")
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(marker)) return false;
        return NoiseWords.Any(word => marker.Contains(word, StringComparison.Ordinal));
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        return Whitespace.Replace(WebUtility.HtmlDecode(s), " ").Trim();
    }

    private static string ExtractJsonLd(HtmlNode root)
    {
        string best = "";
        foreach (var node in root.SelectNodes("//script[@type='application/ld+json']") ?? Enumerable.Empty<HtmlNode>())
        {
            try
            {
                using var json = JsonDocument.Parse(node.InnerText);
                SearchJson(json.RootElement, ref best, 0);
            }
            catch (JsonException) { /* az oldal sérült JSON-LD-jét figyelmen kívül hagyjuk */ }
        }
        return best;
    }

    private static void SearchJson(JsonElement element, ref string best, int depth)
    {
        if (depth > 8) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) SearchJson(item, ref best, depth + 1);
                break;
            case JsonValueKind.Object:
                if (element.TryGetProperty("articleBody", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    string value = Normalize(text.GetString() ?? "");
                    if (value.Length > best.Length) best = value;
                }
                foreach (var property in element.EnumerateObject())
                    if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                        SearchJson(property.Value, ref best, depth + 1);
                break;
        }
    }
}
