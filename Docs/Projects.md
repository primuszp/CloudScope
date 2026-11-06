# Teljes projektek

A `.cloudscope` projekt egy verziózott ZIP-dokumentum. Együtt menti a resident felhő vagy a streaming rétegek hivatkozásait, a címkéket és példányazonosítókat, a címkeosztályok színeit/kódjait, a 3D és síkbeli vonalakat (ívekkel és szélességekkel), a névvel tárolt szelvényeket és az aktív szelvény azonosítóját, a távolság-/területméréseket, a kilenc kamera állapotát, a névvel mentett nézeteket, az elrendezést, a szűrést/ritkítást, a színezést, a pontméretet és a paletták láthatóságát. A rekonstruált felület geometriáját is tartalmazza.

## Használat

```text
SAVEPROJECT "munkakonyvtar/felmeres.cloudscope"
SAVE
SAVEAS "masik-konyvtar/felmeres.cloudscope"
OPENPROJECT "munkakonyvtar/felmeres.cloudscope"
RECOVER
```

A File menü és a Home szalag Project csoportja ugyanezeket a parancsokat használja. A Cmd/Ctrl+S a teljes projektet menti; a Cmd/Ctrl+Shift+S új célfájlt kér. A címkék és síkbeli vonalak korábbi külön mentései továbbra is használhatók adatcserére, de nem helyettesítik a projekt mentését. Az ablakcím csillaggal jelzi a projekt módosítását.

## Források és hordozhatóság

A projekt a külső felméréseket nem másolja be: a források a projektfájlhoz viszonyított útvonalon szerepelnek. A teljes mappaszerkezet együtt áthelyezhető; a külön áthelyezett/hiányzó forrást a megnyitás elutasítja. A betöltött pontszámkorlát és a koordinátakeret is megmarad. Forrásfájl nélküli, programból létrehozott resident adat binárisan beágyazódik.

A resident forrásnál a rendezett XYZ-geometria és koordinátakeret mellett SHA-256 védi az eredeti RGB- és attribútumadatokat is. A streaming rétegek saját projektazonosítóval rendelkeznek; indexük lenyomatát és oszlophosszaikat ellenőrizzük. Ez nem a teljes, akár terabájtos store fájltartalmi ellenőrzőösszege. Minden mentett streaming címke 64 bites pontindexe mellett a pont koordinátája is megmarad, és visszatöltéskor külön egyeznie kell. A rétegsorrend változása nem rendelhet régi címkét egy másik réteghez.

A resident és streaming forrásmód továbbra is külön munkafolyamat. Resident adat mellé az ADDSTORE nem hoz létre kevert, eltérő címkeindexelésű dokumentumot; streaming jelenet indításához az OPENSTORE használható.

## Mentésvédelem és helyreállítás

Mentetlen változás esetén a kilépés, reset, forráscsere és rétegbezárás közös Yes/No/Cancel bekérést használ. A Yes teljes projektet ment. A sikertelen mentés megőrzi a régi célfájlt, az aktív projekt útvonalát és a módosított állapotot. A projekt nem írhatja felül a felmérését, és nem menthető a hivatkozott store könyvtárába.

A módosítások követése tartalmi állapotot és címkerevíziót használ; az undo visszatérhet a mentett állapothoz. A kamera, a rétegláthatóság és a megjelenítési beállítások módosítása is mentendő. Megnyitáskor az összes fájl és geometria külön háttérmunkában készül elő. Csak ellenőrzött eredmény kerülhet az élő dokumentumba; a közben szerkesztett dokumentumot egy késői eredmény nem írhatja felül. A megszakított előkészítés felszabadítja a megnyitott store-okat. A grafikus feltöltés hibája az előző adatok visszatöltését kéri, és megőrzi a dokumentumot.

A módosított projektről legfeljebb 30 másodpercenként automatikus helyreállítási példány készül a platform LocalApplicationData/CloudScope/Recovery könyvtárában. Nyitott alkalmazáspéldány helyreállítási fájlját egy másik példány nem ajánlja fel. A RECOVER a legújabb elhagyott példányt nyitja; külön útvonal is megadható. A helyreállított munka mentetlen projektként nyílik, és explicit mentést igényel. Normál projektmentés, reset/forráscsere és rendezett ablakbezárás eltávolítja az adott példány saját átmeneti helyreállítási mentését. Egy összeomlás után az utolsó sikeres példány marad meg; a még el nem készült változások nem garantálhatók.

A projekt a befejezett dokumentumállapotot tárolja. Az undo-lista, a parancselőzmény szövege, az aktív parancs és a még le nem zárt rajzelőnézet nem része a fájlnak. A GPU-eszköz és a géphez kötött memóriakeretek az alkalmazás beállításai maradnak.

## Ellenőrzés

A ProjectChecks tényleges parancsfuttatón ellenőrzi a teljes mentés/visszanyitás azonosságát, a részleges forrásbetöltést, az attribútum- és forrásváltozások felismerését, a generált adat beágyazását, a relatív útvonalak áthelyezhetőségét, az undo/redo checkpointot, a több réteg címkéit és láthatóságát, a helyreállítási fájlt, a megszakítást és késői eredményt, a hiányos/ismeretlen verziójú dokumentum elutasítását, valamint egy befecskendezett GPU-feltöltési hibát. A natív fájlválasztó és valamennyi operációs rendszer grafikus illesztőprogramja ettől külön ellenőrzési terület.

A 2. formátumverzió a teljes szelvénygyűjteményt és a mérési geometriát tárolja. Az 1. verzió egyetlen szelvénye betöltéskor veszteség nélkül a gyűjteménybe kerül; a beágyazott pont- és felületadatok formátuma változatlan.

A 3. verzió külön, double pontosságú dokumentumorigót tárol. Minden streaming réteg ehhez viszonyítva rajzolódik és válaszol kijelölésre/exportkérésre. Az első réteg bezárása nem változtatja meg a megmaradó objektumok helyét. Az 1–2. verzió migrációja resident forrásnál annak mentett origóját, régi streaming projektnél az első store origóját veszi át. A TIN kiválasztott/átrendezett forráspontokból állhat, ezért a visszaállítás nem követeli meg a felület és a megjelenített ponttömb azonos méretét és sorrendjét; az eredeti teljes forrásazonosság, a felület koordinátakerete, a véges geometria és az indexek érvényessége továbbra is ellenőrzött.

A régi streaming projektek címkepozíciói rétegenként helyi koordinátákban voltak tárolva. A migráció a tartós réteg-/pontazonosság megtartásával a közös dokumentumkeretbe alakítja ezeket, majd ugyanúgy összeveti a store tényleges pontjával.
