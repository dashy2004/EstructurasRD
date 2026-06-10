# STATE — Verdad de estado de EstructurasRD

> Región AUTO regenerada por `./estado-real.sh`. **No editar a mano entre los marcadores.**
> Este archivo es la **fuente única de verdad**; si un doc lo contradice, manda este.

<!-- AUTO:START -->
Estampado: 2026-06-10 19:43 · rama engine/ui1-un-solo-lienzo · commit de72ffb · sin commit: 3 archivos

## Build & Tests (en vivo)
- .NET (LosasPlus.Linux.sln): build 0 err / 0 warn · tests 1208 passed / 0 failed / 0 skipped
- Python (motor-fea/.venv): 208 passed
- ⚠️ pytest SOLO corre en motor-fea/.venv (python3 del sistema no tiene pytest)
<!-- AUTO:END -->

## Subsistemas

- **motor-fea (Python FEM):** solver de pórticos 3D, placa ACM, modal, chequeos ACI 318-19, visor WebXR, capa IA. Verde.
- **.NET/Avalonia UI (`src`, `src.Core`, `src.UI.Shared`):** app de escritorio; Pieper-Martens (Perdomo) + diseñadores ACI 318-19 (vigas/columnas/zapatas). Verde. Losas.exe = respaldo legacy aditivo.
- **IA/CAD/WebXR:** Qwen visión (DXF/foto→elementos), visor three.js, Memoria OpenXML. Funcional.

## Issues conocidos diferidos (NO arreglados en F0)

- ~~4 gráficas en blanco~~ — **resuelto en F1 (2026-06-10)**: los 4 diagramas (`VigaPng`, `SeccionPng`, `InteraccionPng`, `SeccionColumnaPng`) se renderizan a PNG vía `DiagramaPng` y se muestran en `<Image>`; 0 `oxy:PlotView` en `VigaEditorView`/`ColumnasEditorView`. Tests de píxeles verdes. Bonus: fix de z-order (concreto/estribo a `BelowSeries` — el gris semitransparente lavaba las barras de refuerzo).
- ~~Pieper-Martens nativo mapea 1/21 subtipos~~ — **resuelto en F3 (2026-06-10)**: mapeo completo `CodigoASubtipo` 21/21 (biyección con `TablasPerdomo.json`), captura por-losa (una losa sin mapeo no aborta el sistema) y mensaje veraz en `TipoLosaValidoRule`.
- **Solver motor-fea solo cargas nodales** (sin peso propio/distribuidas; viga a gravedad da momento ~0). → **F4**.
- **Reparto viga→columna 50/50** (no por reacciones reales; `src.Core/Transmision/RepartoGeometrico.cs:176`, comentario `:166`). → **F4**. (La UI ya usa descenso geométrico por área tributaria con fallback equitativo — F3.)
- **Mapeo Pieper-Martens x3/x4 (12 códigos de borde libre) con confianza media**: pendiente validación de fixtures contra `Losas.exe` (usuario, Windows) — ver spec F3 §3.3; corrección = 1 línea de `CodigoASubtipo`.
- **`qwen.config.json` no se carga en runtime** (defaults hardcodeados en `MainViewModel`). → **F6**.
- **F2 parcial (2026-06-10)**: paridad batch↔interactivo (losa DXF anclada + Y invertida), descartes con aviso, ambientes en L subdivididos y bbox real de arcos — **hecho**. Pendiente F2b: heurística forma→columna en capa ambigua (`DxfEstructuraMapper.cs`) y columnas en el path de visión (`QwenAnalizador.cs:94-120`).

## Docs stale — esta es la fuente de verdad

Estos documentos pueden mentir; este `STATE.md` manda (ya tienen puntero al tope):
- `ESTADO_ACTUAL.md` (mezcla snapshots WPF / 501 / 753 tests).
- `motor-fea/README.md` (dice 108 tests; real arriba).
- `docs/qwen-setup.md §5` (dice `QwenAnalizador` pendiente; ya implementado y cableado).
- Planes WebXR en `motor-fea/docs/superpowers/plans/` (checkboxes en `[ ]` pero el código está hecho y testeado — son scripts de ejecución, no un tablero de estado).
