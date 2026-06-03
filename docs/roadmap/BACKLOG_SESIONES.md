# Backlog de sesiones — post-consolidación

> Rama de trabajo: `engine/columnas-diseno` (carpeta única tras consolidar).
> Estado: 869/869 verde, build 0/0, sin push. Hecho hasta hoy: Columnas (P-M +
> UI + plot), Aceros export, editor 2D (snapping), fix 3D en vivo, cutover
> `Losas.exe` aditivo (losas + bordes/aceros adicionales), navegación por
> categorías (B) + dibujo de muros 2D (C). Orden pedido por el usuario: **D → E → F**.
>
> **Recordar la visión** (`docs/roadmap/VISION_ROADMAP.md`): suite estructural
> completa, no solo losas — vigas, columnas, zapatas, BIM, escala urbana.

## ⛔ Restricción permanente (hasta nuevo aviso)
**NO eliminar `Losas.exe`.** El cutover por motor FEM queda **aditivo** (menú Engine
→ "Calcular losas con el motor"); `Losas.exe` se mantiene como **respaldo**. No hacerlo
default ni borrar el flujo `.DL`/`.TXT` sin que el usuario lo pida explícitamente.

## D — Carga última directa  ✅ HECHO (headless, 3 commits, 874✓)
**Hallazgo:** el pipeline de carga última ya existía completo en
`CalculoEngine` (`ComputeQmamp→ComputeQmap→ComputeQd→ComputeQl→ComputeQu`, 1.2D+1.6L)
y `Losa.Carga` YA es Wu (lo consume la bajada/zapatas/columnas). La brecha real
era **puentear la geometría de muros dibujados** al Qmamp (antes: metros lineales
tecleados a mano).
- **Entregado:** `src.Core/Transmision/CargaUltimaCalculator.cs` (puro, TDD):
  - `PesoMamposteria(muros/sistema)` → ton (Σ 1.8·L·e·h, misma convención que ComputeQmamp).
  - `Calcular(sistema, cargas, hEq, area)` → `CargaUltimaResultado{Qmamp,Qmap,Qd,Ql,Qu}`.
  - `AplicarCargaUltima(sistema, cargas)` → escribe Wu en cada `Losa.Carga` (mampostería
    repartida sobre el área total = qmap común; cada losa aporta su espesor). Acción
    **explícita y aditiva** — NO reemplaza Losas.exe.
- **Pendiente D (UI mínima → hand-off Antigravity):** botón/menú «Calcular Wu desde
  geometría» que llame `AplicarCargaUltima` sobre los sistemas del edificio activo, y
  mostrar el desglose `CargaUltimaResultado`. Patrón a copiar: el cutover del motor
  (menú Engine, `CalcularConMotorAsync`).
- **Pendiente D (validación usuario):** comparar Wu directo vs Losas.exe en un caso real.

## E — Vigas: más énfasis (diagramas + secciones)  🔨 EN CURSO (headless 2 commits, 876✓)
**Root cause confirmado:** la maquinaria de diagramas FUNCIONA — `VigaEditorView` tiene
3 `<oxy:PlotView>` bindeados a `ModeloViga/Esfuerzos/Deflexion`, la navegación (B) cablea
bien (`ModoSidebar.Vigas` + `IrAModoCommand` + `EnumToBoolConverter`). El problema es que
**`_nivel.Vigas` está vacío**: nada materializa vigas desde las losas (sólo se crean a mano
en `NuevaViga`/`PlantaCanvas`/restore). Sin viga activa → PlotViews vacíos = «no muestran
diagramas». Y el **dibujo de sección** (b×h + armado) sí falta de verdad en la View.
- **Entregado (puente headless, Claude):** `src.Core/Vigas/GeneradorVigas.cs` (puro, TDD):
  - `VigaSimplementeApoyada(longitud, w, caso)` → 1 tramo, 2 apoyos fijos, carga distribuida.
  - `VigasDeLosa(losa, caso)` → 4 vigas cargadas (2 corto + 2 largo) vía `RepartoCargaLosa`,
    ton/m→kN/m (×9.80665). Aproximación: triangular/trapezoidal → uniforme equivalente.
- **Pendiente E-3 (headless, Claude — PRÓXIMO PASO DEL LOOP):** `MaterializarVigas(Nivel)`
  que recorra las losas del nivel, genere las vigas y las **agregue a `nivel.Vigas`**
  (limpiando las previamente generadas), detrás de un comando/menú explícito → así el editor
  se llena y aparecen los diagramas. TDD.
- **Pendiente E (pixeles → Antigravity):** dibujar la **sección transversal** (b×h + armado)
  en `VigaEditorView`, y dar más énfasis visual a los 3 diagramas; botón «Generar vigas del
  nivel».
- Lane mixto: cálculo/materialización (Claude); sección/plot visual (Antigravity).

## F — Columnas  ✅ COMPLETO HEADLESS (900/900 verde)
- **F-2:** `DescensoColumnas.PuDemandaKN(cargaEnBase, nCol)` (puro) + `ColumnasEditorViewModel.TomarPuDelDescenso()`/`Command` → cierra losa→bajada→columna (Pu deja de teclearse). Falta sólo el **botón** (hand-off `HANDOFF_ANTIGRAVITY_D_E_UI.md` tarea F-2-UI → Antigravity).
- **F-3 (esbeltez §6.2.5):** `RadioGiroRectangular`, `RelacionEsbeltez`, `LimiteEsbeltezArriostrado`, `EsEsbeltaArriostrada`.
- **F-3b (magnificación §6.6.4):** `ModuloElasticidadConcreto`, `InerciaBrutaRectangular`, `RigidezEI`, `CargaCriticaPandeo`, `FactorMagnificacion δ`.
- **Pendiente (UI → Antigravity):** mostrar aceros/estribos (ya calculados), esbeltez y δ en la pestaña Columnas; botón «Tomar Pu del descenso». Requiere campos Lu/k (inputs UI).
- **Validación usuario:** Pu/diseño vs un caso conocido.

### Detalle histórico (investigación previa de F)
**Hallazgo:** a diferencia de D/E, F **no tiene una brecha grande de cálculo puro**.
`DescensoColumnas.RepartirEquitativo(columnas, cargaEnBase, presionAdmisible)` ya devuelve
`CargaColumna{Columna, CargaAxial(ton), LadoZapata}` por columna; `ColumnasEditorViewModel`
ya tiene el plot P-M y el punto de demanda, pero el `Pu` (`PuKN`, kN) se **teclea a mano**.
El trabajo es **puente fino + cableado**, no cálculo nuevo:
- **F-1 (headless, Claude — PRÓXIMO):** un helper puro que convierta el axial descendido
  `CargaColumna.CargaAxial` (ton) → `Pu` (kN, ×9.80665), y opcionalmente corra el
  `ChequearDemanda`/`DisenarColumna` existente. Cierra losa→`AplicarCargaUltima`(D)→bajada→
  `DescensoColumnas`→`Pu` de la columna. TDD (aunque sea fino, dejarlo testeado).
- **F-2 (VM/pixeles → Antigravity):** alimentar `PuKN` desde el descenso en vez de manual;
  mostrar **aceros** (barras long. + estribos, ya calculados por `ColumnaDisenador`) y
  **características de diseño**. Botón «Tomar Pu del descenso».
- **Característica de diseño avanzada (fuera de alcance inmediato):** carga tributaria
  por columna real requiere topología columna→losas que el modelo aún no tiene; el descenso
  equitativo es la aproximación actual (documentarlo, no bloquear F por esto).

## G — Vigas continuas + Ejes estructurales + Elevación  ✅ COMPLETO HEADLESS (914/914)
Plan acordado B→A→C + ejes + elevación, todo puro/TDD en `src.Core`:
- **Vigas continuas:** `GeneradorVigas.VigaContinua(luces,cargas)` (B), `VigaContinuaDeLosas(losas)` (A),
  `VigaContinuaDelEje(eje, losas, tol)` (C — capstone: geometría→topología→viga continua real con
  momentos negativos sobre apoyos interiores). Las analiza `VigaContinuaEngine` (ya existía).
- **Ejes/rejillas:** `EjeEstructural` (`DistanciaA`/`EstaEnSeccion`), `Edificio.Ejes`, y
  `SeccionPorEje.Columnas/Losas` (selector para "ver secciones del 3D").
- **Elevación:** `Sistema.Elevacion` (alias aditivo de `CotaMetros`).
- **Pendiente (UI → Antigravity):** `HANDOFF_ANTIGRAVITY_EJES_VIGAS_CONTINUAS.md` — dibujar rejilla,
  vista de sección, botón «Generar viga continua del eje», editar elevación.
- **Aproximaciones documentadas:** viga en dirección Lx, carga tributaria de un lado, apoyos fijos.

## H — Zapatas aisladas (fundaciones)  ✅ COMPLETO HEADLESS (929/929)
Diseño completo en `src.Core/Calculo/ZapataDisenador.cs` (ACI 318-19, SI N/mm/MPa, puro/TDD):
- **Presión de contacto:** `PresionContactoUltima` (q_u = Pu/(B·L)).
- **Punzonamiento §22.6:** `PerimetroCriticoPunzonamiento`, `CortantePunzonamiento`,
  `ResistenciaPunzonamiento` (φVc = φ·min de las 3 fórmulas), `ChequeoPunzonamiento`.
- **Cortante unidireccional §22.5:** `VoladizoZapata`, `CortanteUnidireccional`,
  `ResistenciaCortanteUnidireccional`, `ChequeoCortanteUnidireccional`.
- **Flexión §13.3:** `MomentoFlexionZapata`, `AceroFlexionZapata` (Whitney + As mín retracción).
- **Capstone:** `DisenarZapata(...)` → `DisenoZapata{QuMPa, Punzonamiento, Cortante, MuNmm, Acero, Cumple}`.
- **Exporter:** `ZapataDisenoExporter.ToCsv/ExportCsv` (`src.Core/Services`).
- **Supuestos documentados:** columna interior, zapata cuadrada concéntrica sin momento, λ=1.
- **Pendiente (UI → Antigravity):** mostrar el diseño en la pestaña de bajada de cargas/zapatas
  (alimentar `DisenarZapata` con el Pu del descenso `DescensoColumnas` y la geometría). Ver
  `docs/handoff/HANDOFF_ANTIGRAVITY_ZAPATAS.md`.

## Cómo seguir (loop)
1. Sesión **D** (carga última) — motor headless + UI mínima.
2. Aplicar **E** (vigas) y **F** (columnas).
3. Mantener TDD, build+test verde por paso, commits sin push, no tocar `avalonia-linux`/`main`.
4. UI/pixeles → hand-offs a Antigravity; motor/cálculo → Claude.
