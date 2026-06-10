# STATE — Verdad de estado de EstructurasRD

> Región AUTO regenerada por `./estado-real.sh`. **No editar a mano entre los marcadores.**
> Este archivo es la **fuente única de verdad**; si un doc lo contradice, manda este.

<!-- AUTO:START -->
Estampado: 2026-06-10 05:25 · rama engine/f1-verdad-visual · commit d62adae · sin commit: 2 archivos

## Build & Tests (en vivo)
- .NET (LosasPlus.Linux.sln): build 0 err / 0 warn · tests 1111 passed / 0 failed / 0 skipped
- Python (motor-fea/.venv): 208 passed
- ⚠️ pytest SOLO corre en motor-fea/.venv (python3 del sistema no tiene pytest)
<!-- AUTO:END -->

## Subsistemas

- **motor-fea (Python FEM):** solver de pórticos 3D, placa ACM, modal, chequeos ACI 318-19, visor WebXR, capa IA. Verde.
- **.NET/Avalonia UI (`src`, `src.Core`, `src.UI.Shared`):** app de escritorio; Pieper-Martens (Perdomo) + diseñadores ACI 318-19 (vigas/columnas/zapatas). Verde. Losas.exe = respaldo legacy aditivo.
- **IA/CAD/WebXR:** Qwen visión (DXF/foto→elementos), visor three.js, Memoria OpenXML. Funcional.

## Issues conocidos diferidos (NO arreglados en F0)

- ~~4 gráficas en blanco~~ — **resuelto en F1 (2026-06-10)**: los 4 diagramas (`VigaPng`, `SeccionPng`, `InteraccionPng`, `SeccionColumnaPng`) se renderizan a PNG vía `DiagramaPng` y se muestran en `<Image>`; 0 `oxy:PlotView` en `VigaEditorView`/`ColumnasEditorView`. Tests de píxeles verdes. Bonus: fix de z-order (concreto/estribo a `BelowSeries` — el gris semitransparente lavaba las barras de refuerzo).
- **Pieper-Martens nativo mapea 1/21 subtipos** (`src.Core/Calculo/PieperMartens/TablaPieperMartens.cs:70`, lanza `NotSupportedException`). → **F3**.
- **Solver motor-fea solo cargas nodales** (sin peso propio/distribuidas; viga a gravedad da momento ~0). → **F4**.
- **Descenso de columnas equitativo** (no por área tributaria) (`src.Core/Transmision/DescensoColumnas.cs:13`). → **F4**.
- **`qwen.config.json` no se carga en runtime** (defaults hardcodeados en `MainViewModel`). → **F6**.

## Docs stale — esta es la fuente de verdad

Estos documentos pueden mentir; este `STATE.md` manda (ya tienen puntero al tope):
- `ESTADO_ACTUAL.md` (mezcla snapshots WPF / 501 / 753 tests).
- `motor-fea/README.md` (dice 108 tests; real arriba).
- `docs/qwen-setup.md §5` (dice `QwenAnalizador` pendiente; ya implementado y cableado).
- Planes WebXR en `motor-fea/docs/superpowers/plans/` (checkboxes en `[ ]` pero el código está hecho y testeado — son scripts de ejecución, no un tablero de estado).
