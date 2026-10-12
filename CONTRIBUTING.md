# Fejlesztés és közreműködés

A HírRadar **privát, szerzői jogvédelem alatt álló projekt**. A repositoryhoz kapott hozzáférés önmagában nem jogosítja fel a hozzáférőt a forráskód terjesztésére vagy más projektekbe történő átvételére.

## Munkafolyamat

1. Hibához vagy új funkcióhoz használj GitHub Issue-t.
2. Külön fejlesztői ágon dolgozz (például fix/... vagy feature/...).
3. Kisebb, áttekinthető commitok után nyiss Pull Requestet.
4. Dokumentáld a regressziós kockázatot és a Windowsos teszteredményeket.
5. Kiadás előtt frissítsd a CHANGELOG.md-t és a verziószámokat.

## Fontos követelmények

- C# / WPF / .NET 8; Windows 10/11 x64 kompatibilitás.
- A korábbi hírek, csillagozások, források és beállítások megőrzése.
- Hibás forrás ne állíthassa le a teljes frissítést.
- WPF-hez kötött gyűjteményeket az UI-szál szabályai szerint módosíts.
- A cikkolvasóba ne vezess vissza képeket vagy üres képhelyeket.
- Ne commitolj felhasználói hírarchívumot, tokent, jelszót, tanúsítványt vagy helyi naplót.
- Sikeres build önmagában nem egyenértékű a Windowsos futtatási teszttel.

## Verziózás

- **1.0.x:** javítások és kisebb kompatibilis módosítások
- **1.1.0:** új funkciócsomag
- **2.0.0:** tudatos kompatibilitást érintő váltás

Dokumentáció: [Build](docs/BUILD.md) · [Release checklist](docs/RELEASE_CHECKLIST.md) · [Security](SECURITY.md).
