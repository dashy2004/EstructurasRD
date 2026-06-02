# Plan Maestro — Correcciones LosasPlus + Motor FEA Nativo

> Backlog único y durable para el loop de integración auto-pautado.
> Estado vivo: marcar `[x]` al completar cada tarea. Una tarea por iteración.
> Última actualización: 2026-06-02.

## Visión de integración (por qué los dos tracks son uno solo)

LosasPlus hoy depende de un motor externo propietario (`Losas.exe`, Ing. F.
Perdomo): escribe `.DL`, lo lanza, parsea el `.TXT`. El **Motor FEA nativo**
(Track B) sustituye ese ejecutable por un backend de cálculo propio, expuesto
como servicio (FastAPI) que el C# invoca **en la misma frontera** donde hoy
lanza `Losas.exe`. Así los dos esfuerzos convergen: arreglar/expandir la app
(Track A) y darle un cerebro de cálculo propio y auditable (Track B).

Regla de capas del motor (no se mezclan nunca):
1. **Análisis FEA puro** (rigidez directa, FEM 1D/2D/3D, modal, espectral).
2. **Code-checking normativo** (ACI 318-19 + MOPC R-001/R-033 → CDCRD/ASCE 7-22).
3. **Datos/geometría** (PostgreSQL+PostGIS, IFC vía IfcOpenShell, viz web).

Licencias: referencia PyNite (MIT), Frame3DD (GPL, solo referencia), scikit-fem
(BSD). **No** embeber OpenSees (licencia UC no comercial).

---

## TRACK A — Correcciones LosasPlus (app C#/Avalonia existente)

- [x] **A0 · Sincronizar la verdad + CI Linux** ✅ (2026-06-02) — sln Linux con
  tests, `ci-linux.yml`, docs corregidas, comentario CAD obsoleto borrado.
  Verificado: build 0/0 + 753/753 verde.
  - Reescribir métricas obsoletas en `ESTADO_ACTUAL.md` (753 tests, no 501;
    Avalonia, no WPF; 18 modos; CAD y 3D ya hechos).
  - Corregir `BUILD-Linux.md` (los tests son `net8.0` y corren en Linux).
  - Borrar comentario obsoleto "Lienzo CAD = stub Fase E" (`MainWindow.axaml:27`).
  - Añadir `tests/LosasPlus.Tests` a `LosasPlus.Linux.sln`.
  - CI Linux (`.github/workflows/ci-linux.yml`): build + test en ubuntu-latest.
  - **Verificación:** `dotnet build LosasPlus.Linux.sln --no-incremental` 0/0 +
    `dotnet test` verde.
- [ ] **A1 · Pestaña "Aceros"** — *funcional end-to-end; falta export*
  - [x] **Core (2026-06-02):** `AcerosLosaDesigner` — As req. por flexión
    (ACI 318-19), As mín. por temperatura (§24.4.3.2), espaciamiento máx.
    (§8.7.2.2), selección de barra #3–#6 + "Disponer", diseño de las 4 franjas
    desde `MomentoLosa`. 15 tests (768/768 total verde).
  - [x] **VM (2026-06-02):** `AcerosViewModel` (en `MainViewModel.Aceros`,
    recalcula al importar `.TXT` y al cambiar de sistema). Verificado pipeline:
    `TxtParser.Apply` inyecta momentos en `Losa`; el VM diseña por losa/franja.
    4 tests de VM (772/772 total verde).
  - [x] **View (2026-06-02):** placeholder reemplazado por DataGrid en
    `MainWindow.axaml` (LOSA/TIPO/FRANJA/Mu/d/As req/mín/diseño/Disponer/As
    prov/ESTADO) + editores recubrimiento/barra + estado vacío "importá .TXT".
    Build 0/0, arranca sin excepción, 772/772 verde. **Pestaña Aceros operativa.**
  - [ ] Enganchar export CSV/XLSX + MemoriaPlus. Quitar placeholder de
    `MainWindow.axaml:784-817`.
- [ ] **A2 · Limpieza de wiring/settings**
  - Eliminar el evento muerto `AtajosGuardados` (o conectarlo; decidir uno).
  - Decidir "densidad de tablas": portarla a `AparienciaConfig` aplicando en vivo
    (patrón `AparienciaCambiada`) **o** quitarla de docs.
  - Extraer `JsonSerializerOptions` duplicado (7 clases de `src.Core/Persistence`).
- [ ] **A3 · Profundizar editores**
  - Bajada de Cargas: predim. de zapata + reporte (hoy 44 líneas de view).
  - Panel αfm visual en el Editor (chips OK/CHK + αx/αy/αm; Core ya listo).
  - Editores globales `VigaPrincipal` y `Bovedilla 1D/2D`.
- [ ] **A4 · Roadmap nuevo de la app**
  - Export SAF · diagramas M/V/Δ reales · DXF Fases 2/3 (polígono→Losa, dibujo).

## TRACK B — Motor FEA nativo (Python/NumPy/SciPy → servicio)

- [ ] **B0 · Decisión de arquitectura + scaffolding**
  - Crear `motor-fea/` (paquete Python): estructura `core/`, `normativa/`,
    `api/`, `tests/`. `pyproject.toml`, venv, deps base (numpy, scipy, pytest).
  - ADR: dónde vive, cómo lo invoca el C# (FastAPI HTTP vs CLI tipo `Losas.exe`).
- [ ] **B1 · Solver rigidez directa — frame 3D 12 GDL**
  - Elemento viga-columna 12 GDL (Hermite flexión + axial/torsión), matriz de
    transformación 12×12, ensamblaje disperso (`scipy.sparse`), `spsolve`.
  - Recuperación de esfuerzos internos + reacciones.
  - **Validación:** error < 1% vs PyNite en truss/pórticos de referencia.
- [ ] **B2 · Shells + dinámica**
  - Elemento MITC4/DKMQ (losas/muros), análisis modal (`eigsh`), diafragma
    rígido (constraint a nodo maestro), espectral SRSS/CQC.
- [ ] **B3 · Capa normativa (motor de reglas)**
  - ACI 318-19 (vigas, columnas, losas, zapatas) + R-001 (espectro, Rd, Cb,
    Fa/Fv, zonas I/II) + R-033. Reglas versionadas por código/edición.
  - Casos de regresión contra las hojas Excel ACI/MOPC del equipo.
- [ ] **B4 · BIM + datos**
  - IfcOpenShell import/export, modelo relacional PostgreSQL, viz
    react-three-fiber. Modelo analítico derivado del geométrico.
- [ ] **B5 · Escala ciudad**
  - PostGIS, LOD/tiling, CityGML→3D Tiles. Evaluar ensamblaje en Rust (PyO3)
    sólo si el perfilado lo exige.
- [ ] **B6 · Puente LosasPlus ↔ Motor FEA**
  - Reemplazar el shell-out a `Losas.exe` por una llamada al motor nativo en la
    misma frontera (`MainViewModel.LanzarLosasExeAsync`).

## Atención normativa (fecha dura)

CDCRD oficializado por Resolución MIVHED 007-2026; transición hasta
**2027-04-10**, adopta ASCE/SEI 7-22 + primer mapa sísmico nacional. Codificar
R-001/R-033 hoy, pero **arquitecturar versionado normativo** desde B3.
