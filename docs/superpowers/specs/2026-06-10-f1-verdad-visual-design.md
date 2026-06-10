# F1 — Gobernar la verdad visual (design spec)

- **Fecha:** 2026-06-10
- **Estado:** Aprobado (diseño) → pendiente de plan de implementación
- **Rama de trabajo propuesta:** `engine/f1-verdad-visual` (off `engine/f0-verdad-de-estado`, F0 cerrada)
- **Fase del roadmap:** F1 de `docs/superpowers/roadmap-fases-F0-F9.md:56-65` (← depende de F0, cumplida)
- **Baseline:** `STATE.md` — .NET 1106 passed / Python 208 passed / build 0 err

---

## 1. Contexto y problema

El control `oxy:PlotView` de OxyPlot.Avalonia 2.1.0-Avalonia11 tiene un **bug de render que no pinta ciertas series** aunque el `PlotModel` y sus datos sean correctos — lo confirmó la paridad con el `SvgExporter` de OxyPlot-core (documentado en `src/Rendering/DiagramaPng.cs:14-20` y `src/ViewModels/Vigas/VigaEditorViewModel.cs:211-214`).

F0 ya preservó el **fix probado**: renderizar el MISMO `PlotModel` a PNG con `DiagramaPng.Render` (`src/Rendering/DiagramaPng.cs:31-42`, exportador OxyPlot-core sobre ImageSharp, puro .NET, headless) y mostrarlo en un `<Image>` con el converter `BytesToBitmap` (`src/App.axaml:122`, clase en `src/Converters.cs:52`). Hoy ese fix cubre solo 2 de los 6 diagramas: `EsfuerzosPng`/`DeflexionPng` (`src/Views/Vigas/VigaEditorView.axaml:294,300`; VM `VigaEditorViewModel.cs:215-233,573-574`), con test de píxeles verde (`tests/LosasPlus.Tests/DiagramaPngTests.cs:48-66`).

Quedan **4 gráficas sobre el control con el bug**, documentadas en blanco en vivo por `STATE.md:23`:

| Gráfica | Vista (`oxy:PlotView`) | Modelo (VM) | Contenido |
|---|---|---|---|
| Modelo físico de la viga | `src/Views/Vigas/VigaEditorView.axaml:287` | `ModeloViga` (`VigaEditorViewModel.cs:200`, construye `:592-668`) | `LineSeries` eje (`:614`), `ScatterSeries` apoyos (`:627`), `ArrowAnnotation`/`RectangleAnnotation` cargas (`:647,657`) |
| Sección transversal de viga | `VigaEditorView.axaml:257` | `ModeloSeccion` (`VigaEditorViewModel.cs:209`, construye `:752-829`) | `RectangleAnnotation` concreto/estribo (`:775,792`) **+ `ScatterSeries` barras** (`:816-817` → `AgregarBarrasSeccion :854-868`) |
| Diagrama de interacción P-M | `src/Views/ColumnasEditorView.axaml:209` | `ModeloInteraccion` (`ColumnasEditorViewModel.cs:214`, construye `ConstruirPlot :316-397`) | 2 `LineSeries` (curva `:355`, tope `:369`) + `ScatterSeries` demanda (`:384`) |
| Sección transversal de columna | `ColumnasEditorView.axaml:204` | `ModeloSeccionColumna` (`ColumnasEditorViewModel.cs:220`, construye `:261-299`) | `RectangleAnnotation` concreto/estribo (`:269,278`) **+ `ScatterSeries` barras** (`:285-291`) |

> Nota de cita: el roadmap (`roadmap-fases-F0-F9.md:60`) cita `VigaEditorViewModel.cs:614` — verificado: es la `LineSeries` del eje de viga. `:62` cita `VigaEditorViewModel.cs:775-799` — verificado: son las `RectangleAnnotation` de la sección, pero la sección **también** tiene `ScatterSeries` (`:816-817,854-868`), dato decisivo para §2.2.

Además, el editor de planta sigue **partido en dos lienzos**: `EditorUnificadoView.axaml:12-19` mantiene un `TabControl` con 2 `TabItem` (Estructura = `Planta2DEditorView`, CAD = `CadView`). Del plan previo `docs/plan-unificar-cad-planta2d.md` los pasos 2-3 **ya están hechos**: el underlay PDF/DXF vive en `PlantaCanvas.cs:61-101` (props `PdfRef`/`FondoPdf`/`OpacidadPdf`/`PlanoDxf`) y ya está bindeado en `Planta2DEditorView.axaml:74-76`; la herramienta Muro está en `PlantaCanvas.cs:319-334`. Faltan el paso 4 (cablear Auto-Conectar, hoy solo en `CadView.axaml:145-147` → `CadEditor.AutoAlinearSistemasCommand`) y el paso 5 (retirar la pestaña CAD).

## 2. Decisiones tomadas

### 2.1 Extender el patrón PNG tal cual está probado

Se replica EXACTAMENTE el patrón existente (prop `byte[]?` + `DiagramaPng.Render` + `<Image ... Converter={StaticResource BytesToBitmap}>` + test de píxeles con `ContieneColor`): `VigaPng` para `ModeloViga` e `InteraccionPng` para `ModeloInteraccion`. Los `PlotModel` **no cambian** (mismas series, mismos colores): solo cambia el camino de render. Las props `PlotModel` se conservan (los tests existentes las usan, p. ej. `ColumnasEditorDisenoTests.cs:63-72`).

### 2.2 `ModeloSeccion` / `ModeloSeccionColumna`: **migran al patrón PNG** (no quedan en `oxy:PlotView`)

El roadmap pedía *decidir* el fallback. Investigación (verificada en código, no se pudo verificar en GUI porque el repo no referencia `Avalonia.Headless` en `tests/LosasPlus.Tests`):

1. El bug conocido es **de series** (`DiagramaPng.cs:14-20`: no pinta cortante/deflexión; `VigaEditorViewModel.cs:212`: "sí momento" — es selectivo e impredecible por serie).
2. Pero las dos secciones **no son solo Annotations**: ambas dibujan las barras de refuerzo con `ScatterSeries` (`VigaEditorViewModel.cs:854-868` y `ColumnasEditorViewModel.cs:285-291`) — exactamente la clase de objeto afectada por el bug, y el contenido ingenieril clave del dibujo (el armado).
3. `STATE.md:23` (fuente de verdad) las reporta **en blanco en vivo**, junto a las otras dos.
4. Migrarlas cuesta lo mismo que las otras (patrón ya probado) y deja **0 `oxy:PlotView`** en `VigaEditorView`/`ColumnasEditorView`: la clase de bug entera queda eliminada de ambos editores y TODO diagrama pasa a ser pixel-testeable headless.

**Decisión:** migrar las 4 → `VigaPng`, `SeccionPng`, `InteraccionPng`, `SeccionColumnaPng`. Tras esto, el `xmlns:oxy` de ambas vistas (`VigaEditorView.axaml:3`, `ColumnasEditorView.axaml:3`) queda sin uso y se retira.

### 2.3 Sin cambios de estilo/color en los `PlotModel`

Los modelos conservan `Background = OxyColors.Transparent` (`CrearModeloBase`, `VigaEditorViewModel.cs:891-899`; `ColumnasEditorViewModel.cs:264`) y sus colores actuales: es el mismo camino que ya funciona para `EsfuerzosPng`/`DeflexionPng` (su test de píxeles pasa con fondo transparente). La legibilidad por tema (`BgInput` = `#FFFFFF` claro / `#2D2D30` oscuro, `src/Resources/ThemeLight.axaml:7` / `ThemeDark.axaml:7`) se valida en el pase visual manual del cierre; ajustar paletas sería cambio de comportamiento fuera de alcance.

### 2.4 Lienzo unificado: este spec **absorbe** los pasos 4-5 del plan previo

`docs/plan-unificar-cad-planta2d.md` queda como referencia histórica (recibe un puntero al tope); sus pasos 4-5 se ejecutan aquí:

- **Paso 4 — cablear Auto-Conectar en la base:** botón en la barra de acciones de `Planta2DEditorView.axaml:45-52` bindeado a `CadEditor.AutoAlinearSistemasCommand` (mismo binding que `CadView.axaml:145-147`; el DataContext de ambas vistas es `MainViewModel`, que expone `CadEditor`). Se añaden también los botones `Importar DXF…`/`Importar PDF…` (`CadEditor.ImportarDxfCommand`/`ImportarPdfCommand`, hoy en `CadView.axaml:31-36`): sin ellos, importar el plano que el underlay ya dibuja (`Planta2DEditorView.axaml:74-76`) obligaría a cambiar de modo — quedaría "medio unificado".
- **Paso 5 — retirar la pestaña CAD:** `EditorUnificadoView.axaml` deja de tener `TabControl` y hostea `Planta2DEditorView` directo. **No se pierde funcionalidad:** el modo `PlanoCad` del shell sigue hosteando el `CadView` completo (canvas CAD + calibrar PDF + ajuste espacial) inline en `MainWindow.axaml:875-878` (modo excluido del router `CurrentView`, `MainViewModel.cs:110-113`); el modo `Planta2D` es el que crea `EditorUnificadoView` (`MainViewModel.cs:133-135`). `CadView`/`CadCanvasHost` **no se borran** (reversibilidad, igual que el paso 1 del plan previo).
- **Calibrar PDF queda fuera de F1:** requiere los modos de interacción de `CadCanvasHost` (`CadView.axaml:200-219,427-429` + code-behind); sigue disponible en el modo PlanoCad y se documenta como residuo.

## 3. Objetivo y no-objetivos

**Objetivo:** tras F1, ningún diagrama de Vigas/Columnas depende del renderer con bug (0 `oxy:PlotView` en esas vistas, 4 props PNG nuevas con tests de píxeles verdes) y el editor de planta es un solo lienzo (0 `TabItem` en `EditorUnificadoView.axaml`) con Auto-Conectar e import DXF/PDF cableados en la base.

**No-objetivos (FUERA de F1):**
- Tocar el pipeline CAD/DXF batch (espejado Y, anclas, capa Viga…) → **F2** (`roadmap-fases-F0-F9.md:69-80`).
- Migrar/calibrar PDF dentro de `PlantaCanvas` → queda en modo PlanoCad.
- Borrar `CadView`/`CadCanvasHost` o cualquier vista vieja.
- Cambios de estilo/colores de los `PlotModel` o del motor de cálculo.
- **NUNCA tocar `Losas.exe` ni su import** (restricción permanente del roadmap).

## 4. Diseño detallado

### 4.1 `VigaPng` y `SeccionPng` (`VigaEditorViewModel` + `VigaEditorView`)

- **VM (`src/ViewModels/Vigas/VigaEditorViewModel.cs`):**
  - Constantes junto a las existentes (`:215-217`): `PngAltoViga = 320`, `PngAnchoSeccion = 600`, `PngAltoSeccion = 640` (la sección es retrato: ejes ~0.54×0.80 m, `:771-772`).
  - Props `VigaPng` y `SeccionPng` (`byte[]?` con `OnPropertyChanged`, idénticas a `EsfuerzosPng :219-225`).
  - `ConstruirSeries()` (`:566-575`): tras las 2 líneas de render existentes (`:573-574`), añadir `VigaPng = DiagramaPng.Render(ModeloViga, PngAncho, PngAltoViga);` y `SeccionPng = DiagramaPng.Render(ModeloSeccion, PngAnchoSeccion, PngAltoSeccion);`.
  - Setter de `TramoSeleccionado` (`:135-149`): tras `ConstruirModeloSeccion();` (`:147`), re-renderizar `SeccionPng` (la sección cambia al seleccionar otro tramo sin recálculo). Los early-returns de `ConstruirModeloSeccion` (`:760-768,789`) dejan el modelo limpio → el PNG resultante es un plot vacío, igual que mostraba el PlotView.
  - `LimpiarDiagramas()` (`:577-590`): `VigaPng = null; SeccionPng = null;` junto a `:588-589`.
- **Vista (`src/Views/Vigas/VigaEditorView.axaml`):** reemplazar los 2 `oxy:PlotView` (`:257` y `:287`) por `<Image Source="{Binding XxxPng, Converter={StaticResource BytesToBitmap}}" Stretch="Uniform" .../>` (patrón de `:294,300`). Al quedar 0 usos, retirar `xmlns:oxy` (`:3`).

### 4.2 `InteraccionPng` y `SeccionColumnaPng` (`ColumnasEditorViewModel` + `ColumnasEditorView`)

- **VM (`src/ViewModels/ColumnasEditorViewModel.cs`):**
  - `using LosasPlus.Rendering;` + constantes `PngAnchoInteraccion = 900`, `PngAltoInteraccion = 600`, `PngAnchoSeccionCol = 600`, `PngAltoSeccionCol = 440` (la vista muestra los Border a 300/220 px, `ColumnasEditorView.axaml:203,208`; se exporta a ~2× para nitidez).
  - Props `InteraccionPng` y `SeccionColumnaPng` (`byte[]? { get; private set; }`, estilo del VM `:214,220`).
  - En `RecalcularDiseno()` tras `ConstruirPlot();` (`:247`): asignar ambas con `DiagramaPng.Render(...)` y notificar (`OnPropertyChanged(nameof(...))` junto a `:250,252`). `DiagramaPng.Render(null, …)` devuelve `null` (`DiagramaPng.cs:33`), así que la rama sin selección/armado inválido (`:229-233,318-322`) deja ambas en `null` sin código extra.
- **Vista (`src/Views/ColumnasEditorView.axaml`):** reemplazar los 2 `oxy:PlotView` (`:204` y `:209`) por `<Image>` con el mismo converter (los `Border Height="220"/"300"` se conservan). La vista usa `x:CompileBindings="True"` con `x:DataType="vm:ColumnasEditorViewModel"` (`:7-8`): las props `byte[]?` compilan igual. Retirar `xmlns:oxy` (`:3`).

### 4.3 Tests de píxeles (TDD: test primero, rojo, implementar, verde)

Patrón de `DiagramaPngTests.cs`: helper `ContieneColor(png, r, g, b, tol=24)` (`:33-46`) + fixture `VigaResoluble()` (`:21-30`, tramo con `Base=0.30`/`Peralte=0.50` por defecto, `src.Core/Vigas/TramoViga.cs:23-26`) / `VmConColumna()` (`ColumnasEditorDisenoTests.cs:17-26`, ctor `ColumnasEditorViewModel(Func<Edificio?>, Func<Nivel?>)` `:35`).

| Test | Afirma píxel (RGB) | Fuente del color |
|---|---|---|
| `VigaPng` dibuja eje + carga | (51,58,69) eje **y** (181,138,0) banda de carga | `ColorViga`/`ColorCarga`, `VigaEditorViewModel.cs:43,47` — cubre serie **y** annotation |
| `SeccionPng` dibuja estribo + barras | (139,0,0) DarkRed **y** (0,0,139) DarkBlue | estribo `:797`, barras `:860` |
| `InteraccionPng` dibuja curva P-M | (59,130,246) | `#3B82F6`, `ColumnasEditorViewModel.cs:358` |
| `InteraccionPng` sin selección | `null` | rama `:318-322` |
| `SeccionColumnaPng` dibuja estribo + barras | (139,0,0) **y** (0,0,139) | `:281,288` |

Los de viga se agregan a `DiagramaPngTests.cs`; los de columnas en archivo nuevo `tests/LosasPlus.Tests/ColumnasEditorPngTests.cs` (duplica el helper `ContieneColor`, que es privado del otro archivo — 14 líneas; extraerlo a un helper compartido es refactor opcional fuera de alcance).

### 4.4 Lienzo unificado (XAML-only, sin lógica nueva)

- **`src/Views/Planta2DEditorView.axaml`:** en el StackPanel de acciones (`:45-52`), antes de `BtnRecalcular`, añadir 3 botones bindeados a comandos EXISTENTES de `CadEditor` (la vista es `x:CompileBindings="False"` `:5`): `ImportarDxfCommand`, `ImportarPdfCommand`, `AutoAlinearSistemasCommand` (con los mismos ToolTips de `CadView.axaml:31-36,145-147`).
- **`src/Views/EditorUnificadoView.axaml`:** sustituir el `TabControl` (`:12-19`) por `<v:Planta2DEditorView DataContext="{Binding}"/>`; retirar `xmlns:cad` (`:4`, queda sin uso) y actualizar el comentario (`:9-11`).
- **Sin tests automatizados posibles** (no hay `Avalonia.Headless` en el proyecto de tests): la verificación es `dotnet build` (el XAML compila), greps estructurales (`TabItem` = 0, comandos presentes) y **validación visual manual del usuario** (riesgo ya señalado en `docs/plan-unificar-cad-planta2d.md` §Riesgos).

### 4.5 Plan de commits (rama `engine/f1-verdad-visual`)

- **C1 — `VigaPng`:** test → VM → vista (PlotView `:287` → Image). 1106→1107.
- **C2 — `InteraccionPng`:** tests (2) → VM → vista (`:209` → Image). 1107→1109.
- **C3 — `SeccionPng` + `SeccionColumnaPng`:** tests (2) → VMs → vistas (`:257`,`:204` → Image) + retirar `xmlns:oxy` de ambas. 1109→1111.
- **C4 — lienzo unificado:** botones en `Planta2DEditorView` + `EditorUnificadoView` sin TabControl + puntero en el plan previo.
- **C5 — cierre:** `./estado-real.sh` re-estampa `STATE.md` (1111); actualizar a mano la región curada (issue "4 gráficas en blanco" → resuelto en F1); commit de spec + plan F1.

## 5. Testing / verificación de la propia F1

- Cada commit C1-C3 nace de un test que primero **falla** (error de compilación: la prop no existe) y termina con `dotnet test LosasPlus.Linux.sln` verde con el conteo esperado.
- C4: `dotnet build` 0 err; `grep -c "TabItem" src/Views/EditorUnificadoView.axaml` = 0; `grep -Ec "<oxy:PlotView|xmlns:oxy"` = 0 en las 2 vistas migradas (grepear el ELEMENTO `<oxy:` y el xmlns — el comentario de `VigaEditorView.axaml:290-292` contiene el texto "oxy:PlotView" y se conserva); pase visual manual del usuario (6 diagramas visibles, Auto-Conectar e import operan desde Planta 2D, modo PlanoCad intacto).
- Cierre: `./estado-real.sh` exit 0 y región AUTO de `STATE.md` con 1111 passed; pytest sigue 208 (F1 no toca `motor-fea`).

## 6. Criterios de aceptación

1. Existen `VigaPng` e `InteraccionPng` con tests de píxeles verdes (criterio literal del roadmap `:65`).
2. Existen además `SeccionPng` y `SeccionColumnaPng` con tests de píxeles verdes (decisión §2.2).
3. `VigaEditorView.axaml` y `ColumnasEditorView.axaml` tienen 0 elementos `<oxy:PlotView` y 0 `xmlns:oxy` (el comentario explicativo de `VigaEditorView.axaml:290-292` menciona "oxy:PlotView" como texto y puede conservarse).
4. `EditorUnificadoView.axaml` ya no tiene 2 `TabItem` (criterio literal del roadmap `:65`); Auto-Conectar + Importar DXF/PDF disponibles en la barra de Planta 2D.
5. `CadView`/`CadCanvasHost` siguen en el repo y el modo PlanoCad (`MainWindow.axaml:875-878`) intacto.
6. Suites verdes: .NET 1106+5 = 1111 / Python 208; `STATE.md` re-estampado y su región curada marca el issue de gráficas como resuelto.
7. `Losas.exe` y su import: intactos.

## 7. Riesgos y mitigaciones

- **XAML no verificable headless** (C4 y el "se ve bien" de los PNG) → tests de píxeles cubren el contenido; lo estructural se cubre con build+grep; lo estético queda en pase manual del usuario antes de cerrar (mismo protocolo que el plan previo de unificación).
- **Pérdida de interactividad del PlotView** (tooltips/zoom del diagrama P-M) → trade-off ya aceptado en el fix original de Esfuerzos/Deflexión (F0/C1); los diagramas hoy están en blanco: PNG estático > nada.
- **Falsos verdes por colisión de color en el test de píxeles** → tolerancia ±24 por canal y colores afirmados distantes de texto/ejes (`ColorEje` = (107,114,128) vs `ColorViga` (51,58,69): Δ≥56 en R).
- **Fuentes ausentes en otra máquina** → ya mitigado por `DiagramaPng.ElegirFuente` (`DiagramaPng.cs:49-63`).
- **Regresión de import al retirar la pestaña** → no aplica: el modo PlanoCad conserva `CadView` completo; además los comandos de import quedan TAMBIÉN en la barra de Planta 2D.
- **`SeccionPng` stale al cambiar de tramo sin recalcular** → cubierto re-renderizando en el setter de `TramoSeleccionado` (§4.1).

## 8. Evidencia residual que F1 deja documentada (no arregla)

- Calibrar PDF solo en modo PlanoCad (`CadView.axaml:200-219,427-429`) — candidato a F2/F7.
- Bug de `oxy:PlotView` sigue latente para cualquier vista FUTURA que lo use (el paquete no se quita del proyecto); regla práctica: diagramas nuevos nacen con el patrón `DiagramaPng`.
- Pipeline CAD/DXF batch (espejado Y, anclas, capa Viga) → **F2** (`roadmap-fases-F0-F9.md:73-78`).
