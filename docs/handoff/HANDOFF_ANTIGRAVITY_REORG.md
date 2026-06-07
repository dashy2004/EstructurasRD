# Hand-off → Antigravity — Reorganización, unificación, interfaces y sincronización CAD

> **Lane.** Antigravity ejecuta (ve la app corriendo, hace `git mv`, refactor de UI
> y verificación visual). Claude preparó este brief desde el motor headless: el
> análisis de duplicación e interfaces y el **root-cause de los bugs de
> sincronización** está hecho y verificado contra el código; falta la mano que ve
> pixeles. Contrato de lanes: [`DIVISION_TRABAJO.md`](DIVISION_TRABAJO.md).
>
> **Estado del repo al escribir esto:** rama `avalonia-linux`, build 0/0, **791/791
> .NET verde** (incluye el export de Aceros recién cerrado por Claude). No hay nada
> roto que debas arreglar antes de empezar.

Cuatro tareas, de la más segura a la más arquitectónica: **A** reorganización de
archivos · **B** unificación de proyectos · **C** extracción de interfaces · **D**
bugs de sincronización CAD/Planta2D/3D (el que más urge al usuario).

---

## Mapa actual (la verdad del árbol de proyectos)

```
src.Core      LosasPlus.Core          (motor puro)         → sin refs
src.UI.Shared MemoriaPlus.UI.Shared   (UI compartida)      → Core
src           LosasPlus               (app LosasPlus)      → Core + UI.Shared
src.Memoria   MemoriaPlus             (app MemoriaPlus)    → Core + UI.Shared
src.Linux     LosasPlus.Linux         (2º shell LosasPlus) → Core SOLO
```

Soluciones: `LosasPlus.sln` y `LosasPlus.Linux.sln` (en la raíz).

Dos olores que confirman tu intuición de *"el código es el mismo, hay que unificar"*:

1. **`src.Linux` es un segundo entrypoint de LosasPlus** que parece duplicar a `src`
   (ambos `WinExe`/`net8.0`/`AssemblyName=LosasPlus*`). Hoy `src` ya corre en Linux
   con Avalonia, así que `src.Linux` es muy probablemente **redundante** (a verificar:
   ¿se buildea/usa en algún CI o doc? si no, candidato a borrar).
2. **Namespaces cruzados.** `src.Core` y `src` usan `RootNamespace=LosasPlus`, pero la
   librería compartida `src.UI.Shared` usa `RootNamespace=MemoriaPlus`. Una app
   *LosasPlus* dependiendo de tipos en namespace *MemoriaPlus* es confuso y delata
   que "UI.Shared" nació dentro de MemoriaPlus y nunca se renombró.

Nombres de archivo presentes en `src` **y** en (`src.Memoria` + `src.UI.Shared`):
`App.axaml.cs`, `MainWindow.axaml.cs`, `MainViewModel.cs`, `Program.cs`. Para dos
shells de app eso es **normal** (cada app tiene el suyo) — el trabajo no es
fusionarlos sino **subir lo común a `src.UI.Shared`** y dejar en cada `MainViewModel`
sólo lo específico de su app.

---

## Tarea A — Reorganización de archivos para mejor organización

**Pedido:** *"mover todos los archivos de EstructurasRD para mejor organización."*

La raíz hoy mezcla código, soluciones, planes y prompts sueltos:
`PLAN_CAD_V1.md`, `PLAN_V1.1…`, `PLAN_V1.2…`, `PROMPTS_STITCH*.md`,
`PROPUESTA_UPDATE_v1.md`, `BUILD.md`, `BUILD-Linux.md`, `ESTADO_ACTUAL.md`,
`PLAN_MAESTRO.md`, `README.md`, `LICENSE`, dos `.sln`, `ICONOS/`, `plugins/`,
`scripts/`, `motor-fea/`, y los seis `src*`.

**Ya hecho por Claude (lado docs, seguro):** `docs/` quedó reorganizado **por tipo**
(`handoff/`, `roadmap/`, `releases/`, `referencia/`, `negocio/`, `screenshots/`) con
un índice en [`docs/README.md`](../README.md). Tomalo como patrón.

**Propuesta para la raíz (a ejecutar por vos, con `git mv` para preservar historia):**

```
/  (raíz)
├── src/                  ← agrupar TODO el código .NET acá
│   ├── Core/             (= src.Core)
│   ├── UI.Shared/        (= src.UI.Shared)
│   ├── LosasPlus/        (= src)
│   ├── MemoriaPlus/      (= src.Memoria)
│   └── Linux/            (= src.Linux, si sobrevive a la Tarea B)
├── motor-fea/            (queda igual: subproyecto Python)
├── tests/               (queda igual)
├── build/               ← BUILD.md, BUILD-Linux.md, los .sln
├── docs/                ← ya ordenado; mover acá los PLAN_*/PROMPTS_*/PROPUESTA_*
│   ├── planificacion/    (PLAN_CAD_V1, PLAN_V1.1, PLAN_V1.2, PLAN_MAESTRO, PROPUESTA_UPDATE_v1)
│   └── prompts/          (PROMPTS_STITCH, PROMPTS_STITCH_FASE2)
├── assets/               ← ICONOS/
├── plugins/  scripts/    (quedan)
├── README.md  LICENSE  ESTADO_ACTUAL.md   (quedan en raíz)
```

> ⚠️ **Mover los `src*` y los `.sln` rompe rutas.** Cada `git mv` de proyecto exige
> actualizar: los `<ProjectReference>` (rutas relativas `..\src.Core\…`), ambos
> `.sln`, los `.github/workflows/*.yml` (paths de build/test) y los enlaces en docs.
> Hacelo **un proyecto por commit**, con `dotnet build` + `dotnet test` verde entre
> cada uno. No es trabajo de un solo golpe.
>
> **Pendiente heredado:** `docs/RUNNER_BEHAVIOR.md` quedó en la raíz de `docs/` a
> propósito porque lo referencian por ruta `README.md:268`, `src/README.md:13` y un
> comentario en `src/ViewModels/MainViewModel.cs:913`. Si lo movés a
> `docs/referencia/`, actualizá esas 3 referencias en el mismo commit.

---

## Tarea B — Unificación de proyectos

**Pedido:** *"el código es el mismo, hay que unificar."*

1. **Decidir el destino de `src.Linux`.** Verificá si algo lo construye/usa
   (CI, scripts, docs). Si `src` ya cubre Linux vía Avalonia (lo hace), `src.Linux`
   es deuda: **borralo** o documentá por qué existe. Quitarlo simplifica una de las
   dos soluciones.
2. **Una sola solución.** Hoy hay `LosasPlus.sln` (Windows-era) y `LosasPlus.Linux.sln`.
   Consolidá en una `.sln` multiplataforma (la Linux ya tiene todo + tests). Borrá la
   vieja o dejala como alias documentado.
3. **Renombrar `MemoriaPlus.UI.Shared` → namespace neutro** (p. ej. `EstructurasRD.UI`
   o `Shared.UI`). Hoy `RootNamespace=MemoriaPlus` aunque la usa LosasPlus. Es un
   rename mecánico (IDE) + ajustar `using`s; hacelo en su propio commit.
4. **Subir lo común de los dos `MainViewModel`/`App`/`MainWindow` a `UI.Shared`.**
   No fusiones las apps; extraé a `UI.Shared` lo que ambas repiten (theming, atajos,
   diálogos, navegación por modos, logging) como **clases base** (`AppBase`,
   `MainViewModelBase`) y dejá en cada app sólo su contenido propio. El indicio de
   qué subir: cualquier tipo que hoy exista igual en `src/` y `src.Memoria/`.

> Regla: **un rename/movida por commit, suite verde entre cada uno.** Si algo del
> motor (`src.Core`) necesita cambiar de firma para esto, **pedímelo a mí (Claude)**:
> esa capa es mi lane y la cambio con tests.

---

## Tarea C — Más interfaces (hacer la app más desacoplada)

**Pedido:** *"hacer más interfaces."* Las oportunidades concretas, por valor:

1. **`IMotorCalculo` (la grande).** Hoy la app habla con dos motores por tipo
   concreto: el runner de `Losas.exe` y `MotorFeaService` (puente B6, `src.Core`).
   Extraé una interfaz común —
   `DisenarLosaAsync(Losa, Sistema) → ResultadoDiseno`— para que la UI no sepa cuál
   está detrás y se puedan **intercambiar / comparar** (Losas.exe vs motor-fea vs
   futuro). Es el cierre natural del Track B. *(La firma del lado motor la fijo yo;
   contrato-primero como en `DIVISION_TRABAJO.md`.)*
2. **`IResultExporter`.** Hay 6 exportadores estáticos en `src.Core/Services`
   (`CsvExporter`, `XlsxExporter`, `SafExporter`, `BajadaCargasExporter`,
   `IfcExporter`, `AcerosLosaExporter`). Una interfaz `Export(contexto, path)` +
   registro permitiría un menú "Exportar…" data-driven en vez de un handler por
   formato. (Las funciones puras pueden seguir estáticas por dentro.)
3. **`IDialogService`.** El code-behind usa `AppServices.Dialogs.SaveFileAsync(...)`.
   Verificá si ya hay interfaz; si es una clase concreta, extraéla — habilita testear
   los flujos de UI sin pantalla y es lo correcto para la lane headless.
4. **`IModeloObservable` / bus de cambios del modelo.** No existe y es la raíz de la
   Tarea D: hoy ninguna vista se entera cuando el modelo cambia. Ver abajo.

---

## Tarea D — Bugs de sincronización del motor (CAD ↔ Planta2D ↔ 3D)

> **Este es el que el usuario siente roto.** Claude ya hizo el root-cause leyendo el
> código; vos confirmás en la app corriendo y aplicás el fix (lane UI). *"Antigravity
> son otros ojos, fuera del código"* — exactamente: el motor marcó esto "arreglado"
> (ver `PLAN_MAESTRO.md:50`, bugfix 2026-06-02) pero **no puede ver** que en pantalla
> sigue mal.

### BUG 1 — Las columnas de la pestaña Columnas no aparecen en el CAD

**Causa raíz (confirmada en código):** el control del **Lienzo CAD**
(`src/Views/Cad/CadView.axaml.cs`) tiene **cero referencias a `Columna`**. No hay
código que dibuje columnas — el comentario `<!-- MODO Lienzo CAD (stub Fase E) -->`
en `MainWindow.axaml:796` es literal. Por contraste:

| Vista | Refs a `Columna` | ¿Dibuja columnas? |
|---|---|---|
| Lienzo CAD (`CadView`) | **0** | ❌ nunca se implementó |
| Planta 2D (`Planta2DEditorView`) | 16 | ✅ sí |
| Vista 3D (`Vista3DControl` → `EscenaEdificio`) | vía escena | ✅ sí |

**Además**, aunque el CAD las leyera: una columna nueva nace en (0,0). En
`src/ViewModels/ColumnasEditorViewModel.cs:61-71`, `Agregar()` hace
`new Columna { Id, Nombre }` + `nivel.Columnas.Add(...)` y **nunca asigna
`CoordenadaX/Y`**. `SincronizadorPlanta.SincronizarColumnas`
(`src.Core/Services/SincronizadorPlanta.cs:64`) sólo las distribuye en grilla cuando
**todas** están en (0,0) y sólo **al entrar** a la pestaña — una columna nueva entre
otras ya colocadas nunca recibe posición.

**Fix sugerido (lane UI):** decidir entre **(a)** implementar el dibujo de columnas en
`CadView` leyendo `nivel.Columnas` (igual que hace Planta2D), o **(b)** —preferible—
**retirar `CadView` en favor de `Planta2DEditorView`** (ver BUG 2, son redundantes).
En cualquier caso, en `Agregar()` asignar una coordenada por defecto (o llamar a
`SincronizadorPlanta.SincronizarColumnas(nivel, forzar:true)` tras agregar).

### BUG 2 — Dos lienzos 2D + el 3D no se coordinan

**Causa raíz:** hay **dos canvases 2D que se solapan** —
`CadView` ("Lienzo CAD", `ModoActivo=PlanoCad`) y `Planta2DEditorView` ("Planta 2D",
`ModoActivo=Planta2D`)— más `Vista3DControl`. **Cada uno se reconstruye sólo cuando él
se vuelve visible**, leyendo el modelo de cero; **nadie observa los cambios del
modelo**. No existe un evento "el modelo cambió" al que las tres vistas (y el
sincronizador) se suscriban. Resultado: editás en una pestaña y las otras quedan
desfasadas hasta el próximo cambio de modo — y el CAD, que ni lee columnas, nunca se
pone al día.

`SincronizadorPlanta` es **conservador a propósito** (`RequiereSincronizacion` = *todas*
en (0,0)) para no pisar lo que el usuario arrastró a mano — correcto para drags, pero
por eso no recoloca una sola columna nueva.

**Fix sugerido (arquitectura, lane UI con apoyo del motor):**
1. **Unificar los dos canvases 2D.** `Planta2DEditorView` ya dibuja losas, vigas y
   columnas (16 refs); `CadView` es un stub. Quedate con uno solo ("Planta 2D /
   CAD") y eliminá el modo duplicado de la navegación. Esto sólo ya resuelve la mitad
   del "existen dos CAD".
2. **Introducir un canal de cambios del modelo** (la `IModeloObservable` de la Tarea C):
   `Edificio`/`Nivel`/`Sistema` emiten un evento al mutar (o un `INotificarCambio` que
   el VM dispara tras `Agregar/Eliminar/editar`). Las 3 vistas se suscriben y se
   redibujan en vivo, no sólo al cambiar de pestaña. *(Si querés que el evento viva en
   los modelos de `src.Core`, esa parte la pongo yo con tests — pedímela.)*
3. **Hornear coordenadas al crear**, no sólo al entrar a la pestaña.

---

## Notas de seguridad y verificación (para Antigravity)

- **`ControlTheme` de controles nativos:** siempre `BasedOn="{StaticResource {x:Type
  <Control>}}"`. Sin `BasedOn` se rompe el template por defecto → crash en layout
  (ya pasó con "Cargas y Combinaciones").
- **Mantené la suite verde** entre cada commit:
  `~/.dotnet/dotnet test tests/LosasPlus.Tests` (791/791 al escribir esto).
- **Contrato-primero para lo que toca el motor.** Cualquier firma de `src.Core`
  (motor, exportadores, sincronizador, `IMotorCalculo`) la fija Claude; vos cableás la
  UI contra esa firma en paralelo.
- **Un rename / una movida / un fix por commit.** Las Tareas A y B son varios commits
  pequeños con build+test verde entre cada uno, no un PR monolítico.

## Resumen de archivos clave citados

- Navegación / modos: `src/MainWindow.axaml` (líneas ~796–829), `src/ViewModels/MainViewModel.cs`.
- Bug columnas: `src/Views/Cad/CadView.axaml.cs` (0 refs a `Columna`),
  `src/Views/Planta2DEditorView.axaml.cs` (16), `src/ViewModels/ColumnasEditorViewModel.cs:61-71`.
- Sincronización: `src.Core/Services/SincronizadorPlanta.cs`, `src.Core/Render3D/EscenaEdificio.cs`.
- Exportadores (interfaz candidata): `src.Core/Services/{Csv,Xlsx,Saf,AcerosLosa}Exporter.cs`,
  `src.Core/Transmision/BajadaCargasExporter.cs`, `src.Core/Interop/IfcExporter.cs`.
- Puente motor (interfaz `IMotorCalculo`): `src.Core/Services/MotorFeaService.cs`.
