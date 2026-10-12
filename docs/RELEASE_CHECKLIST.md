# Kiadás előtti ellenőrzőlista

## Kód és verzió
- [ ] A HirRadar.csproj, build.ps1 és ZIP verziója azonos.
- [ ] CHANGELOG és release notes naprakészek.
- [ ] Nincs privát kulcs, jelszó, helyi felhasználói adat vagy diagnosztikai napló a commitban.

## Fordítás
- [ ] Windows x64 alatt a BUILD_RELEASE.cmd hibamentesen lefut.
- [ ] A publish/HirRadar.exe fájlverziója helyes.
- [ ] Megvan a terjesztési ZIP és SHA-256 hash.
- [ ] Tiszta Windows 10/11 x64 gépen önállóan indul.

## Funkciók
- [ ] Splash után megnyílik a főablak.
- [ ] Kézi és több automatikus frissítés sikeres.
- [ ] Keresés, szűrés, csillagozás, mentett nézetek működnek.
- [ ] A szöveges cikkolvasóban nincsenek képek vagy üres képhelyek.
- [ ] Cikkmásolás működik.
- [ ] Forrásdiagnosztika és naplózás működik.
- [ ] Tálcaikon jobb gombos menüje és teljes kilépés működik.
- [ ] Korábbi hírarchívum és beállítások megmaradnak.

## Kiadás
- [ ] CPU/RAM használat ellenőrizve.
- [ ] A végrehajtott és elmaradt tesztek dokumentálva.
- [ ] A Git-tag a tényleges, tesztelt forráscommitot mutatja.
- [ ] A Release csak ellenőrzött Windows-binárist tartalmaz.

**A CI-ben sikeres build nem egyenértékű a valós Windowsos regressziós teszttel.**
