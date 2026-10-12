# Fordítás és Release-build

## Előfeltételek
- Windows 10/11 x64
- .NET 8 SDK (nem csak Runtime)
- Opcionálisan Visual Studio 2022 vagy újabb, .NET desktop development komponens
- NuGet csomagok első letöltéséhez internet

## Kiadás készítése

A HírRadart előbb teljesen zárd be a rendszertálca **Kilépés** menüpontjával, majd a repository gyökérmappájában futtasd a **BUILD_RELEASE.cmd** fájlt. PowerShellből alternatív parancs: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

| Kimenet | Szerep |
|---|---|
| publish/HirRadar.exe | Portable Windows x64 alkalmazás |
| dist/HirRadar_v1.0.2_Windows_x64.zip | Terjesztési csomag |
| dist/HirRadar_v1.0.2_Windows_x64_SHA256.txt | EXE hash |
| BUILD_RELEASE.log | Buildnapló |

Hibás fordításnál olvasd el a BUILD_RELEASE.log végét. Ha az EXE nem készül el, ellenőrizd a .NET SDK-t, a Windows architektúrát és hogy a program nem fut-e. A Windows SmartScreen figyelmeztethet, mert a jelenlegi EXE nincs digitálisan aláírva.

A GitHub Actions artifact **nem hivatalos kiadás**. A tényleges Windowsos alkalmazást csak működési teszt után szabad Release-ként feltüntetni.
