# Hand-off → Antigravity — UI de Carga Última (D) y Vigas (E)

> El motor (Claude) dejó **headless y verde** (878/878) el cálculo de D y el núcleo
> de E. Faltan los **pixeles**: botones que disparen el cálculo y el dibujo de la
> sección de viga. Rama: `engine/columnas-diseno`.

---

## Tarea D-UI — Botón «Calcular Wu desde la geometría»

**Qué hace headless ya disponible:**
`src.Core/Transmision/CargaUltimaCalculator.cs`
- `AplicarCargaUltima(Sistema sistema, CargasGlobales cargas)` → calcula la carga
  última directa de cada losa (peso propio + mampostería de los **muros dibujados** +
  carga viva por uso, LRFD 1.2D+1.6L) y la **escribe en `Losa.Carga`** (= Wu, que la
  bajada/zapatas/columnas ya consumen). Devuelve `IReadOnlyList<CargaUltimaResultado>`
  con el desglose `{Qmamp, Qmap, Qd, Ql, Qu}` por losa.

**UI a construir** (patrón = el cutover del motor, menú Engine → `CalcularConMotorAsync`):
- Un comando `CalcularCargaUltimaCommand` en `MainViewModel` que recorra los sistemas
  del edificio activo y llame `AplicarCargaUltima(sistema, _cargasGlobales)` por sistema.
  *(Si necesitás que el comando lo exponga Claude en el VM, pedilo — es lane de motor.)*
- Un ítem de menú **«Calcular carga última (Wu) desde geometría»** que lo dispare.
- **Aditivo:** no toca el flujo de Losas.exe ni el cutover FEM; es otra vía explícita.
- Opcional: mostrar el desglose `CargaUltimaResultado` (Qmamp/Qmap/Qd/Ql/Qu) en un panel.

---

## Tarea E-UI — Vigas: botón «Generar» + dibujo de sección

**Causa raíz del «no se ven diagramas»:** NO era la navegación. `VigaEditorView` ya
tiene los 3 `<oxy:PlotView>` bien bindeados; el problema es que **`_nivel.Vigas`
estaba vacío** (nada materializaba vigas desde las losas). Eso ya se resolvió headless.

**Qué hace headless ya disponible:** `src.Core/Vigas/GeneradorVigas.cs`
- `MaterializarVigas(Nivel nivel, string caso = "D")` → genera las vigas de apoyo de
  todas las losas del nivel (con su carga tributaria) y las agrega a `Nivel.Vigas`.
  Idempotente (regenerar no duplica; preserva las vigas manuales). Tras llamarlo, el
  editor de Vigas mostrará diagramas reales.

**UI a construir:**
1. **Botón «Generar vigas del nivel»** en `VigaEditorView` (o menú) que llame
   `GeneradorVigas.MaterializarVigas(nivelActivo)` y refresque la lista. El VM
   `VigaEditorViewModel` ya selecciona `VigaActiva = _nivel.Vigas.FirstOrDefault()` y
   recalcula los diagramas solo.
2. **Dibujo de la sección transversal** (esto sí falta de verdad): un control que
   pinte el rectángulo **b×h** del `TramoViga` seleccionado (`Base`, `Peralte` en m) con
   el armado (barras long. + estribo). Espejá el patrón de la sección de columna que ya
   existe en la pestaña Columnas (P-M + sección). Ubicalo prominente junto a los diagramas.
3. **Más énfasis a los diagramas**: el usuario quiere que M/V/δ se vean grandes y claros.

**Archivos:** `src/Views/Vigas/VigaEditorView.axaml` (+ `.axaml.cs`),
`src/ViewModels/Vigas/VigaEditorViewModel.cs` (sólo si hace falta un comando/propiedad).

---

## Tarea F-2-UI — Botón «Tomar Pu del descenso» (editor de Columnas)

**Headless ya disponible y testeado (888/888):** `ColumnasEditorViewModel` expone
`TomarPuDelDescensoCommand` (y el método `TomarPuDelDescenso()`). Calcula la carga
última en base del edificio (bajada de cargas), la reparte equitativamente entre
todas las columnas y fija `PuKN` (que recalcula la demanda P-M del diagrama).

**UI a construir:** un botón en `src/Views/ColumnasEditorView.axaml` junto al campo
`PuKN`, con `Command="{Binding TomarPuDelDescensoCommand}"` y rótulo p. ej.
«Tomar Pu del descenso». Sin más lógica — el comando ya hace todo. (Este archivo NO
está en tu WIP actual, podés tomarlo.)

**Esbeltez/δ (F-4, headless listo y testeado):** el VM ya expone los inputs
`LuMm` y `FactorK`, y la propiedad `EsbeltezActual`
(`ColumnaDisenador.ResumenEsbeltezColumna` con `RMm`, `KLuSobreR`, `Limite`,
`EsEsbelta`, `EINmm2`, `PcN`, `Delta`), recalculada junto al diseño P-M. **UI:**
dos `TextBox` (Lu en mm, k) y un panel que muestre el resumen — p. ej.
«k·Lu/r = {KLuSobreR} (límite {Limite}) → {EsEsbelta? "ESBELTA":"corta"}; δ = {Delta}».
Sólo binding; sin lógica.

## Notas de seguridad (Antigravity)
- `ControlTheme` de controles nativos: siempre `BasedOn="{StaticResource {x:Type <Control>}}"`.
- Mantené la suite verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (878/878 hoy).
- `src.Core` es lane de Claude — si necesitás un cambio de firma o un comando nuevo en
  un VM de motor, pedilo en vez de tocarlo.
- ⛔ No eliminar `Losas.exe`: todo esto es **aditivo**.
