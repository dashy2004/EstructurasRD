# Hand-off → Antigravity — Pestaña de Vigas (no se visualizan diagramas/sección/botones) + apoyos en columnas

> Reportado por el usuario: en la pestaña de Vigas «los botones/esquemas/sección no se
> visualizan / no funcionan». Rama `engine/columnas-diseno`. La View y el VM existen y
> compilan (930/930 verde); el problema es de **estado/runtime**, tu lane.

## Diagnóstico de Claude (no es falta de binding)

`VigaEditorView.axaml` ya tiene los 4 `<oxy:PlotView>` bien bindeados
(`ModeloViga`, `ModeloEsfuerzos`, `ModeloDeflexion`, `ModeloSeccion`) y los botones
(«✨ Generar vigas del nivel», «➕ Nueva Viga», «🗑 Eliminar Viga»). El VM los expone.
**Causa más probable:** los plots están **vacíos porque no hay viga activa**
(`HayVigaActiva == false`) — sólo se llenan cuando hay una `VigaActiva` seleccionada,
y el edificio no tiene vigas hasta que se generen.

**Pasos a verificar (con la app corriendo):**
1. Seleccioná/creá una viga (botón «Nueva Viga»). ¿Se llenan los 4 plots? Si **sí** → el
   problema es que no había vigas; ver paso 2. Si **no** → bug en `ConstruirModelo…`/recalcular.
2. «Generar vigas del nivel» llama `GeneradorVigas.MaterializarVigas(nivel)`, que crea vigas
   **a partir de las losas del nivel**. Si el nivel no tiene losas, no genera nada → plots vacíos.
   Verificá que el `Nivel` activo tenga losas, y que el botón esté efectivamente cableado.
3. La **sección** usa `ModeloSeccion`: confirmá que el VM lo **construye** (no sólo lo declara)
   al cambiar la viga/tramo seleccionado — si nunca se rellena, queda vacío.
4. Si la pestaña entera no responde: revisá que `VigaEditorView.DataContext` sea el
   `VigaEditorViewModel` correcto (no el MainViewModel).

## Nuevo en el motor (Claude) — apoyos en las columnas

El usuario también pidió que **la viga detecte los apoyos de las columnas**. Ya está headless:
`GeneradorVigas.VigaContinuaDeColumnas(eje, columnas, cargaLinealKNm, tolerancia, caso)`
→ pone un apoyo en cada columna sobre el eje (N columnas = N apoyos, N−1 tramos, luces =
distancias entre columnas). Complementa a `VigaContinuaDelEje` (que usa bordes de losa).

**UI sugerida:** en el botón/flujo de «Generar viga continua del eje», ofrecer **apoyos en
columnas** (usar `VigaContinuaDeColumnas` con las `Nivel.Columnas`) — es lo físicamente
correcto. La carga lineal podés tomarla del tributario o dejar un input por ahora.

## Notas
- Mantené verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (930/930 hoy).
- `src.Core` es lane de Claude — si querés un comando de VM (p. ej. `GenerarVigaDelEjeCommand`),
  pedímelo y lo agrego; vos hacés el botón.
