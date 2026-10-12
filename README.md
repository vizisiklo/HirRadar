# HírRadar v1.0.2 – hírfrissítési patch

**Készítette: Közbeszól Média – Minden jog fenntartva 2026**

Windows 10/11 x64 asztali alkalmazás, C# / WPF / .NET 8. A v1.0.2 a v1.0.1 teljes funkciókészletét megőrzi. Ez a patch a hírlista-frissítést blokkoló WPF CollectionView.DeferRefresh kivételt javítja; az 1.0.1-es Release-build diagnosztika és csomagolás megmarad.

## Funkciók

- Hírforrások automatikus ellenőrzése az előző ciklus befejezésétől számított 5 percenként, kézi frissítéssel.
- RSS/Atom és HTML-alapú címlista-feldolgozás, szerkeszthető forráslista és diagnosztika.
- Kizárólag szöveges, bekezdéses cikkolvasó; eredeti link megnyitása; cikkcím és szöveg formázásmentes másolása.
- Kulcsszavas és településfigyelés, témabesorolás, becsült magyar vonatkozás szűrése.
- Csillagozás, olvasottság, keresés, mentett nézetek, kapcsolódó hírek, CSV/JSON export.
- Tényszerű kiadói/intézményi címkék; ezek **nem** állítják, hogy a cikk igaz vagy hamis.
- Rendszertálcás háttérfutás, értesítések, opcionális Windows-automatikus indítás.
- Három másodperces keret nélküli splash, élő indulási státusz és a Közbeszól Média szerzői sora.
- Frissítési előzmények és forrásonkénti hibadiagnosztika; részletes naplózás.

**Nem része:** BAON és Qubit alapforrás; cikkképek letöltése/megjelenítése; cikkek átírása vagy automatikus publikálása.

## Fordítás – egyetlen önálló EXE

A fordító gépre .NET 8 (vagy újabb) SDK és a Windows WPF desktop build támogatása szükséges. A projektet teljesen csomagold ki; ZIP-előnézetből ne indítsd a CMD-t. A célgépen .NET telepítése nem szükséges.

1. Zárd be a régi HírRadar példányt az értesítési terület menüjének **Kilépés** pontjával.
2. Nyisd meg a `HirRadar.csproj` projektet Visual Studio 2022-ben (vagy újabban), az ASP.NET és webfejlesztés helyett a **.NET desktop development** terheléssel és .NET 8 SDK-val.
3. **Egyszerűbb:** dupla kattintás a `BUILD_RELEASE.cmd` fájlra, vagy futtasd PowerShell-ben a `build.ps1` scriptet (például `powershell -ExecutionPolicy Bypass -File .\build.ps1`).
4. Kimenet: `publish\HirRadar.exe` és `dist\HirRadar_v1.0.2_Windows_x64.zip`, valamint SHA-256 ellenőrző összeg és `BUILD_RELEASE.log`. Hiba esetén ez utóbbit küldd el.

A kiadott EXE **x64, self-contained, single-file, portable** konfigurációval készül. A képek/logók az EXE-be fordítva kerülnek. Ha a `dotnet publish` esetleg szükséges kísérőfájlokat hoz létre, a terjesztési ZIP ezeket is tartalmazza. A script 1.0.2.0 EXE-verziót ellenőriz, és világos hibával leáll SDK/fordítás/csomagolás gond esetén.

**Korlát:** a jelen csomag készítési környezetében Windowsos `.NET 8/WPF` fordító nem érhető el, ezért a Windows EXE-t a fenti parancs állítja elő a saját gépeden. A kiadás még nem ment át teljes, 24 órás és Windows 10/11 közötti regressziós teszten.

## Felhasználói adatok és frissítés

Az adatok helye: `%LOCALAPPDATA%\HirRadar`. A program továbbra is a korábbi `news.json`, `sources.json`, `settings.json`, `watch_rules.json` stb. fájlokat használja. Változatlan a hírek, kedvencek és beállítások formátuma. Új programverzióhoz nem szükséges a fájlok importja, de frissítés előtt tanácsos mentést készíteni az adatmappáról.

A rendszerből való teljes kilépés a tálcaikonon érhető el. Az ablak X gombja a felhasználói beállítástól függően a tálcára rejti a programot.

## Kiadási dokumentáció

- `docs\RELEASE_NOTES.md` – v1.0 funkciók, korlátozások, változások.
- `docs\RELEASE_NOTES_1_0_1.md` – build script-javítások és hibaelhárítás.
- `docs\RELEASE_TESTS.md` – kiadás utáni Windows ellenőrzések.
- `docs\PATCH_POLICY.md` – 1.0.2, 1.0.2 és további javítókiadások kezelése.
- `QA-Monitor-24h.ps1` – külön futtatható teljesítmény-monitoring teszt, nem része a végfelhasználói EXE-nek.

## Fontos változás v1.0.2-ben

A hírlista adatainak hozzáadása/eltávolítása nem történik már `CollectionView.DeferRefresh()` keretén belül. Ez megszünteti a WPF-ben fellépő `CurrentItem`/halasztott frissítés kivételt, amely miatt új hírek maradhattak el. Részletek: `docs/RELEASE_NOTES_1_0_2.md`.
