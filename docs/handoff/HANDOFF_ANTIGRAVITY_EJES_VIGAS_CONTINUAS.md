# Hand-off → Antigravity — UI de ejes/rejillas, secciones del 3D y vigas continuas

> El motor (Claude) dejó **headless y verde** (914/914) el plan de vigas continuas +
> ejes estructurales + elevación de sistemas. Falta el **pixeles**. Rama:
> `engine/columnas-diseno`. Todo lo de abajo es binding/render; la lógica ya existe y
> está testeada en `src.Core`.

---

## Tarea 1 — Ejes / rejillas estructurales (modelo listo)

`Edificio.Ejes` es una `ObservableCollection<EjeEstructural>` (compartida por todas las
plantas). `EjeEstructural` (`src.Core/Models/Cad/EjeEstructural.cs`):
- `Etiqueta` (string: "A", "1"…), `PuntoInicio`/`PuntoFin` (`PuntoCad`, m).
- `DistanciaA(p)`, `EstaEnSeccion(p, tol)` — geometría pura ya testeada.

**UI:** dibujar la rejilla en la **Planta 2D** y la **Vista 3D** (líneas + etiqueta en el
extremo), y permitir agregar/editar ejes (como los muros). Espejá el dibujo de `Muro` en
`PlantaCanvas`.

## Tarea 2 — Vista de sección del 3D (selector listo)

`SeccionPorEje` (`src.Core/Models/Cad/SeccionPorEje.cs`, puro):
- `Columnas(eje, columnas, tol)` y `Losas(eje, losas, tol)` → los elementos cuyo centro
  cae "cortado" por el eje.

**UI:** al elegir un eje, mostrar la **sección**: filtrar con `SeccionPorEje` los elementos
del edificio que el eje corta y dibujarlos en una vista de elevación (X = proyección sobre
el eje, Y = cota/`Sistema.Elevacion`). Es la base de "ver secciones del 3D".

## Tarea 3 — Vigas continuas (motor listo)

`GeneradorVigas` (`src.Core/Vigas/GeneradorVigas.cs`, puro y testeado):
- `VigaContinuaDelEje(eje, losasDelNivel, tol, caso)` → la **viga continua real** del eje
  (toma las losas en sección, las ordena por proyección y arma N tramos con N+1 apoyos).
- También `VigaContinua(luces[], cargas[], caso)` y `VigaContinuaDeLosas(losas, caso)`.

**UI:** un botón **«Generar viga continua del eje»** en el editor de Vigas (o en la Planta
2D al seleccionar un eje) que llame `VigaContinuaDelEje(...)`, agregue la viga a
`Nivel.Vigas` y la seleccione. El editor ya recalcula y dibuja sus diagramas — y ahora
mostrará los **momentos negativos sobre los apoyos interiores** (lo que la viga biapoyada
no tenía). *(Si querés que el wiring lo exponga un comando del VM, pedímelo — el
`VigaEditorViewModel` está en tu WIP, así que decidilo vos para no chocar.)*

## Tarea 4 — Elevación del sistema (modelo listo)

`Sistema.Elevacion` (alias aditivo de `CotaMetros`) ya existe. **UI:** mostrar/editar la
elevación en el panel de sistemas («cada sistema es un nivel de elevación»).

---

## Notas de seguridad (Antigravity)
- `ControlTheme` de controles nativos: siempre `BasedOn="{StaticResource {x:Type <Control>}}"`.
- Mantené la suite verde: `~/.dotnet/dotnet test tests/LosasPlus.Tests` (914/914 hoy).
- `src.Core` es lane de Claude — si necesitás un comando/propiedad nueva en un VM de
  motor, pedilo en vez de tocar `src.Core`.
- ⛔ No eliminar `Losas.exe`: todo esto es **aditivo**.
