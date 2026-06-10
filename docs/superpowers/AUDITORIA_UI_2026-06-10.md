# Auditoría UI/UX + Re-arquitectura incremental · 2026-06-10

> Diagnóstico de los 12 síntomas reportados por el usuario, con evidencia `archivo:línea`
> (5 agentes de investigación en paralelo + verificación directa). Base para las fases UI1–UI4.
> Conteos vivos: ver `/STATE.md`.

## 0. Arreglado YA (commits en `engine/f2-cad-deterministico`)

| Síntoma | Causa raíz | Fix | Commit |
|---|---|---|---|
| Apoyos no siguen a la viga al estirarla | `PlantaCanvas.cs:389/397` recalcula `Tramos[0].Longitud` pero `ApoyoViga.CoordenadaX` es absoluta y quedaba fija | `Viga.ReescalarApoyos()` (posición relativa conservada) + cableado en ambos endpoints | `7fc2b04` |
| Toolbar de Planta 2D se rompe con la resolución | `StackPanel` horizontal fijo (`Planta2DEditorView.axaml:29`) | `WrapPanel` (segunda fila en pantallas chicas) | `7fc2b04` |

## 1. Diagramas de vigas "en blanco" (cortante/deflexión) — DIAGNÓSTICO

- Los tests de píxeles (`DiagramaPngTests`) **prueban que el pipeline PNG funciona headless**: el PNG contiene las curvas. Si en la app se ven en blanco, los sospechosos son **runtime**:
  1. **Build stale**: el pipeline PNG de esfuerzos/deflexión entró el 2026-06-09 y `VigaPng`/`SeccionPng` HOY — verificar que se corre el build nuevo (`dotnet run` sobre `LosasPlus.Linux.sln`).
  2. `Converters.cs:54-62` (`BytesToBitmap`): `using var ms` + `new Bitmap(ms)` — en Avalonia 11 el Bitmap decodifica al construir (debería ser seguro), pero es el único punto runtime no cubierto por tests. **Verificación pendiente: correr la app y mirar.**
  3. Viga inestable ⇒ PNG en blanco CON aviso (`VigaEditorViewModel.cs:558-561`) — comportamiento correcto, puede confundir.
- **Sección transversal "muy pequeña"**: los ejes del modelo reservan márgenes enormes (`VigaEditorViewModel.cs:796-797`: X −40%…+140%, Y −28%…+132%) para cotas/resumen como TextAnnotations fuera de la sección. Ajustar requiere mover las cotas, no solo encoger ejes (si no, se recortan). → UI3.

## 2. Hover / Export de diagramas → **EXPORT XLSX primero** (UI3)

- Datos crudos ya existen: `ResultadoViga` (`PuntoDiagrama{X, Cortante, Momento, Deflexion}`, `EnvolventeViga.Puntos`, `Reacciones`) y `DisenoColumna.Diagrama` (`PuntoPM{C, Pn, Mn, Phi}`, ~40 puntos; ya hay `ColumnaDisenoExporter.ToCsv`).
- Patrón XLSX consolidado y testeado para reutilizar: `src.Core/Services/XlsxExporter.cs` y `AcerosLosaExporter.cs:140-210` (ClosedXML, headers, shading, freeze).
- Propuesta: botón "Exportar a Excel" en el toolbar del editor de vigas (hojas: Esfuerzos V-M, Deflexión, Reacciones) y bajo el P-M de columnas (hojas: Diagrama P-M con c/Pn/Mn/φ/φPn/φMn, Resumen de diseño). **Esfuerzo S-M, alto valor.**
- Hover (tooltip con valores): factible vía overlay sobre el `<Image>` interpolando `ResultadoViga` por X (no requiere OxyPlot interactivo). **Esfuerzo M — después del export.**

## 3. Plano CAD ↔ Planta 2D — desincronización y fusión (UI1)

- **Decisión del usuario: eliminar Plano CAD y absorber sus capacidades en Planta 2D.**
- Causa de la desincronización: dualidad de coordenadas — CadEditor escribe `Losa.PosX/PosY` (lienzo CAD, Y-down, `CadEditorViewModel.cs:723-724`) mientras PlantaCanvas escribe/lee `CoordenadaX/Y` (`PlantaCanvas.cs:260-261`, `Planta2DEditorView.axaml.cs:122`); además `LayoutSolver` puede cachear placements. Dos sistemas de verdad para la misma geometría.
- Gaps de Planta 2D para retirar CadView: (1) **calibración interactiva de PDF** (`CadEditorViewModel.cs:585-664`), (2) **MapearPoligono** (click sobre polilínea DXF → losa, `:847-887`), (3) **leyenda "suma de colores" de muros** (`:529`). Underlay DXF/PDF, muros, Auto-Conectar, snap, zoom/pan ya existen en PlantaCanvas.
- Esfuerzo estimado de la fusión completa: 3-4 días con tests; riesgo principal = caché del LayoutSolver.

## 4. Edición interactiva en Planta 2D (UI1)

- **Losa sin resize**: `PlantaCanvas.cs:374-378` solo mueve `CoordenadaX/Y`; no hay handles (CadCanvasHost sí los tiene — `:956-970` — reciclar ese patrón). Esfuerzo M.
- **Columnas "elevan niveles"**: `Nivel.Cota` y `Columna.Altura` son independientes (correcto en el modelo); el síntoma está en el **visor 3D/escena** (revisar `EscenaEdificio*`: cómo calcula la Z de losas/columnas cuando `Altura` ≠ separación de cotas). Requiere decisión de semántica: ¿la columna de 6 m en nivel de 3 m debe atravesar 2 niveles? → definir en UI2.

## 5. Modelo de datos: Nivel vs Sistema (UI2 — la fase estructural)

- Jerarquía actual: `Edificio → Niveles → {Vigas, Columnas, Sistemas[ → Losas, BordesX/Y, SalidaPerdomo]}`. En la práctica se usa **un** Sistema por nivel (`Sistemas[0]` hardcodeado en 6 sitios de `src/`).
- Propuesta incremental (sin romper proyectos guardados):
  1. Fachada: `Nivel.Losas` ⇒ delega a `Sistemas[0]` (creándolo si falta); la UI nueva habla solo con `Nivel`.
  2. Migrar consumidores de `Sistemas[0]` a la fachada.
  3. Serialización: mantener el shape JSON actual (el Sistema queda como detalle interno); deprecación visible solo en código.
- **Carga viva**: `Losa.Carga` es un escalar único (`Sistema.cs:264-268`); D/L viven a nivel de proyecto (`CargasGlobales.cs`: tabla qd por espesor + CV por uso + combinaciones 1.2D+1.6L). Para CV/CM por losa: overrides anulables (`CargaMuerta?`/`CargaViva?`) + UI + lectura en `CalculoEngine`. Esfuerzo M.

## 6. Identidad, versión, configuración (UI4)

- **Versión en desacuerdo triple**: la UI muestra `v0.5.0` hardcodeado (`MainViewModel.cs:191`), los csproj dicen `0.1.0`, y los releases reales van por v1.4.0/v1.5.0-rc1. Fix: una sola fuente (`<Version>` del csproj → `Assembly.GetName().Version` o `AssemblyInformationalVersion`) y eliminar el literal.
- **"Perdomo" visible**: barra de estado "motor: F. Perdomo" (`MainWindow.axaml:176`) + tooltip (`:271`). La atribución legal/créditos (`:992-1034`) se queda en "Acerca de"; del shell principal se retira. Ya existe `MemoriaPlus.Common.Branding.Producto` para centralizar.
- **Configuración**: `qwen.config.json` no se carga en runtime (ya rastreado → F6); la página de opciones es mínima. UI4 define el inventario de settings (tema, unidades, rutas, IA, tolerancias de snap, factores de combinación visibles).

## Fases propuestas (orden por apalancamiento)

| Fase | Contenido | Tamaño |
|---|---|---|
| **UI1 — Un solo lienzo** | Eliminar CadView (calibración PDF + MapearPoligono + leyenda muros → Planta 2D), resize de losas con handles, una sola fuente de coordenadas | L |
| **UI2 — Modelo unificado** | Fachada Nivel⊕Sistema, semántica columna/cota en 3D, CV/CM por losa | L |
| **UI3 — Diagramas vivos** | Export XLSX (vigas + columnas), verificación runtime de los PNG, sección a escala (mover cotas), hover | M |
| **UI4 — Identidad/config** | Versión única, branding, página de configuración + qwen.config runtime (absorbe F6) | M |

Cada fase con su spec+plan (plantilla F0) y TDD, como F1–F3. Pendientes previos que siguen vivos: F2b (heurística columna + visión), F4 (motor: cargas distribuidas + peso propio).
