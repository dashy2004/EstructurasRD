# Handoff UI → Antigravity

Spec de dos tareas de **UI (lane Antigravity)** derivadas del feedback del usuario.
El **motor (Claude)** ya dejó listo lo suyo en `avalonia-linux`; esto es lo que
queda del lado de la interfaz.

> Contexto del motor ya hecho (no tocar, solo apoyarse):
> - Crash de «Cargas y Combinaciones» arreglado (`ControlTheme TabItem` sin `BasedOn`).
> - Escena 3D: columna, viga y losa ya se construyen como **volúmenes extruidos**
>   (cajas de 12 aristas) en `src.Core/Render3D/EscenaEdificio.cs`.
> - Export IFC 4.3: los 4 elementos exportan `IfcExtrudedAreaSolid` (K.6 completo).

---

## Tarea 1 — Modos de estilo del visor 3D (wireframe / sólido / hollow)

**Pedido del usuario:** «debería haber opciones de estilo: hollows, líneas, 3d, etc.»

**Estado actual** (`src/Views/Vista3DControl.cs`):
- Es un **renderer por software de modo inmediato** (no usa OpenGL): proyecta la
  escena 3D a 2D (`Proyector3D`) y dibuja con el `DrawingContext` de Avalonia.
- Hoy **solo dibuja líneas** (`context.DrawLine` sobre `Escena3D.Segmentos`). Como
  `EscenaEdificio` ya genera cajas (12 aristas), el modo actual ya se ve como
  **wireframe de volúmenes** — eso ya está.

**Lo que falta (UI):** un selector de modo **wireframe / sólido / hollow**.

**Gap técnico a coordinar con el motor:** para *sólido*/*hollow* hay que **rellenar
caras**, y `Escena3D` hoy solo expone **aristas** (`Segmentos`), no caras. En un
renderer por software sin z-buffer hay que pintar las caras con **algoritmo del
pintor** (ordenar por profundidad). Propuesta de contrato de datos:

- **Motor** (si Antigravity lo pide): añadir a `Escena3D` una lista de **caras**
  (`IReadOnlyList<Cara3D>` con 4 vértices y una normal por caja) — es geometría,
  lane motor. Pedirlo y lo agrego.
- **Antigravity (UI):**
  1. Propiedad de modo en el control (enum `ModoRender { Wireframe, Solido, Hollow }`)
     enlazada a un `ComboBox`/toggle en la barra del visor.
  2. En `Render(...)`: si `Solido`/`Hollow`, ordenar caras por profundidad
     (centroide·dirección de cámara), rellenar con `context.DrawGeometry`
     (`Hollow` = relleno semitransparente + aristas; `Solido` = relleno opaco).
  3. `Wireframe` = comportamiento actual (solo `Segmentos`).

**Archivos:** `src/Views/Vista3DControl.cs` (control), y la barra donde se aloja el
visor (modo `Vista3D` en `MainWindow.axaml`).

---

## Tarea 2 — Rediseño de navegación (tabs/slide → menús desplegables por categorías)

**Pedido del usuario:** «las pestañas están arriba y hay que navegar con un slide.
sería bueno categorías con menús desplegables… tenemos el ejecutador de losas.exe
en todas las vistas.»

**Estado actual:**
- La navegación vive en `src/MainWindow.axaml`: cada modo es un `ContentControl`
  con `IsVisible="{Binding ModoActivo, Converter=EnumToBoolConverter, ConverterParameter=<Modo>}"`.
- El modo activo es `MainViewModel.ModoActivo` (enum **`ModoSidebar`**, ~13 valores:
  `Explorador, Editor, Planta2D, VisorPdf, Vista3D, DLEditor, Salida,
  Aceros, CargasCombinaciones, Vigas, Columnas, Validacion, BajadaCargas`
  <!-- PlanoCad retirado en UI1.6 (2026-06-11) -->).
- Ya existen comandos por modo en `MainViewModel` (p. ej. `IrAExploradorCommand`,
  `IrABusquedaCommand`); cambiar de modo = setear `ModoActivo`.

**Clave que reduce el riesgo:** el **hospedaje de cada vista no cambia** — siguen
siendo `ContentControl` toggled por `IsVisible`. Antigravity **solo cambia cómo se
SETEA `ModoActivo`**: en vez de la tira de tabs con slide, un **`Menu` con
`MenuItem` agrupados por categoría**, cada `MenuItem` con
`Command`/`CommandParameter` que fija el modo.

**Categorías sugeridas** (agrupar los 14 modos):
- **Proyecto:** Explorador · Editor · Validación
- **Geometría:** Planta 2D · Plano CAD · Vista 3D · Visor PDF
- **Análisis:** Cargas y Combinaciones · Bajada de Cargas · Vigas · Columnas
- **Salida:** DL Editor · Salida .TXT · Aceros

**Implementación (UI):**
1. En `MainWindow.axaml`, reemplazar la tira de tabs/slide por un `Menu` superior
   con un `MenuItem` por categoría y subítems por modo.
2. Cada subítem: `Command="{Binding IrAModoCommand}" CommandParameter="<ModoSidebar>"`
   (o reutilizar los comandos existentes). Conviene un único
   `IrAModoCommand(ModoSidebar)` en `MainViewModel` para no multiplicar comandos.
3. «losas.exe en todas las vistas»: dejar el `Menu` (o una barra de acciones con el
   botón de ejecutar) **siempre visible** por encima del `ContentControl` de modos,
   no dentro de cada vista.

**Archivos:** `src/MainWindow.axaml` (región de navegación), `src/ViewModels/MainViewModel.cs`
(`ModoActivo`, comandos). No tocar las vistas de cada modo.

---

## Notas de seguridad para Antigravity

- **`ControlTheme` de controles nativos:** SIEMPRE incluir
  `BasedOn="{StaticResource {x:Type <Control>}}"`. Sin `BasedOn` se rompe el template
  por defecto → crash en layout (fue la causa del crash de Cargas y Combinaciones).
  El motor ya auditó y corrigió `TabSecundario` y los `BotonCompacto`.
- Mantener la suite verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (753/753 al
  cierre de K.6).
