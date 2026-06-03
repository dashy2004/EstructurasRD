# Sesión 2026-06-03 — Motor headless D → E → F (carga última, vigas, columnas)

> Rama: `engine/columnas-diseno` · **900/900 tests verde** · sin push ·
> `avalonia-linux`/`main` intactos · 18 commits (`b9a91d1..HEAD`).
> Lane: **motor/cálculo = Claude** (este trabajo) · **UI/pixeles = Antigravity** (en paralelo).

## Resumen ejecutivo
Se completaron, en TDD estricto y de forma aditiva, los tres arcos pedidos —**D**
(carga última directa), **E** (vigas materializadas) y **F** (columnas: carga
transmitida + esbeltez + magnificación)— más refinamientos y un fix de UI. Todo
headless, verificado con `dotnet build src.Core` y la suite xUnit. ⛔ `Losas.exe`
se mantiene como respaldo (nada se eliminó).

## D — Carga última directa  ✅
**Hallazgo:** el pipeline LRFD ya existía en `CalculoEngine`
(`ComputeQmamp→Qmap→Qd→Ql→Qu`) y `Losa.Carga` ya ES Wu (lo consume la bajada). La
brecha real era **puentear la geometría de los muros dibujados** (antes: metros
lineales tecleados).
- `src.Core/Transmision/CargaUltimaCalculator.cs` (puro):
  - `PesoMamposteria(muros/sistema)` → ton (Σ 1.8·L·e·h).
  - `Calcular(sistema, cargas, hEq, area)` → `CargaUltimaResultado{Qmamp,Qmap,Qd,Ql,Qu}`.
  - `AplicarCargaUltima(sistema | edificio, cargas)` → escribe Wu en cada `Losa.Carga`.
- `src.Core/Services/CargaUltimaExporter.cs`: `ToCsv/ExportCsv/ExportXlsx`, por
  **sistema y por edificio** — para **validar Wu vs Losas.exe** fila a fila.

## E — Vigas: diagramas + secciones  ✅ (núcleo headless)
**Root cause del «no se ven diagramas»:** NO era la navegación (los `PlotView` y la
nav estaban bien); era que **`_nivel.Vigas` estaba vacío** (nada materializaba vigas
desde las losas).
- `src.Core/Vigas/GeneradorVigas.cs` (puro):
  - `VigaSimplementeApoyada(longitud, w, caso)`.
  - `VigasDeLosa(losa, caso)` → 4 vigas cargadas (reparto por áreas tributarias,
    ton/m→kN/m).
  - `MaterializarVigas(nivel | edificio)` → puebla `Nivel.Vigas` (idempotente,
    preserva vigas manuales).
- **Pendiente (Antigravity):** dibujo de la sección b×h + armado; botón «Generar vigas».

## F — Columnas: carga transmitida + características de diseño  ✅ (headless)
- **F-2 — Pu del descenso:** `DescensoColumnas.PuDemandaKN(carga)` y
  `PuDemandaKN(cargaEnBaseTon, numColumnas)` (puro, sin mutar zapatas) +
  `ColumnasEditorViewModel.TomarPuDelDescenso()`/`Command` → cierra
  losa→bajada→columna (el Pu deja de teclearse).
- **F-3 — Esbeltez (ACI 318-19 §6.2.5):** `RadioGiroRectangular`,
  `RelacionEsbeltez (kLu/r)`, `LimiteEsbeltezArriostrado (34−12·M1/M2 ≤ 40)`,
  `EsEsbeltaArriostrada`.
- **F-3b — Magnificación de momento (§6.6.4):** `ModuloElasticidadConcreto (4700√fc)`,
  `InerciaBrutaRectangular (b·h³/12)`, `RigidezEI (0.4·Ec·Ig)`,
  `CargaCriticaPandeo (π²EI/(kLu)²)`, `FactorMagnificacion (δ=Cm/(1−Pu/0.75Pc)≥1)`.
- **Pendiente (Antigravity):** botón «Tomar Pu del descenso»; mostrar aceros/esbeltez/δ.

## Otros
- **Fix:** `Planta2DEditorView.OnEliminarClick` ahora **elimina muros** (faltaba la
  rama `Muro`; ya se seleccionaban/arrastraban/dibujaban).
- **Concurrencia:** se manejó un choque real (Antigravity rompió/arregló el build de
  `MainViewModel` en vivo). Regla aplicada: re-verificar el estado justo antes de
  actuar y **stagear sólo mis archivos** en cada commit; su WIP UI nunca se tocó.

## Restricciones honradas
- Commits **sin push**; nunca se tocó `avalonia-linux` ni `main`.
- En cada commit se stageó **sólo `src.Core` + tests + mis VMs/Views**, dejando el WIP
  de Antigravity (`MainWindow.axaml`, `MainViewModel.cs`, `VigaEditorViewModel.cs`,
  `VigaEditorView.axaml`) intacto.
- ⛔ `Losas.exe` intacto como respaldo; el cálculo directo es una vía **aditiva**.

## Pendientes (para próximas vueltas del loop)
1. **Verificaciones que pueda correr** (suite completa, `dotnet build src.Core`,
   golden de exporters) y refinamientos pequeños sin churn.
2. **Vigas continuas** (multi-tramo): requiere detección de topología (qué losas
   comparten eje) — feature grande, decidir alcance.
3. **UI → Antigravity:** botones (Wu, generar vigas, Tomar Pu), sección de viga,
   mostrar aceros/esbeltez/δ.
4. **Usuario:** validar Wu directo vs `Losas.exe` (CSV/XLSX listos).
