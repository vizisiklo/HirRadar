# HírRadar v1.0.2 – Release ellenőrzőlista

A listában lévő pontok **nem előre elvégzett tesztek**. A kiadott EXE-n, Windows 10 és 11 x64 alatt kell ellenőrizni őket.

- [ ] `BUILD_RELEASE.cmd` ablakban hiba esetén látható a hiba, keletkezik `BUILD_RELEASE.log`. Siker esetén `build.ps1` sikeresen befejeződik, a `publish\HirRadar.exe` létrejön és az EXE Properties → Details alatt 1.0.2.0 verziót mutat. Esetleges kísérőfájlok is a ZIP-be kerülnek.
- [ ] A `dist\HirRadar_v1.0.2_Windows_x64.zip` egy tiszta Windows 10/11 x64 gépen, telepített .NET runtime nélkül is elindul.
- [ ] A logó, splash, tálcaikon és főablak címsora rendben van; a splash 3 mp, keret nélkül.
- [ ] A tálcaikon jobb kattintására a menü nyitott/rejtett főablaknál is azonnal előjön; bal dupla kattintás megnyit; Kilépés valóban leállít.
- [ ] Kézi és automatikus frissítés legalább több forrásból hoz címet, egy rossz forrás nem akasztja meg a többit.
- [ ] Lenyitott cikkben kizárólag folyamatos szöveg jelenik meg, üres képhely nélkül.
- [ ] Cikk másolása Jegyzettömbbe plain text, foglalt vágólapnál automatikus újrapróbálkozás.
- [ ] Kulcsszó/helyi/téma/belföldi szűrők, mentett nézetek, csillagozás és CSV/JSON export működik.
- [ ] QA-Monitor-24h.ps1 mérésben CPU, RAM, threads, handles stabil, nincs indokolatlan memória-növekedés.
- [ ] A `refresh_history.json` a legfeljebb 200 ciklus/14 nap korlátot követi.
- [ ] Meglévő Alpha 0.7.15 / v1.0.0 telepítés adatai a v1.0.2 alatt változatlanul megjelennek; az adatmappát nem írja felül üres adatokkal.
- [ ] Offline, 403/404, DNS-hiba, timeout és egyetlen nagy HTML/RSS válasz mellett az app használható marad.

**A release kiadási döntése:** a felhasználó az Alpha 0.7.15 működésével elégedett, CPU 0–0,1%, RAM kb. 122 MB volt az első méréseken. Ezek nem helyettesítik a tartós stressztesztet.

## V1.0.2 - hírlista regresszió

- [ ] 30 percig hagyd futni a programot. Legalább 2-3 automatikus ellenőrzési ciklus jusson el a befejezett állapotig.
- [ ] Kézi frissítés közben nincs `Cannot change or check ... while Refresh is being deferred` kivétel.
- [ ] Nyiss meg egy cikket, válassz másik cikket, majd frissíts; a kijelölés nem okozhat hibát.
- [ ] Több száz hírrel és szűrőváltással is stabil marad a lista; a csillagozások megmaradnak.
- [ ] A napló és az ellenőrzési előzmények nem tartalmaznak új `Refresh` feldolgozási kivételt.
