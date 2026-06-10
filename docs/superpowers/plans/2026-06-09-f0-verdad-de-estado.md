# F0 — Verdad de estado · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar el árbol commiteado y crear un mecanismo autogenerado (`STATE.md` + `estado-real.sh`) que haga que toda superficie de estado concuerde con la verdad de máquina (.NET 1106 ✓ / Python 208 ✓), sin tocar comportamiento.

**Architecture:** Un script bash (`estado-real.sh`) corre ambas suites, regenera `build.log`/`test.log`, y reescribe la región AUTO de `STATE.md` (entre marcadores). `STATE.md` es la fuente única; los docs stale reciben un puntero al tope sin editar su cuerpo. El trabajo sin commit en riesgo se preserva primero.

**Tech Stack:** Bash, .NET 8 (`dotnet build`/`test`), pytest en `motor-fea/.venv`, git.

**Spec de referencia:** `docs/superpowers/specs/2026-06-09-f0-verdad-de-estado-design.md`

---

## Estructura de archivos

- **Crear** `STATE.md` (raíz) — fuente única de verdad; región AUTO + región curada.
- **Crear** `estado-real.sh` (raíz) — regenera logs y estampa la región AUTO de `STATE.md`.
- **Modificar** (solo puntero al tope, sin tocar cuerpo): `ESTADO_ACTUAL.md`, `motor-fea/README.md`, `docs/qwen-setup.md`.
- **Regenerar** (por el script): `build.log`, `test.log`.
- **Modificar** (opcional C3, warnings): `tests/LosasPlus.Tests/AcerosViewModelTests.cs`, `tests/LosasPlus.Tests/EscenaEdificioColumnasTests.cs`, `tests/LosasPlus.Tests/EscenaEdificioVigasTests.cs`.

Restricción permanente: **nunca** borrar Losas.exe ni su import; F0 no toca eso.

---

## Task 1: Rama + baseline verde + preservar trabajo en riesgo (C1)

**Files:**
- Sin archivos nuevos; preserva los ya existentes en el working tree.

- [ ] **Step 1: Confirmar que el baseline está verde ANTES de tocar nada**

Run:
```bash
cd /home/gdc/Downloads/EstructurasRD-engine
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
( cd motor-fea && .venv/bin/python -m pytest -q 2>&1 | tail -2 )
```
Expected: `Passed!  - Failed: 0, Passed: 1106` y `208 passed`. Si NO está verde, detener y reportar (no continuar).

- [ ] **Step 2: Crear la rama de trabajo desde `avalonia-linux`**

Run:
```bash
git rev-parse --abbrev-ref HEAD   # esperado: avalonia-linux
git checkout -b engine/f0-verdad-de-estado
```
Los cambios sin commit del working tree viajan a la rama nueva (es lo deseado).

- [ ] **Step 3: Ver exactamente qué se va a preservar**

Run:
```bash
git status --porcelain
```
Expected (trabajo en riesgo ya hecho y verde):
```
 M src/App.axaml
 M src/Converters.cs
 M src/LosasPlus.csproj
 M src/ViewModels/Vigas/VigaEditorViewModel.cs
 M src/Views/Vigas/VigaEditorView.axaml
?? src/Rendering/
?? tests/LosasPlus.Tests/DiagramaPngTests.cs
```

- [ ] **Step 4: Commit C1 (preservación, no F1)**

Run:
```bash
git add src/App.axaml src/Converters.cs src/LosasPlus.csproj \
        src/ViewModels/Vigas/VigaEditorViewModel.cs \
        src/Views/Vigas/VigaEditorView.axaml \
        src/Rendering/ tests/LosasPlus.Tests/DiagramaPngTests.cs
git commit -m "feat(render): preservar DiagramaPng (OxyPlot->PNG ImageSharp) + tests

Trabajo ya integrado y verde (parte del fix de diagramas en blanco de
Esfuerzos/Deflexion). Se preserva en F0; el resto de graficas en blanco
va en F1. Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Verificar working tree limpio y suites aún verdes**

Run:
```bash
git status --porcelain    # esperado: vacío
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: sin salida en `git status`; `Passed: 1106`.

---

## Task 2: Crear `STATE.md` (skeleton con marcadores AUTO + región curada)

**Files:**
- Create: `STATE.md` (raíz del repo)

- [ ] **Step 1: Crear `STATE.md` con este contenido exacto**

```markdown
# STATE — Verdad de estado de EstructurasRD

> Región AUTO regenerada por `./estado-real.sh`. **No editar a mano entre los marcadores.**
> Este archivo es la **fuente única de verdad**; si un doc lo contradice, manda este.

<!-- AUTO:START -->
(sin estampar todavía — corré `./estado-real.sh`)
<!-- AUTO:END -->

## Subsistemas

- **motor-fea (Python FEM):** solver de pórticos 3D, placa ACM, modal, chequeos ACI 318-19, visor WebXR, capa IA. Verde.
- **.NET/Avalonia UI (`src`, `src.Core`, `src.UI.Shared`):** app de escritorio; Pieper-Martens (Perdomo) + diseñadores ACI 318-19 (vigas/columnas/zapatas). Verde. Losas.exe = respaldo legacy aditivo.
- **IA/CAD/WebXR:** Qwen visión (DXF/foto→elementos), visor three.js, Memoria OpenXML. Funcional.

## Issues conocidos diferidos (NO arreglados en F0)

- **4 gráficas en blanco** (`oxy:PlotView` no pinta series): `src/Views/ColumnasEditorView.axaml:204,209` (sección columna, interacción P-M) y `src/Views/Vigas/VigaEditorView.axaml:257,287` (sección viga, `ModeloViga`). → **F1**.
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
```

- [ ] **Step 2: Verificar que los marcadores están presentes (los necesita el script)**

Run:
```bash
grep -n "AUTO:START\|AUTO:END" STATE.md
```
Expected: dos líneas, una con `AUTO:START` y otra con `AUTO:END`.

---

## Task 3: Crear `estado-real.sh` (script completo)

**Files:**
- Create: `estado-real.sh` (raíz del repo)

- [ ] **Step 1: Crear `estado-real.sh` con este contenido exacto**

```bash
#!/usr/bin/env bash
# estado-real.sh — Regenera la "verdad de estado" del repo y estampa STATE.md.
#
# Uso:
#   ./estado-real.sh          estampa STATE.md, regenera build.log/test.log,
#                             soft-check de conteos (exit 0 salvo suite roja)
#   ./estado-real.sh --check  además exit !=0 si un doc rastreado declara un
#                             conteo de tests divergente del vivo
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

CHECK=0
[ "${1:-}" = "--check" ] && CHECK=1

SLN="LosasPlus.Linux.sln"
STATE="STATE.md"
FAIL=0

echo "==> .NET build ($SLN)"
dotnet build "$SLN" > build.log 2>&1
BUILD_RC=$?
NET_WARN=$(grep -cE "warning [A-Za-z]+[0-9]+:" build.log || true)
[ "$BUILD_RC" -ne 0 ] && FAIL=1

echo "==> .NET test ($SLN)"
dotnet test "$SLN" --no-build > test.log 2>&1
NET_PASS=$(grep -oE "Passed: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
NET_FAIL=$(grep -oE "Failed: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
NET_SKIP=$(grep -oE "Skipped: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
[ "${NET_FAIL:-1}" != "0" ] && FAIL=1

echo "==> Python pytest (motor-fea/.venv)"
if [ -x "motor-fea/.venv/bin/python" ]; then
  PY_OUT="$(cd motor-fea && .venv/bin/python -m pytest -q 2>&1)"
  PY_SUMMARY="$(printf '%s\n' "$PY_OUT" | grep -oE "[0-9]+ passed[^,]*" | tail -1)"
  [ -z "$PY_SUMMARY" ] && PY_SUMMARY="desconocido"
  PY_PASS="$(printf '%s\n' "$PY_SUMMARY" | grep -oE "[0-9]+ passed" | grep -oE "[0-9]+" || echo "?")"
  printf '%s\n' "$PY_OUT" | grep -qE "[0-9]+ failed" && FAIL=1
else
  PY_SUMMARY="BLOQUEO: motor-fea/.venv no existe (pytest no disponible)"
  PY_PASS="N/A"
  FAIL=1
fi

BUILD_TXT=$([ "$BUILD_RC" -eq 0 ] && echo "0 err" || echo "FALLO")
STAMP="Estampado: $(date '+%Y-%m-%d %H:%M') · rama $(git rev-parse --abbrev-ref HEAD) · commit $(git rev-parse --short HEAD) · sin commit: $(git status --porcelain | wc -l | tr -d ' ') archivos"

AUTO="$(cat <<EOF
<!-- AUTO:START -->
$STAMP

## Build & Tests (en vivo)
- .NET ($SLN): build $BUILD_TXT / $NET_WARN warn · tests $NET_PASS passed / $NET_FAIL failed / $NET_SKIP skipped
- Python (motor-fea/.venv): $PY_SUMMARY
- ⚠️ pytest SOLO corre en motor-fea/.venv (python3 del sistema no tiene pytest)
<!-- AUTO:END -->
EOF
)"

# Reescribe SOLO la región entre marcadores; preserva el resto (región curada).
awk -v repl="$AUTO" '
  /<!-- AUTO:START -->/ { print repl; skip=1; next }
  /<!-- AUTO:END -->/   { skip=0; next }
  !skip                 { print }
' "$STATE" > "$STATE.tmp" && mv "$STATE.tmp" "$STATE"
echo "==> STATE.md estampado."

echo "==> Soft-check de conteos en docs (.md rastreados)"
STALE=0
while IFS= read -r f; do
  [ "$f" = "$STATE" ] && continue
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    n="$(printf '%s' "$line" | grep -oE "[0-9]+ +(tests|passed|pruebas)" | grep -oE "^[0-9]+" | head -1)"
    if [ -n "$n" ] && [ "$n" != "$NET_PASS" ] && [ "$n" != "$PY_PASS" ]; then
      echo "  STALE? $f: $line"
      STALE=$((STALE + 1))
    fi
  done < <(grep -nE "[0-9]+ +(tests|passed|pruebas)" "$f" 2>/dev/null)
done < <(git ls-files '*.md')
[ "$STALE" -gt 0 ] && echo "==> $STALE línea(s) de conteo posiblemente stale."
[ "$CHECK" -eq 1 ] && [ "$STALE" -gt 0 ] && FAIL=1

[ "$FAIL" -eq 0 ] && echo "==> OK: verde y consistente." || echo "==> ATENCION: suite roja o conteo divergente (--check)."
exit $FAIL
```

- [ ] **Step 2: Hacerlo ejecutable**

Run:
```bash
chmod +x estado-real.sh
```

---

## Task 4: Ejecutar `estado-real.sh` y verificar estampado + idempotencia

**Files:** ninguno (verificación).

- [ ] **Step 1: Primera corrida**

Run:
```bash
./estado-real.sh; echo "exit=$?"
```
Expected: imprime los pasos, `==> STATE.md estampado.`, `==> OK: verde y consistente.`, `exit=0`. (Es normal que el soft-check liste líneas `STALE?` de docs stale aún sin puntero — eso es esperado y será warning.)

- [ ] **Step 2: Verificar que la región AUTO se estampó con los conteos vivos**

Run:
```bash
sed -n '/AUTO:START/,/AUTO:END/p' STATE.md
```
Expected: muestra `tests 1106 passed / 0 failed / 0 skipped` y `208 passed`.

- [ ] **Step 3: Verificar que los logs se regeneraron (ya no mienten)**

Run:
```bash
grep -c "Build FAILED\|Build succeeded" build.log; tail -2 test.log
```
Expected: `build.log` ya NO contiene `Build FAILED`; `test.log` muestra `Passed: 1106`.

- [ ] **Step 4: Verificar idempotencia (región curada intacta)**

Run:
```bash
cp STATE.md /tmp/state1.md
./estado-real.sh >/dev/null
diff <(sed '/AUTO:START/,/AUTO:END/d' /tmp/state1.md) <(sed '/AUTO:START/,/AUTO:END/d' STATE.md)
```
Expected: `diff` vacío (la región curada NO cambió entre corridas; solo cambia la región AUTO por fecha/SHA).

---

## Task 5: Punteros en docs stale

**Files:**
- Modify (solo línea al tope): `ESTADO_ACTUAL.md`, `motor-fea/README.md`, `docs/qwen-setup.md`

- [ ] **Step 1: Insertar el puntero al inicio de cada doc stale**

Run:
```bash
PTR='> ⚠️ Estado real autogenerado → ver [/STATE.md](STATE.md) (este documento puede estar desactualizado).'
for f in ESTADO_ACTUAL.md motor-fea/README.md docs/qwen-setup.md; do
  if ! head -1 "$f" | grep -q "Estado real autogenerado"; then
    printf '%s\n\n%s' "$PTR" "$(cat "$f")" > "$f.tmp" && mv "$f.tmp" "$f"
  fi
done
```
(Los planes WebXR NO se editan uno por uno; quedan cubiertos por la sección "Docs stale" de `STATE.md`.)

- [ ] **Step 2: Verificar los punteros**

Run:
```bash
for f in ESTADO_ACTUAL.md motor-fea/README.md docs/qwen-setup.md; do echo "== $f =="; head -1 "$f"; done
```
Expected: cada uno empieza con la línea `> ⚠️ Estado real autogenerado → ...`.

- [ ] **Step 3: Re-estampar (el soft-check debería quedar más limpio)**

Run:
```bash
./estado-real.sh >/dev/null; echo "exit=$?"
```
Expected: `exit=0`.

---

## Task 6: Commit C2 (verdad de estado)

**Files:** ninguno nuevo; commitea lo creado.

- [ ] **Step 1: Ver qué entra en C2**

Run:
```bash
git status --porcelain
```
Expected (orden puede variar):
```
 M ESTADO_ACTUAL.md
 M build.log
 M docs/qwen-setup.md
 M motor-fea/README.md
 M test.log
?? STATE.md
?? docs/superpowers/plans/2026-06-09-f0-verdad-de-estado.md
?? docs/superpowers/specs/2026-06-09-f0-verdad-de-estado-design.md
?? estado-real.sh
```

- [ ] **Step 2: Commit C2**

Run:
```bash
git add STATE.md estado-real.sh ESTADO_ACTUAL.md motor-fea/README.md docs/qwen-setup.md \
        build.log test.log \
        docs/superpowers/specs/2026-06-09-f0-verdad-de-estado-design.md \
        docs/superpowers/plans/2026-06-09-f0-verdad-de-estado.md
git commit -m "chore(estado): STATE.md + estado-real.sh como verdad de estado

- STATE.md: fuente unica autoestampada (1106 .NET / 208 Python verdes).
- estado-real.sh: regenera build.log/test.log y estampa la region AUTO.
- Punteros 'ver STATE.md' en docs stale (ESTADO_ACTUAL, README motor-fea, qwen-setup).
- Regenera build.log/test.log (ya no muestran el falso Build FAILED del 06-03).
- Documenta issues diferidos (4 graficas en blanco -> F1, etc.).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 3: Verificar**

Run:
```bash
git log --oneline -2; git status --porcelain
```
Expected: el commit C2 aparece; working tree limpio.

---

## Task 7 (OPCIONAL): Arreglar 3 warnings + commit C3

**Files:**
- Modify: `tests/LosasPlus.Tests/AcerosViewModelTests.cs:102` (CS8602)
- Modify: `tests/LosasPlus.Tests/EscenaEdificioColumnasTests.cs:22` (xUnit2013)
- Modify: `tests/LosasPlus.Tests/EscenaEdificioVigasTests.cs:56` (xUnit2013)

- [ ] **Step 1: Ver las 3 líneas exactas**

Run:
```bash
sed -n '102p' tests/LosasPlus.Tests/AcerosViewModelTests.cs
sed -n '22p'  tests/LosasPlus.Tests/EscenaEdificioColumnasTests.cs
sed -n '56p'  tests/LosasPlus.Tests/EscenaEdificioVigasTests.cs
```

- [ ] **Step 2: Aplicar los fixes (transformaciones concretas)**

- `EscenaEdificioColumnasTests.cs:22` — xUnit2013 "use Assert.Empty": reemplazar `Assert.Equal(0, <coleccion>.Count)` por `Assert.Empty(<coleccion>)` (misma colección, sin `.Count`).
- `EscenaEdificioVigasTests.cs:56` — xUnit2013 "use Assert.Single": reemplazar `Assert.Equal(1, <coleccion>.Count)` por `Assert.Single(<coleccion>)`.
- `AcerosViewModelTests.cs:102` — CS8602 dereference de posible null: insertar `Assert.NotNull(<expr>);` en la línea anterior al uso, y usar `<expr>!` (null-forgiving) en el deref si el analizador lo sigue marcando.

Usar el tool de edición sobre cada archivo con el texto exacto leído en el Step 1.

- [ ] **Step 3: Verificar 0 warnings y suite verde**

Run:
```bash
dotnet build tests/LosasPlus.Tests/LosasPlus.Tests.csproj --no-incremental 2>&1 | grep -E "Warning|Error" | tail
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: `0 Warning(s)` (o sin líneas de warning) y `Passed: 1106`.

- [ ] **Step 4: Commit C3**

Run:
```bash
git add tests/LosasPlus.Tests/AcerosViewModelTests.cs \
        tests/LosasPlus.Tests/EscenaEdificioColumnasTests.cs \
        tests/LosasPlus.Tests/EscenaEdificioVigasTests.cs
git commit -m "chore(tests): limpiar 3 warnings (CS8602 null-check, xUnit2013 Empty/Single)

Behavior-neutral. Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 5: Re-estampar STATE.md (warn count ahora 0)**

Run:
```bash
./estado-real.sh >/dev/null && git add STATE.md test.log build.log && \
git commit -m "chore(estado): re-estampar STATE.md tras C3" || true
```

---

## Task 8: Verificación final (criterios de aceptación)

**Files:** ninguno.

- [ ] **Step 1: Recorrer los 7 criterios de aceptación del spec**

Run:
```bash
echo "1. working tree limpio:"; git status --porcelain
echo "2/3. STATE.md AUTO:"; sed -n '/AUTO:START/,/AUTO:END/p' STATE.md
echo "5. logs sin Build FAILED:"; grep -c "Build FAILED" build.log
echo "6. issues blancos documentados:"; grep -c "gráficas en blanco" STATE.md
echo "7. suites verdes:"; dotnet test LosasPlus.Linux.sln 2>&1 | tail -1; ( cd motor-fea && .venv/bin/python -m pytest -q 2>&1 | tail -1 )
echo "commits F0:"; git log --oneline -4
```
Expected: `git status` vacío; STATE.md con 1106/208; `build.log` con `0` ocurrencias de `Build FAILED`; STATE.md menciona las gráficas en blanco; `Passed: 1106` y `208 passed`; commits C1, C2 (y C3 si se hizo) presentes.

- [ ] **Step 2: Marcar la fase F0 como cerrada**

F0 cumplido. Próximas fases (de mayor apalancamiento): F1 (4 gráficas en blanco + unificar lienzos), F3 (Pieper-Martens 21/21), F6 (IA con revisión). Cada una con su propio spec + plan.

---

## Self-Review (cobertura del spec)

- §4.1 `estado-real.sh` → Task 3 (script completo) + Task 4 (verificación). ✔
- §4.2 `STATE.md` AUTO + curada → Task 2 + Task 4 Step 4 (idempotencia). ✔
- §4.3 punteros en docs stale → Task 5. ✔
- §4.4 commits C1/C2/C3 → Task 1 / Task 6 / Task 7. ✔
- §5 verificación de F0 → Task 4 + Task 8. ✔
- §6 criterios de aceptación → Task 8 Step 1. ✔
- §8 issues diferidos documentados → Task 2 Step 1 (sección "Issues conocidos diferidos"). ✔
- No-objetivos (no arreglar gráficas en blanco / no unificar lienzos / sin cambios de comportamiento) → respetado; C1 es preservación, C3 es behavior-neutral. ✔
