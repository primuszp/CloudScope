# CloudScope parancsrendszer – terv

> **Állapot: megvalósítva.** A terv mind a kilenc lépése elkészült. A rendszer leírása a
> [CommandSystem.md](CommandSystem.md), a mai parancskészlet a
> [CommandInventory.md](CommandInventory.md). Ellenőrzés:
> `dotnet run --project Source/CloudScope.CommandChecks` — 40 ellenőrzés, és a
> lefedettségi jelentés szerint a viewer 51 publikus képességéből 51 elérhető parancsból.
>
> Amit a megvalósítás közben másképp döntöttem, mint ahogy a terv írta, a dokumentum
> végén, a *Megvalósítási eltérések* alatt áll.

Egyetlen, végiggondolt parancsrendszer terve. Nem a meglévő 34 parancs
foltozása, hanem egy zárt modell, amelyből a mai parancsok is levezethetők, és
amelybe a hiányzók természetesen illeszkednek.

Kiindulási állapot: [CommandInventory.md](CommandInventory.md).
A mai runtime leírása: [CommandSystem.md](CommandSystem.md).

---

## 1. Mi a baj a mai rendszerrel

A meglévő infrastruktúra – `CommandRuntime`, `Keyword`, `PromptOptions`,
`CommandLineSession`, `CommandCompletionSource`, `CommandMenu` – **jó**, és a
terv megtartja. A törés nem itt van, hanem négy helyen:

**1.1 A parancsok törzse kézzel írt állapotgép.**
Mert a runtime minden bevitelnél újra meghívja ugyanazt a metódust, minden
többlépéses parancsnak saját fázismezőt kell tartania:
`_selectPhase`, `_zoomPhase`, `_zoomFirstCorner`, `_filterPhase`,
`_pendingFilterAttribute`, `_vportsLayoutPrompt`, `_vportsArrangementPrompt`,
`_vportsViewPrompt`, `_pendingViewportLayout`, `_lastViewportLayout`, …
Egyetlen fájlban 53 hivatkozás ezekre a mezőkre. Ehhez jön az
`ICommandCancellationHandler`, amely egy `switch`-ben kézzel nullázza őket
parancsnév szerint – ha valaki elfelejt egy mezőt, a következő indításkor a
parancs egy fázis közepén ébred. A `SELECT` már ma is védekezni kényszerül
ellene: „*if the tool was cancelled externally … reset*”.

**1.2 Nincs pontbevitel, ezért funkciók sem lehetnek.**
A `CommandSystem.md` maga rögzíti: „`ZOOM Window` is reserved until the editor
supports interactive point prompts”. Ugyanez az egy hiányzó képesség az oka,
hogy nincs `MOVE`, `ROTATE`, `SCALE`, `PAN`, `PIVOT` – a gizmó-API
(`BeginGrab`/`BeginRotate`/`BeginScale`) régóta megvan, csak nincs mód
*pontot vagy távolságot kérni* a felhasználótól.

**1.3 Az állapotnak nincs egy helye.**
A pontméret a `CameraInputController`-ben, a vetítés az `OrbitCamera`-ban, a
színforrás a `PointCloudDataset`-ben, a renderelő háttér egy környezeti
változóban él. A `ViewerStatusSnapshot` ezeket kézzel gyűjti össze, a menü
kézzel képezi vissza (`CommandMenu.IsChecked`). Három leképezés ugyanarról.

**1.4 A parancs nem atomi.**
A `CommandFlags.NoUndoMarker` és a `CommandStarted`/`CommandEnded`/
`CommandCancelled` életciklus elő van készítve, de nincs mögötte semmi. Egyetlen
egylépéses kijelölés-undo van.

---

## 2. A modell

Öt fogalom, és minden más ebből következik.

```
                       ┌──────────────────────────┐
   menü, eszköztár,    │                          │
   gyorsbillentyű,  ─► │        Editor            │ ◄─ SCRIPT, API, teszt
   viewport-kattintás  │  (egyetlen bemenet)      │
                       └────────────┬─────────────┘
                                    │
                       ┌────────────▼─────────────┐
                       │      CommandTable        │  név, alias, flag, csoport,
                       │  (egyetlen névtér)       │  leírás, szintaxis
                       └────────────┬─────────────┘
                                    │ korutin lépteti
                       ┌────────────▼─────────────┐
                       │   parancs törzse         │  lineáris kód,
                       │   IEnumerable<Prompt>    │  fázismező nélkül
                       └──────┬──────────────┬────┘
                              │              │
                  ┌───────────▼──┐    ┌──────▼──────────┐
                  │ SysVarTable  │    │  Transaction    │
                  │ (állapot)    │    │  (undo/redo)    │
                  └──────────────┘    └─────────────────┘
```

### 2.1 Editor – egyetlen bemeneti csatorna

Minden bevitel egy helyen lép be: `Editor.Execute(string)`. A menü, az
eszköztár, a gyorsbillentyű, a `SCRIPT`, a tesztek és az API mind
parancsszöveget küldenek. Ez ma már majdnem így van (`CommandMenu` csak
parancsszöveget ad ki) – a terv annyit tesz hozzá, hogy **a viewportban tett
kattintás is ide fut be**, ha éppen pontkérés aktív.

Az `Editor` a parancssori kérdezés API-ja, az AutoCAD `Editor` osztályának
mintájára:

```csharp
PromptStringResult   GetString(PromptStringOptions options);
PromptResult         GetKeywords(PromptKeywordOptions options);
PromptIntegerResult  GetInteger(PromptIntegerOptions options);
PromptDoubleResult   GetDouble(PromptDoubleOptions options);
PromptDoubleResult   GetDistance(PromptDistanceOptions options);
PromptDoubleResult   GetAngle(PromptAngleOptions options);
PromptPointResult    GetPoint(PromptPointOptions options);
PromptPointResult    GetCorner(PromptCornerOptions options);
PromptFileNameResult GetFileNameForOpen(PromptFileOptions options);
PromptFileNameResult GetFileNameForSave(PromptFileOptions options);
void                 WriteMessage(string message);
```

Mindegyik `PromptStatus`-t ad vissza: `OK`, `Keyword`, `Cancel`, `None`.
Ez a mai `PromptOptions`/`Keyword` osztályokra ül rá – azok maradnak, a
`PromptOptions` lesz a `PromptKeywordOptions` alapja.

**Pontbevitel.** A `GetPoint` két forrásból elégíthető ki: begépelt `x,y[,z]`
koordinátából, vagy a viewportban tett kattintásból. A kattintás
világkoordinátáját az `OrbitCamera.TryPickWorldPoint` már ma megadja; a
`ViewerController.MouseDown` csak annyit változik, hogy ha az `Editor` pontra
vár, a kattintást válaszként továbbadja, nem kijelölő gesztusként dolgozza fel.
`GetCorner` a második sarok gumizott előnézetével ugyanez.

Ez az egy képesség nyitja meg a `ZOOM Window`-t, a `MOVE`/`ROTATE`/`SCALE`-t,
a `PAN`-t és a `PIVOT`-ot – nem véletlen, hogy mind ugyanazon hiányzott el.

### 2.2 A parancs törzse korutin, nem állapotgép

A parancsmetódus ma minden válasznál újraindul, ezért kénytelen fázismezőben
emlékezni. A terv szerint **egyszer indul, és a helyén megáll**, amíg válasz
nem jön: a törzs `IEnumerable<PromptStep>`, a runtime pedig a felhasználó
minden bevitelénél egyet léptet az enumerátoron.

Így néz ki ma a `VPORTS` váza (négy fázismező, három belépési pont):

```csharp
if (_vportsViewPrompt)        { … }
if (_vportsLayoutPrompt && …) { … }
if (_vportsArrangementPrompt) { … }
if (input.Length == 0)        { _vportsLayoutPrompt = true; … }
```

És így nézne ki a modellben – a kód sorrendje maga a párbeszéd sorrendje:

```csharp
[CommandMethod("VPORTS", "VP", Group = CommandGroup.View)]
public IEnumerable<PromptStep> Viewports(Editor ed)
{
    var layout = ed.GetKeywords(LayoutPrompt);
    if (layout.Status != PromptStatus.OK) yield break;

    if (RequiresArrangement(layout.Keyword))
    {
        var arrangement = ed.GetKeywords(ArrangementPrompt);
        if (arrangement.Status != PromptStatus.OK) yield break;
        layout = arrangement;
    }

    var view = ed.GetKeywords(ViewPrompt);
    if (view.Status != PromptStatus.OK) yield break;

    ed.WriteMessage(Viewer.SetViewportLayout(Parse(layout), Parse(view)));
}
```

Négy mező, egy `CancelCommand`-ág és három belépési pont helyett nyolc sor.

**Megszakítás ingyen jön.** `Esc` esetén a runtime eldobja az enumerátort;
a `yield break` és a `finally` blokkok lefutnak, a lokális változók
megszűnnek. Nincs mit kézzel nullázni: az `ICommandCancellationHandler` és a
`ViewerCommands.CancelCommand` `switch`-e **törölhető**.

*Miért korutin és nem `async/await`:* a korutin determinisztikus, egy szálon
fut, nem kell ütemező, és nem nyit újrabelépési rést a render-ciklusba. Az
`await` ugyanezt adná, de szinkronizációs kontextussal és nehezebben
követhető megszakítással.

### 2.3 SysVarTable – az állapot egy helyen

Minden megfigyelhető vagy állítható állapot rendszerváltozó: név, típus,
olvasó, író, hatókör, írásvédettség. A parancs nem mezőt ír, hanem változót;
a `ViewerStatusSnapshot` és a menü pipái a változókból származnak, nem külön
leképezésből.

| Változó | Típus | Kiváltja |
| --- | --- | --- |
| `PDSIZE` | real | `CameraInputController._pointSize` |
| `COLORSOURCE` | enum | `PointCloudDataset._colorSource` |
| `PERSPECTIVE` | bool | `OrbitCamera.IsPerspective` |
| `VPORTLAYOUT`, `VIEWNAME` | enum, string | `ViewerController` mezők |
| `SELMODE`, `SELTOOL` | enum | `SelectionController` |
| `CLABEL`, `CINSTANCE` | string, int | aktív címke és példány |
| `PTMAX`, `LODBIAS` | int, real | `PointLodPlanner`, `PointRenderLimits` |
| `RENDERBACKEND` | enum | `CLOUDSCOPE_RENDER_BACKEND` környezeti változó |
| `LOADEDPOINTS`, `VISIBLEPOINTS`, `FPS` | int, int, real | írásvédett, `STATUS` forrása |

`SETVAR` és `GETVAR` átlátszó parancsok; `SETVAR ?` mintával listáz.
A `POINTSIZE`, `COLORBY`, `PROJECTION` megmarad kényelmi parancsnak, de
változót ír – így egy forrása van az igazságnak.

### 2.4 Transaction – minden parancs atomi

Az életciklus-horgok már megvannak, csak nincs mögöttük tranzakció:

- `CommandStarted` → `UndoManager.BeginMark(parancsnév)`
- `CommandEnded` → `Commit()`
- `CommandCancelled`, `CommandFailed` → `Rollback()`
- `CommandFlags.NoUndoMarker` → nem nyit markert (lekérdező parancsok)

A visszafordítható rekord egységes: címkeváltozás, gizmó-geometria,
rétegállapot, változóírás. Erre épül az AutoCAD-szintű `UNDO` (`<szám>`,
`Mark`, `Back`, `Auto`) és a `REDO`.

### 2.5 CommandTable – egyetlen névtér, egyetlen igazságforrás

**Megvalósított állapot (2026-09-03):** a korábbi több-executoros
`CommandDispatcher` és a külön `HostCommands` megszűnt. Minden shell és renderer ugyanazt a
`ViewerCommandDispatcher`-t delegálja; az egyetlen runtime-regisztrációban a név- és
aliasütközés azonnali hiba. A parancs deklarálja a hatókörét
(`CommandScope.Application` / `Document` / `Viewer`), és ezt az indítás előtt a közös
`CommandScopePolicy` érvényesíti.

A `CommandMethodAttribute` kiegészül azzal, ami ma hiányzik és amiért a súgó
elavul:

```csharp
[CommandMethod("ZOOM", "Z",
    Group  = CommandGroup.View,
    Scope  = CommandScope.Viewer,
    Flags  = CommandFlags.Transparent | CommandFlags.NoUndoMarker,
    Syntax = "ZOOM [All/Center/Extents/Object/Window/<scale>]",
    Summary = "Nagyítás, kicsinyítés, ablakos zoom.")]
```

Ebből az **egy** deklarációból származik a `HELP`, a `HELP <parancs>`, az
automatikus kiegészítés, a menü tooltipje és a lefedettségi jelentés.

---

## 3. A parancskészlet

A modellből következő teljes katalógus. **M** = ma is van, változatlanul;
**B** = ma is van, bővül; **Ú** = új.

### Fájl és adat
| Parancs | Áll. | Megjegyzés |
| --- | --- | --- |
| `OPEN` | B | `GetFileNameForOpen` – a héj fájlpárbeszéde is prompt lesz |
| `OPENSTORE`, `ADDSTORE` | M | |
| `INDEX` | B | `Chunk` / `Grid` / `MinPoints` / `Scratch` / `Source` kulcsszavak |
| `LAYER` | B | `List` / `ON` / `OFf` / `Close` mellé `Rename`, `Color` |
| `STOREINFO` | Ú | store fejléce, cellaszám, forrásoszlop megléte |
| `QUIT` (`EXIT`) | Ú | mentetlen címkénél megerősítő prompt |
| `SCRIPT` | Ú | parancsfájl; hibánál megáll és jelenti a sort |

### Szerkesztés
| Parancs | Áll. | Megjegyzés |
| --- | --- | --- |
| `SELECT` | M | fázismezők nélkül |
| `CONFIRM`, `CANCEL`, `FIT`, `FITGROUND` | M | |
| `MOVE` | Ú | `GetPoint` × 2 vagy elmozdulásvektor; `X`/`Y`/`Z` tengelykényszer |
| `ROTATE` | Ú | `GetAngle`, `Reference` opció |
| `SCALE` | Ú | `GetDouble`, `Reference` opció |
| `UNDO` | B | `<szám>` / `Mark` / `Back` / `Auto` |
| `REDO` | Ú | |

### Címkézés
| Parancs | Áll. | Megjegyzés |
| --- | --- | --- |
| `LABEL`, `INSTANCE`, `LABELS` | M | |
| `LABELDEF` | B | `[Color r,g,b]`, `Delete <név>`, `List` |
| `UNLABEL` (`ERASE`) | Ú | `LabelManager.RemoveLabels`, undo-val |
| `LABELSTAT` | Ú | címkénkénti pont- és példányszám |
| `SAVELABELS`, `LOADLABELS` | B | opcionális fájlút; `SAVELABELS LAS [cél]` |
| `CLEARLABELS` | M | |

### Nézet
| Parancs | Áll. | Megjegyzés |
| --- | --- | --- |
| `ZOOM` | B | a `Window` végre valóban interaktív |
| `VIEW` | B | irányok mellé `Save` / `Restore` / `List` / `Delete` |
| `PAN` | Ú | `<dx,dy>` vagy alap- és célpont |
| `ORBIT` | Ú | `<azimut,elevatio>`, `Reset` |
| `PIVOT` | Ú | `Point` / `Screen` / `Extents` |
| `PROJECTION`, `RESET` | M | |
| `VPORTS` | B | `4` elrendezés és a már létező `Previous` enum-tag bekötése |
| `POINTSIZE`, `COLORBY`, `FILTER` | M | változót írnak |

### Lekérdezés és beállítás
| Parancs | Áll. | Megjegyzés |
| --- | --- | --- |
| `STATUS` | B | a Core-ba kerül, mindkét héjban működik |
| `ATTRIBUTES` | M | |
| `SETVAR`, `GETVAR` | Ú | átlátszó |
| `GRAPHICSCONFIG` | Ú | renderelő háttér parancsból, nem környezeti változóból |
| `POINTCLOUDCONFIG` | Ú | LOD-keret, rezidencia – `PTMAX`, `LODBIAS` |
| `TIME` | Ú | `FrameTimingDiagnostics` |
| `HELP` | B | `HELP <parancs>`: aliasok, flagek, kulcsszavak, szintaxis |
| `HISTORY` | B | `Copy` / `Clear` – az ImGui menüpontok parancsra képezve |

**Elvi határ:** a folyamatos gesztus nem parancs. Az orbit-húzás, a görgős
zoom és a W/A/S/D repülés bemeneti esemény marad. A `PAN` és az `ORBIT` a
*diszkrét, számmal megadható* változatot adja – ahogy az AutoCAD-ben is
együtt él a `PAN` parancs és a középgomb húzása.

---

## 4. Építési sorrend

Egy rendszer, de nem egy commit. A sorrendet a függőségek adják, és minden
lépés végén a program működik.

| # | Lépés | Miért itt | Hozam |
| --- | --- | --- | --- |
| 1 | `CommandTable` metaadatokkal, `HELP <parancs>`, `STATUS` a Core-ba | Nincs függősége, minden későbbi lépés erre hivatkozik | A súgó megszűnik elavulni |
| 2 | `Editor` + prompt API + korutin-runtime; a 31 meglévő parancs átírása | Ez a rendszer gerince; amíg nincs, minden új parancs újabb fázismezőt szül | −53 fázishivatkozás, `ICommandCancellationHandler` törölve |
| 3 | `SysVarTable`, `SETVAR`/`GETVAR`; `ViewerStatusSnapshot` és a menüpipák változókból | Az `Editor` már kész, a parancsok egy helyre írhatnak | Három leképezésből egy |
| 4 | `SCRIPT`, `QUIT` | A `SCRIPT`-től kezdve minden további lépés parancsfájlból tesztelhető | Automatizálható füstteszt |
| 5 | `UndoManager`, tranzakciók, `UNDO` opciók, `REDO` | A geometriai parancsok undo nélkül veszélyesek | Minden parancs atomi |
| 6 | Pontbevitel a viewportból, `MOVE` / `ROTATE` / `SCALE` / `PAN` / `PIVOT` / `ZOOM Window` / `VIEW Save` | Undo-ra és `GetPoint`-ra épül | A legnagyobb hiányzó felület megszűnik |
| 7 | `UNLABEL`, `LABELSTAT`, `LABELDEF` bővítés, fájlutas mentés | Az undo már megvédi őket | A címkefelület teljes |
| 8 | `GRAPHICSCONFIG`, `POINTCLOUDCONFIG`, `STOREINFO`, `TIME`, `INDEX` opciók | Kényelmi réteg, önállóan halasztható | A konfiguráció kikerül a környezeti változókból |
| 9 | Lefedettségi ellenőrzés (5. pont) | Zárja a kört | A nyilvántartás magát tartja karban |

A 2. lépés a kritikus: ez az egyetlen, ami meglévő kódot ír át, és minden más
utána olcsó. Ha csak egy lépés fér bele, ez legyen az.

---

## 5. Mi tartja karban

Négy ellenőrzés, hogy a rendszer ne csússzon szét újra:

1. **Regisztrációs validáció.** A `CommandRuntime.Register` ma az aláírást
   ellenőrzi; ezentúl megköveteli a `Summary`-t és a `Syntax`-ot is. Leírás
   nélküli parancs nem regisztrálható.
2. **Névtér-ellenőrzés indulásnál.** Az ütközés a `CommandTable`
   felépítésekor derül ki, nem az első ütköző parancs kiadásakor.
3. **Menü-lefedettség.** Teszt, amely a `CommandMenu` minden parancsszövegét
   feloldja a táblában, és a kulcsszavait a prompt kulcsszavai közt.
4. **Lefedettségi jelentés.** A `ViewerController` publikus, állapotot
   módosító metódusait összeveti azzal, hogy hívja-e őket parancs. Ami nem, azt
   kilistázza – ez a nyilvántartás automatikus utódja.

---

## 6. Kockázatok

| Kockázat | Kezelés |
| --- | --- |
| A 2. lépés egyszerre írja át mind a 31 parancsot | A runtime átmenetileg mindkét törzsformát futtatja, a parancsok egyesével migrálnak; a régi forma a migráció végén tűnik el |
| A korutin élettartama túlnyúlik egy felhőcserén | Az `Editor` a dokumentum eseményére megszakítja az aktív parancsot – ugyanaz az út, mint az `Esc` |
| A pontbevitel ütközik a kijelölő gesztussal | Egyértelmű elsőbbség: ha pontkérés aktív, a kattintás válasz; a kijelölő eszköz csak egyébként kapja meg |
| A rendszerváltozók megkettőzik az állapotot | A változó nem tárol, hanem a meglévő mezőre mutató olvasó/író pár – nincs második másolat |


---

## Megvalósítási eltérések

Amit a kód másképp csinál, mint ahogy a terv leírta, és miért:

**1. Az átlátszó parancs aposztróffal indul.** A terv nem mondta ki, hogyan lehet egy
átlátszó parancsot elindítani egy másik parancs *érték*-promptjánál. Kiderült, hogy sehogy:
ott minden begépelt szöveg adat, különben egy `PEEK` nevű pont nevét nem lehetne beírni. Az
AutoCAD válasza az aposztróf-előtag (`'ZOOM`), és a rendszer most ezt követi. Keresztül-
esett teszt derítette ki, nem tervezés.

**2. A `HOSTHELP` megszűnt, a `STATUS` a viewerhez került.** A `HOSTSTATUS` a későbbi
egységesítésben szintén megszűnt: a shell nem birtokol saját parancsot. A `STATUS`, `HELP` és
`COMMANDS` egyetlen viewer-parancstáblából szolgáltatja az állapotot, súgót és teljes leltárt.

**3. A `PTMAX` él, a `PTRESIDENT` nem teljesen.** A képkockánkénti pontkeret futásidőben
átállítható. A rezidens keret viszont csak a változás *után* megnyitott felhőkre hat: a már
feltöltött GPU-pufferek újraépítése a renderelő út átalakítása lenne, nem parancsrendszer-
kérdés. A változó leírása kimondja.

**4. A renderelő háttér továbbra is indításkor dől el.** A `GRAPHICSCONFIG` megmondja, mi fut
és hogyan lehet váltani, de nem vált futásidőben — a háttér a natív ablakhoz kötődik. Ez
őszintébb, mint egy parancs, ami csendben nem csinál semmit.

**5. A lefedettségi jelentés IL-t olvas.** A terv „összeveti a metódusokat azzal, hogy hívja-e
őket parancs" pontját nem lehetett névegyeztetéssel megoldani anélkül, hogy megint kézzel
karbantartott lista legyen belőle. A `CommandCoverage` a lefordított kódot járja be — a
korutinok fordító által generált állapotgépébe és a parancsosztályok saját segédmetódusaiba is
belépve. Ez rögtön talált egy valódi holt API-t (`SetViewportLayout` egyparaméteres túlterhelése),
ami törölve lett.
