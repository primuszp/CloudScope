# Megismételhető natív mérnöki munkafolyamat

A forrásoktól és a személyes felmérésektől független LAS/LAZ próbaadat újragenerálható:

```sh
dotnet run --project Source/CloudScope.CommandChecks -c Release -- \
  --write-native-fixture /absolute/native-test
```

8529 pontot tartalmaz: 6561 osztályozott talajpontot egy ismert lejtős rácson és két, egyenként 984 pontos függőleges objektumot. A LAS-fájl tárolási koordinátaeltolása 1000,2000,100; a megjelenítő saját helyi origót számol a felmérés közepén. A LAS/LAZ fájl ugyanazt a pontadatot hordozza; mindkettő külső forrás marad a mentett projekt mellett.

A natív próba során:

1. `OPEN "/absolute/native-test/native-survey.laz" 0`; várjuk meg a 8529 pont megjelenését.
2. Analyze → Distance; adjunk nevet, majd két tényleges pontot az egérrel. Area XY esetén legalább három pont után Close zárja a határt. Escape nem hagy félkész mérést.
3. Terrain TIN → Ground → 0.25 → 1. Egy folytató kérdésre egy választ adjunk. A teljes `TIN Ground 0.25 1` parancs egy üres parancssorból is kiadható. Az eredmény 6561 hely és 12 800 háromszög.
4. Section gomb → két egérpont → 1.6 szélesség. A terv- és szelvénynézet együtt jelenik meg. Rename után készítsünk második szelvényt; az objektumlistán dupla kattintással aktiválható a korábbi.
5. `MEASURE Export Csv/Dxf`, `XSECTION Export Csv/Dxf`, `EXPORTOBJ` ellenőrizhető fájlt hoz létre. Adott fájlútvonal esetén a parancs közvetlenül exportál, interaktív útvonalmegadásnál a natív fájlválasztó nyílik meg.
6. `SAVEPROJECT`, majd RESET és OPENPROJECT után a két mérésnek, mindkét szelvénynek, az aktív szelvénynek, a kameráknak és a 12 800 lapos felületnek vissza kell állnia. A mentett címben nincs módosítást jelző csillag.
7. Négy nézetben külön orbitálás/nagyítás, Escape, kijelölés és undo/redo; a natív bevitel és a grafikus rajzolás különböző réteg az ellenőrzésben.

## Megfigyelt eredmény, 2026-11-06

Az aktuális, aláírás-ellenőrzött macOS `.app` Metal hostján a LAZ-megnyitás, két egérpontos távolság, három egérpontos terület, szalagról indított TIN, egérrel készített szelvény, második szelvény létrehozása, átnevezés és objektumlistából visszaváltás sikeres. A mentés → reset → tényleges visszanyitás 8529 pontot, két mérést, két szelvényt és 12 800 lapos felületet állított vissza. A távolság CSV-értéke 10,317997905274435; a pontkoordinátákból külön számítva ugyanez adódott. A terület külön ellenőrzött értéke 0,46972155570983887. Ezek a kézzel kiválasztott próbahelyek eredményei, nem referenciafelmérési mérési hibák.

A szelvény CSV 347 egyedi forráspontot, az OBJ 6561 csúcsot és 12 800 lapot tartalmazott. A DXF-et független ezdxf olvasó megnyitotta: két 3D polivonal (2 és 3 csúcs, az utóbbi zárt), AC1009; az audit 0 hibát és 0 javítást jelzett. A tesztcsomag fájljai a helyi ideiglenes próbafolderben maradtak, személyes felmérés nem került a repositoryba.

Az automatizált Avalonia felületpróba minden szalagfület ellenőriz, 1040×680 és 1480×920 ablakmérettel, helyettesített viewporttal. Ez nem GPU-próba. A külön Metal-próba 2400 primitívrajzolást, valamint két eltérő origójú és eltérő GPU-lapon élő réteget vizsgál tényleges pixeles/mélységi visszaolvasással; a szelvényszűrés is ellenőrzött. Windows natív ablakból végzett egérpróba ebben a munkamenetben nem történt. A három operációs rendszer CI-ellenőrzése nem helyettesíti ezt.
