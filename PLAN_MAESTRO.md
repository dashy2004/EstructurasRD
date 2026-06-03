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

## Capstone motor FEA (2026-06-02)

Test de integración `test_integracion_edificio.py`: flujo de edificio completo en
un solo análisis — pórtico 3D + diafragma rígido → estático lateral → modal →
cortante basal modal-espectral (R-001) → diseño de losa por FEM. **108/108
motor-fea verde.** El motor es un sistema de análisis y diseño estructural
completo y validado; el fork Shells B2 y los refinamientos elegidos están hechos.
Restante: MITC4 (nicho) y el **puente B6** (conectar la app viva — decisión del usuario).

## Hito (2026-06-02) — tras 21 iteraciones del loop

**Track A** efectivamente completo (A0–A3 ✅; A4 SAF/M-V-Δ ya existían; solo DXF
Fases 2/3 sin construir). **Track B** = MVP del motor FEA completo y validado:
solver estático 3D · modal multi-modo + participación · ACI 318-19
(viga/columna/zapata/losa/combos) · sismo R-001 · CLI JSON · CI pytest.
Suites: **772/772 .NET + 68/68 Python**, verde en cada commit.

Forks grandes restantes (requieren decisión): **shells B2** (losas por FEM,
keystone para reemplazar `Losas.exe`; grande, validación por convergencia de
malla), **puente B6** (conectar LosasPlus al motor; **toca la app viva** →
confirmar antes), **DXF Fases 2/3** (CAD), **BIM/escala urbana** (B4/B5).

---

## Bugfix (2026-06-02) — sincronización 2D↔3D

`SincronizadorPlanta` (src.Core): Planta 2D y Vista 3D leen `Losa.CoordenadaX/Y`
y `Columna.CoordenadaX/Y`, pero sólo se asignaban al arrastrar; lo creado en el
grid del Editor o importado de `.DL`/`.TXT` quedaba en (0,0) → **todo apilado en
el origen**. Ahora se hornea el layout de `LayoutSolver` en las coordenadas
(losas) y se distribuyen las columnas en grilla (zapatas siguen), al entrar a
Planta2D/Vista3D/PlanoCad; `Vista3DControl` reconstruye al volverse visible. 8
tests; 780/780 .NET verde. Pendiente menor: posicionamiento por defecto de vigas.

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
- [x] **A1 · Pestaña "Aceros"** — *funcional end-to-end con export (2026-06-02)*
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
  - [x] **Export CSV/XLSX (2026-06-02):** `AcerosLosaExporter` (src.Core/Services) —
    diseño de acero por franja (X/Y centro y apoyo) a CSV (UTF-8/BOM, `;`) y XLSX
    (1 hoja, shading por ESTADO). `ToCsv` puro/determinista; paridad fila-a-fila con
    `AcerosViewModel.Filas` testeada. Botones ⬇CSV/⬇XLSX en la barra de Aceros →
    `ExportarAcerosCsv/Xlsx`. 6 tests. **790/790 .NET verde.** El claim del
    "placeholder en `MainWindow.axaml:784-817`" estaba obsoleto (la View ya era un
    DataGrid; esa región hoy es Columnas/CAD).
  - [ ] *(Residual menor)* Integrar el schedule de aceros por franja en **MemoriaPlus**
    (hoy MemoriaPlus no consume `AcerosLosaDesigner`/`DisenoAceroFranja`).
- [x] **A2 · Limpieza de wiring/settings** ✅ (2026-06-02)
  - [x] Eliminado el evento muerto `AtajosGuardados` (sin suscriptores; la
    aplicación en vivo ya va por `AtajosService.AtajosCambiados`, al que LosasPlus
    y MemoriaPlus se suscriben). Build 0/0, 772/772 verde.
  - [x] *(Ya resuelto, claim obsoleto)* `JsonSerializerOptions` ya está
    centralizado en `JsonConfigHelper.DefaultOptions` (7 servicios lo usan);
    `ProyectoSerializer` queda aparte **a propósito** (Populate + encoder relajado).
  - [x] *(No es bug)* "Densidad de tablas" no existe en el `AparienciaConfig` de
    Avalonia: era setting WPF **no portado**; el banner de `ESTADO_ACTUAL.md` ya
    lo aclara. No se promete en la UI actual.
- [x] **A3 · Profundizar editores** ✅ (2026-06-02)
  - [x] *(Ya estaba completo — verificado)* Bajada de Cargas: la View (densa, 44
    líneas) ya expone todo el VM: presión admisible, Recalcular, **Exportar
    XLSX**, **Predimensionar zapatas** (reparto por columnas vía
    `DescensoColumnas`) + grilla por niveles. Core `Transmision/` completo
    (incl. `PredimZapata.CuadradaDesdeUltima` para Wu→servicio). Tests:
    `BajadaCargasViewModelTests`, `PredimZapataTests`. No requería trabajo.
  - [x] **Panel αfm visual (2026-06-02):** en el sidebar del Editor, muestra
    αx/αy/αm de la `LosaSeleccionada` + chip OK (αm&gt;2) / CHK (revisar espesor),
    con estado vacío. Nueva prop `LosaSeleccionada` en `MainViewModel`. Build
    0/0, arranca, 772/772.
  - [x] **Editores VigaPrincipal/Bovedilla (2026-06-02):** en el sidebar del
    Editor, bindean `Sistema.VigaPrincipal` (b/h cm) y `Sistema.Bovedilla1D/2D`
    (S·B·L·H m), que alimentan αfm y los cómputos métricos. Build 0/0, 772/772.
- [ ] **A4 · Roadmap nuevo de la app** — *casi todo ya existía*
  - [x] *(Ya hecho)* **Export SAF**: cableado — menú "Exportar SAF (vigas)…" →
    `OnExportSafClick` → `MainViewModel.ExportarSaf` → `SafExporter.Export`.
  - [x] *(Ya hecho)* **Diagramas M/V/Δ reales**: OxyPlot `PlotView` en el editor
    de Vigas → `ModeloViga`/`ModeloEsfuerzos`(V/M)/`ModeloDeflexion`, computados
    en `VigaEditorViewModel`. No son placeholders.
  - [ ] **DXF Fases 2/3** (polígono DXF→`Losa`, editor de dibujo manual) — el
    único item de Track A genuinamente sin construir. Trabajo CAD grande.

> **Hallazgo del loop (2026-06-02):** la auditoría inicial (basada en docs de la
> era WPF + conteo de líneas) sobre-marcó pendientes. El código Avalonia ya tenía
> A3 (BajadaCargas) y A4 (SAF, M/V/Δ) implementados. Los gaps **reales** de Track A
> eran la pestaña **Aceros** (placeholder), la visibilidad del **panel αfm** y los
> **editores Viga/Bovedilla** — los tres ya integrados (A1, A3). **Track A queda
> efectivamente completo salvo DXF Fases 2/3.** El esfuerzo restante del loop se
> concentra en **Track B (motor FEA)**, donde cada incremento es capacidad nueva.

## TRACK B — Motor FEA nativo (Python/NumPy/SciPy → servicio)

- [x] **B0 · Decisión de arquitectura + scaffolding** ✅ (2026-06-02)
  - Paquete `motor-fea/` con capas `core/` (dominio: Nodo/Material/Seccion/
    ElementoFrame/Apoyo/CargaNodal/ModeloEstructural, 6 GDL/nodo), `normativa/`
    (R-001: zonas I/II, espectro, cortante basal), `api/` (CLI `--version`).
  - `pyproject.toml`, `docs/ADR-0001-integracion.md` (frontera CLI/HTTP que
    reemplaza el shell-out a `Losas.exe`), `README.md`, `.gitignore`.
  - CI `ci-motor-fea.yml`. **7 smoke-tests stdlib verde** (Python 3.14, sin numpy).
- [x] **B1 · Solver rigidez directa — frame 3D 12 GDL** ✅ (2026-06-02)
  - `core/solver.py`: matriz local 12×12 (axial EA/L, torsión GJ/L, flexión EIz
    plano x-y y EIy plano x-z), triada local + transformación 12×12, ensamblaje
    global, condiciones de borde, solve (eliminación gaussiana pura), reacciones.
  - **Validación cerrada (error ~1e-9, <<1%):** axial `PL/AE`, voladizo
    `PL³/3EI` (ambos planos), torsión `TL/GJ`, giro `ML/EI`, reacciones en
    equilibrio, simetría, convergencia 2-elementos → 1-elemento. 8 tests.
  - Implementado en Python puro (corre en 3.14 sin numpy); numpy/scipy quedan
    para escala (B5). Cross-check vs PyNite: pendiente cuando haya wheels.
  - **15/15 tests motor-fea verde.**
- [x] **B1.5 · Contrato JSON + CLI `--analyze`** ✅ (2026-06-02) — *frontera B6*
  - `api/contrato.py`: (de)serialización `ModeloEstructural ↔ JSON` (round-trip
    exacto) + `analizar_dict/analizar_json` (modelo→resultados).
  - `api/cli.py`: `motor-fea --analyze modelo.json` (o `-` stdin) → resultados
    JSON por stdout. El C# lo invocará igual que hoy lanza `Losas.exe`.
  - 6 tests (round-trip, defaults, análisis, CLI version+analyze). **21/21 verde.**
  - Verificado end-to-end: voladizo por CLI → `uz = -6.667e-4 m` (= `PL³/3EI`).
- [ ] **B2 · Shells + dinámica** — *en progreso*
  - [x] **Período fundamental (2026-06-02):** `core/modal.py` — masas nodales,
    condensación estática Guyan de GDL sin masa, iteración de potencia inversa
    para ω₁/T₁ + forma modal. Validado vs voladizo+masa `ω=√(3EI/L³/m)`
    (T=0.1622 s, error ~1e-9). 4 tests. **43/43 motor-fea verde.**
  - [x] **Modos múltiples + participación (2026-06-02):** `modos(n)` por
    iteración inversa con **deflación M-ortogonal**; `participacion_modal`
    (masa modal efectiva + % por dirección). Validado vs cadena de 2 masas-
    resorte `ω²=(k/m)(3∓√5)/2` y participación que suma 100% (modo 1 ~95%).
    **51/51 motor-fea verde.**
  - [~] **Shells (en curso — fork elegido):**
    - [x] **Elemento de placa ACM (2026-06-02):** `core/placa.py` — rectángulo
      Kirchhoff de 12 GDL (w,θx,θy), `rigidez_placa` por `K=C⁻ᵀ(∫BᵀDbB)C⁻¹` con
      Gauss 3×3. Validado a nivel de elemento: simetría, 3 modos de cuerpo
      rígido con energía nula, escala t³. 7 tests. **75/75 motor-fea verde.**
    - [x] **Mallador + solver de losa (2026-06-02):** `core/losa_fem.py` —
      malla nx×ny, ensamblaje global (3 GDL/nodo), apoyos simple/empotrado, carga
      uniforme, solve de w. **Validado por convergencia monótona** vs placa
      cuadrada SS (err 4×4→10×10: −3.4%→−0.48%; <1% a 10×10). 5 tests.
      **80/80 motor-fea verde.**
    - [x] **Momentos + diseño (2026-06-02):** `placa.momentos_elemento` (M=−Db·κ),
      `losa_fem` recupera Mx/My/Mxy máximos; `motor_fea/diseno_losa.py` cierra
      **análisis→diseño** (FEM → momentos → `aci318.diseno_losa_franja`). Momento
      convergente y simétrico (coef Mx/qa² 4×4→10×10: 0.035→0.043). 5 tests.
      **85/85 motor-fea verde.** El motor iguala a `Losas.exe` (momentos→armadura).
    - [x] **CLI de diseño de losa (2026-06-02):** `contrato.disenar_losa_json` +
      `motor-fea --disenar-losa params.json` → JSON {w_central, mx/my_max,
      franja_x/y: As + Disponer}. Demo: `#3 @ 190 mm, cumple`. 2 tests, 87/87.
      **El motor diseña una losa por línea de comandos (JSON→JSON) — listo para B6.**
    - [x] **Losa rectangular validada (2026-06-02):** deflexión y momentos vs
      Timoshenko (`w=α q b⁴/D`, b=corto): a/b=1.5 → −0.7%, a/b=2 → −0.9% a malla
      fina (convergente); My>Mx (domina el vano corto). 3 tests. **90/90 verde.**
    - [x] **Combinación modal SRSS/CQC (2026-06-02):** `core/combinacion_modal.py`
      — `srss`, `coeficiente_correlacion` (Der Kiureghian) y `cqc`. Validado:
      SRSS(3,4)=5; CQC≈SRSS con modos separados y CQC>SRSS con frecuencias
      cercanas; ρ=1 coincidentes. 7 tests. **97/97 motor-fea verde.**
    - [x] **Análisis modal-espectral (2026-06-02):** `sismo.cortante_basal_modal_espectral`
      — por modo Sa(Ti)·M_eff_i combinado con CQC; reporta masa participativa
      (≥90%). Validado vs estático en cadena 2 GDL: V=0.95× estático, 100% masa,
      modo 1 domina. 3 tests. **100/100 motor-fea verde.**
    - [x] **Acero superior (momento de apoyo) (2026-06-02):** `losa_fem` recupera
      el momento de apoyo (bordes); `diseno_losa` diseña `franja_apoyo` (acero
      superior) + lo expone en el contrato/CLI. Validado: SS → apoyo≈0; empotrada
      → apoyo gobierna (coef→0.049, >vano) y converge. 4 tests. **104/104 verde.**
      El motor iguala la salida completa de `Losas.exe` (acero inferior+superior).
    - [x] **Diafragma rígido (2026-06-02):** `core/diafragma.py` — constraint
      master-slave (método de transformación `K_red=TᵀKT`) que liga ux/uy/rz de
      los nodos del nivel a un nodo maestro. Validado: tie cinemático (ux iguales),
      equilibrio (ΣR=−P), reparto de carga (deflexión a la mitad con 2 columnas).
      3 tests. **107/107 motor-fea verde.**
    - [ ] MITC4 (placas gruesas, sin shear-locking) — refinamiento de nicho; el
      ACM (placa delgada) ya cubre las losas típicas.
- [x] **B·sismo — cortante basal estático (2026-06-02)** — *3 capas juntas*
  - `r001.aceleracion_espectral(T)`: espectro de diseño (rampa T0 / meseta SDS /
    rama SD1/T). `sismo.cortante_basal_sismico`: módulo de composición que une
    `core.modal` (T) + `r001` (Sa, Cb) → Cb=max(U·Sa/Rd, 0.03), V=Cb·W.
  - 6 tests (espectro por tramos + continuidad, V end-to-end, piso 0.03).
    Demo voladizo Zona I: T=0.162 s → Sa=1.03 g → Cb=0.188 → V=1843 N.
    **49/49 motor-fea verde.**
- [ ] **B3 · Capa normativa (motor de reglas)** — *en progreso*
  - [x] **Viga ACI 318-19 (2026-06-02):** `normativa/aci318.py` — flexión (As req,
    As mín §9.6.1.2, φMn, ratio Mu/φMn) y cortante (Vc §22.5.5.1, Vs, φVn, tope
    Vs §22.5.1.2, ratio Vu/φVn). SI (N·mm·MPa). 8 tests (consistencia interna
    As→φMn, valor a mano, Vc). **29/29 motor-fea verde.**
  - [x] R-001 (espectro, Cb, zonas I/II) ya en `normativa/r001.py` (B0).
  - [x] **Columnas P-M (2026-06-02):** `aci318.py` — β1 (§22.2.2.4.3), φ por εt
    (Tabla 21.2.2, 0.65→0.90), Po/φPn,max (§22.4.2.1), profundidad balanceada,
    `punto_interaccion(c)` por compatibilidad de deformaciones (εcu=0.003,
    descuento de hormigón desplazado). 6 tests (β1, transición φ, balanceado
    εt=εy, monotonía Pn). **35/35 motor-fea verde.**
  - [x] **Zapatas/punzonamiento (2026-06-02):** `aci318.py` — perímetro crítico
    bo (interior/borde/esquina, §22.6.4.1), los 3 términos de vc §22.6.5.2 (base
    0.33√f'c, βc, αs=40/30/20), φVc y ratio. 4 tests (gobierna base / gobierna βc
    / esquina αs=20 / cumple). **39/39 motor-fea verde.**
  - [x] **Combinaciones de carga (2026-06-02):** `normativa/combinaciones.py`
    — las combos LRFD §5.3.1 (D/L/Lr/S/R/W/E), W y E reversibles (±),
    "(Lr o S o R)"=max, y `envolvente` (máx/mín; el mín captura uplift). 7 tests.
    **58/58 motor-fea verde.**
  - [x] **Losas a flexión (2026-06-02):** `aci318.diseno_losa_franja` (SI) — As
    req. + As mín. por temperatura (§24.4.3.2, ρ·b·h) + espaciamiento máx.
    (§8.7.2.2) + selección de barra #3–#6 + "Disponer". Espeja el
    `AcerosLosaDesigner` de C# (A1). 6 tests. **64/64 motor-fea verde (pytest).**
  - [ ] Losas DDM/EFM (momentos por coeficientes); R-033; versionado por edición;
    regresión vs Excel ACI/MOPC. *(Code-checking cubre viga, columna, zapata,
    combos y losa a flexión.)*
- [ ] **B4 · BIM + datos**
  - IfcOpenShell import/export, modelo relacional PostgreSQL, viz
    react-three-fiber. Modelo analítico derivado del geométrico.
- [ ] **B5 · Escala ciudad**
  - PostGIS, LOD/tiling, CityGML→3D Tiles. Evaluar ensamblaje en Rust (PyO3)
    sólo si el perfilado lo exige.
- [ ] **B6 · Puente LosasPlus ↔ Motor FEA** — *en progreso (aditivo)*
  - [x] **Servicio C# (2026-06-02):** `MotorFeaService` (src.Core) — construye
    los params JSON desde una `Losa`/`Sistema` (conversión ton·cm→SI: fc·98.0665,
    q·9806.65, E=4700√fc), invoca `motor-fea --disenar-losa -` por proceso (como
    `Losas.exe`) y parsea el resultado. Build+parseo puros y testeados (4 tests,
    784/784 .NET). Integración real verificada: params C# → motor → diseño válido.
  - [x] **UI cableada (2026-06-02):** comando async `DisenarConMotorFeaCommand`
    en `MainViewModel` (corre `MotorFeaService.DisenarLosaAsync` sobre la
    `LosaSeleccionada`, expone resultado/ocupado); botón **"🧮 Diseñar con motor
    FEA"** + campo de comando + panel de resultado en la pestaña Aceros de
    `MainWindow.axaml`. **Aditivo** (no toca el flujo `Losas.exe`). Build 0/0,
    arranca sin excepción, 784/784 .NET. **Puente B6 funcional end-to-end.**

## Atención normativa (fecha dura)

CDCRD oficializado por Resolución MIVHED 007-2026; transición hasta
**2027-04-10**, adopta ASCE/SEI 7-22 + primer mapa sísmico nacional. Codificar
R-001/R-033 hoy, pero **arquitecturar versionado normativo** desde B3.
