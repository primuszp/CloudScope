# Mérések és szelvények

A `MEASURE Distance` két pontból névvel tárolja a térbeli és vízszintes távolságot, az előjeles magasságkülönbséget, a százalékos esést és a hajlásszöget. Függőleges mérésnél az esés százalékban nem értelmezhető. A `MEASURE Area` egyszerű zárt poligon vízszintes vetületi területét és alaprajzi kerületét számolja; a magasságok nem befolyásolják a területet. Az önmetsző, önmagát érintő, elfajult vagy nem véges geometria hibás. A számok a felmérés koordinátaegységét követik; az alkalmazás nem feltételez automatikusan métert.

```text
MEASURE Distance "D1" 0,0,0 3,4,12
MEASURE Area "A1" 0,0,0 4,0,0 4,3,0 0,3,0 Close
MEASURE List
MEASURE Remove "A1"
MEASURE Export Csv "/path/measurements.csv"
MEASURE Export Dxf "/path/measurements.dxf"
```

A mérési vonal a nézetben zöldeskék színnel látszik. A mérés egérrel és tárgyraszterrel is megadható, megszakítása nem ment félkész geometriát. A hozzáadás és törlés egy-egy undo-tranzakció. Minden mérés a teljes projekt és a helyreállítás része.

Az `XSECTION` új szelvény létrehozásakor megőrzi a korábbiakat. A `Rename` az aktív szelvényt nevezi át; az `Activate` név alapján vált; a `List` minden szelvényt felsorol és csillaggal jelzi az aktívat. A `CLear` az aktívat eltávolítja, majd a megmaradó legutóbbit aktiválja. A szélesség, irány és fogópontok mindig az aktív szelvényhez tartoznak. A gyűjtemény és az aktív azonosító undo/redo során együtt áll vissza.

```text
XSECTION Rename "Longitudinal"
XSECTION Activate "Longitudinal"
XSECTION Export Csv "/path/section.csv"
XSECTION Export Dxf "/path/section.dxf"
```

A szelvényexport a betöltött resident forrás vagy a látható streaming rétegek teljes szelvénysávját vizsgálja, a rajzolási pontkerettől, ritkítástól és resident szűrőtől függetlenül. A resident import pontkorlátján túli, be sem töltött pontokat nem exportálja. A művelet háttérben fut, CANCEL/Escape megszakítja, és atomikusan cseréli a célfájlt. CSV-ben forrásindex, alapvonal kezdőpontjától mért szelvényezés, abszolút magasság, előjeles oldaltávolság és világkoordináták szerepelnek. DXF-ben a szelvényezés az X, az abszolút magasság az Y tengely; a mérési geometriák DXF-je az eredeti világkoordinátákban marad.

A CSV tizedespontot és idézett szövegmezőket használ. Az ASCII DXF R12 a [POLYLINE](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm) és [VERTEX](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm) szerződéseit követi. A DXF-felirat nem ASCII karakterei aláhúzásként jelennek meg; a projekt és a CSV teljes Unicode neveket tárol.
