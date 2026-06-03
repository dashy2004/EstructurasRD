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

## F — Columnas: aceros + carga transmitida + características de diseño
Surface en la pestaña Columnas (que ya tiene el plot P-M):
- **Aceros** (ya calculado: P-M, estribos) — asegurarse que se muestren bien.
- **Carga transmitida de las losas** → usar `DescensoColumnas` para el axial `Pu` que baja
  de las losas/niveles a cada columna, y **alimentar el `Pu` del chequeo P-M** (hoy el Pu
  es manual). Cerrar el lazo: losa → descenso → columna.
- Otras **características de diseño** de la columna (esbeltez, longitud efectiva, etc. — evaluar alcance).

## Cómo seguir (loop)
1. Sesión **D** (carga última) — motor headless + UI mínima.
2. Aplicar **E** (vigas) y **F** (columnas).
3. Mantener TDD, build+test verde por paso, commits sin push, no tocar `avalonia-linux`/`main`.
4. UI/pixeles → hand-offs a Antigravity; motor/cálculo → Claude.
