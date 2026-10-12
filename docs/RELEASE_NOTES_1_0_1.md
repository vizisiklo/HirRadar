# HírRadar v1.0.1 – kiadási build javítása

Ez kizárólag a v1.0.0 fordítási/terjesztési folyamatának és verziójelölésének javítása. A hírfigyelési logika, a felhasználói adatok és az UI változatlan.

- `BUILD_RELEASE.cmd`: külön ellenőrzi a kicsomagolt projektet, visszajelzést ad a sikerről/hibáról, és a konzolt nem zárja be azonnal.
- `build.ps1`: `BUILD_RELEASE.log` napló, Windows/x64 és .NET 8+ SDK ellenőrzés, futó HírRadar felismerése.
- A `dotnet publish` valódi hibakódját kiírja; SDK- vagy NuGet-hibánál nem állítja, hogy kész az EXE.
- Megszűnt a hibás feltételezés, miszerint minden WPF publish-nak pontosan egy fájlt kell eredményeznie; ha van szükséges kísérőfájl, a ZIP tartalmazza.
- `publish\HirRadar.exe`, `dist\HirRadar_v1.0.1_Windows_x64.zip`, SHA256 és helyi buildnapló a kimenet.

## Használat

1. Csomagold ki az egész ZIP-et egy normál mappába (ne a Windows ZIP-előnézetéből futtasd).
2. Ha HírRadar már fut, lépj ki a tálcaikon > Kilépés paranccsal.
3. Dupla kattintás a `BUILD_RELEASE.cmd` fájlra.
4. Hiba esetén küldd el a projekt mappájában keletkező `BUILD_RELEASE.log` fájlt vagy annak végét.

A `BUILD_RELEASE.log` a fordítási környezet elérési útjait is tartalmazhatja, ezért nyilvános megosztás előtt ellenőrizd.

A kiadás önmagában nincs digitálisan aláírva. Microsoft Store/MSIX-terjesztéshez külön kiadói identitás és csomagolási folyamat szükséges.
