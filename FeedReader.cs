using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace HirRadar;

public sealed record FeedReadResult(List<NewsItem> Items, string Method);

/// <summary>Csak cikkcímeket és eredeti linkeket gyűjt, nem tölti le a cikkeket.</summary>
public sealed class FeedReader
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private const int MaxPageLength = 8_000_000;

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("hu-HU,hu;q=0.9,en;q=0.5");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/rss+xml, application/atom+xml, application/xml, text/xml, text/html;q=0.9, */*;q=0.5");
        return client;
    }

    private const string GovernmentBackupFeed =
        "https://news.google.com/rss/search?q=site%3Akormany.hu%2Fhirek%20when%3A7d&hl=hu&gl=HU&ceid=HU%3Ahu";

    public async Task<FeedReadResult> ReadAsync(NewsSource source, CancellationToken token)
    {
        if (IsGovernmentNewsSource(source.FeedUrl))
            return await ReadGovernmentAsync(source, token);
        return await ReadDirectAsync(source, token);
    }

    private async Task<FeedReadResult> ReadGovernmentAsync(NewsSource source, CancellationToken token)
    {
        var errors = new List<string>();
        try
        {
            var page = await DownloadAsync("https://kormany.hu/hirek", token);
            if (!IsHtml(page.ContentType, page.Content))
                throw new FormatException("A hivatalos hírlista nem HTML-t adott.");
            var parsed = ExtractGovernmentHeadlines(source, page.FinalUrl, page.Content, token);
            if (parsed.Count > 0) return new FeedReadResult(parsed, "Kormány.hu HTML (közvetlen)");
            throw new FormatException("A hírlista HTML-je nem tartalmazott felismerhető /hirek/ cikkhivatkozást.");
        }
        catch (Exception ex) when (MayRetry(ex, token)) { errors.Add("HTML: " + ShortFailure(ex)); }

        // Opcionális tartalék, *nem* a kormany.hu saját RSS-e.
        // Így a diagnosztika nem állítja tévesen, hogy a hivatalos oldal RSS-t kínál.
        try
        {
            var feed = await DownloadAsync(GovernmentBackupFeed, token);
            if (IsHtml(feed.ContentType, feed.Content)) throw new FormatException("A hírszolgáltatás HTML-t adott RSS helyett.");
            var parsed = ParseFeed(source, feed.FinalUrl, feed.Content, token);
            if (parsed.Count == 0) throw new FormatException("A kormany.hu-ra szűrt keresés üres.");
            return new FeedReadResult(parsed, "Kormány.hu tartalék (Google RSS)");
        }
        catch (Exception ex) when (MayRetry(ex, token)) { errors.Add("tartalék kereső: " + ShortFailure(ex)); }
        throw new FormatException("A kormany.hu hírlistája nem volt feldolgozható. " + string.Join(" | ", errors));
    }

    private static bool IsGovernmentNewsSource(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        (uri.Host.Equals("kormany.hu", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".kormany.hu", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.TrimEnd('/').StartsWith("/hirek", StringComparison.OrdinalIgnoreCase);

    private static List<NewsItem> ExtractGovernmentHeadlines(NewsSource source, string pageUrl, string html, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var found = new Dictionary<string, NewsItem>(StringComparer.Ordinal);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var anchor in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            token.ThrowIfCancellationRequested();
            string href = anchor.GetAttributeValue("href", "");
            if (!TryResolve(pageUrl, href, out var articleUrl) ||
                !new Uri(articleUrl).Host.Equals("kormany.hu", StringComparison.OrdinalIgnoreCase) ||
                !new Uri(articleUrl).AbsolutePath.StartsWith("/hirek/", StringComparison.OrdinalIgnoreCase)) continue;
            var heading = anchor.Descendants().FirstOrDefault(n => n.Name is "h2" or "h3" or "h4" or "h5");
            string title = Clean(heading?.InnerText ?? anchor.GetAttributeValue("title", ""));
            if (title.Length < 12) title = Clean(anchor.InnerText);
            if (title.Length is < 12 or > 220) continue;
            if (title.Contains("Böngésszen a kormány", StringComparison.OrdinalIgnoreCase)) continue;
            var parent = anchor.ParentNode;
            string context = Clean(parent?.InnerText ?? "");
            Match dateMatch = Regex.Match(context, @"(?<year>20\d{2})\.\s*(?<month>\d{1,2})\.\s*(?<day>\d{1,2})\.", RegexOptions.None, RegexTimeout);
            bool known = false;
            DateTimeOffset date = now;
            if (dateMatch.Success && int.TryParse(dateMatch.Groups["year"].Value, out int year) &&
                int.TryParse(dateMatch.Groups["month"].Value, out int month) &&
                int.TryParse(dateMatch.Groups["day"].Value, out int day))
            {
                try { date = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.FromHours(2)); known = true; }
                catch (ArgumentOutOfRangeException) { date = now; }
            }
            var item = MakeItem(source, title, articleUrl, date, known, now);
            found.TryAdd(item.Id, item);
            if (found.Count >= 120) break;
        }
        // Strukturált adatokból hírgyűjtemény is érkezhet; ott is csak a hivatalos
        // /hirek/ cikkútvonalakat engedjük át.
        if (found.Count < 4)
        {
            var structured = ExtractHtmlHeadlines(source, pageUrl, html, token);
            foreach (var item in structured)
                if (Uri.TryCreate(item.Url, UriKind.Absolute, out var url) &&
                    url.Host.Equals("kormany.hu", StringComparison.OrdinalIgnoreCase) &&
                    url.AbsolutePath.StartsWith("/hirek/", StringComparison.OrdinalIgnoreCase))
                    found.TryAdd(item.Id, item);
        }
        return found.Values.ToList();
    }

    private static bool MayRetry(Exception ex, CancellationToken token) =>
        !token.IsCancellationRequested && (ex is HttpRequestException or FormatException or XmlException or TaskCanceledException or RegexMatchTimeoutException);

    private static string ShortFailure(Exception ex)
    {
        string message = ex.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 160 ? message : message[..157] + "...";
    }

    private async Task<FeedReadResult> ReadDirectAsync(NewsSource source, CancellationToken token)
    {
        var first = await DownloadAsync(source.FeedUrl, token);
        if (!IsHtml(first.ContentType, first.Content))
            return new FeedReadResult(ParseFeed(source, first.FinalUrl, first.Content, token), "RSS/Atom");

        string? advertised = DiscoverFeed(first.Content, first.FinalUrl);
        if (advertised is not null)
        {
            try
            {
                var feed = await DownloadAsync(advertised, token);
                var parsed = ParseFeed(source, feed.FinalUrl, feed.Content, token);
                if (parsed.Count > 0) return new FeedReadResult(parsed, "RSS felismerés");
                // Üres, hirdetett feed esetén is megpróbáljuk a címlapot feldolgozni.
            }
            catch (Exception ex) when (ex is HttpRequestException or FormatException or XmlException)
            {
                // A fejlécben hirdetett hibás RSS nem zárja ki a címlapon való címfelismerést.
            }
        }
        var htmlItems = ExtractHtmlHeadlines(source, first.FinalUrl, first.Content, token);
        if (htmlItems.Count == 0)
            throw new FormatException("Nem található feldolgozható RSS/Atom vagy HTML cikkcím. A forrás URL-je ellenőrizendő.");
        return new FeedReadResult(htmlItems, "HTML címlap");
    }

    private static async Task<(string Content, string FinalUrl, string? ContentType)> DownloadAsync(string address, CancellationToken token)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http"))
            throw new FormatException("Érvénytelen HTTP(S) forráscím.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPageLength)
            throw new FormatException("Túl nagy válasz; a maximum 8 MB.");
        // Valódi byte-limit a beolvasás KÖZBEN. A ReadAsStringAsync a méretellenőrzés
        // előtt tetszőlegesen nagy válaszüzenetet is lefoglalhatna a memóriában.
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var bytes = new MemoryStream();
        var block = new byte[16 * 1024];
        int received;
        while ((received = await stream.ReadAsync(block.AsMemory(), token)) > 0)
        {
            if (bytes.Length + received > MaxPageLength)
                throw new FormatException("A hírfolyam vagy HTML-oldal meghaladta a 8 MB-os biztonsági korlátot.");
            await bytes.WriteAsync(block.AsMemory(0, received), token);
        }
        bytes.Position = 0;
        Encoding encoding = Encoding.UTF8;
        string? charset = response.Content.Headers.ContentType?.CharSet?.Trim(' ', '"', '\'');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset); }
            catch (ArgumentException) { /* Hibás vagy ismeretlen charset: UTF-8. */ }
        }
        using var reader = new StreamReader(bytes, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
        string body = await reader.ReadToEndAsync(token);
        return (body, response.RequestMessage?.RequestUri?.AbsoluteUri ?? address,
            response.Content.Headers.ContentType?.MediaType);
    }

    private static bool IsHtml(string? contentType, string content) =>
        contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true ||
        Regex.IsMatch(content[..Math.Min(content.Length, 1024)], @"<!doctype\s+html\b|<html(?:\s|>)", RegexOptions.IgnoreCase, RegexTimeout);

    private static List<NewsItem> ParseFeed(NewsSource source, string feedUrl, string body, CancellationToken token)
    {
        using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxPageLength,
            IgnoreComments = true
        });
        var doc = XDocument.Load(reader);
        if (doc.Root?.Name.LocalName is not ("rss" or "RDF" or "feed"))
            throw new FormatException("A válasz nem RSS/Atom XML.");

        var now = DateTimeOffset.UtcNow;
        var result = new List<NewsItem>();
        foreach (var entry in doc.Descendants().Where(x => x.Name.LocalName is "item" or "entry"))
        {
            token.ThrowIfCancellationRequested();
            string title = Clean(Child(entry, "title"));
            string link = ReadLink(entry);
            if (title.Length < 5 || !TryResolve(feedUrl, link, out string absolute)) continue;
            string published = FirstNonEmpty(Child(entry, "pubDate"), Child(entry, "published"),
                Child(entry, "updated"), Child(entry, "date"), Child(entry, "issued"));
            var (date, known) = ParseDate(published, now);
            result.Add(MakeItem(source, title, absolute, date, known, now));
        }
        return result.GroupBy(x => x.Id, StringComparer.Ordinal).Select(g => g.First()).ToList();
    }

    private static List<NewsItem> ExtractHtmlHeadlines(NewsSource source, string pageUrl, string html, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var found = new Dictionary<string, NewsItem>(StringComparer.Ordinal);
        // A strukturált NewsArticle JSON-LD elsődleges: kizárja a menü-/reklámlinkek jelentős részét.
        foreach (Match script in Regex.Matches(html,
            @"<script\b[^>]*type\s*=\s*['""']application/ld\+json['""'][^>]*>(?<json>.*?)</script\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var json = JsonDocument.Parse(WebUtility.HtmlDecode(script.Groups["json"].Value));
                CollectStructured(json.RootElement, source, pageUrl, now, found, 0);
            }
            catch (JsonException) { /* Hibás strukturált blokk: megpróbáljuk a HTML article elemeket. */ }
        }

        // RSS nélküli portáloknál az <article> elemek címsorait és hivatkozásait vizsgáljuk.
        foreach (Match article in Regex.Matches(html, @"<article\b[^>]*>(?<inner>.*?)</article\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout))
        {
            token.ThrowIfCancellationRequested();
            string inner = article.Groups["inner"].Value;
            foreach (Match anchor in Regex.Matches(inner, @"<a\b(?<attrs>[^>]*)>(?<body>.*?)</a\s*>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout))
            {
                string anchorBody = anchor.Groups["body"].Value;
                bool containsHeading = Regex.IsMatch(anchorBody, @"<h[1-4]\b", RegexOptions.IgnoreCase, RegexTimeout);
                bool insideHeading = Regex.IsMatch(inner[..anchor.Index], @"<h[1-4]\b[^>]*>(?:(?!</h[1-4]>).)*$",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
                if (!containsHeading && !insideHeading) continue;
                string? href = ReadAttribute(anchor.Groups["attrs"].Value, "href");
                if (href is null || !TryResolve(pageUrl, href, out var url) || !SameSite(pageUrl, url)) continue;
                string title = Clean(anchorBody);
                if (title.Length is < 12 or > 240) continue;
                var item = MakeItem(source, title, url, now, false, now);
                found.TryAdd(item.Id, item);
            }
        }
        // Many modern regional portals do not wrap their homepage cards in <article>.
        // Parse heading links with an HTML DOM too; navigation/category links are excluded.
        var document = new HtmlDocument();
        document.LoadHtml(html);
        foreach (var heading in document.DocumentNode.SelectNodes("//h2 | //h3 | //h4") ?? Enumerable.Empty<HtmlNode>())
        {
            token.ThrowIfCancellationRequested();
            if (heading.Ancestors().Any(n => n.Name is "header" or "footer" or "nav" or "aside")) continue;
            var link = heading.Descendants("a").FirstOrDefault()
                ?? heading.Ancestors("a").FirstOrDefault();
            if (link is null) continue;
            var href = link.GetAttributeValue("href", "");
            if (!TryResolve(pageUrl, href, out var resolved) || !SameSite(pageUrl, resolved)) continue;
            var title = Clean(heading.InnerText);
            if (title.Length is < 15 or > 220 || title.Equals("Friss hírek", StringComparison.OrdinalIgnoreCase)) continue;
            if (new Uri(resolved).AbsolutePath.Trim('/').Length < 12) continue;
            var item = MakeItem(source, title, resolved, now, false, now);
            found.TryAdd(item.Id, item);
            if (found.Count >= 120) break;
        }
        // Helyi portálok: több címlap nem használ <article> taget, és a címeket
        // közvetlen hírkártya-linkekben tárolja. Szűrt, domainegyező DOM-fallback.
        if (found.Count < 15 && IsRegionalSite(pageUrl))
        {
            foreach (var link in document.DocumentNode.Descendants("a"))
            {
                token.ThrowIfCancellationRequested();
                if (link.Ancestors().Any(n => n.Name is "header" or "footer" or "nav" or "aside")) continue;
                string href = link.GetAttributeValue("href", "");
                if (!TryResolve(pageUrl, href, out var resolved) || !SameSite(pageUrl, resolved)) continue;
                var absolutePath = new Uri(resolved).AbsolutePath.Trim('/');
                if (absolutePath.Length < 15 || IsNonArticlePath(absolutePath)) continue;
                string hints = (link.GetAttributeValue("class", "") + " " +
                    link.ParentNode?.GetAttributeValue("class", "")).ToLowerInvariant();
                bool likelyTitle = link.Descendants().Any(n => n.Name is "h1" or "h2" or "h3" or "h4") ||
                    hints.Contains("title") || hints.Contains("headline") || hints.Contains("article") ||
                    hints.Contains("post-title") || hints.Contains("story");
                if (!likelyTitle) continue;
                string title = Clean(link.InnerText);
                if (title.Length is < 19 or > 210 || title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 3) continue;
                var item = MakeItem(source, title, resolved, now, false, now);
                found.TryAdd(item.Id, item);
                if (found.Count >= 120) break;
            }
        }
        return found.Values.ToList();
    }

    private static bool IsRegionalSite(string pageUrl)
    {
        string host = new Uri(pageUrl).Host;
        return new[] { "bajahangja.hu", "kecsup.hu", "hang.hu", "magyarnemzet.hu",
            "hirado.hu", "valaszonline.hu", "atlatszo.hu", "direkt36.hu", "magyarnarancs.hu",
            "merce.hu", "g7.hu", "qubit.hu", "index.hu" }.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNonArticlePath(string relativePath)
    {
        string first = relativePath.Split('/')[0].ToLowerInvariant();
        return first is "cimke" or "tag" or "category" or "kategoria" or "rovat" or
            "szerzo" or "author" or "impresszum" or "kapcsolat" or "privacy" or
            "adatvedelem" or "feed" or "rss" or "search" or "kereses" or
            "component" or "index.php" or "galeria" or "media";
    }

    private static void CollectStructured(JsonElement node, NewsSource source, string baseUrl,
        DateTimeOffset now, Dictionary<string, NewsItem> found, int depth)
    {
        if (depth > 10) return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) CollectStructured(child, source, baseUrl, now, found, depth + 1);
            return;
        }
        if (node.ValueKind != JsonValueKind.Object) return;
        if (node.TryGetProperty("@type", out var type) && HasNewsType(type))
        {
            string? headline = JsonString(node, "headline") ?? JsonString(node, "name");
            string? link = JsonString(node, "url") ?? JsonString(node, "@id");
            if (!string.IsNullOrWhiteSpace(headline) && !string.IsNullOrWhiteSpace(link) &&
                TryResolve(baseUrl, link, out var url) && SameSite(baseUrl, url))
            {
                string title = Clean(headline);
                if (title.Length is >= 12 and <= 240)
                {
                    var (date, known) = ParseDate(JsonString(node, "datePublished") ?? "", now);
                    var item = MakeItem(source, title, url, date, known, now);
                    found.TryAdd(item.Id, item);
                }
            }
        }
        // @graph, itemListElement, mainEntity és hasonló beágyazott schema.org elemek.
        foreach (var property in node.EnumerateObject())
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                CollectStructured(property.Value, source, baseUrl, now, found, depth + 1);
    }

    private static bool HasNewsType(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString()?.Contains("NewsArticle", StringComparison.OrdinalIgnoreCase) == true ||
            type.GetString()?.Contains("Article", StringComparison.OrdinalIgnoreCase) == true,
        JsonValueKind.Array => type.EnumerateArray().Any(HasNewsType),
        _ => false
    };

    private static string? JsonString(JsonElement node, string name) =>
        node.TryGetProperty(name, out var val) && val.ValueKind == JsonValueKind.String ? val.GetString() : null;

    private static bool SameSite(string baseUrl, string articleUrl)
    {
        string a = new Uri(baseUrl).Host;
        string b = new Uri(articleUrl).Host;
        a = a.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? a[4..] : a;
        b = b.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? b[4..] : b;
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static NewsItem MakeItem(NewsSource source, string title, string url,
        DateTimeOffset published, bool knownDate, DateTimeOffset detected) => new()
        {
            Id = NewsItem.BuildId(url), SourceId = source.Id, SourceName = source.Name,
            Title = title, Url = url, PublishedUtc = published.ToUniversalTime(),
            HasPublishedDate = knownDate, FirstSeenUtc = detected
        };

    private static (DateTimeOffset Date, bool Known) ParseDate(string value, DateTimeOffset now)
    {
        bool known = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var date);
        if (!known || date.Year < 1990 || date > now.AddDays(2)) return (now, false);
        return (date, true);
    }

    private static string? DiscoverFeed(string html, string pageUrl)
    {
        foreach (Match tag in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase, RegexTimeout))
        {
            var rel = ReadAttribute(tag.Value, "rel") ?? "";
            var type = ReadAttribute(tag.Value, "type") ?? "";
            var href = ReadAttribute(tag.Value, "href");
            if (!rel.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("alternate", StringComparer.OrdinalIgnoreCase) ||
                (!type.Contains("rss", StringComparison.OrdinalIgnoreCase) &&
                 !type.Contains("atom", StringComparison.OrdinalIgnoreCase))) continue;
            if (href is not null && TryResolve(pageUrl, href, out var absolute)) return absolute;
        }
        return null;
    }

    private static string? ReadAttribute(string tag, string key)
    {
        foreach (Match attr in Regex.Matches(tag,
            "(?<name>[a-z][\\w:-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))",
            RegexOptions.IgnoreCase, RegexTimeout))
            if (attr.Groups["name"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(attr.Groups["value"].Value);
        return null;
    }

    private static string Child(XElement item, string name) => item.Elements()
        .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

    private static string ReadLink(XElement item)
    {
        foreach (var link in item.Elements().Where(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase)))
        {
            var rel = link.Attributes().FirstOrDefault(a => a.Name.LocalName == "rel")?.Value;
            var href = link.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
            if (!string.IsNullOrWhiteSpace(href) && (string.IsNullOrWhiteSpace(rel) || rel == "alternate")) return href;
            if (!string.IsNullOrWhiteSpace(link.Value)) return link.Value.Trim();
        }
        return "";
    }

    private static bool TryResolve(string baseUrl, string target, out string absolute)
    {
        absolute = "";
        if (string.IsNullOrWhiteSpace(target) || !Uri.TryCreate(new Uri(baseUrl), WebUtility.HtmlDecode(target.Trim()), out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        absolute = uri.AbsoluteUri;
        return true;
    }

    private static string Clean(string value) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(value, @"<[^>]+>", " ", RegexOptions.Singleline, RegexTimeout)),
        @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "";
}
