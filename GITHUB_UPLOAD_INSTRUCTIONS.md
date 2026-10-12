# HírRadar v1.0.2 – GitHub feltöltő (javított 2. kiadás)

Repository: https://github.com/vizisiklo/HirRadar (**Private**).

A korábbi szkript `error: No such remote 'origin'` hibával megállhatott Windows PowerShell 5.1 alatt. A probléma megoldva.

**Ha már kicsomagoltad az előző csomagot:** csak a javított `publish-github.ps1` fájlt másold az előző mappába, és írd felül. Ezután futtasd ismét a `PUBLISH_TO_GITHUB.cmd` fájlt. A korábban létrehozott `.git` mappát **ne töröld**.

**Ha új csomagból indulsz:** csomagold ki az összes fájlt egy új mappába, majd indítsd el a `PUBLISH_TO_GITHUB.cmd` fájlt. Telepített Git for Windows és Git Credential Manager szükséges. Ha GitHub hitelesítést kér, hagyd jóvá a böngészőben.

A szkript létrehozza vagy ellenőrzi a `main` ágat, felküldi a forrást, és elkészíti a `v1.0.2` verziócímkét. Nem használ force push-t és nem ír felül eltérő távoli tartalmat.

A .gitignore kizárja a build-, cache- és helyi felhasználói fájlokat. Az EXE nincs a forrásrepositoryban.
