# Progreso de fases F0–F9 · Bitácora del loop autónomo

> Documento vivo mantenido por el loop (cada ~30 min). Para revisar y decidir.
> Mapa de fases: [`roadmap-fases-F0-F9.md`](roadmap-fases-F0-F9.md) · Verdad de estado: [`/STATE.md`](../../STATE.md)

**Última actualización:** 2026-06-10 01:05 · rama `engine/f0-verdad-de-estado`

---

## ✅ Realizado

### Iteración 1 (2026-06-10 00:00–00:30)

1. **Roadmap F0–F9 reconstruido y persistido** (`docs/superpowers/roadmap-fases-F0-F9.md`) — la sección perdida al compactar la sesión anterior. Re-auditoría de 6 subsistemas + verificación adversarial de 3 lentes; 36 pendientes con evidencia `archivo:línea`, 0 sin ubicar.
2. **F0 CERRADA** (los 7 criterios de aceptación del spec pasan):
   - **GATE 1:** `estado-real.sh` ahora marca FAIL si `test.log` no contiene el resumen (antes estampaba conteos fantasma; el `test.log` en disco estaba truncado sin ninguna línea `Passed:`).
   - **GATE 2:** build con `--no-incremental` + conteo de warnings desde la línea-resumen de MSBuild (el build incremental escondía 3 warnings reales; el grep por líneas los duplicaba a 6).
   - **C2** (`ff7cc32`): `STATE.md` + `estado-real.sh` + punteros en 3 docs stale + logs versionados con `git add -f` + spec/plan/roadmap.
   - **C3** (`ed90bda`): 3 warnings limpiados — `Assert.NotNull` (CS8602 `AcerosViewModelTests.cs`), `Assert.Empty` (`EscenaEdificioColumnasTests.cs`), `Assert.Single` (`EscenaEdificioVigasTests.cs`). Behavior-neutral.
   - **Re-estampado** (`582e118`): `STATE.md` dice `0 err / 0 warn · 1106 passed / 208 passed` — y ahora es verdad.
3. **Specs+planes de F1 y F3 en redacción** (workflow en background; ver próxima iteración).

### Iteración 2 (2026-06-10 00:30–01:05)

4. **F8 PARCIAL — CI gateada con la verdad de estado** (`4a8931f`):
   - Nuevo `.github/workflows/estado-real.yml`: corre `./estado-real.sh --check` en cada push/PR a `main`/`avalonia-linux` (build `--no-incremental` + ambas suites + consistencia de docs). Sube `STATE.md`/logs como artefacto.
   - **Soft-check con alcance:** excluye históricos (`plans/`, `specs/`, `releases/`, `roadmap/`) y docs con banner de supersesión → `--check` pasó de 74 falsos positivos a **0** y ya puede gatear.
   - **Banner "ver STATE.md" a 6 docs vivos más** que declaraban conteos viejos: `README.md` (501), `PLAN_MAESTRO.md`, `PLAN_CAD_V1.md` (488), `PROMPTS_STITCH.md`, `PROPUESTA_UPDATE_v1.md` (501), `docs/RELEASE-v1.4.0.md` (957).
   - **`ci.yml` (WPF legacy) resuelto:** pasa a solo `workflow_dispatch` — la cobertura automática la dan `ci-linux.yml` + `estado-real.yml` sobre la solución de verdad (`LosasPlus.Linux.sln`). No se borró nada.
   - Verificado local: `./estado-real.sh --check` → exit 0, suites verdes 1106/208.

## 🧭 Decisiones tomadas (autónomas, revisables)

| # | Decisión | Razón |
|---|----------|-------|
| 1 | Versionar `build.log`/`test.log` con `git add -f` pese a `.gitignore:62 *.log` | Lo exige el plan F0 Task 6 y el criterio del roadmap: STATE.md no puede afirmar logs que no existen en el repo. Alternativa (relajar la regla) descartada para no des-ignorar otros `.log`. |
| 2 | Reparar GATE 1/2 **antes** de C2 (reordenar el plan) | No versionar evidencia falsa: el commit C2 original habría congelado un `test.log` truncado y un `0 warn` falso. |
| 3 | Conteo de warnings desde `N Warning(s)` (línea-resumen), no grep de líneas | Cada warning aparece inline Y en el resumen → el grep duplicaba (3 reales ≈ 6 líneas). |
| 4 | Incluir el roadmap F0–F9 en C2 | Es el "brainstorm de fases" que el spec F0 referencia; pertenece al mismo cierre. |
| 5 | Orden de fases siguientes: F1 y F3 en paralelo (no F4 todavía) | F3/F4 son independientes (verificado: solver Python ≠ Pieper-Martens .NET), pero F4 es XL; F1 y F3 tienen GATES pequeños de alto valor. |
| 6 | Soft-check excluye históricos + docs con banner (iter. 2) | Los conteos viejos en planes/releases son bitácoras legítimas; sin alcance, `--check` daba 74 falsos positivos y era inutilizable como gate de CI. |
| 7 | Banner de supersesión a 6 docs vivos en vez de editar sus conteos | Estrategia F0: superseder, no reescribir cuerpos. El soft-check ahora exige que docs vivos NUEVOS no mientan (o lleven banner). |
| 8 | `ci.yml` WPF → solo manual (no borrado) | Construía la sln WPF vieja con `/warnaserror` en cada push a main; la verdad es `LosasPlus.Linux.sln`. Se conserva ejecutable a demanda por si el snapshot WPF se necesita. |
| 9 | El workflow de CI crea `motor-fea/.venv` en la ruta exacta | `estado-real.sh` exige ese path a propósito (el python del sistema sin pytest era una de las mentiras originales de estado). |

## ⏸️ Pendiente de TU decisión (saltado por el loop)

- **Destino de la rama `engine/f0-verdad-de-estado`:** ¿merge directo a `avalonia-linux`, PR en GitHub, o seguir acumulando fases en esta rama? El loop seguirá commiteando aquí hasta que decidas.
- **F3 — validación de fixtures vs `Losas.exe`:** el mapeo de los 20 códigos faltantes se puede implementar desde `TABLAS-PERDOMO.md`, pero la validación final contra el motor del Ing. Perdomo requiere tu copia de `Losas.exe` (Windows). El loop implementará el mapeo + tests del JSON; la corrida comparativa queda para ti.
- **F8 — firma de código:** requiere certificado Authenticode (compra/gestión humana). Se saltará cuando llegue.

## 📋 Pendiente (cola del loop)

| Fase | Estado | Próximo paso |
|------|--------|--------------|
| F0 | ✅ cerrada | — (solo decisión de merge) |
| F1 | 🔄 spec+plan en redacción | Implementar VigaPng/InteraccionPng + unificar lienzos |
| F3 | 🔄 spec+plan en redacción | GATES (captura por-losa, mensaje engañoso) + mapeo 20 códigos |
| F2 | ⬜ | Tras F1 (espejado Y, PosX/PosY, ambientes en L) |
| F4 | ⬜ | CargaElemento + peso propio (XL; varias iteraciones) |
| F5 | ⬜ | Tras F4 (deflexión, deriva, torsión) |
| F6 | ⬜ | Tras F2 (qwen.config runtime, UI de revisión) |
| F7 | ⬜ | Tras F5 (contrato IA, memoria con diagramas) |
| F8 | 🟡 parcial | ✅ CI gateada (`estado-real.yml`) + `ci.yml` resuelto · ⏸️ firma/instalador/release-Linux (firma necesita certificado tuyo) |
| F9 | ⬜ | Tras F7 (IFC 4.3, MITC4, CDCRD — fecha dura 2027-04-10) |

## 📝 Notas

- Suites verdes en cada commit: **1106 .NET / 208 Python**.
- Restricción permanente respetada: `Losas.exe` y su import intactos.
- El loop corre cada 30 min (job `513966bc`, expira en 7 días); cancelable con CronDelete.
