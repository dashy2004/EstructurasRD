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
# --no-incremental: un build incremental no recompila y "esconde" los warnings
# reales (falso 0 warn). Recompilar siempre = conteo veraz.
dotnet build "$SLN" --no-incremental > build.log 2>&1
BUILD_RC=$?
# Conteo desde la linea-resumen de MSBuild ("N Warning(s)"), no por grep de
# lineas: cada warning aparece inline Y en el resumen (duplicaria el conteo).
NET_WARN=$(grep -oE "[0-9]+ Warning\(s\)" build.log | tail -1 | grep -oE "[0-9]+" || echo "?")
[ "$BUILD_RC" -ne 0 ] && FAIL=1

echo "==> .NET test ($SLN)"
dotnet test "$SLN" --no-build > test.log 2>&1
NET_PASS=$(grep -oE "Passed: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
NET_FAIL=$(grep -oE "Failed: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
NET_SKIP=$(grep -oE "Skipped: +[0-9]+" test.log | tail -1 | grep -oE "[0-9]+" || echo "?")
[ "${NET_FAIL:-1}" != "0" ] && FAIL=1
# Si el log no contiene el resumen (corrida interrumpida/truncada), la verdad
# es "desconocido": marcar FAIL en vez de estampar un conteo fantasma.
[ "$NET_PASS" = "?" ] && FAIL=1

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
  # Archivos historicos (planes/specs/releases/roadmap): sus conteos viejos son
  # legitimos — son bitacoras de ejecucion, no tableros de estado.
  case "$f" in
    docs/superpowers/plans/*|docs/superpowers/specs/*|motor-fea/docs/superpowers/*|docs/releases/*|docs/roadmap/*) continue ;;
  esac
  # Docs superseded: declaran al tope que STATE.md manda (banner F0); no se
  # auditan sus conteos porque su cuerpo se conserva como historico.
  head -3 "$f" | grep -q "Estado real autogenerado" && continue
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
