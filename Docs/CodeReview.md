# Kódáttekintés és konszolidáció — 2026. november 5.

Az áttekintés a solution projektjeire, a közös parancsfuttatóra, a viewer adat- és állapotkezelésére, az import/export rétegre, a visszavonásra, a térbeli és erdészeti algoritmusokra, valamint az Avalonia/ImGui dizájn közös alapjaira terjedt ki. A javítások a közös tulajdonosokba kerültek; a két grafikus háttér nem kapott külön parancs- vagy algoritmusváltozatot.

Ez a dokumentum a reprodukált hibákat és az ellenőrzés határait rögzíti. Nem állítja, hogy minden adathalmaz, valamennyi interaktív parancság és minden GPU/operációsrendszer-kombináció hibamentességét bizonyítottuk.

## Javítások

| Terület | Feltárt probléma | Egységes megoldás |
| --- | --- | --- |
| Parancsargumentumok | A fájlbekérés elnyelte a következő pontszámkorlátot/kapcsolókat; az összefűzés elvesztette az idézőjeles névhatárokat. | A fájl egy argumentum, a teljes soros bekérés megőrzi az idézőjeleket. A külön útvonalbekérés továbbra is elfogad szóközöket. |
| Szkriptek | A beágyazott fájl a hívó folytatása után futott; az üres válaszok elvesztek; ismeretlen parancs után továbbfutott a sorozat. | Helyes beágyazási sorrend, alapértéket elfogadó üres sorok, megszakítás hibás parancsnál/számnál, 100 000 soros rekurzióvédelmi korlát és lineárisan gyűjtött kimenet. |
| Bemenetek | NaN, végtelen és floattá alakításkor túlcsorduló értékek eljuthattak a geometriáig. | Közös végesérték-ellenőrzések, ellenőrzött double→float határ, szigorú pont- és képernyőkoordináták; hibás CINSTANCE nem törli az előző értéket. |
| Betöltés | A LAS és a szöveges import külön háttérfolyamatot tartott fenn; egy késői eredmény visszaírhatta az időközben törölt jelenetet. | Közös betöltési életciklus mind a hat resident formátumhoz, generációval ellenőrzött publikálás és megosztott előrehaladás. |
| Dokumentumazonosság | Új resident felhő megnyitásakor régi címkék maradhattak ugyanazokon az indexeken. | A felhőcsere törli a korábbi címkéket és dokumentumszintű visszavonási előzményeket; szűrés/színezés közben megmarad az eredeti pontazonosság. |
| Címkék és undo | Nem LAS forrásnál a JSON-mentés nem működött; az import nem volt visszavonható; a közös history a címkekezelő korlátozott belső stackjét hívta. | Külön forrásazonosság és LAS-exportképesség, általános JSON-mentés, egy műveletként visszavonható import, a közös historyban önálló állapotváltozások. A mutatószerkesztés is ugyanebbe a historyba kerül. |
| LAS | Hiányzó fejléc-/rekordellenőrzések; rövid régi fejléc helyett mindig 1.4 méretű struktúra olvasása; VLR-határok átlépése. | Verziófüggő beolvasás és ellenőrzés, rekordhossz-/pontszám-/koordinátaellenőrzés, hibás vagy csonkolt VLR/EVLR elutasítása. |
| LAS címkeexport | A régi formátumban a 31 feletti osztálykód csendesen megcsonkult; a célfájl közvetlenül íródott. | Nem ábrázolható kód elutasítása, forrásvédelem és ideiglenes fájlból atomikus csere. A jelzőbitek megmaradnak. |
| Koordinátakeret | Az importálók külön számoltak középpontot és sugarat; véges adatok köztes float-műveletei is túlcsordulhattak. | Közös PointCloudFrame, double pontosságú méretszámítás és véges helyi koordináták. A használatlan puffervég nem válik látható ponttá. |
| Felületrekonstrukció | A cellaszám alapján leálló, legfeljebb ötcellás keresés nem garantálta a legközelebbi szomszédokat; a PCA abszolút küszöbei léptékfüggést okoztak. | Pontos, kiegyensúlyozott k-d index és normalizált kovarianciamátrix. Az eredeti egyező fixture-ek megmaradtak; a független próbák a referencia korlátain túl is ellenőriznek. |
| Erdészeti algoritmusok | Nem véges paraméterek/koordináták; egészre alakítás előtti túl nagy CSF-rács; kizárt talajpont lehetett famag. | Paraméter- és koordinátavizsgálat, double pontosságban ellenőrzött rácsméret, kizárt pontok kihagyása a magkeresésből. |
| Helyi API | Az API OFF bezárhatta a saját válaszát, mielőtt az megérkezett volna. | Válasz utáni leállítás, ismételhető felszabadítás, a saját discovery-bejegyzés kezelése. |
| Fordítás és indítás | Az ImGui nélküli konfiguráció még fordította az ImGui-témát; a standalone program figyelmen kívül hagyta a dokumentált indítási argumentumokat. | Javított feltételes fájlkiválasztás; működő help és argumentumellenőrzés, a megadott LAS indításkori átadása a közös betöltőnek. |
| Rajzolás és diagnosztika | Egyetlen aktív nézetnél is kirajzolódhatott a többnézetes keret; ideiglenes hibakereső kód minden képkockában fájlt írt. | A tényleges elrendezés dönti el a keretet; a képkocka-fázisnapló a közös diagnosztikába került, külön engedélyezéssel. |
| Grafikai dizájn | Gyenge másodlagos szövegkontraszt a grafitsávon; túl halvány bekapcsolható állapotfeliratok; visszafogott parancssori fókusz. | Közös palettakorrekció, olvashatóbb 11 px-es állapot-/szekciófeliratok és hangsúlyosabb fókuszjel. A grafit karakter megmaradt. |
| Dokumentáció | A kézi parancslista és több futtatási/formátumleírás elavult volt. | A 74 parancs és 93 név listája a regisztrációból generálható; frissített README, formátum-, backend- és ellenőrzési leírások. |

## Ellenőrzés

| Ellenőrzés | Eredmény |
| --- | --- |
| Teljes solution, Debug, figyelmeztetések hibaként | 0 figyelmeztetés, 0 hiba |
| Teljes solution, Release, figyelmeztetések hibaként | 0 figyelmeztetés, 0 hiba |
| CloudScope ImGui és SharpMetal nélkül, elkülönített kimenettel | 0 figyelmeztetés, 0 hiba |
| CommandChecks | 385 sikeres ellenőrzés |
| UiChecks | 17 sikeres ellenőrzés |
| CLI `--help`, hiányzó fájl, negatív pontszámkorlát | Helyes kimenet és kilépési kód, grafikus ablak létrehozása nélkül |
| `git diff --check` | Rendben |

A parancsellenőrzés mind a 74 parancs és 93 kanonikus/alias név elindítását és leállítását valódi ViewerControlleren, helyettesített renderelőkkel vizsgálja. Ez a teljes parancstáblára kiterjedő indulási próba, nem minden lehetséges interaktív ág végigjátszása. A statikus API-elérhetőség 99/99; a keret-, bemeneti és életciklusműveletek külön szerepelnek.

Külön munkafolyamat-próba ellenőrzi az idézőjeles XYZ-importot pontszámkorláttal, a JSON-címkementést/-importot, az import undo/redo körét, a felhőcserét, a felületrekonstrukciót és OBJ-exportot, a ritkított PLY-exportot, valamint a reset után befutó régi betöltés elutasítását. A címke-history próbája 250 művelet visszavonását és újraalkalmazását ellenőrzi.

A LAS-próbák mind a 11 pontformátumot, a megfelelő rövid/verziófüggő fejlécet, a batch/iterátoros olvasás egyezését, a címkeexportot és a jelzőbitek megtartását vizsgálják. A formátummezők értelmezésének elsődleges hivatkozása az [ASPRS LAS 1.4 specifikáció](https://www.asprs.org/wp-content/uploads/2019/03/LAS_1_4_r14.pdf).

A térbeli index minden vizsgált pontjára és több k értékre nyers, minden pontot végigjáró keresés ad független referenciát. Szerepelnek duplikátumok, elkülönült pontok és eltérő pontsűrűségek. A PCA-próba ismert ferde síkon, 1e-6, 1 és 1e6 koordinátaléptékkel ellenőriz. A korábbi négy külső felületfixture továbbra is egyezik. Ezek konkrét ellenőrzött esetek, nem általános helyességbizonyítások.

A UI-próba a tényleges Avalonia ablakot, stílusokat, palettákat és szalagvezérlőt rendereli 1480×920 és 1040×680 méreten. Mind a hat szalagfülről PNG készül; a fő elrendezést és a Home fület szemrevételeztem is. A másodlagos szöveg kontrasztja a grafit- és hover-felületen legalább 4,5:1. A natív GPU-vezérlő helyett üres nézet szerepel, ezért ez nem igazol 3D-renderelést.

Reprodukció a repó gyökeréből:

```bash
dotnet build Source/CloudScope.slnx -warnaserror
dotnet build Source/CloudScope.slnx -c Release -warnaserror
dotnet run --project Source/CloudScope.CommandChecks --no-build
dotnet run --project Source/CloudScope.UiChecks --no-build
dotnet build Source/CloudScope/CloudScope.csproj --artifacts-path /tmp/cloudscope-portable -p:EnableSharpMetal=false -p:EnableImGui=false -warnaserror
dotnet run --project Source/CloudScope.CommandChecks --no-build -- --write-inventory Docs/CommandInventory.md
```

## Nyitott ellenőrzések és tényleges korlátok

- A natív Avalonia-indítás ebben a futtatókörnyezetben `Avalonia.Native was not able to start the RenderTimer`, `-6661` hibával leállt. A natív 3D-nézet, Metal/OpenGL shaderek, mélységi picking, fogópontok és többnézetes egérkezelés működését ezért ebben a körben nem sikerült futás közben igazolni. Ezek ellenőrzésére valódi grafikus munkamenet szükséges.
- Tömörített LAZ-dekóder nincs a projektben. A korábbi LAZ-támogatási ígéretet a felületről és a dokumentációból eltávolítottuk; az olvasó egyértelmű konvertálási üzenettel utasítja el a tömörített adatot.
- A felület a meglévő szög szerinti legyezőmódszerrel készül: lehet átfedő/nemmanifold, nem térfogati CAD-test és nem általános Delaunay-háló. A szomszédkeresés javítása nem változtatja meg ezt az algoritmikus célt.
- A CSF és a famagból növesztés szintetikus eseteken ellenőrzött. Új felméréstípusokra a paramétereket mérési adatokkal kell értékelni; univerzális szegmentálási pontosságot nem állítunk.
- Az E57-olvasó dokumentált numerikus bitpack részhalmaza maradt támogatott. A mostani ellenőrzés nem helyettesít nagy, valódi felméréseken végzett memória-, teljesítmény- és többplatformos próbát.

Az első konszolidációs csomag commitja: `a0c59c5`. Push nem történt.


## Folytatás: a háttérműveletek életciklusa

Az első csomag után külön stabilizációs kör készült a betöltés és a felületrekonstrukció közös életciklusára.

- A `BackgroundOperation<T>` egy helyen kezeli a futó munkát, a megszakítást, az előrehaladást, a hibákat és a viewer szálán végzett publikálást. A külön generációszámláló és a külön rekonstrukciós cancellation/progress mezők megszűntek.
- A reset, a forráscsere, a felülettörlés és a viewer lezárása tényleges megszakítási jelzést ad. A hat resident importformátum ezt kooperatívan ellenőrzi a beolvasási/dekódolási fázisokban; LAS-nál a batch-ek között. Ez nem az operációs rendszer egy már blokkoló szinkron lemezműveletének megszakítását jelenti.
- A megszakított munka nem küldhet késői százalékot, hibát vagy eredményt az új művelethez. A művelet helye azonnal felszabadul, ezért a törlés után új rekonstrukció indítható a régi UI-befejezési üzenetének bevárása nélkül.
- A háttérmunka cancellation-forrása a worker kilépésekor felszabadul, akkor is, ha a viewer már nem dolgoz fel képkockákat. Egy publikálási/GPU-feltöltési kivétel a normál hibacsatornára kerül, nem hagyja beragadva a műveletet.
- A lezárt viewer későn érkező képkocka-/átméretezési hívásai nem érintik a renderelő erőforrásait. A parancsfuttató a lezárt viewerre irányuló parancsokat már a belépési ponton elutasítja; a helyi API sem indítható újra lezárás után.

A regressziók determinisztikus szinkronizációval vizsgálják a futás közbeni megszakítást, a már sorba állított régi eredmény lecserélését, a késői progresszt, a lezárást, a worker hibáját és a publikáló kivételét. A valódi ViewerController-próba mesterséges feltöltési hibát követő sikeres rekonstrukciót is ellenőriz. Az importpróbák a megszakítás előtti/utáni viselkedést és az inputfájl felszabadítását vizsgálják.

Az új összesített eredmény: **420 sikeres CommandChecks-ellenőrzés**, a korábbi 385 helyett; a **17 UI-ellenőrzés** megmaradt. A teljes Debug/Release megoldás és az ImGui/SharpMetal nélküli konfiguráció figyelmeztetések hibaként kezelésével is sikeresen lefordult: mindhárom esetben 0 figyelmeztetés és 0 hiba. A 420 ellenőrzés Debug és Release módban is átment.

A natív Avalonia-indítást újrapróbáltuk, a `-6661` hiba változatlanul jelentkezett. Ugyanezt a hibát az [Avalonia #18895 hibajegye](https://github.com/AvaloniaUI/Avalonia/issues/18895) a macOS kijelző-/DisplayLink-állapotához kapcsolja; a kapcsolódó [#21453 javítási javaslat](https://github.com/AvaloniaUI/Avalonia/pull/21453) az ellenőrzéskor nyitott volt. Ez támogatja a környezeti ok lehetőségét, de önmagában nem bizonyítja a helyi okot. Egyedi időzítős kerülőmegoldás és ellenőrizetlen függőségfrissítés nem került a projektbe. A natív GPU-próba továbbra is nyitott.


## Natív indítás és interaktív munkafolyamatok — 2026-11-05

Ez a kör feloldott grafikus munkamenetben már ténylegesen ellenőrizte az Avalonia natív ablakát és a beágyazott Metal/OpenGL nézetet macOS 26.7.1-en, Apple Siliconon. A korábbi natív ellenőrzési akadályt az alábbi helyi mérés pontosítja:

| Munkamenet | Fő kijelző | Aktív kijelzők | DisplayLink eredmény |
| --- | --- | --- | --- |
| Zárolt | 1 | 0 | -6661 |
| Feloldott | 1 | 1 | 0 |

Ugyanaz az alkalmazás feloldott munkamenetben elindult. A fő kijelző azonosítója önmagában nem bizonyítja a grafikus munkamenet használhatóságát. Az új `--diagnostics` a natív CoreGraphics/CoreVideo feltételeket méri; a konkrét Avalonia RenderTimer-hiba érthető hibaüzenettel és 2-es kilépési kóddal zárul. Egyéb indítási kivételeket nem nyel el. Nem került egyedi időzítő a keretrendszer helyére.

A `scripts/package-macos.sh` az aktuális architektúrára készít helyben aláírt `.app` csomagot, külön végrehajtható és erőforráskönyvtárral. A meglévő generált csomagot egészében cseréli, így eltávolított függőség nem marad benne. A csomaghoz telepített .NET 10 szükséges; ez fejlesztői csomag, nem notarizált kiadás. Az alkalmazás natív neve CloudScope.

### A kézi próbák során javított hibák

- Az igény szerint rajzoló Metal host nem kért további képkockát a nézetátmenetekhez és a tartott navigációs billentyűkhöz. A látható nézetek kameraátmenetei és aktív bemenetei most fenntartják a rajzolást; a befejezett átmenet visszatér nyugalomba. Rejtett nézet nem tartja folyamatosan aktívan a rajzolót.
- A státusz nézetneve az elrendezési beállítást tükrözte a valódi kamera helyett. Most a tényleges irányból származik. Több nézetben a billentyűs navigáció csak az aktív kamerát mozgatja. A hosszú üresjárat utáni első billentyű nem alkalmazza az egész eltelt időt egyetlen elmozdulásként; kézi orbitálás megszakítja a régebbi nézetátmenetet.
- Ha a felhasználó a nézetben lenyomott billentyűt már a parancsmezőre váltva engedte fel, a kamera lenyomva tartottnak érzékelhette. A felengedés most fókusztól függetlenül eljut a nézethez.
- A Metal pontfelhő három megosztott uniformpuffert forgatott rajzolásonként. Négy nézet vagy több réteg egyetlen parancspufferén belül a későbbi rajzolás felülírhatta a korábbi kamera-/színadatokat. A kiemelés és a pivot is megosztott adatot írt át, a primitívrajzoló pedig 512 rajzolás felett újrahasználta az utolsó helyet. Közös `MetalDrawUniforms` adja át a kis adatokat encoder által másolt értékként minden érintett rajzolónál. Ez megszünteti a nézetek/rétegek számától függő pufferfelülírást; a mechanizmus az [Apple kis dinamikus pufferekre ajánlott API-ja](https://developer.apple.com/library/archive/documentation/3DDrawing/Conceptual/MTLBestPracticesGuide/BufferBindings.html).
- Az OpenGL beágyazás a globális kurzorpozíciót kérte le az esemény helye helyett. A natív próbában a jobb alsó mezőre adott görgetés a bal alsót nagyította. Az AppKit-híd most a megfelelő ablakhoz tartozó aktuális egéresemény helyét alakítja át, más bemenetnél a kurzorlekérdezés marad. A javítás után ugyanaz a görgetés a jobb alsó mezőt aktiválta és nagyította. Az eseménykoordináta értelmezése az [Apple locationInWindow dokumentációját](https://developer.apple.com/documentation/appkit/nsevent/locationinwindow) követi.
- Az importok alapértelmezett 0 értéke minden pontot jelentett, de a kifejezetten beírt 0-t a parancs elutasította. A négy korlátozható resident importparancs közös pontszámkérdése most következetesen elfogadja a 0-t.

### Ténylegesen végigpróbált natív műveletek

A szintetikus XYZ 8529 pontból állt: 81×81 pontos ferde talajrács és két színezett hengeres torony. Az egérpróbák a tényleges natív nézetbe érkeztek.

| Terület | Ellenőrzött eredmény |
| --- | --- |
| Metal indítás/import | A csomagból indult ablak, 8529 XYZ pont, RGB megjelenítés, pontméret, felülnézet és párhuzamos vetítés |
| Több nézet | Négy nézet, egyik kamera orbitálása és egy másik nagyítása egymástól függetlenül; a javítás előtti felülírás megszűnt |
| Kijelölés | Doboz, gömb és henger egérrel; térbeli fogópontok és előnézet; megszakítás Escape-pel |
| Címkék | 23 pontos, 7-es példányú címke mentése/visszatöltése; undo/redo; gömbkijelölés megerősítése után 2952 címkézett pont |
| Pontbevitel | PAN két natív kattintással, látható eltolás; két kattintással indított keresztszelvény, szélességmegadás, terv- és szelvénynézet |
| Rajzolás/nézetek | Három csúcsból zárt PLINE, névvel mentett és visszaállított kamera |
| Felület/export | RECONSTRUCT 8 0.8: 34 837 háromszög; tényleges Metal felületrajzolás; OBJ-fájlban 8529 csúcs és 34 837 lap; felület elrejtése |
| OpenGL | Külön tesztcsomagban igazolt OpenGL host; ugyanazon XYZ importja és megjelenítése; négy nézet; orbitálás/nagyítás a megfelelő mezőben; dobozkijelölés és előnézet mind a négy nézetben |

A JSON-fájlok forrásindexeit és az OBJ csúcs-/lapszámát külön fájlolvasással is ellenőriztük. A nagyobb gömbkijelölés a korábbi címke 15 pontját felülírta: 2944 Ground + 8 Native test = 2952 egyedi címkézett pont.

### Automatizált regresszió és fennmaradó határok

Az új kameratesztek minden standard nézet átmenetét, megállását és helyes nevét, az aktív kamera billentyűkezelését, az üresjárati elmozdulás korlátját és az orbitálással megszakított átmenetet vizsgálják. A tényleges Avalonia ablakon külön próba tartja lenyomva a W-t, majd a parancsmezőre fókuszálva engedi fel. Az explicit 0-s import a teljes pontfelhőt további kérdés nélkül tölti be.

A natív próbák szintetikus resident felhőn történtek. Nem igazolnak nagy felméréseken mért teljesítményt, minden GPU/operációs rendszer támogatását vagy minden parancs minden interaktív ágát. A streaming több réteges rajzolási útja megkapta ugyanazt a uniformjavítást, de nagy streaming adatállománnyal külön natív terhelési próba még szükséges. A CSF/TREESEG algoritmusok automatizált próbái megmaradtak; valós erdészeti adatokkal végzett interaktív és pontossági ellenőrzés külön feladat.

Végső összesítés ebben a körben: **442/442 CommandChecks** Debug és Release módban, **21/21 UiChecks**, teljes Debug/Release megoldás és elkülönített ImGui/SharpMetal nélküli build: **0 figyelmeztetés, 0 hiba**. A csomagolás és a kész `.app` aláírás-ellenőrzése sikeres; a csomag `--help` parancsa grafikus ablak nélkül működik. A feloldott natív diagnosztika egy aktív kijelzőt és sikeres DisplayLink-létrehozást mutatott.


## A hét karbantartási pont végrehajtása — 2026-11-05

A funkció- és kódminőség-felmérés hét javaslatát egymás után végrehajtottuk. A változtatások az alkalmazás működését, az erőforrások tulajdonlását és az ellenőrzést együtt rendezik.

| Pont | Megvalósítás | Ellenőrzés |
| --- | --- | --- |
| 1. Mentésvédelem | A címkék mentési állapotát revízióazonosító követi, a globális és önálló undo/redo is vissza tud térni a mentett állapothoz. Az összes címke törlése is mentendő módosítás. A sikertelen mentés nem zárja be a dokumentumot; az Avalonia és OpenTK ablakbezárás is a mentésvédelmet használja. Közös AtomicFile írja a címkéket, síkbeli vonalakat, store-címkéket és beállításokat. A címkementés a forrásfelhőt nem írhatja felül. | Sikertelen íráskor a régi fájl megmarad, nincs félkész ideiglenes fájl; hibás cél, cél nélküli kilépés, teljes törlés, undo/redo és mentési checkpoint próbák. |
| 2. Forrásazonosítás | A 3-as címkeformátum a betöltött pontok rendezett helyi XYZ-geometriájának SHA-256 értékét, a pontszámot és a koordinátakeret origóját tárolja. Az azonosító a resident import háttérmunkájában készül; a pathname nem része az azonosságnak. A szűrés/ritkítás ezt nem változtatja meg. | Átnevezett forrás elfogadása; eltérő sorrend, keret és részleges betöltés elutasítása. Régi vagy tudatosan más forrásra vitt címkéhez LOADLABELS Transfer szükséges; az indexhatár ekkor is kötelező. |
| 3. Streaming hibák | Külön cellaolvasó és egyszeri tulajdonú PointTileCellData kezeli a kölcsönzött tömböket. A hibás cella nem állítja le a következő olvasást, nem marad örökké függőben, és nem próbálkozik minden képkockában újra. A feltöltési hiba visszaadja a kiosztott lapokat. Bezáráskor a reader kilépése megelőzi a fájlok felszabadítását. | Olvasási és feltöltési hibák befecskendezése, hiba utáni sikeres cella, kölcsönzött adat felszabadítása, megszakításra ébredő reader és ismételt Dispose. |
| 4. GPU-élettartam | Közös FrameResourcePool tárol minden dinamikus rajzolást; nincs 64 elem utáni helyfelülírás. Újrahasználat csak tényleges parancspuffer-befejezés után lehetséges, legfeljebb három függő kerettel és szükség szerinti visszanyomással. A highlight-adat publikálás után változatlan. A streaming előkészítése képkockánként egyszer fut, a lapok GPU-szekvencia alapján válnak szabaddá. A host a backend sorát használja, így a mélységi olvasás a rajzolás után következik. | 600 egyidejű logikai rajzolási hely, befejezetlen keretek elkülönítése, 9 nézet miatti téves felszabadítás kizárása; valódi Metalon négy keret × 600 rajzolás szín- és mélységi visszaolvasással. |
| 5. Háttérfeldolgozás | A ViewerProcessingService a meglévő BackgroundOperation életciklusával kezeli az INDEX, GROUNDSEG és TREESEG munkákat. A fő szálra csak az eredmény publikálása kerül. A szegmentálás külön undo-tranzakció; forrás-/címkeváltozás elavult eredménye nem alkalmazható. CANCEL/Escape megszakítja a munkát. A CSF, a faalgoritmus és az indexelő hosszú ciklusai ellenőrzik a cancellationt. Az index új, előkészített könyvtárból publikál, meglévő nem üres célra nem ír. | Előzetes és futás közbeni megszakítás, kizárólag viewer-szálon történő publikálás, külön undo, reset után sorban álló eredmény eldobása, félkész index elutasítása, teljes index újranyitása. |
| 6. Felelősségek | A ViewerDocument birtokolja a resident adatot, forrásadatokat, rétegeket és felületállapotot, valamint az atomikus pontexportot. A ViewNavigation saját kameratörténetet és névvel mentett nézeteket kezel. A feldolgozás külön szolgáltatás, a kijelölés/szerkesztés a SelectionController feladata. A WorkspaceMenu a menüadapter, a ViewerStatusPresenter a státusz megjelenítését kezeli. | Dokumentumpéldányok és kameratörténetek függetlensége; régi exportútvonal törlése forrásváltáskor; az előrehaladási sáv és szöveg ugyanazt a műveletet jelzi; a meglévő parancs- és UI-próbák megmaradtak. |
| 7. Folyamatos ellenőrzés és memória | SDK-kiválasztás global.json-ban, közös buildbeállítások és központi csomagverziók. Linux/Windows/macOS × Debug/Release CI-konfiguráció, parancsnyilvántartás-ellenőrzés, portable build és mérési artefaktum. Külön kézi GPU-workflow valódi Mac runnerre. Az undo legfeljebb 1000 parancsot tart becsült 128 MiB keretben, a viewerben nincs párhuzamos helyi címke-history. A befejezett GPU-pufferek cache-e 64 MiB, a közös vonaltesszelláció cache-e 32 MiB. TIME diagnosztikát ad. | Memóriakeret miatti history-ürítés és utána helyes undo/redo, egyszeri GPU-felszabadítás, közös 9 nézetes tesszelláció, módosítás miatti invalidálás, túlméretes geometria kihagyása a cache-ből, újrafuttatható allokációs benchmark. |

A GPU által már használt, változatlan pufferek korábbi CPU-tulajdonának felszabadítása a normál parancspufferek erős erőforrás-hivatkozásaira támaszkodik; ezt az [Apple retainedReferences szerződése](https://developer.apple.com/documentation/metal/mtlcommandbuffer/retainedreferences) rögzíti. A dinamikus pufferek készlete ezen felül saját tulajdont tart a tényleges befejezésig.

A központi history régi csoportok eldobásakor üzenetet küld. A 128 MiB becslés az undo által megtartott adatot korlátozza; a 64/32 MiB a szabad grafikus/előállított vonalgeometriai cache-ekre vonatkozik. Egyik sem a teljes betöltött felmérésre vagy a folyamat teljes RAM/GPU-memóriájára ígért kemény plafon.

A helyi Release mikromérés 500 csúcsú, íves és változó szélességű vonalat készít elő 30 képkockára, 9 nézetben: az ismételt számítás 162 572 472 bájtot foglalt, a bemelegített cache 248 bájtot. Az adott futásban 87,16 ms és 2,10 ms adódott, a cache 130 040 bájtot tartott. Ez a tesszelláció előkészítésének célzott mérése; nem az alkalmazás teljes képkockasebességének vagy egy valódi felmérés teljesítményének aránya.

Helyi összesítés: **499 sikeres CommandChecks**, **23 sikeres UiChecks**, **2400 valódi Metal rajzolás négy keretben**, szín- és mélységi ellenőrzéssel. A Debug/Release megoldás és a Metal/ImGui nélküli build figyelmeztetéseket hibaként kezelve fordul. A UI-próbák továbbra is a natív viewport helyettesítésével renderelik az Avalonia felületet; a GPU-próba ettől külön, tényleges Metal-eszközön fut.

A CI-fájlok helyben elkészültek és a YAML-szintaxisuk ellenőrzött. Távoli GitHub-futtatás és Windows/Linux végrehajtás ebben a munkamenetben nem történt. A Metal-workflow-hoz a cloudscope-gpu címkével regisztrált, valódi Apple Silicon runner szükséges. A commitok helyiek; push nem történt.

További termék-/terhelési munka marad a teljes projektmentés (3D rajzok, szelvények és nézetek közös dokumentuma), a nagy valódi streaming felmérések, a terepi CSF/TREESEG pontosság és a többplatformos natív egérpróba. Ezekre a mostani célzott tesztek nem adnak általános igazolást. Kényszerített folyamatleállítás után az indexelő előkészítési könyvtára megmaradhat; félkész store a célhelyen ekkor sem publikálódik.


## Main integráció — 2026-11-05

A két korábbi fejlesztési ág teljes commitelőzménye egy közös main ágba került. A különálló fogópont-stílusjavítás mindkét renderelőben megmaradt: a végpont és felezőpont közös geometriai jelet és közös hover/aktív színkezelést kap, miközben a snap-funkciójuk különbözik. A Retina-próba a teljes jel kiterjedését méri, ezért négyzet és rombusz esetén is ugyanazt a kétszeres fizikai méretet ellenőrzi.

A távoli első CI-kör Linuxon és macOS-en sikeres volt, Windowson a beágyazott parancsok sorrendjét vizsgáló teszt LF-et várt a natív CRLF helyett. Az elvárt üzenet most a platform sortörésével készül; a parancsfuttató sorrendjén nem kellett változtatni. A main új ellenőrzése már ezt a javítást tartalmazza.

A commitmetaadatokból az AI-szerzői/társszerzői és munkamenet-bejegyzések kikerültek, a két gépi szerzői rekord a repository tulajdonosának konfigurált Git-azonosítójára került. A többi szerzői adat megmaradt. Az asszisztenshez kötött helyi engedélyfájl nem része az alkalmazásnak, ezért az előzményekből is eltávolítottuk. Emiatt a korábbi commitazonosítók megváltoztak; a korábbi szakaszokban szereplő azonosítók az átírás előtti állapotot jelölik. A teljes eredeti történetről helyi biztonsági mentés készült.

## Teljes projektmentés — 2026-11-05

A korábban nyitott teljes projektmentés elkészült. A `.cloudscope` dokumentum forráshivatkozásokat, stabil rétegazonosítókat, címkéket és osztálydefiníciókat, 3D/síkbeli vonalakat, szelvényt, kamerákat, névvel mentett nézeteket, megjelenítési állapotot és felülethálót tartalmaz. A részletes formátum- és használati szerződés a [Projects.md](Projects.md) dokumentumban található.

A közös resident beolvasó az import és a projektvisszaállítás tulajdonosa. A háttérben előkészített projekt a forrásgeometria és a megtartott RGB/attribútumadatok SHA-256-ellenőrzésén, illetve a rétegek és címkepontok ellenőrzésén megy át, mielőtt az aktív állapothoz nyúlna. A megszakított, még nem publikált eredmény felszabadítja a megnyitott store-okat. A streaming kijelölés rétegazonosító alapján köt újra: új réteg hozzáadása nem veszti el a régi címkereferenciákat, egy eltávolított réteg pontjait pedig nem címkézheti át véletlenül a megmaradó rétegen. A már eltávolított réteg indexeire épülő undo előzmény megszűnik.

A SAVE/SAVEPROJECT, SAVEAS, OPENPROJECT és RECOVER közös parancsfelületen és a grafikus menüből/szalagról is elérhető. A kilépés, reset, forráscsere és rétegbezárás ugyanazt a projektmentés-védelmet használja. Az automatikus helyreállítási fájl külön háttérmunkában, atomikusan készül; az aktív példányokat fájlzár különíti el. A mentett checkpointot a címke-revízió és az állapot tartalmi lenyomata határozza meg; a geometrialenyomat változatlan entitáson nem számolódik újra minden státuszlekérdezéskor.

A ProjectChecks a teljes mentés/újranyitás egyezését, a forráskorlát és koordinátakeret megtartását, a megváltozott RGB-adat elutasítását, a beágyazott adatot, a relatív mappák áthelyezését, az undo/redo mentési checkpointot, a rétegek címkéit és láthatóságát, az automatikus helyreállítást, a hiányos/új verziójú állapot elutasítását, a késői eredményt, a store-felszabadítást és a befecskendezett GPU-publikálási hibát ellenőrzi. Összesen **533 sikeres CommandChecks és 23 sikeres UiChecks** futott helyben. A 1040 pixeles ablak Home füle az új Project csoporttal szemrevételezve is olvasható. A natív fájlválasztót és a mostani új funkciókat valódi Windows grafikus munkamenetben ebben a körben nem ellenőriztük.

A végső helyi ellenőrzés Debug és Release módban is 533/533 parancs- és 23/23 felületellenőrzést adott. Mindkét teljes solution-build és a Metal/ImGui nélküli portable build figyelmeztetéseket hibaként kezelve, 0 figyelmeztetéssel és 0 hibával futott. A parancsnyilvántartás újragenerálva: 78 parancs, aliasokkal 98 név. Távoli CI az új projektmentési változtatáson ebben a pontban még nem futott.
