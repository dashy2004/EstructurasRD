# F2 — Pipeline CAD/DXF determinístico · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. TDD estricto: test RED → impl → GREEN en cada task.

**Goal:** losas importadas por DXF batch en la posición/orientación exacta del plano (paridad con el path interactivo), sin pérdidas silenciosas, con ambientes en L subdivididos y bbox de arcos correcto.

**Spec de referencia:** `docs/superpowers/specs/2026-06-10-f2-cad-deterministico-design.md`

Restricciones: NUNCA tocar `Losas.exe`; NO tocar `CadEditorViewModel` (path interactivo); NO tocar `QwenAnalizador` (diferido a F2b).

---

## Task 1: Rama + baseline ✔ (hecho al redactar este plan)
Baseline verde 1171 .NET / 208 Py · rama `engine/f2-cad-deterministico` desde `engine/f3-pieper-martens-21`.

## Task 2: C0 — commit de spec + plan

## Task 3: C1 — Paridad batch (TDD)
- [ ] Test nuevo `tests/LosasPlus.Tests/DxfBatchParityTests.cs`: `CrearLosaBatch` con `maxYPlano=20`, losa propuesta (X=2, Y=3, Lx=5, Ly=4) → `PosX=2`, `PosY=20−(3+4)=13`, `CoordenadaX=2`, `CoordenadaY=13`, `TienePosicionExplicita=true`, `Tipo` propagado; fallback `Lx<=0 → 4.0`. RED por CS0117.
- [ ] Implementar `DxfEstructuraMapper.CrearLosaBatch(LosaPropuesta, double maxYPlano, int id)` (spec §2.1).
- [ ] Wiring: `MainViewModel.GenerarDesdeDxfAsync` usa el helper con `plano.MaxY` (reemplaza el `new Losa{...}` inline `:1606-1617`).
- [ ] Suite completa verde. Commit C1.

## Task 4: C2 — Rect en capa Viga: contar + avisar (TDD)
- [ ] Test en `DxfEstructuraMapperTests`: polilínea rectangular cerrada en capa "VIGAS" → 0 losas, 0 columnas, 0 vigas, `Advertencias` contiene "rectángulo" y "Viga". RED.
- [ ] Implementar contador + advertencia en `DxfEstructuraMapper.Mapear` (spec §2.2).
- [ ] Suite verde. Commit C2.

## Task 5: C3 — Descomposición rectilínea (ambientes en L) (TDD)
- [ ] Tests en `PoligonoLosaMapperTests` (o nuevo archivo): L de 6 vértices → 2 rects con área total exacta; rectángulo → un único rect igual al bbox; triángulo → `false`.
- [ ] Test en `DxfEstructuraMapperTests`: L en capa Losa → ≥2 `LosaPropuesta`, área total = área del L, advertencia informativa.
- [ ] Implementar `PoligonoLosaMapper.TryDescomponerRectilineo` (celda-barrido, spec §2.3) + integración en `Mapear`.
- [ ] Suite verde. Commit C3.

## Task 6: C4 — BBox real de arcos parciales (TDD)
- [ ] Test: arco 0°→90° centrado en (10,10) r=5 → bounds del plano (con esa única entidad) = (10,10)-(15,15); círculo completo sigue (5,5)-(15,15).
- [ ] Implementar en `DxfImportService` (cardinales dentro del sweep, spec §2.4).
- [ ] Suite verde. Commit C4.

## Task 7: C5 — Cierre
- [ ] STATE.md región curada: marcar F2 **parcial** (2.5 diferido: heurística columna + visión).
- [ ] `./estado-real.sh --check` exit 0. Bitácora iteración 4. Commit C5.

## Verificación final
- Criterios spec §4: paridad (test C1), L→≥2 losas (C3), sin pérdidas silenciosas (C2), suites verdes, Losas.exe intacto (`git log -- '*Losas.exe*'` vacío en la rama).
