# HírRadar – Technikai áttekintés

**Platform:** C# / .NET 8 / WPF, Windows 10/11 x64. A Windows Forms NotifyIcon biztosítja az értesítési területi ikont.

| Részrendszer | Fő fájlok |
|---|---|
| Főablak, hírlista és modellek | MainWindow.xaml(.cs), Models.cs |
| RSS/Atom/HTML és cikklekérés | FeedReader.cs, ArticleReader.cs |
| Hírarchívum és beállítások | NewsStore.cs, HirRadarSettings.cs |
| Besorolás, témák és figyelési szabályok | DomesticRelevanceClassifier.cs, TopicClassifier.cs, TopicRules.cs, WatchRules.cs |
| Forrásdiagnosztika és előzmények | DiagnosticsReport.cs, RefreshHistoryStore.cs, RuntimeHealth.cs |
| Szövegmásolás | ClipboardHelper.cs |
| Splash és ablakkezelés | SplashWindow.xaml(.cs), ElasticWindowDrag.cs |
| Release-build | BUILD_RELEASE.cmd, build.ps1 |

A program a forrásokat időszakosan lekéri, a híreket szűri és helyi JSON-adatokkal együtt kezeli. A helyi adatmappa **%LOCALAPPDATA%\HirRadar**. Hálózati adatkapcsolat külső híroldalakhoz szükséges; ezek rendelkezésre állása nem garantálható.

**Regressziós figyelmeztetés:** A v1.0.2 a WPF CollectionView.DeferRefresh és ObservableCollection módosítások ütközését javította. A WPF-hez kötött listák kezelését optimalizáláskor Windows alatt külön tesztelni kell.
