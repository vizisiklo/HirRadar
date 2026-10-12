# HírRadar v1.0.2 – hírlista-frissítés javítása

## Jelenség

A v1.0.1-ben a főablak alsó státuszsorában megjelenhetett a következő WPF-hiba:
`Cannot change or check the contents or CurrentItem of CollectionView while Refresh is being deferred`.
Ilyenkor a hírlekérés elindulhatott, de a feldolgozási ciklus idő előtt megszakadt.

## Javítás

- A WPF-hez kötött hírlista hozzáadásánál és a 12 000-es archiválási korlátnál
  eltávolítottuk a CollectionView.DeferRefresh() használatát. A WPF kiválasztási
  mechanizmusai így nem futnak deferred refresh alatt.
- Új cikk azonosítója a `_knownIds` gyűjteménybe csak a sikeres listába
  beszúrás után kerül, hogy kivétel után ne legyen cikk nélküli, ismert ID.
- Az 1.0.1 Release-build naplózó és kiadási csomagoló mechanizmusa megmaradt,
  csak a fájlverzió és a csomagnevek változtak 1.0.2-re.
- A felhasználói adatok útvonala és struktúrája nem változott:
  `%LOCALAPPDATA%\HirRadar`.

## Ellenőrzés

1. Fordítás: `BUILD_RELEASE.cmd` vagy Visual Studio Release.
2. Indítás előtt lépj ki a régi példányból a tálcaikon menüjével.
3. `Frissítés most` – a státusz végül legyen `Utolsó ellenőrzés: ...`.
4. `Források` → `Ellenőrzési előzmények`: sikeres ciklus, forrásonkénti
   eredmények és új hírek ellenőrzése.
5. Nem ismétlődhet a `Cannot change or check ... Refresh is being deferred` hiba.
6. A korábban mentett hírek és a csillagozások jelenjenek meg.

**Valódi .NET 8 Windows fordítás és élő RSS-teszt ebben a környezetben nem történt.**
