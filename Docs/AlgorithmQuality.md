# Algoritmusok számszerű ellenőrzése

## Terep-TIN

A `TIN Ground <cellsize> <maximumedge>` az eredeti Ground osztályú és/vagy Ground címkéjű resident pontokból épít 2,5D terepet. A `TIN All` minden betöltött forráspontot használ; növényzetből ettől még nem lesz automatikusan talaj. A ritkítás és a megjelenítési szűrő nem módosítja a bemenetet. A magasság egyértékű XY-függvény; függőleges fal, áthajlás és több szint egymás felett ebben a modellben nem ábrázolható.

0 cellaméret az összes különböző XY-t tartja meg, pozitív cellaméret rácscellánként a legalacsonyabb tényleges forráspontot. Azonos magasságnál az XY sorrend dönt, így az eredmény nem függ a fájlsorrendtől. Az alapértelmezett 200 000 helykorlát túllépése érthető hibát ad; nagyobb cellamérettel csökkenthető a méret. Nincs csendes ponteldobás a korlátnál.

A triangulációt a [NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0) végzi. Az alkalmazás a forráspontok magasságát rendeli a Delaunay-topológiához, felfelé irányítja a lapokat, és minden olyan háromszöget elhagy, amelynek bármelyik vízszintes éle hosszabb a megadott maximumnál. A pozitív élhossz kötelező: nagy adathiányokat a felhasználó engedélye nélkül nem hidal át. Ez nem kényszervonalas/határpoligonos mérnöki TIN; törésvonalak még nem adhatók meg. A régi RECONSTRUCT megmarad általános, közelítő térbeli vizualizációnak.

Az ellenőrzőprogram független determinánssal vizsgálja az üres körök feltételét, a sík területét, a laporientációt, az élek incidenciáját és az Euler-karakterisztikát. Analitikus síkon 25 helyből 32 háromszög, 16 négyzetegység terület és pontos magasság adódik. Külön próbák fedik a duplikált XY-t, rácsritkítást, kollineáris adatot, megszakítást és két elkülönült felmérési szigetet.

## CSF és TREESEG

A közös `ClassificationMetrics` valódi/hamis pozitív és negatív darabszámot, precisiont, recallt, F1-et és accuracyt számol. A nem értelmezhető arány null marad, nem válik 100%-os eredménnyé. A CSF lejtős és hullámos, ismert referenciafelületén a precision/recall minimuma 98%; a két ismert törzsből álló TREESEG-próbán 99%. Ezek szintetikus regressziós küszöbök, nem általános terepi pontossági ígéretek.

A CSF új regressziója ugyanazt a pontkészletet fordított sorrendben is feldolgozza. Azonos XY-távolságnál korábban a fájl első pontja döntött a ruha ütközési magasságáról, ezért az eredmény sorrendfüggő volt. A javítás ilyenkor a normál Z szerinti alacsonyabb visszaverődést választja. A legközelebbi XY-pont elve az [eredeti CSF rasterizációját](https://github.com/jianboqi/CSF/blob/master/src/Rasterization.cpp) követi; az egyenlő távolságú eset most kifejezetten determinisztikus.

## Jeli referencia-összevetés, 2026-11-06

A felhasználó helyi `jeli_ground.las` és `jeli_off_ground.las` fájljának közös 100×100 koordinátaegységes kivágatából egyenletes forrásindex-mintavétellel 358 910 talaj- és 299 777 egyéb pont került az összevetésbe. A forrásindex-lépés fájlonként 39, illetve 61; a precision/F1 a létrejött minta osztályarányára vonatkozik, nem a teljes kivágatra súlyozott becslés. Az eredmények a megadott fájlfelosztással való egyezést mutatják; a fájlok címkézését nem ellenőriztük független terepi referenciával. A környezet megegyezik a [teljesítménymérésével](Performance.md).

| Ruhafelbontás | Precision | Recall | F1 | Feldolgozás |
| --- | ---: | ---: | ---: | ---: |
| 0,5 | 94,857% | 99,978% | 97,350% | 0,648 s |
| 1 | 95,165% | 99,996% | 97,521% | 0,161 s |
| 2 | 95,535% | 99,954% | 97,695% | 0,064 s |

A küszöb mindhárom esetben 0,5; a többi CSF-paraméter alapértelmezett. Az alacsony növényzet/talaj határán 16 766–19 456 nem talaj referencia-pontot sorolt talajnak a módszer. Emiatt az alapértékeket egyetlen kivágat alapján nem írtuk át. Ugyanennek a kivágatnak a referencia-talajpontjaiból 0,5 cellával és 2 maximális éllel 35 241 hely, 69 810 TIN-lap készült 0,647 másodperc alatt. A teljes források áttekintése/mintavétele 5,635 másodperc volt; ez egy helyi futás.

Újrafuttatás:

```sh
dotnet run --project Source/CloudScope.CommandChecks -c Release -- \
  --ground-reference /absolute/jeli_ground.las /absolute/jeli_off_ground.las /absolute/report-directory
```

A JSON-jelentés a pontos paramétereket, referenciakivágatot, mintavételt és nyers hibamátrixokat is tartalmazza. A faalgoritmus terepi pontosságához egyedfánként ellenőrzött referencia még szükséges; a ground/off-ground felosztás erre nem használható.
