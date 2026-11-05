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

A módosítások helyben vannak; commit és push nem történt.
