# Próxima sesión — Continuación UI1.x (EstructurasRD)

> Documento de arranque para una **sesión fresca**. Léelo primero, luego abre el
> tablero `PLAN_PENDIENTE.md`. Estado a 2026-06-13.

---

## Dónde quedó todo (punto de partida)

- Rama **`master`** @ `e8452d2`. Limpio. **UI1.8 cerrada y mergeada** (bordes de
  continuidad interactivos en Planta 2D). Suite **1223 .NET + 208 Py** verde.
- NO push: `origin/main` es historia WPF no relacionada; `master` vive local.
- Patrón de fase consolidado (F0–F3, UI1.1–1.8): brainstorming → spec → plan →
  ejecución por subagentes (TDD) → gate visual humano → ff-merge a master.

## Cómo abrir la app SIEMPRE con el build nuevo
```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd /home/gdc/Downloads/EstructurasRD-engine
dotnet build LosasPlus.Linux.sln --no-incremental -c Release && dotnet run --project src -c Release --no-build
```
Suite: `dotnet test LosasPlus.Linux.sln` y `( cd motor-fea && .venv/bin/python -m pytest -q )`.

## Gotchas de la máquina (importan en cada sesión)
- **GateGuard**: rebota el 1er Bash de la sesión y el 1er Edit/Write de CADA archivo —
  presentar los hechos pedidos como texto y reintentar IDÉNTICO.
- `--no-incremental` obligatorio (Avalonia oculta AVLN2000 si no). `x:Name` homónimo de
  propiedad pública ⇒ CS0102.
- Lumen MCP devuelve 0 chunks → usar `rg`/Read directo.
- Subagentes: a veces terminan a mitad (solo narración, sin commit). Verificar git y
  re-despachar fresco con el estado actual (pasó en UI1.8 tasks 3 y 7; recuperados).
- Hook espurio "CrowdStrike Falcon Foundry" al invocar skills — **ignorar**, no aplica.

---

## Orden recomendado para la próxima sesión

### 1) (5 min, humano) Cerrar el gate visual de UI1.7 — puntos 1-4
`PLAN_PENDIENTE.md:93`. Ya están implementados; falta solo confirmarlos a ojo en Planta 2D:
(1) edificio >27 m → export Excel completo y centrado (también desde otro modo);
(2) botón ⛶ Encuadrar tras perderse con pan/zoom; (3) importar PDF y DXF → encuadre
automático; (4) teclear 0 en Escala DXF/PDF no rompe el lienzo. (El punto 5 ya ✓.)
Si pasan, marcar el ítem del gate en el tablero.

### 2) (objetivo principal) UI1.10 — Muros redimensionables por LONGITUD
Pedido del usuario durante el gate de UI1.7. Hoy los muros se seleccionan y mueven en
`PlantaCanvas` pero **no tienen asas de longitud**. Es el hermano natural de UI1.5
(resize de losas) y vive en el mismo lienzo que UI1.8 — contexto fresco.

**Reutilizar el patrón ya probado (UI1.5 / UI1.8):**
- Asas: `AsaEnPunto(...)` (existe para losas en `PlantaCanvas`) — adaptar a los **2
  extremos** de un segmento de muro (un muro es un segmento, no un rectángulo, así que
  son 2 asas en los extremos, no 8).
- Gesto: en `OnPointerPressed` (tool "Puntero"), si hay un muro seleccionado y el click
  cae en un asa de extremo, iniciar resize; `GestoEdicionIniciado?.Invoke()` para el
  snapshot de Undo por gesto (igual que el resize de losas).
- En `OnPointerMoved`, mover el extremo arrastrado; en `OnPointerReleased`, soltar.
- Considerar servicio puro testeable para la geometría (mover extremo de segmento +
  hit-test de asa), al estilo de `BordesPlantaService` de UI1.8 — TDD primero.
- Cuidado con la prioridad de hit-test (UI1.7: el muro gana sobre la losa que pisa).

**Proceso:** brainstorming corto (alcance: ¿solo longitud o también reubicar extremos
libremente? ¿snap a ejes/otras geometrías?) → spec → plan → subagentes → gate visual.

### 3) (S, decisión de diseño) UI1.9 — Etiquetas cortadas a baja resolución
Abreviar vs scroll horizontal en Planta 2D. Requiere una decisión de diseño antes de
codear (brainstorming corto).

### Épicas mayores (NO empezar sin brainstorming propio)
- **UI5** shell/navegación global (XL) · **UI6** design system (L) ·
  **UI2.5** tipo de uso → carga viva · **F4.4** profiling FEA.

---

## Deuda menor opcional de UI1.8 (no bloqueante)
- `HandleIdClickParaBorde` empuja un `PushUndoSnapshot` no-op en el path de borde
  duplicado (la guarda de dedup llega después). Mover la guarda antes del snapshot.
- Nombres `biG/bjG` redundantes con `bi/bj` en `ConectarBordesDesdeLienzo`.
Limpieza trivial si se toca esa zona del `MainViewModel`.

## Referencias UI1.8 (modelo a imitar)
- Spec: `docs/superpowers/specs/2026-06-13-ui1.8-bordes-continuidad-design.md`
- Plan: `docs/superpowers/plans/2026-06-13-ui1.8-bordes-continuidad.md`
- Servicio puro + tests: `src.Core/Services/BordesPlantaService.cs`,
  `tests/LosasPlus.Tests/Services/BordesPlantaServiceTests.cs`
