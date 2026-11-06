# Nagy felmérések ellenőrzése

A CommandChecks külön, kézzel indítható méréseket tartalmaz. A hétköznapi tesztfuttatás kis, szintetikus adaton ellenőrzi a mérő és a regressziók működését; nagy felmérést nem olvas be automatikusan a CI.

```bash
dotnet run --project Source/CloudScope.CommandChecks -c Release -- --survey-benchmark /data/survey.las /tmp/cloudscope-survey
dotnet run --project Source/CloudScope.CommandChecks -c Release -- --survey-resident /data/survey.las /tmp/cloudscope-resident
dotnet run --project Source/CloudScope.CommandChecks -c Release -- --survey-gpu /tmp/cloudscope-survey/store /tmp/cloudscope-survey
```

A teljes mérés resident beolvasást, forrásazonosítást, szűrést, indexelést, store-megnyitást, térbeli kijelölést és 300 kameranézet térbeli bejárását méri. Már meglévő store is megadható bemenetként: ekkor a resident betöltés és az indexelés kimarad. Az index célja új vagy üres könyvtár legyen; a mérő nem írja felül a forrásadatot vagy a már elkészült store-t. A JSON-kimenet `survey-cpu.json`, illetve a külön resident mérésnél `survey-resident.json`.

A kijelölést egy külön, a teljes pontfájlt szekvenciálisan bejáró keresés ellenőrzi. A szűrt nézet pontszámát külön magasságszámlálás igazolja. Az allokációs adat a folyamat által a művelet alatt kiosztott kezelt bájtok száma; nem azonos a végén megtartott memóriával. A 25 ms-onként mintázott folyamat-working-set tartalmazhat memóriatérképezett fájllapokat is, és rövid csúcsokat nem feltétlenül lát.

## Helyi mérés — 2026-11-06

Adat: `jeli_off_ground.las`, LAS 1.2 / 3-as pontformátum, **61 598 322 pont**, 2 094 344 029 bájt. Környezet: macOS 26.7.1, .NET 10.0.10, 10 logikai processzor, 24 GiB RAM. A fájl helyben elérhető; ezek nem hideg lemezes vagy hálózati letöltési mérések.

| Művelet | Megfigyelt eredmény |
| --- | --- |
| Teljes resident beolvasás, első teljes mérés | 3,58 s |
| Geometria és RGB/attribútum-lenyomat | 2,39 s |
| Indexelés, alapértelmezett chunk-/rácsparaméterek | 12,56 s; a teljes 61 598 322 pont megmaradt |
| Indexelés mintázott folyamat-working-set maximuma | 10,08 GB; nem kizárólag kezelt memória |
| Indexelt térbeli kijelölés | 18,39 ms, 815 pont |
| Ugyanaz a kijelölés teljes fájlbejárással | 667,61 ms, ugyanaz a 815 pontindex |
| 300 kameranézet térbeli bejárása | összesen 50,09 ms; nem GPU-rajzolási idő |

A magasságtartomány felére szűrés ugyanazt a **34 513 434** pontot adta a javítás előtt és után. Az elkülönítve végzett resident mérés:

| Szűrés | Előtte | Utána |
| --- | ---: | ---: |
| Kiosztott kezelt memória | 4,52 GB | 0,97 GB |
| Mintázott folyamat-working-set maximuma | 6,01 GB | 3,28 GB |
| Műveleti idő az adott futásban | 1109 ms | 915 ms |

A nézetépítés most megszámolja a találatokat, és közvetlenül a végleges pont-/forrásindex-tömböket tölti. Nincs növekvő lista és utólagos teljes tömbmásolás. A 78,6%-os allokációcsökkenés erre az adatra és szűrésre mért eredmény; az időadat nem általános gyorsulási ígéret.

## Valódi Metal-próba és javított streaming korlát

A `--survey-gpu` tényleges Metal-eszközön, 1920×1080 képpontos szín-/mélységi célba rajzol. Nem nyit natív ablakot, és nem igényli az Avalonia RenderTimert. 360 orbitáló képkockán RGB, magasság, osztály, intenzitás és visszatérés szerinti színezés szerepel; a végső képet visszaolvassa, és elutasítja az üres képet. A `survey-metal.json` rögzíti a ténylegesen rajzolt pontszámot és a Metal által jelentett memóriafoglalást is.

A nagy állomány első próbája hibát jelzett: a 2 milliós képkockakeret 171 kis cellát választott, amelyek a pontszám alapján belefértek volna, de 171 teljes, 65 536 pontos GPU-lapot igényeltek. A 6 milliós resident keretből ez nem fért ki. A tervező ezért most a pontkeret mellett a tényleges lapkeretet is figyelembe veszi, mindkét grafikus háttéren. A Metal utolsó puffere részleges: a foglalás nem kerekedik egy teljes további pufferre a beállított kereten túl. A kijelölt pontok visszaolvasása összefüggő futamokat olvas, megőrzi a sorrendet/duplikátumokat, és megszakítható.

A javított, azonos adaton végzett Metal-futásban 360/360 képkocka sikerült, legfeljebb 1 081 559 ponttal képkockánként, 185 008 128 bájt jelentett Metal-foglalással. A végső kép 183 292 háttértől eltérő képpontot tartalmazott. A szekvenciális parancsbeküldés és GPU-befejezés kivárásának mediánja 2,38 ms, 95. percentilise 3,53 ms volt. Ez **offscreen képkockakésleltetés**, nem a natív alkalmazás képernyőn mért FPS-e vagy egérválaszideje.
