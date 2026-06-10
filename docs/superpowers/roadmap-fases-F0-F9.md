# Roadmap de fases F0–F9 · EstructurasRD

- **Fecha:** 2026-06-09
- **Fuente de verdad de estado:** [`/STATE.md`](../../STATE.md) (build/tests en vivo: .NET 1106 ✓ / Python 208 ✓).
- **Cómo se construyó:** re-auditoría de 6 subsistemas en paralelo → síntesis → verificación adversarial de 3 lentes (grounding / orden-dependencias / completitud) → reconciliación. 46 pendientes detectados con evidencia `archivo:línea`, consolidados a 36 distribuidos en 10 fases. 0 items sin ubicar.
- **Anclas fijas** (ya referenciadas en `STATE.md`, no se renumeran): F0 verdad de estado · F1 verdad visual · F3 Pieper-Martens 21/21 · F4 correctitud física · F6 IA con revisión.
- **Restricción permanente:** **nunca** borrar `Losas.exe` ni su import (motor legacy aditivo).

> Cada item lleva su evidencia `archivo:línea`. Si este roadmap contradice a `STATE.md`, manda `STATE.md`.

---

## Grafo de dependencias (corrección clave de la verificación)

```
F0 ──┬── F1 ── F2 ── F6 ── F7
     ├── F3                  (F3 y F4 son correctitud-de-motor INDEPENDIENTES:
     ├── F4 ── F5 ── F7       solver Python motor-fea ≠ Pieper-Martens .NET nativo;
     └── F8                   grep PieperMartens en motor-fea/src = vacío → paralelizables tras F0)
F7 ── F9
```

| Fase | Tema | Estado | Esfuerzo | Depende de |
|------|------|--------|----------|------------|
| **F0** | Verdad de estado | 🟡 en-progreso | M | — |
| **F1** | Gobernar la verdad visual | ⬜ pendiente | L | F0 |
| **F2** | Pipeline CAD/DXF determinístico | ⬜ pendiente | L | F1 |
| **F3** | Pieper-Martens nativo 21/21 subtipos | ⬜ pendiente | L | F0 |
| **F4** | Correctitud física del solver | ⬜ pendiente | XL | F0 |
| **F5** | Completitud física de normativa y diseño | ⬜ pendiente | L | F4 |
| **F6** | IA con revisión humana | ⬜ pendiente | L | F2 |
| **F7** | Completar superficie de motor e IA | ⬜ pendiente | L | F5 |
| **F8** | Empaquetado, firma y CI gateada | ⬜ pendiente | L | F0 |
| **F9** | Escala BIM, multi-dominio y gemelo urbano | ⬜ pendiente | XL | F7 |

**Orden de apalancamiento:** la *verdad* (F0) y la *correctitud de motor* (F3 y F4 en paralelo tras F0; luego F5) preceden a la *verdad visual* (F1), el *pipeline CAD* (F2), la *IA con revisión* (F6) y la *superficie motor/IA* (F7). F8 convierte F0 en gating de CI. F9 abre la gran escala más la deuda MITC4 y el versionado normativo CDCRD.

---

## F0 — Verdad de estado · 🟡 en-progreso · M

**Objetivo:** una sola fuente de verdad verificable sobre build/tests, sin métricas falsas, gateada en disco y commiteada — **reparando primero los logs** para no versionar evidencia falsa.

> ⚠️ **La verificación descubrió que `estado-real.sh` hoy MIENTE** (el mismo pecado que F0 vino a corregir). Dos GATES van **antes** del commit C2:

- **GATE 1 — `test.log` truncado:** el log regenerado **no contiene** la línea `Passed:` que respalda el conteo 1106. Quitar `--no-build` o capturar el resumen completo. — `test.log` termina en `A total of 1 test files matched...` sin `Passed: 1106`; `estado-real.sh:28-29` parsea `NET_PASS` de un log incompleto.
- **GATE 2 — falso negativo de warnings:** `estado-real.sh:22-24` hace `dotnet build` sin `--no-incremental` y reporta `0 warn` cuando hay **3**. Regenerar `build.log` veraz. — `STATE.md:10` `build 0 err / 0 warn`; `build.log` declara `0 Warning(s)` por build incremental.
- **Commit C2 (tras GATE 1 y 2):** versionar `STATE.md`, `estado-real.sh`, plan y spec F0 + 3 docs stale con puntero. — `git status` muestra `?? STATE.md`, `?? estado-real.sh`, plan/spec untracked; plan Task 6 `...f0-verdad-de-estado.md:326-366`.
- **Forzar versionado de `build.log`/`test.log`** (`git add -f` o relajar regla) para que `STATE.md` no afirme logs inexistentes. — `.gitignore:62 '*.log'`; `git ls-files build.log test.log` = vacío.
- **C3 (opcional) — 3 warnings:** CS8602 `AcerosViewModelTests.cs:102`; xUnit2013 `EscenaEdificioColumnasTests.cs:22` → `Assert.Empty`; xUnit2013 `EscenaEdificioVigasTests.cs:56` → `Assert.Single`. — `dotnet build -c Release --no-incremental` ⇒ `3 Warning(s)`.

**Aceptación:** orden forzado (reparar logs → luego C2). `git ls-files STATE.md build.log test.log` devuelve los 3; `test.log` contiene `Passed: 1106`; `STATE.md` ya no afirma `0 warn` siendo falso; `./estado-real.sh --check` sale 0.

---

## F1 — Gobernar la verdad visual · ⬜ pendiente · L · (← F0)

**Objetivo:** eliminar el riesgo de gráficas en blanco extendiendo el fix PNG a los 2 modelos con `LineSeries`, y dejar un solo lienzo CAD/Planta.

- **Extender `DiagramaPng` a `ModeloViga`** (eje de viga = `LineSeries`), aún en `oxy:PlotView` crudo. — `src/Views/Vigas/VigaEditorView.axaml:287`; `VigaEditorViewModel.cs:614`; no existe `VigaPng`; el fix `EsfuerzosPng`/`DeflexionPng` (`:294,300`) no se extendió.
- **Extender `DiagramaPng` a `ModeloInteraccion`** (curva P-M = `LineSeries`). — `src/Views/ColumnasEditorView.axaml:209`; `ColumnasEditorViewModel.cs:355,369`; no existe `InteraccionPng`.
- **Decidir fallback para `ModeloSeccion` / `ModeloSeccionColumna`** (`Annotations` + `ScatterSeries`) sobre el control con bug conocido. — `VigaEditorView.axaml:257` y `ColumnasEditorView.axaml:204`; `VigaEditorViewModel.cs:775-799`.
- **Unificar lienzo CAD+Planta2D:** retirar la pestaña CAD (paso 5) y cablear Auto-Conectar (paso 4), dejando un único canvas. — `src/Views/EditorUnificadoView.axaml:12-19` mantiene 2 `TabItem`; pasos 2-3 ya hechos en `PlantaCanvas.cs:61-100,319-334`; `docs/plan-unificar-cad-planta2d.md`.

**Aceptación:** existen `VigaPng`/`InteraccionPng` + tests de píxeles verdes; `EditorUnificadoView.axaml` ya no tiene 2 `TabItem`.

---

## F2 — Pipeline CAD/DXF determinístico · ⬜ pendiente · L · (← F1)

**Objetivo:** que una losa importada por DXF batch quede en la posición y orientación **exactas** del plano — sin espejado ni reubicación, sin pérdida silenciosa, soportando ambientes en L.

- **Invertir eje Y en el pipeline batch** (hoy usa Y crudo → geometría espejada). — `MainViewModel.cs:1610` `CoordenadaY = l.YMetros`; el path interactivo sí invierte (`CadEditorViewModel.cs:866`); convención Y-descendente `PlantaCanvas.cs:152`.
- **Anclar `PosX/PosY` exactos del DXF en batch** (hoy `null` ⇒ `LayoutSolver` reubica). — `MainViewModel.cs:1606-1617` fija solo `CoordenadaX/Y`; path interactivo ancla en `CadEditorViewModel.cs:723-724`.
- **Dejar de descartar en silencio el rectángulo en capa Viga** (ni viga, ni aviso, ni contador). — `src.Core/Services/DxfEstructuraMapper.cs:70-78`, comentario `:77` *(rectángulo en capa Viga/Eje: se ignora)*.
- **Subdividir polilíneas no rectangulares (ambientes en L):** hoy se cuentan y solo se advierte, descartando el ambiente entero. — `DxfEstructuraMapper.cs:57,89`; `PoligonoLosaMapper` rechaza todo lo que no sean 4 vértices ortogonales; `PLAN_CAD_V1.md:431`.
- **Heurística forma→columna + soporte de columnas en path de visión** (hoy círculo en capa ambigua ⇒ 0 columnas). — `DxfEstructuraMapper.cs:65,72`; `QwenAnalizador.cs:94-120` devuelve `Array.Empty`.
- **Corregir bounding box de arcos parciales** (usa círculo circunscrito completo → infla el encuadre). — `DxfImportService.cs:268-272`, comentario `:269`.

**Aceptación:** test `MapearPoligono` (interactivo) vs `GenerarDesdeDxfAsync` (batch) da misma Y y mismos `PosX/PosY`; un contorno en L produce ≥2 losas.

---

## F3 — Pieper-Martens nativo 21/21 subtipos · ⬜ pendiente · L · (← F0)

**Objetivo:** que el motor nativo calcule los **23 códigos** del catálogo (no 3), con degradación por-losa primero (gate) y sin mensajes engañosos. *(El reparto viga-columna por reacciones se movió a F4: necesita las reacciones del solver.)*

- **GATE — captura por-losa en el cálculo nativo:** hoy la 1ª losa no soportada **aborta el sistema entero**. Ejecutar PRIMERO para aislar cada subtipo. — `SistemaPieperMartensCalculator.cs:42-50`; `MomentosCalculator.cs:42` → `TablaPieperMartens.cs:73` lanza `NotSupportedException` que sube a `MainViewModel.cs:1472`; contrasta con `MotorFeaService.cs:304-310` que sí captura por-losa.
- **GATE — corregir mensaje engañoso de `TipoLosaValidoRule`** (declara 23 tipos soportados cuando procesa 3). — `src.Core/Validation/Rules/TipoLosaValidoRule.cs:44-50`.
- **Completar `CodigoASubtipo`:** los 20 códigos que hoy lanzan `NotSupportedException`. — `TablaPieperMartens.cs:76-79` solo `[40]='4'`; catálogo `Sistema.cs:583-644`; `TABLAS-PERDOMO.md:104-110`.
- **Activar los 20 subtipos de `TablasPerdomo.json`** cargados pero inalcanzables. — el JSON contiene 21 subtipos; solo `'4'` se alcanza.
- **Cablear descenso geométrico por área tributaria a la UI principal** (Bajada de Cargas y Editor de Columnas siguen equitativo). — `BajadaCargasViewModel.cs:158` (`RepartirEquitativo`) y `ColumnasEditorViewModel.cs:137`; la ruta correcta ya existe en `DescensoColumnas.cs:92` + `RepartoGeometrico.cs:168`, solo cableada en `Planta2DEditorView`.

**Aceptación:** test parametrizado de los 23 códigos sin `NotSupportedException` + fixtures validados vs `Losas.exe`; la UI usa descenso geométrico.

---

## F4 — Correctitud física del solver · ⬜ pendiente · XL · (← F0)

**Objetivo:** que una viga a gravedad genere **momento flector real** (peso propio + distribuidas vía fixed-end forces en core/contrato/CLI/IA), más continuidad real de paneles (path .NET FEA, independiente del solver Python y de F3) y reparto viga-columna por reacciones reales.

- **Nuevo tipo `CargaElemento` + vector de empotramiento perfecto por elemento** (hoy el core solo ensambla `CargaNodal`). — `motor_fea/core/solver.py:190-197` itera SOLO `modelo.cargas`; `modelo.py:108-124` `CargaNodal` única; diferido a F4 en `STATE.md`.
- **Peso propio en el path core/contrato/CLI/IA** (hoy solo viz lumped `fz=-m*g/2`, no genera momento de miembro). — `viz/resultados.py:27-41`; no está en `api/contrato.py:97-100` ni `casos.py`; `modelo.py:52` densidad existe pero el core nunca la consume.
- **Vector de carga consistente del elemento de placa ACM** (hoy losa lumped por área tributaria). — `core/losa_fem.py:66-73` *lumped en el GDL w*, no usa ∫N·q·dA.
- **Continuidad real de dos paneles adyacentes en aceros por FEA** (hoy corre solo la 1ª losa empotrada y aplica ese momento a TODOS los bordes). *Path .NET FEA, independiente del solver Python.* — `src.Core/Services/MotorFeaService.cs:352-372` (`losaRep = sistema.Losas[0]`).
- **Reparto viga-columna por reacciones reales** (hoy 50/50, mal para vigas continuas/asimétricas). *Movido de F3.* — `RepartoGeometrico.cs:158-193` (`mitad = carga.FuerzaTotal / 2.0`); comentario `:166`.

**Aceptación:** existe `CargaElemento` y el solver ensambla el vector de empotramiento por elemento; viga simplemente apoyada con q da `M_centro = wL²/8 ≠ 0`; el reparto asimétrico difiere de 50/50.

---

## F5 — Completitud física de normativa y diseño · ⬜ pendiente · L · (← F4)

**Objetivo:** cerrar los chequeos ACI/R-001 que el motor **reporta pero no verifica** (deflexión, deriva) y completar el diseño de elementos (cortante de columna, cortante detallado, torsión), apoyados en las cargas correctas de F4.

- **Chequeo de deflexiones/serviciabilidad ACI 24.2.2 (L/240, L/360):** hoy `w_central` solo se reporta. — no existe `verificar_deflexion`; `viz/resultados_losa.py:36-39` solo reporta w en mm.
- **Chequeo de deriva de entrepiso (story drift, R-001):** el módulo sismo solo calcula cortante basal. — `sismo.py:33-101` devuelve V, no deriva; `r001.py` sin límites de drift.
- **Inercia efectiva/agrietada Ie (24.2.3) + deflexiones a largo plazo λΔ (24.2.4)** (hoy solo inercia bruta). — `modelo.py:64-67` inercia bruta en `solver.py:113-142`.
- **`disenar_columna` por fuerzas: incluir cortante/estribos** (hoy solo P-M). — `diseno_elemento.py:54-60`, comentario `:57`.
- **Cortante de concreto de viga detallado 22.5.5.1 con ρ_w** (hoy `0.17·λ·√fc·bw·d` simplificado). — `normativa/aci318.py:83-85` *(path Python, no .NET)*.
- **Diseño a torsión ACI 22.7 + envolvente biaxial real** (hoy `_demanda` descarta T y usa max uniaxial). — `diseno_elemento.py:26-36`.

**Aceptación:** `verificar_deflexion` compara contra L/240 y L/360; existe chequeo de deriva; `disenar_columna` por fuerzas incluye cortante/estribos; cortante de viga usa ρ_w; existe Ie. Tests que fallan/pasan según el límite.

---

## F6 — IA con revisión humana · ⬜ pendiente · L · (← F2)

**Objetivo:** que la IA respete su config en runtime, valide/sanee su salida y **nunca cree elementos sin revisión humana**, apoyada en datos y geometría ya correctos.

- **Cargar `qwen.config.json` en runtime** (hoy `QwenConfig` hardcodeado; modelo/endpoint/timeout/temperatura/`entradasPermitidas` del JSON son inertes). — `MainViewModel.cs:1510,1595` `new QwenConfig()`; no existe `QwenConfig.Load`.
- **UI de revisión/confirmación antes de aplicar la propuesta IA** (hoy `Add` directo sin diálogo). — `MainViewModel.cs:1517-1549` y `:1606-1652` hacen `Losas.Add`/`Vigas.Add`/`Columnas.Add` directo; `docs/qwen-setup.md:53,60-61` exige revisión.
- **Validar/sanear la salida IA:** coords/dimensiones negativas, NaN o solapadas entran al modelo sin chequeo. — `QwenAnalizador.cs:107,111,122-124`; `MainViewModel.cs:1523-1524`; el prompt pide coords≥0 (`:35`) sin enforcement.
- **Visión IA (foto) que proponga columnas y ejes** (hoy siempre vacíos pese a docs/config). — `QwenAnalizador.cs:118-119` `Array.Empty`; `docs/qwen-setup.md:7` lo promete.
- **Soportar PDF como entrada** (hoy base64 a Ollama sin renderizar a imagen). — `qwen.config.json:9` incluye `.pdf`; `QwenAnalizador.AnalizarAsync:59-74`; file-picker `MainWindow.axaml.cs:327` solo png/jpg/webp/bmp.
- **Enforce `entradasPermitidas` en `AnalizarAsync`** (hoy acepta cualquier extensión). — `QwenAnalizador.cs:54-60` solo valida `File.Exists`.

**Aceptación:** cambiar el modelo en el JSON cambia el modelo usado; existe UI de revisión antes de aplicar; una propuesta con coord negativa es rechazada; visión propone columnas y ejes.

---

## F7 — Completar superficie de motor e IA · ⬜ pendiente · L · (← F5)

**Objetivo:** cerrar las brechas que dejan capacidades del motor **invisibles o desacopladas**: contrato JSON/IA incompleto, visor de losa demo, mampostería smeared, memoria sin gráficas.

- **Ampliar contrato JSON + capa IA con diseño por fuerzas/combos, sismo y modal** (hoy la IA expone solo 2 herramientas). — `motor_fea_ia/herramientas.py:58-69`; `motor_cliente.py:26-38`; `api/contrato.py` no expone `diseno_elemento`/`sismo`/`modal`.
- **Acoplar el heatmap de losa del visor al modelo cargado** (hoy placa demo fija 5×5). — `api/servidor.py:84-85` `/losa` sin args; `resultados_losa.py:17-19` geometría hardcodeada; `app.js:428-431`.
- **Mampostería por franjas tributarias por muro** (hoy smearing sobre toda el área). — `CargaUltimaCalculator.cs:69-74,92-98` qmap común sobre `areaTotal`.
- **Embeber los `DiagramaPng` (momentos/cortante) en la memoria `.docx`** (hoy solo texto/tablas + logo). — `MemoriaGenerator.cs` embebe solo logo (`:725,754,762,777`); `PlaceholderConstants.cs` sin placeholder de diagrama.
- **Cablear diafragma rígido al contrato/CLI/visor** (hoy `resolver_con_diafragma` solo lo usan tests). — `core/diafragma.py:86-87`; no se llama desde `api/contrato.py` ni `servidor.py`.

**Aceptación:** las herramientas IA listan diseño/sismo/modal (>2); `/losa` refleja la losa del JSON; un muro pesado no se diluye; la memoria embebe los diagramas.

---

## F8 — Empaquetado, firma y CI gateada · ⬜ pendiente · L · (← F0)

**Objetivo:** convertir la verdad de estado de F0 en un **guardrail de CI** y entregar binarios multiplataforma firmados, **sin tocar `Losas.exe`**.

- **Cablear `estado-real.sh --check` a un workflow de CI** (depende de que F0 ya produzca logs veraces). — `grep estado-real/--check` en `.github/` = vacío; `estado-real.sh:14-15,235` implementa `--check`.
- **Firmar los `.exe` (Authenticode) e instalador** (hoy single-file sin firmar ⇒ SmartScreen; sin MSI). — `release.yml:44-92` publica `.exe` crudos; `grep signtool/WiX/Inno/MSI` = 0.
- **Job de release multiplataforma Linux (AppImage/.deb/tar.gz)** (hoy `release.yml` solo win-x64). — `release.yml:18` windows-latest, `:46` `-r win-x64`; `ci-linux.yml:3-5` y README declaran Linux primaria.
- **Resolver `ci.yml` WPF obsoleto** tras el port a Avalonia (build con `/warnaserror` sobre la sln WPF vieja). — `ci.yml:17,29,32` restaura `LosasPlus.sln` (WPF) con `/warnaserror`; la verdad es `LosasPlus.Linux.sln`; los 3 warnings romperían `/warnaserror`.

**Aceptación:** un workflow invoca `estado-real.sh --check` y falla si el conteo diverge; `release.yml` firma los `.exe` e incluye instalador; existe job Linux además de win-x64; `ci.yml` WPF resuelto. **`Losas.exe` nunca se borra.**

---

## F9 — Escala BIM, multi-dominio y gemelo urbano · ⬜ pendiente · XL · (← F7)

**Objetivo:** habilitar las ambiciones declaradas de gran escala (BIM/IFC, obras de arte, ciudad) **reusando el motor de rigidez**, más la deuda MITC4 y el versionado normativo. *Fase tardía más liviana: la evidencia es aspiracional (docs de visión) salvo MITC4 y CDCRD que anclan a código/regulación concreta.*

- **Completar IFC 4.3** (import + geometría de elementos + round-trip) sobre el `IfcExporter.cs` **ya existente** en `src.Core/Interop`, reusando el patrón SAF de `src.Core/Services/SafExporter.cs` (no greenfield). — `IfcExporter.cs` ya exporta IFC 4.3 parcial (Project→Site→Building→Storey; elementos en incrementos posteriores); `PLAN_MAESTRO.md:247-249`; `docs/roadmap/VISION_ROADMAP.md:31,38-44`.
- **Obras de arte / multi-dominio** (puentes, muros de contención, tanques, geotecnia) reusando el motor de rigidez directa. — `docs/roadmap/VISION_ROADMAP.md:46-49` *(Fase L)*.
- **Escala ciudad / gemelo digital urbano** (PostGIS, CityGML→3D Tiles, CesiumJS, IncidenciasRD). — `PLAN_MAESTRO.md:250-252`; `docs/roadmap/VISION_ROADMAP.md:32-34,51-58` *(Fases M-N)*.
- **Versionado normativo CDCRD** (Resolución MIVHED 007-2026, ASCE/SEI 7-22) + DDM/EFM por coeficientes y regresión vs Excel. **Deuda con fecha dura: 2027-04-10.** — `PLAN_MAESTRO.md:266-270,245-246`.
- **Consolidar deuda de placa: elemento MITC4** / placas gruesas libre de shear-locking (hoy solo ACM Kirchhoff no-conforme). — `core/placa.py:13-14` *(MITC4 queda para una iteración futura)*; `PLAN_MAESTRO:212-213`.

**Aceptación:** IFC 4.3 con round-trip que preserva geometría; el motor de rigidez se reusa para ≥1 dominio de obra de arte; hay un camino de escala ciudad o se consolida la deuda MITC4 + versionado CDCRD.

---

## Notas de método

- **Verificación adversarial (3 lentes):** *grounding* confirmó cada `archivo:línea`; *orden/dependencias* corrigió el bloqueante F4→F3 (son independientes) y reubicó el reparto viga-columna a F4; *completitud* recuperó la subdivisión de polígonos en L (item antes omitido) → 36/36 distribuidos, 0 sin ubicar.
- **Cada fase merece su propio spec + plan** (como F0) antes de implementarse. Este documento es el **mapa**, no el plan de ejecución de cada fase.
- **Próximo paso natural:** cerrar F0 (GATE 1 + GATE 2 + commit C2), luego abrir F1 **o** F3/F4 en paralelo (no se bloquean entre sí).
