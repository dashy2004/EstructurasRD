# ADR-0001 — Frontera de integración motor-fea ↔ LosasPlus (C#)

- Estado: **aceptado** (B0, 2026-06-02)
- Contexto: Track B del `PLAN_MAESTRO.md`.

## Contexto

LosasPlus (C#/.NET, Avalonia) calcula losas hoy lanzando un ejecutable externo
propietario, `Losas.exe` (Ing. F. Perdomo): escribe un `.DL`, lo ejecuta y
parsea el `.TXT` de salida (ver `MainViewModel.LanzarLosasExeAsync` /
`ImportarTxtAsync` y `src.Core/Services/TxtParser`). Queremos un motor de cálculo
**propio y auditable** (FEA nativo) sin reescribir la app de escritorio.

## Decisión

El motor vive como **paquete Python independiente** (`motor-fea/`, este repo) y se
integra con el C# en la **misma frontera** donde hoy se invoca `Losas.exe`:

1. **MVP — CLI (paridad con `Losas.exe`):** el C# invoca el ejecutable
   `motor-fea` (entry point `motor_fea.api.cli:main`) pasándole un modelo
   estructural en JSON por archivo/stdin y recibiendo resultados en JSON por
   stdout. Esto reusa el patrón de `ProcessFileLauncher` ya existente y no obliga
   a levantar un servicio.
2. **Evolución — servicio HTTP (FastAPI):** para escenarios interactivos o a
   escala urbana, el mismo núcleo se expone por HTTP (`motor_fea.api`, extra
   `[api]`). El contrato de datos (JSON del modelo y de resultados) es el mismo
   que el CLI.

El núcleo de análisis (`core/`) y la capa normativa (`normativa/`) **no conocen**
la frontera: son librerías puras. Sólo `api/` toca I/O.

## Capas (no se mezclan)

| Capa | Paquete | Responsabilidad | Dependencias |
|---|---|---|---|
| 1 Análisis | `motor_fea.core` | Rigidez directa, frame 3D 12 GDL, shells, modal | NumPy/SciPy |
| 2 Normativa | `motor_fea.normativa` | ACI 318-19 + MOPC R-001/R-033 → CDCRD/ASCE 7-22 | — |
| 3 Frontera | `motor_fea.api` | CLI/HTTP, (de)serialización JSON | (FastAPI opt.) |

## Consecuencias

- **+** Migración de bajo riesgo: el C# cambia *a qué ejecutable* llama, no *cómo*.
- **+** El motor es testeable y versionable aparte (CI Python propio).
- **+** Permite, a futuro, retirar la dependencia de `Losas.exe`.
- **−** Dos runtimes (.NET + Python) en el empaquetado del producto de escritorio;
  se mitiga con un build self-contained del motor (PyInstaller) o el servicio HTTP.

## Versionado normativo (fecha dura)

La capa 2 debe soportar **selección de código por edición**: R-001/R-033 hoy;
CDCRD + ASCE/SEI 7-22 obligatorio desde **2027-04-10**. Las reglas se parametrizan
por código/edición desde el día uno (ver `normativa/r001.py`).

## Estado de implementación

- **B0 (hecho):** scaffolding, dominio (`core/modelo.py`), constantes R-001
  (`normativa/r001.py`), CLI `--version`, smoke-tests stdlib.
- **B1 (siguiente):** solver de rigidez directa frame 3D, validado vs PyNite.
