# F0 — Verdad de estado (design spec)

- **Fecha:** 2026-06-09
- **Estado:** Aprobado (diseño) → pendiente de plan de implementación
- **Rama de trabajo propuesta:** `engine/f0-verdad-de-estado` (off `avalonia-linux`, carril Claude = `src.Core` + `motor-fea` + tooling de repo)
- **Fase del roadmap:** F0 de las 9 fases de pendientes (ver brainstorm de fases)

---

## 1. Contexto y problema

El build del proyecto está **verde** en vivo, pero las superficies de estado **mienten**:

- `dotnet build LosasPlus.Linux.sln` → 0 errores; `dotnet test` → **1106 passed / 0 failed / 0 skipped**.
- `motor-fea/.venv/bin/python -m pytest -q` → **208 passed / 0 failed / 0 skipped**.
- Pero `build.log`/`test.log` (raíz, del 2026-06-03) muestran un **`Build FAILED` (CS7036 en `ColumnasEditorDisenoTests.cs`)** que ya no reproduce.
- `motor-fea/README.md` dice **108 tests** (real 208) y sugiere `python -m pytest` global (el `python3` del sistema **no** tiene pytest; solo corre en `motor-fea/.venv`).
- `ESTADO_ACTUAL.md` mezcla snapshots WPF/501/753; `qwen-setup.md §5` dice que `QwenAnalizador` está pendiente (ya implementado); los planes WebXR tienen todos los checkboxes en `[ ]` aunque el código está hecho.

Auditar "lo pendiente" leyendo esos docs produce un backlog **falso e inflado**. Además hay **trabajo sin commit en riesgo de pérdida** en el working tree.

**Decisión de causa raíz:** la verdad debe ser **autogenerada por máquina** y **superseder** (no editar uno por uno) a los docs stale.

## 2. Decisiones tomadas (con el usuario)

1. **Alcance = solo F0 (higiene / verdad de estado).** Los bugs de F1 — incluidas las 4 gráficas en blanco — quedan **fuera**; F0 solo los **documenta** como issue conocido diferido. Sin cambios de comportamiento.
2. **Estrategia de docs = superseder con `STATE.md`.** Se crea un único `STATE.md` autoestampado como fuente de verdad y se ponen **punteros** al tope de los docs stale, **sin** editar su cuerpo.

## 3. Objetivo y no-objetivos

**Objetivo:** tras F0, el árbol está commiteado y toda superficie de estado concuerda con la verdad de máquina (.NET 1106 ✓ / Python 208 ✓ / 0 errores de build), con un mecanismo que **no se vuelve a desincronizar**.

**No-objetivos (FUERA de F0):**
- Arreglar las 4 gráficas en blanco (`oxy:PlotView`): `ColumnasEditorView.axaml:204,209` (`ModeloSeccionColumna`, `ModeloInteraccion`) y `VigaEditorView.axaml:257,287` (`ModeloSeccion`, `ModeloViga`). → spec **F1**.
- Unificar lienzos (`CadCanvasHost` ↔ `PlantaCanvas`). → spec aparte.
- Cualquier cambio de comportamiento de UI o de motor.
- Hooks de git / CI gating (YAGNI por ahora; el script deja la puerta abierta con una bandera `--check` futura).

## 4. Diseño detallado

### 4.1 `estado-real.sh` (raíz del repo)

Script bash idempotente, sin argumentos (con bandera futura opcional `--check`). Responsabilidades:

1. **Build + test .NET:** corre `dotnet build LosasPlus.Linux.sln` y `dotnet test LosasPlus.Linux.sln`; captura `0 err / N warn` y `passed/failed/skipped`; vuelca salida a `build.log` y `test.log` (regenerados — dejan de mentir).
2. **Test Python:** corre `motor-fea/.venv/bin/python -m pytest -q`; captura el conteo. Si el `.venv` no existe, lo reporta como bloqueo explícito (no asume pytest global).
3. **Estampa `STATE.md`:** reescribe **solo** la región entre `<!-- AUTO:START -->` y `<!-- AUTO:END -->` con: fecha (`date`), rama (`git rev-parse --abbrev-ref HEAD`), commit (`git rev-parse --short HEAD`), los 3 conteos, warnings, y nº de archivos sin commit (`git status --porcelain | wc -l`).
4. **Soft-check de docs:** grepea los `.md` rastreados (`git ls-files '*.md'`) por patrones tipo `[0-9]+ *(tests|passed|pruebas)` y lista los que difieren del conteo vivo. Por defecto es **warning** (exit 0). `--check` haría exit ≠ 0 (para un CI futuro).
5. **Errores:** si una suite falla, estampa los números reales (en rojo) y **sale ≠ 0** — la verdad incluye lo roto; no se oculta.

**Contrato:** entradas = el repo en su estado actual; salidas = `build.log`, `test.log`, región AUTO de `STATE.md` actualizada, código de salida (0 = todo verde y docs consistentes; ≠0 = suite roja o, con `--check`, doc divergente). Determinista salvo fecha/SHA.

### 4.2 `STATE.md` (raíz del repo) — fuente única, una página

Dos regiones:

- **Región AUTO** (entre marcadores, regenerada por el script): stamp + tabla de build/tests en vivo + nota "pytest solo corre en `motor-fea/.venv`".
- **Región CURADA** (a mano, **preservada** entre regeneraciones, fuera de los marcadores):
  - *Resumen de subsistemas* (1-2 líneas c/u: motor-fea, .NET UI, IA/CAD/WebXR).
  - *Issues conocidos diferidos* — incluye las 4 gráficas en blanco (con archivos:línea) → F1; descenso de columnas equitativo; Pieper-Martens 1/21 → F3; etc.
  - *Docs stale* — lista con "ver STATE.md" (los que reciben puntero).

Estructura objetivo:

```markdown
# STATE — Verdad de estado (región AUTO autogenerada por estado-real.sh)
<!-- AUTO:START -->
Estampado: <fecha> · rama <rama> · commit <sha> · sin commit: <n> archivos
## Build & Tests (en vivo)
- .NET (LosasPlus.Linux.sln): build 0 err / <N> warn · tests <P> passed / <F> failed / <S> skipped
- Python (motor-fea/.venv): <P> passed / <F> failed / <S> skipped
- ⚠️ pytest SOLO corre en motor-fea/.venv (python3 del sistema no tiene pytest)
<!-- AUTO:END -->

## Subsistemas (curado)
...
## Issues conocidos diferidos (curado)
- 4 gráficas en blanco (oxy:PlotView): ColumnasEditorView 204/209, VigaEditorView 257/287 → F1
...
## Docs stale — ver este archivo (curado)
- ESTADO_ACTUAL.md, motor-fea/README.md (dice 108, real 208), qwen-setup.md §5, planes WebXR
```

### 4.3 Punteros en docs stale

Una línea al tope (sin tocar el cuerpo) en: `ESTADO_ACTUAL.md`, `motor-fea/README.md`, `docs/qwen-setup.md`, y los planes WebXR bajo `motor-fea/docs/superpowers/plans/`:

```markdown
> ⚠️ Estado real autogenerado → ver /STATE.md (este documento puede estar desactualizado)
```

### 4.4 Plan de commits (rama `engine/f0-verdad-de-estado` off `avalonia-linux`)

- **C1 — preservar lo que está en riesgo:** `src/Rendering/DiagramaPng.cs`, `tests/LosasPlus.Tests/DiagramaPngTests.cs` y los 5 modificados (`src/App.axaml`, `src/Converters.cs`, `src/LosasPlus.csproj`, `src/ViewModels/Vigas/VigaEditorViewModel.cs`, `src/Views/Vigas/VigaEditorView.axaml`). Es trabajo **ya hecho y verde**; C1 es **preservación**, no "hacer F1".
- **C2 — verdad de estado:** `STATE.md` + `estado-real.sh` + punteros en docs stale + `build.log`/`test.log` regenerados + el spec doc.
- **C3 (opcional) — warnings:** `AcerosViewModelTests.cs:102` (CS8602 null-check), `EscenaEdificioColumnasTests.cs:22` y `EscenaEdificioVigasTests.cs:56` (xUnit2013 → `Assert.Empty`/`Assert.Single`). Behavior-neutral.

## 5. Testing / verificación de la propia F0

- Correr `estado-real.sh` y confirmar que `STATE.md` (región AUTO) coincide con una corrida manual de ambas suites.
- Confirmar que `dotnet test` sigue 1106/0/0 y pytest 208/0/0 tras los commits (C1/C3 no rompen nada).
- Confirmar que existen los 3 (o 2) commits en la rama, los punteros añadidos, y los logs regenerados con fecha de hoy.
- Re-correr `estado-real.sh` dos veces seguidas: la segunda corrida no debe producir diff fuera de fecha/SHA (idempotencia de la región AUTO; región CURADA intacta).

## 6. Criterios de aceptación

1. Trabajo en riesgo commiteado (working tree limpio salvo lo intencional).
2. `STATE.md` existe, una página, con regiones AUTO + CURADA; refleja 1106/208/0-err.
3. `estado-real.sh` existe, es idempotente, regenera logs, estampa `STATE.md`, y hace el soft-check.
4. Punteros añadidos a los docs stale listados; **sin** editar su cuerpo.
5. `build.log`/`test.log` regenerados (ya no muestran el falso `Build FAILED`).
6. Las 4 gráficas en blanco quedan **documentadas** en `STATE.md` (no arregladas) con archivo:línea, referidas a F1.
7. Suites siguen verdes (1106 / 208).

## 7. Riesgos y mitigaciones

- **Soft-check frágil** (regex de conteos en docs) → mantenerlo como *warning*, no fallar el build; cubrir solo el patrón de "N tests/passed/pruebas".
- **Mezclar AUTO + CURADO en un archivo** → marcadores `<!-- AUTO:START/END -->`; el script solo reescribe entre ellos.
- **Scope-creep** (tentación de arreglar las gráficas en blanco al "verificar") → criterio 6 lo prohíbe explícitamente; van a F1.
- **`dotnet`/`.venv` ausentes en otra máquina** → el script detecta y reporta el bloqueo en vez de fingir verde.

## 8. Issues conocidos a documentar en STATE.md (no arreglar en F0)

- 4 gráficas en blanco `oxy:PlotView` → F1 (`ColumnasEditorView:204,209`, `VigaEditorView:257,287`).
- Pieper-Martens nativo mapea 1/21 subtipos (`TablaPieperMartens.cs:70`) → F3.
- Solver `motor-fea` solo cargas nodales (sin peso propio) → F4.
- Descenso de columnas equitativo (no por área tributaria) (`DescensoColumnas.cs:13`) → F4.
- `qwen.config.json` no se carga en runtime → F6.
