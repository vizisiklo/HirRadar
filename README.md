<p align="center"><img src="Assets/HirRadarHeaderLogo.png" alt="HírRadar" width="350"></p>

# HírRadar

**Magyar hírek figyelése, rendszerezése és gyors áttekintése Windows alatt.**

**Készítette:** Közbeszól Média · **Aktuális verzió:** v1.0.2 · **Platform:** Windows 10/11 x64

> **Private repository.** © 2026 Közbeszól Média. Minden jog fenntartva. A forráskódhoz nem tartozik nyílt forráskódú licenc.

## Mi a HírRadar?

A HírRadar egy C# / .NET 8 / WPF alapú asztali hírfigyelő, amely különböző magyar hírforrások cikkeit gyűjti össze és helyben, kereshető listában jeleníti meg. Szerkesztőségi és napi hírmonitorozási munkafolyamatokhoz készült.

## Fő funkciók

- **Automatikus hírfigyelés:** RSS/Atom és egyes HTML-alapú források ellenőrzése, alapértelmezetten ötpercenként; kézi frissítés.
- **Hírlista:** időpontok, források, keresés, olvasottság, csillagozás, kapcsolódó hírek és mentett nézetek.
- **Szerkesztőségi szűrés:** belföldi relevancia becslése, kulcsszavas és településfigyelés, témabesorolás.
- **Szöveges cikkolvasó:** a cikk kibontása képek és üres képhelyek nélkül; eredeti link megnyitása; szöveg másolása.
- **Forrásinformációk:** kiadói/intézményi kapcsolatok címkézése – ezek nem minősítik a hírek igazságtartalmát.
- **Háttérműködés:** Windows system tray, értesítések, opcionális automatikus indítás.
- **Diagnosztika:** forrásállapotok, frissítési előzmények, részletes helyi naplózás, CSV/JSON-export.

A program nem publikál automatikusan cikkeket, és nem tölt le cikkképeket. A külső híroldalak változása hatással lehet a cikkek kinyerésére.

## Rendszerkövetelmények

| Összetevő | Követelmény |
|---|---|
| Operációs rendszer | Windows 10/11 x64 |
| Technológia | C#, WPF, .NET 8 |
| Fordításhoz | .NET 8 SDK és Windowsos WPF/.NET desktop támogatás |
| Futtatáshoz | A self-contained Release EXE esetén nem kell külön .NET Runtime |
| Terjesztés | Hordozható (portable) EXE/ZIP |

## Fordítás és indítás

1. Klónozd vagy töltsd le a repositoryt.
2. Telepítsd a [.NET 8 SDK-t](https://dotnet.microsoft.com/download/dotnet/8.0).
3. Lépj ki a korábban futó HírRadarról a rendszertálcán.
4. A gyökérmappából futtasd a **BUILD_RELEASE.cmd** fájlt, vagy nyisd meg a **HirRadar.csproj** projektet Visual Studióban.
5. A sikeres build után indítsd a **publish/HirRadar.exe** alkalmazást.

A script létrehozza a **dist/HirRadar_v1.0.2_Windows_x64.zip** kiadási csomagot, az SHA-256 ellenőrző összeget és a **BUILD_RELEASE.log** fájlt. Bővebben: [Build útmutató](docs/BUILD.md).

> A jelenlegi EXE **nem rendelkezik Authenticode-aláírással**, ezért a Windows SmartScreen figyelmeztethet. A forráskód feltöltése nem jelent digitális aláírást.

## Használat és adatok

A főablakban kereshetsz, szűrhetsz, kibonthatsz híreket, és megnyithatod az eredeti forrásoldalt. Az adatok a **%LOCALAPPDATA%\HirRadar** könyvtárban maradnak. Frissítés előtt készíts mentést róla. A bezárógomb elrejtheti a programot, teljes kilépés a rendszertálca ikonjának jobb gombos menüjével történik.

Részletesen: [Felhasználói útmutató](docs/USER_GUIDE.md).

## Verziótörténet

| Kiadás | Fő változás |
|---|---|
| **v1.0.2** | WPF hírlista-frissítési hiba javítása; elmaradó hírek helyreállítása |
| **v1.0.1** | Release-build és hibadiagnosztika javítása |
| **v1.0.0** | Első hivatalos kiadás |

Részletek: [CHANGELOG.md](CHANGELOG.md) és [v1.0.2 tag](https://github.com/vizisiklo/HirRadar/tree/v1.0.2).

## Dokumentáció

- [Fordítás és kiadás](docs/BUILD.md)
- [Felhasználói útmutató](docs/USER_GUIDE.md)
- [Technikai áttekintés](docs/ARCHITECTURE.md)
- [Kiadás előtti ellenőrzőlista](docs/RELEASE_CHECKLIST.md)
- [Változásnapló](CHANGELOG.md)
- [Hibabejelentés és közreműködés](CONTRIBUTING.md)
- [Biztonsági szabályzat](SECURITY.md)
- [Részletes v1.0.2 release notes](docs/RELEASE_NOTES_1_0_2.md)

A repositoryban a **forráskód** található. A GitHub Actionsből származó build-artifact **nem automatikusan hivatalos kiadás**. A hivatalos EXE-t csak Windowsos ellenőrzés után célszerű Release-ként közzétenni.

## English summary

**HírRadar** is a Hungarian-language Windows 10/11 desktop news monitoring app built with C#, WPF and .NET 8. It combines configurable RSS/Atom and selected HTML news feeds with filters, search, local storage, a text-only article reader, notifications, diagnostics and portable builds. Developed by **Közbeszól Média**. **Private repository; all rights reserved.**

---

© 2026 **Közbeszól Média**. Minden jog fenntartva.
