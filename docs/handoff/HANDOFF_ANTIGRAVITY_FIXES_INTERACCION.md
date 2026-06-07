# Hand-off → Antigravity — Fixes de interacción (muro, drag de vigas/ejes/columnas)

> Reportados por el usuario. Viven todos en archivos de **tu WIP** (`PlantaCanvas.cs`,
> `Planta2DEditorView.axaml.cs`, `SeccionElevacionCanvas.cs`). El motor (Claude) ya
> arregló por su lado la **elevación en el 3D** (ver abajo). Rama `engine/columnas-diseno`.

> ⚠️ **Importante (proceso):** tu WIP **sobrescribió** un fix ya commiteado de Claude
> (commit `4132eb7`, borrado de muros) al reescribir `Planta2DEditorView.axaml.cs`.
> Por favor **commitea tu WIP** y reincorporá ese fix (abajo) para que no se vuelva a
> perder. Ideal: ambos no editar el mismo archivo a la vez.

---

## Fix 1 — El botón «Eliminar» no borra muros (REGRESIÓN)

**Causa:** en `Planta2DEditorView.axaml.cs`, `OnEliminarClick` maneja `Losa`/`Viga`/`Columna`
pero **ya no tiene la rama `Muro`** (estaba en el commit `4132eb7`, se perdió en tu
reescritura). El `PlantaCanvas` sí permite seleccionar muros, pero al borrar no pasa nada.

**Fix:** re-agregar la rama en `OnEliminarClick` (y el `using LosasPlus.Models.Cad;`):
```csharp
else if (selected is Muro m)
{
    foreach (var sys in nivel.Sistemas)
        if (sys.Muros.Contains(m)) { sys.Muros.Remove(m); break; }
}
```
Conviene también que `OnCanvasSelectionChanged` muestre un panel para `Muro` (hoy sólo
Losa/Viga/Columna), para feedback visual al seleccionarlo.

## Fix 2 — Extender/acortar vigas y ejes con el puntero

**Pedido:** arrastrar los **extremos** de vigas y ejes para alargarlos/acortarlos (no sólo
mover el elemento completo).

**Dónde:** `PlantaCanvas.cs`, en el hit-test/drag. Hoy el drag mueve el elemento entero.
Agregar **handles** en los extremos:
- Viga: extremos = `OrigenX/Y` y `ExtremoX/Y` (derivado de `LongitudTotal`+`AnguloGrados`).
  Arrastrar un extremo recalcula longitud/ángulo (o el tramo).
- Eje (`EjeEstructural`): extremos = `PuntoInicio`/`PuntoFin` (`PuntoCad`). Arrastrar un
  extremo actualiza esa propiedad.
- Patrón: al hacer hit-test, detectar si el click cae cerca de un extremo (radio ~8px) →
  modo "resize-endpoint"; si cae en el cuerpo → modo "move" (el actual).

## Fix 3 — En la vista de sección, mover/editar columnas con el puntero

**Pedido:** en `SeccionElevacionCanvas` (la ventana «Ver Elevación 3D»), poder mover/editar
las columnas con el puntero, como en la planta.

**Dónde:** `SeccionElevacionCanvas.cs` — agregar hit-test + drag de las columnas proyectadas
(actualizando su posición/dimensión en el modelo). Reusá el patrón de selección/drag de
`PlantaCanvas`.

---

## Ya resuelto por Claude (headless, no toques)
- **Elevación en el 3D:** `EscenaEdificio` ahora dibuja cada sistema a `cota + Sistema.Elevacion`
  (commit en `engine/columnas-diseno`). Tu `Vista3DControl` lo recibe automáticamente vía
  `EscenaEdificio.Construir`. Verificá que el 3D ya separe los sistemas por elevación.

## Notas
- `ControlTheme` nativo: `BasedOn="{StaticResource {x:Type <Control>}}"`.
- Mantené verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (916/916 hoy).
- ⛔ No eliminar `Losas.exe`.
