namespace HirRadar;

/// <summary>
/// Átlátható, tényalapú kiadói/állami forrásjelölések. Nem minősíti a cikkek
/// igazságtartalmát vagy az egyes szerkesztőségek megbízhatóságát.
/// Domainhez kötött szabályok, ezért a felhasználó által átnevezett forrásra is érvényesek.
/// A listát és a források tulajdoni helyzetét időről időre felül kell vizsgálni.
/// </summary>
public static class SourceAttribution
{
    private static readonly HashSet<string> Mediaworks = new(StringComparer.OrdinalIgnoreCase)
    {
        "origo.hu", "magyarnemzet.hu", "baon.hu", "bama.hu", "beol.hu", "boon.hu",
        "delmagyar.hu", "duol.hu", "feol.hu", "kisalfold.hu", "haon.hu", "heol.hu",
        "szoljon.hu", "kemma.hu", "nool.hu", "sonline.hu", "szon.hu", "teol.hu",
        "vaol.hu", "veol.hu", "zaol.hu"
    };

    private static readonly HashSet<string> Indamedia = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.hu", "dex.hu", "economx.hu"
    };

    private static readonly HashSet<string> PublicService = new(StringComparer.OrdinalIgnoreCase)
    {
        "hirado.hu", "mti.hu", "mediaklikk.hu"
    };

    public static SourceAttributionInfo Get(string? address, string? sourceName = null)
    {
        var host = "";
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host[4..];
        // Tartalék Google Hírek RSS esetén a cikkek linkje köztes Google URL lehet.
        // Kizárólag a beépített hivatalos BAON/Kormány.hu forrásnevekre alkalmazzuk a tartalék címkét.
        if (host is "news.google.com" && !string.IsNullOrWhiteSpace(sourceName))
        {
            if (sourceName.StartsWith("BAON", StringComparison.OrdinalIgnoreCase)) host = "baon.hu";
            else if (sourceName.StartsWith("Kormány.hu", StringComparison.OrdinalIgnoreCase)) host = "kormany.hu";
        }

        if (host == "kormany.hu" || host.EndsWith(".kormany.hu", StringComparison.OrdinalIgnoreCase))
            return new("KORMÁNYZATI", "Kormányzati közlés", "Magyarország Kormánya hivatalos közlési felülete. Elsődleges forrás; az állítások más forrásból is ellenőrizhetők.", "https://kormany.hu/hirek");
        if (MatchesAny(host, Mediaworks))
            return new("MEDIAWORKS", "Mediaworks kiadó", "Mediaworks-portfólióhoz tartozó sajtótermék. Ez kiadói kapcsolatra figyelmeztet, nem állítás a hír valótlanságáról.", "https://www.origo.hu/kapcsolat");
        if (MatchesAny(host, Indamedia))
            return new("INDAMEDIA", "Indamedia-csoport", "Indamedia-csoporthoz kötődő médiatermék. A jelölés kiadói kapcsolatra vonatkozik; nem automatikus tényellenőrzés.", "https://indamedia.hu/");
        if (MatchesAny(host, PublicService))
            return new("KÖZMÉDIA", "Közszolgálati média", "Közszolgálati/állami médiarendszerhez kötődő forrás; érdemes más beszámolókkal is összevetni.", "https://hirado.hu/");
        return SourceAttributionInfo.Empty;
    }

    private static bool MatchesAny(string host, IEnumerable<string> domains) =>
        domains.Any(domain => host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
}

public sealed record SourceAttributionInfo(string Badge, string Classification, string Details, string EvidenceUrl)
{
    public static readonly SourceAttributionInfo Empty = new("", "", "", "");
    public bool HasBadge => Badge.Length > 0;
    public string Tooltip => HasBadge ? $"{Classification}. {Details}\nTájékoztató forrás: {EvidenceUrl}" : "Nincs dokumentált kiadói jelölés; ez nem megbízhatósági minősítés.";
}
