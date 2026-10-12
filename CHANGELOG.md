# HírRadar – Változásnapló / Changelog

A verziók MAJOR.MINOR.PATCH formátumot követnek. A v1.0.2 GitHub-tag a forráskód eredeti, rögzített kiadását jelöli.

## 1.0.2 – 2026-10-12

**Javítva**
- A WPF CollectionView.DeferRefresh közben végzett lista-hozzáadás és -törlés hibás állapotot okozhatott, megszakítva a hírfrissítéseket.
- Az új hírazonosító csak sikeres listába helyezés után kerül a feldolgozott azonosítók közé.

A v1.0.1-es Release-build folyamat és a meglévő helyi adatok kompatibilitása megmaradt.

## 1.0.1 – 2026-10-12

**Javítva**
- Windows Release-build hibaellenőrzése és BUILD_RELEASE.log naplózása.
- .NET SDK, Windows x64 architektúra, futó HírRadar és EXE-verzió ellenőrzése.
- Self-contained EXE, terjesztési ZIP és SHA-256 hash előállítása.

## 1.0.0 – 2026-10-12

**Első hivatalos kiadás**
- RSS/Atom/HTML hírgyűjtés és automatikus ellenőrzés.
- Keresés, témák, kulcsszavak, településfigyelés, szűrők és mentett nézetek.
- Szöveges cikkolvasó, másolás, csillagozás, olvasottság és kapcsolódó hírek.
- Helyi hírarchívum, diagnosztika és CSV/JSON-export.
- Rendszertálcás működés, értesítések, splash és önálló Windows x64 Release-build.

Az eredeti kiadási feljegyzések a docs mappában találhatók.
