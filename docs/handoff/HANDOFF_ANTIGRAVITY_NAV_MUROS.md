# Hand-off → Antigravity — Navegación por categorías + dibujo de muros en 2D

> Dos tareas de **UI (lane Antigravity)** sobre `engine/columnas-diseno` (la carpeta
> única tras la consolidación). El motor (Claude) ya dejó todo lo headless verde
> (869/869). Estas dos necesitan pixeles.

---

## Tarea B — Navegación más fácil (categorías / desplegables)

**Pedido del usuario:** *«me gusta la navegación de las pestañas de main… que sea más
fácil»*.

**Hallazgo:** `engine` y `main` usan **la misma** navegación — una tira de **14
`RadioButton.navtab`** en el sidebar, todos bindeados a `MainViewModel.ModoActivo`
(`src/MainWindow.axaml`, región ~124–160). No hay un nav distinto en `main` para
copiar: lo que hace falta es **implementar la mejora** que el usuario pidió desde el
principio (ver `HANDOFF_UI_ANTIGRAVITY.md` Tarea 2): **agrupar los 14 modos en
categorías** con menús desplegables o secciones colapsables, en vez de la tira plana.

**Clave que reduce riesgo:** el hospedaje de cada vista **no cambia** — siguen siendo
`ContentControl` toggled por `IsVisible="{Binding ModoActivo, …}"`. Antigravity sólo
cambia **cómo se SETEA `ModoActivo`**: en lugar de 14 RadioButtons sueltos, un `Menu`
(o un sidebar con grupos colapsables) por categoría, cada ítem fijando el modo.

**Categorías sugeridas** (enum `ModoSidebar`):
- **Proyecto:** Explorador · Búsqueda · Editor · Validación
- **Geometría:** Planta 2D · Plano CAD · Vista 3D · Visor PDF
- **Análisis:** Cargas y Combinaciones · Bajada de Cargas · Vigas · Columnas
- **Salida:** DL Editor · Salida .TXT · Aceros

**Implementación:** un único `IrAModoCommand(ModoSidebar)` en `MainViewModel` (para no
multiplicar comandos), y cada ítem del menú/grupo con
`Command="{Binding IrAModoCommand}" CommandParameter="<ModoSidebar>"`.

**Archivos:** `src/MainWindow.axaml` (región de navegación), `src/ViewModels/MainViewModel.cs`.

---

## Tarea C — Dibujar los muros en la Planta 2D

**Pedido del usuario:** *«los muros no se mostraban en el 2D»*.

**Causa raíz (confirmada):** `src/Views/PlantaCanvas.cs` tiene **cero referencias a
`Muro`** — dibuja losas, vigas y columnas, pero **no los muros**. Los muros viven en
`Sistema.Muros` (`ObservableCollection<Muro>`).

**Modelo `Muro`** (`src.Core/Models/Cad/Muro.cs`):
- `PuntoInicio` / `PuntoFin` : `PuntoCad` (con `.X`, `.Y` en **metros de lienzo**) — el eje del muro.
- `Espesor` : double (m) — ancho en planta.
- `Altura` : double (m) — alto (para el 3D).
- `Longitud` : derivada.

**Implementación (UI):** agregar un pase de dibujo en `PlantaCanvas.Render(...)`,
**espejando cómo se dibujan las vigas** (segmento `OrigenX/Y`→`ExtremoX/Y`). Para cada
muro de `Nivel.Sistemas[*].Muros`:
- Convertir `PuntoInicio`/`PuntoFin` a pixeles con `MetrosAPixel(...)`.
- Dibujar el muro como un **segmento grueso** (o un rectángulo a lo largo del eje con
  ancho = `Espesor * _scale`), con un color/relleno propio (p. ej. gris/ladrillo) para
  distinguirlo de vigas (azul) y losas (verde).
- Opcional: hit-test + arrastre, como losas/vigas/columnas, si se quiere editar.

**Ojo (relacionado):** el usuario también quiere **unificar CAD + Planta 2D** en un
solo lienzo (ver `HANDOFF_ANTIGRAVITY_REORG.md` Tarea D). Como los muros nacieron en el
"Lienzo CAD", al unificar conviene que el canvas único (Planta 2D) sea el que los
dibuje — esta Tarea C es el primer paso de esa unificación.

**Archivos:** `src/Views/PlantaCanvas.cs` (método `Render`), modelo `src.Core/Models/Cad/Muro.cs` (sólo leer).

---

## Notas de seguridad (Antigravity)

- `ControlTheme` de controles nativos: siempre `BasedOn="{StaticResource {x:Type <Control>}}"`.
- Mantené la suite verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (869/869 hoy).
- Trabajás sobre `engine/columnas-diseno`; el modelo (`src.Core`) es lane de Claude —
  si necesitás un cambio de firma ahí, pedilo.
- El refresco en vivo del 2D ya está: `PlantaCanvas` se suscribe a `Nivel.ModeloCambiado`
  (`IModeloObservable`), así que al dibujar los muros se redibujarán solos cuando cambien.
