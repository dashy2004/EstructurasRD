# Análisis completo de UI — EstructurasRD (ex-LosasPlus) · v2

> Líder técnico · Síntesis de (A) 7 diagnósticos profundos de área + (B) 5 sweeps de caza de bugs.
> Bugs deduplicados y unificados. Las referencias `archivo:línea` provienen de los diagnósticos/sweeps; verifiqué que todos los archivos citados existen en el repo (no se inventó ningún path). Las líneas exactas no se re-verificaron una a una en runtime — la app no se ejecutó; confianza por hallazgo declarada abajo.

---

## 1. Resumen ejecutivo

La app **no está globalmente "rota", pero sí está peligrosamente incompleta en su capa de presentación**: el dominio (Edificio→Nivel→Sistema), los motores de cálculo (Pieper-Martens nativo, `VigaContinuaEngine` de rigidez directa, `ColumnaDisenador`, `ZapataDisenador`, motor FEA Python) y la persistencia base están **bien diseñados y son correctos**; el daño está casi todo en el **wiring MVVM y en una fachada de compatibilidad (`Proyecto.Sistemas` = solo `Niveles[0]`)** que provoca síntomas visibles (botones congelados, editores que no refrescan al cambiar de nivel) y, lo más grave, **pérdida de datos silenciosa real**: guardar/exportar un proyecto multinivel conserva solo el primer nivel, y cerrar/abrir/nuevo descarta el trabajo sin confirmación. Los temas **más graves** son: (a) pérdida de datos al guardar/exportar y al cerrar sin "dirty-check"; (b) dos formatos JSON incompatibles con el mismo nombre `proyecto.lpx.json`; (c) crash del motor FEA por tokens `NaN` que el parser C# rechaza; y (d) el patrón omnipresente de `RaiseCanExecuteChanged` faltante que congela los botones de todos los editores. El **rebrand** y el **logo** son cosméticos (assets + literales) y la atribución a F. Perdomo **no es un bug** (es correcta y necesaria). **Veredicto global: se parchea la mayoría, pero NO solo con cosmética** — hay un núcleo de pérdida de datos que exige reescritura *parcial* y quirúrgica de varios VMs (Columnas, Niveles/Sistemas del `MainViewModel`) y del shell de navegación de LosasPlus. Ninguna **interfaz completa** necesita reconstruirse desde cero: el contenido (sub-vistas), los motores y el modelo se conservan.

---

## 2. Matriz por interfaz

| Interfaz | ¿Funciona? | Severidad | Veredicto | Esfuerzo | Causa raíz (1 frase) |
|---|---|---|---|---|---|
| **Editor de Vigas** | Parcial (calcula bien; botones de selección congelados; sección desacoplada de la rigidez) | Alta | Reescribir-parcial (VM) | Medio | Setters no llaman `RaiseCanExecuteChanged` y `Inercia` no se recalcula al editar `Base/Peralte`. |
| **Editor de Columnas** | No (tabla vacía, sin sección ni P-M) | Alta | Reescribir-parcial (VM+axaml) | Medio | `Recargar()` solo se llama en el ctor (cuando `NivelActivo`=null) y el ComboBox bindea `Niveles`/`NivelSeleccionado` inexistentes. |
| **Niveles vs Sistemas (dominio en VM)** | No con >1 nivel (pierde datos) | **Crítica** | Reescribir-parcial (VMs) | Medio-alto | La fachada `Proyecto.Sistemas`=`Niveles[0]` se usa para mutaciones/exports mientras la UI lee `NivelActivo.Sistemas`. |
| **Persistencia (guardar/abrir/undo/cerrar)** | No de forma segura | **Crítica** | Reescribir-parcial (servicio + VM) | Medio-alto | Guardado por fachada (`Niveles[0]`), dos formatos homónimos, sin dirty-check ni confirmación. |
| **Shell / barra File·Engine·Export** | Sí (navega; comandos sanos) | Media | Reescribir-parcial (chrome) | Medio | Dos bandas de menú en idiomas distintos + content-switch monolítico de 19 `IsVisible`. |
| **Integración motor FEA** | Parcial (crashea con losa fina; modela continuidad mal) | **Crítica** (crash) | Reescribir-parcial (servicio) | Medio | `JsonDocument.Parse` sin `AllowNamedFloatingPointLiterals` rechaza `NaN`; bordes no mapeados → toda losa simple. |
| **CAD / render / interacción** | Parcial (funciona en defecto; hit-test/perf degradan con escala/offset y muchas losas) | Alta | Reescribir-parcial (host) | Medio | Hit-test no deshace Escala/Offset del DXF y `LayoutSolver.Solve` corre por frame. |
| **Rebrand de display (textos)** | Cosmético | Media | **Parchar** | Bajo | ~20 literales hard-codeados sin constante central de marca. |
| **Branding / Logo** | Cosmético (cuadrado oscuro sobre barra clara) | Media | **Parchar** | Bajo | SVG/PNG opacos con fondo `#1a1a2e` horneado, sin transparencia. |
| **Mención F. Perdomo (Memoria)** | Sí (atribución correcta) | Baja | **Parchar** (bugs colaterales) | Bajo | No es bug; checklist decorativo y generación sin aviso de datos faltantes sí lo son. |

---

## 3. Diagnóstico detallado por área

### 3.1 Editor de Vigas — Reescribir-parcial (VM) · Severidad alta · Confianza alta

**Causa raíz.** Dos problemas independientes:
1. **CanExecute congelado.** `RaiseCanExecuteChanged` tiene **0 ocurrencias** en `VigaEditorViewModel.cs`. Los comandos `EliminarViga/EliminarTramo/EliminarApoyo/AgregarCarga/EliminarCarga` (`src/ViewModels/Vigas/VigaEditorViewModel.cs:77-83`) dependen de `_vigaActiva`/`_tramoSeleccionado`/`_apoyoSeleccionado`/`_cargaSeleccionada`, pero los setters (`VigaActiva` L110-124, `TramoSeleccionado` L130-141, `ApoyoSeleccionado` L143-148, `CargaSeleccionada` L150-155) nunca notifican el cambio. Avalonia **no** tiene el auto-requery de WPF (`CommandManager.RequerySuggested`), así que el `Enabled` se evalúa una sola vez al bindear y queda congelado: seleccionar una fila del DataGrid no rehabilita el botón 🗑 correspondiente.
2. **Rigidez desacoplada de la sección (más grave de ingeniería).** Editar `Base/Peralte` redibuja la sección (`ConstruirModeloSeccion`) pero **no recalcula `Inercia`** (`src.Core/Vigas/TramoViga.cs:37`), y el motor usa `tramo.ModuloElasticidad*tramo.Inercia` (`src.Core/Services/VigaContinuaEngine.cs:95`). Resultado: los diagramas M/V/δ **no cambian** al editar la sección. Además, cambiar `NivelActivo` (`src/ViewModels/MainViewModel.cs:630`) no refresca el editor → queda mostrando vigas de otro nivel.

**Evidencia clave.** `VigaEditorViewModel.cs:77,130,455`; `src.UI.Shared/Common/RelayCommand.cs:14` (comentario que afirma que "los setters ya disparan `RaiseCanExecuteChanged`" — contradicho aquí); `VigaContinuaEngine.cs:55` (motor correcto); `tests/LosasPlus.Tests/VigaEditorViewModelTests.cs:68` (los tests llaman `Execute(null)` directo y saltan el gate → el bug no está cubierto).

**Hay dos clases `RelayCommand` distintas** en el repo (`src.UI.Shared/Common/RelayCommand.cs` ns `MemoriaPlus.Common`, ctor `Action`; y la local en `MainViewModel.cs:1869` ns `LosasPlus.ViewModels`, ctor `Action<object?>`). El VM resuelve a la **local**. Ambas exponen `RaiseCanExecuteChanged()`, pero el VM nunca lo invoca.

**Diseño objetivo (si se reescribe el VM).** Migrar a `CommunityToolkit.Mvvm` (`[ObservableProperty]`/`[RelayCommand]` + `[NotifyCanExecuteChangedFor]`) elimina de raíz toda la clase de bugs de CanExecute. Acoplar geometría: `set` de `Base/Peralte` recalcula `Inercia = b·h³/12` y la columna "I" pasa a readonly/derivada. Suscribir el VM al cambio de `NivelActivo` (callback `NotificarCambioDeNivel()` que reemite `OnPropertyChanged(nameof(Vigas))` y resetea `VigaActiva = Nivel.Vigas.FirstOrDefault()`). La vista `.axaml` y el motor quedan intactos. **Parche mínimo** si se prioriza desbloqueo: helper `NotificarComandos()` que llame `RaiseCanExecuteChanged` en los 4 setters + `RehookViga`.

---

### 3.2 Editor de Columnas — Reescribir-parcial (VM + axaml) · Severidad alta · Confianza alta

**Causa raíz.** La tabla nunca se llena en runtime:
- `Columnas` es **propiedad calculada** `=> _getNivel()?.Columnas` (`src/ViewModels/ColumnasEditorViewModel.cs:66`); solo se re-evalúa cuando se emite `OnPropertyChanged(nameof(Columnas))`, lo cual **únicamente** ocurre en `Recargar()` (L327-331).
- `Recargar()` se llama **una sola vez**, en el ctor (L39), construido desde `MainViewModel.cs:859` cuando `NivelActivo` **todavía es null**. El DataGrid bindea a `null` y se queda así.
- El setter de `NivelActivo` (`MainViewModel.cs:630-647`) **nunca** llama `ColumnasEditor.Recargar()` (contraste: `SistemaActivo` sí llama `Aceros?.Recargar()` en L621). `grep`: `ColumnasEditor.Recargar` no aparece fuera del ctor.
- Sin filas no hay `Seleccionada` → toda la cadena `RecalcularDiseno`/`ConstruirSeccionColumna`/`ConstruirPlot` (L126-157, correcta) nunca corre → panel derecho permanente "Selecciona una columna…".

**Bug adicional confirmado:** el ComboBox de nivel (`src/Views/ColumnasEditorView.axaml:42`) bindea `Niveles`/`NivelSeleccionado`, **propiedades inexistentes** en el VM (solo viven en comentarios XML doc); con `x:CompileBindings=False` falla en silencio → selector muerto.

**Diseño objetivo.** Rehacer el VM para ser **autónomo del nivel**: exponer `Niveles` y `NivelSeleccionado` como propiedades reales; el setter de `NivelSeleccionado` reevalúa `Columnas`, limpia `Seleccionada`, re-suscribe `CollectionChanged`; suscribir `Columna.PropertyChanged` en el setter de `Seleccionada` para recalcular al editar `Base/Peralte/Coordenada`; sincronizar con `MainViewModel.NivelActivo` vía `PropertyChanged` o callback. Activar `x:CompileBindings=True` una vez existan las propiedades para que bindings fantasma fallen en compile-time. Migrar `OnAgregar/OnEliminar` (code-behind, `ColumnasEditorView.axaml.cs:18`) a Commands con CanExecute. **Parche mínimo:** añadir `ColumnasEditor?.Recargar()` en el setter de `NivelActivo`, en el restore y tras `AbrirDL` (deja vivos el ComboBox muerto y el recálculo al editar dimensiones).

---

### 3.3 Nivel vs Sistema (jerarquía de dominio en la capa VM) — Reescribir-parcial · Severidad **crítica** · Confianza alta

**Causa raíz.** El **modelo es correcto** (`Nivel.Sistemas` es `ObservableCollection<Sistema>`, 1-a-N, `src.Core/Models/Edificio.cs:134`). El daño está en la **capa de presentación**, que convive con dos abstracciones incompatibles sin elegir fuente de verdad:
1. **Fachada de compatibilidad** `Proyecto.Sistemas => Edificios[0].Niveles[0].Sistemas` (`src.Core/Models/Sistema.cs:98-106`) — siempre el primer nivel, `[JsonIgnore]`.
2. **Doble semántica de nivel**: `Sistema` recibió `Uso`/`CotaMetros`/`Elevacion`/`SalidaPerdomo` (`src.Core/Models/Sistema.MemoriaPlus.cs:238-282`) cuando `Nivel` ya tiene `Nombre`/`Cota` → dos "cotas" sin reconciliar.

**LosasPlus es split-brain:** la UI lee `NivelActivo.Sistemas` (`src/MainWindow.axaml:201`) pero las mutaciones escriben la fachada: `AgregarSistema/EliminarSistema` operan sobre `_proyecto.Sistemas` (`MainViewModel.cs:996-1027`), export/validación/búsqueda recorren solo `Niveles[0]` (`918,1037,872,1714,1752`). Parado en el Nivel 2, "Agregar sistema" lo crea en el Nivel 1 y "Guardar .DL" exporta solo el Nivel 1 → **pérdida de datos**. **Landmine:** el setter alias `Sistema` (`MainViewModel.cs:705-719`) hace `Clear()+Add()` → colapsa la colección del nivel a un único sistema. **Memoria** aplana del todo: trata cada `Sistema` como un nivel (`NivelesView.axaml:51`).

**Diseño objetivo.** Una sola fuente de verdad jerárquica; fachada **solo lectura legacy**. Mover `Uso`/`Cota` a `Nivel` (migración v3→v4). Enrutar **toda** mutación a `NivelActivo.Sistemas` (borrar el setter alias destructivo). `EnumerarSistemas(edificio)=SelectMany(Niveles).Sistemas` para export/validación/búsqueda (decisión de producto: .DL por-nivel vs por-edificio). `ProyectoSerializer.ReadMetadata.CantidadNiveles` = nº de niveles, no de sistemas (`ProyectoSerializer.cs:294`). Tests que cubran estar en `Niveles[1]`.

---

### 3.4 Persistencia (guardar/abrir/undo/cerrar) — Reescribir-parcial · Severidad **crítica** · Confianza alta

Esta área amplifica 3.3 con hallazgos del sweep de persistencia:
- **Guardar pierde todo salvo `Niveles[0]`**: `ProyectoService.GuardarProyecto` itera `p.Sistemas` (fachada) (`src.Core/Services/ProyectoService.cs:52-93`). Se pierden niveles, edificios, vigas, columnas, muros, ejes, cargas globales, combinaciones y metadata. El formato `.DL` ni siquiera puede representar vigas/columnas/muros.
- **Dos formatos homónimos**: `ProyectoSerializer.Save` escribe un envelope JSON y `ProyectoService.GuardarProyecto` escribe un manifest distinto, ambos como `proyecto.lpx.json`. Abrir el envelope con el lector de manifest devuelve un proyecto vacío con un "Sistema 1" demo **sin error** (`ProyectoService.cs:96-137` vs `ProyectoSerializer.cs:164-215`).
- **Sin dirty-check ni confirmación**: ninguna ventana registra `Closing` cancelable; no hay flag `IsDirty`. Cerrar/Nuevo/Abrir descarta el trabajo (`src/MainWindow.axaml.cs:70`; `src.Memoria/MainWindow.axaml.cs:24`).
- **Carga incompleta**: abrir `.lpx.json` no copia `Cargas`/`Combinaciones`/metadata MemoriaPlus al proyecto vivo (`MainViewModel.cs:1593-1628`).
- **Undo parcial**: `RestoreSnapshot` no restaura `Cargas` ni placeholders MemoriaPlus (`MainViewModel.cs:468-514`).
- **`EliminarSistema` sin snapshot ni confirmación** (`MainViewModel.cs:1013-1027`); Memoria sí confirma (`src.Memoria/.../MainViewModel.cs:727`).
- **`GuardarDLAsync` sella `_proyecto.Archivo` con la ruta `.dl`** → el siguiente Ctrl+S escribe JSON dentro de un `.dl` (`MainViewModel.cs:1039`).
- **Memoria no hace backup** antes de `GuardarBorrador/GuardarComo` (asimetría con el main app, que sí llama `MaybeBackup()`).

**Diseño objetivo.** Unificar a **un solo** servicio de persistencia que serialice el árbol completo (`Edificios→Niveles→Sistemas`) con un único formato/extensión y un magic-header que distinga envelope de manifest. Introducir `IsDirty` en ambos VMs y un handler `Closing` cancelable con diálogo Guardar/Descartar/Cancelar. Carga completa (Cargas/Combinaciones/metadata). Undo total. `EliminarSistema` con snapshot+confirmación. Separar identidad de archivo `.dl` (export) de `.lpx.json` (proyecto). Backup simétrico en ambas apps.

---

### 3.5 Shell / barra File·Engine·Export — Reescribir-parcial (chrome) · Severidad media · Confianza alta

**Causa raíz.** Es de **arquitectura, no de lógica**: ningún handler está roto, todos los `CommandParameter` mapean al enum `ModoSidebar`. El problema: `src/MainWindow.axaml` es un Window de ~1046 líneas con **dos bandas de menú** apiladas con paradigmas/idiomas distintos — Banda 1 de navegación en MAYÚSCULAS español (PROYECTO/GEOMETRÍA/…, L132-166) y Banda 2 de acciones en inglés (File/Engine/Export, L253-296) — más un content-switch monolítico de **19 bloques `IsVisible`**. El menú **Engine** mezcla cálculo (Wu/FEM/Pieper-Martens) + geometría (Generar ejes/foto IA/DXF) + import (.TXT) en un solo dropdown. El **blueprint correcto ya existe en el repo**: MemoriaPlus (`src.Memoria/MainWindow.axaml:16-58`) usa sidebar de 240px + RadioButtons + router `CurrentView` + top-bar contextual.

**Diseño objetivo.** Adoptar el shell de MemoriaPlus: sidebar único agrupado (Proyecto/Geometría/Análisis/Salida/Sistema), exponer `CurrentView` en `MainViewModel` y reemplazar las ~700 líneas de switch por `<ContentControl Content="{Binding CurrentView}"/>`, y top-bar contextual que parte el viejo "Engine" en **"Calcular"** (Wu/FEM/Pieper/Importar .TXT) y **"Generar geometría"** (Ejes/Foto IA/DXF). Reutilizar **todas** las sub-vistas sin cambios. Idioma 100% español.

---

### 3.6 Integración motor FEA — Reescribir-parcial (servicio) · Severidad **crítica** (crash) · Confianza alta (verificado empíricamente)

**Causa raíz (crash).** `ParsearResultado` usa `JsonDocument.Parse` con opciones por defecto (sin `AllowNamedFloatingPointLiterals`) (`src.Core/Services/MotorFeaService.cs:76`). Cuando una franja es `seccion_insuficiente`, el motor Python emite literales `NaN`; el sweep **lo reprodujo**: corriendo el CLI real con `t=0.05`, `q=50000` salen tokens `NaN`, y un repro .NET confirma que `JsonDocument.Parse` lanza `JsonReaderException`. Disparador: "Diseñar con motor FEA" sobre una losa sub-dimensionada → error de parseo en vez del resultado "SECCIÓN INSUFICIENTE". En lote (`CalcularConMotorAsync`) **una sola losa insuficiente aborta todo**.

**Discrepancias de modelo.** El cutover modela **toda losa como simplemente apoyada** (no pasa el borde a `DisenarLosaAsync`, `MotorFeaService.cs:171`) → desprecia la continuidad del catálogo Pieper-Martens. `AplicarMomentos` asigna `MSx==MSy==m_apoyo_max` (`:157`). `CalcularBordesConMotorAsync` calcula acero de apoyo que **la UI nunca muestra** (ruta muerta, `:210`). En vigas, `fy` está **hard-codeado a 420 MPa** ignorando `Sistema.Fy` (`VigaEditorViewModel.cs:729`).

**Diseño objetivo.** Habilitar `JsonSerializerOptions { NumberHandling = AllowNamedFloatingPointLiterals }` y degradar grácilmente "sección insuficiente" sin abortar el lote. Mapear `Tipo`→bordes (continuo/empotrado) antes de invocar el motor. Diferenciar `MSx`/`MSy` por dirección. Conectar el acero de borde a la UI o retirar la ruta muerta. Tomar `fy`/`fc` de `Sistema`, no constantes.

---

### 3.7 CAD / render / interacción — Reescribir-parcial (host) · Severidad alta · Confianza media (no compilado)

**Causa raíz.** Dos defectos de altura alta:
1. **Hit-test no deshace Escala/Offset del DXF** (`src/Views/Cad/CadCanvasHost.cs:1549-1553`): el render aplica `(p.X*esc+OffsetX)*Px` pero `HitTestPoligono` invierte solo `preX/Px` sin dividir por `esc` ni restar offsets. Con `Escala≠1` u `Offset≠0` (panel "AJUSTE ESPACIAL"), clic sobre un polígono no crea la losa o crea la equivocada. Enmascarado en el estado por defecto (Escala=1, Offset=0).
2. **`LayoutSolver.Solve` corre por frame** (`CadCanvasHost.cs:544-545`): `DibujarLosas` lo invoca en cada `InvalidateVisual` (pan/zoom/drag/mouse-move) → BFS + LINQ + asignaciones nuevas decenas/cientos de veces por segundo con muchas losas (GC + caída de FPS). Debe cachearse atado a `RevisionSistema`.

**Otros (medios/bajos).** Grilla/ejes fijos en ±40 m → invisibles para DXF con coordenadas de sitio (`:389-406`); muros/losas en Y-descendente sin flip vs DXF con flip-Y → sin correspondencia espacial muro↔DXF (`:585-598`); captura PNG sin encuadre cuando no hay DXF (`:350-368`); timer de re-rasterizado que no converge (`:1575-1586`); coords de foto IA (Y-up) en `CoordenadaX/Y` que el canvas ignora (`MainViewModel.cs:1247-1258`).

**Diseño objetivo.** Una sola transformación de coordenadas reversible (render e inverse-hit comparten matriz). Cachear `LayoutResult` e invalidar solo por cambio topológico. Grilla en función del viewport visible. Encuadre por losas como fallback de captura. Convención Y unificada entre capas y con la fuente IA.

---

## 4. Bugs adicionales hallados (deduplicados y agrupados)

> Los bugs de CanExecute/refresh de los sweeps coinciden con las áreas 3.1-3.3; se **unifican** aquí y no se cuentan dos veces. Marcados con ↳ los que ya forman parte de un diagnóstico de área.

### CRÍTICA
- **Guardar pierde todos los niveles salvo `Niveles[0]`** (`src.Core/Services/ProyectoService.cs:52-93`; `MainViewModel.cs:983-993`). ↳ 3.3/3.4
- **Dos formatos JSON homónimos `proyecto.lpx.json`**: abrir el envelope con el lector de manifest devuelve proyecto vacío sin error (`ProyectoService.cs:96-137` vs `ProyectoSerializer.cs:164-215`). ↳ 3.4
- **Cerrar/Nuevo/Abrir descarta trabajo sin confirmación** (ambas apps; `src/MainWindow.axaml.cs:70`, `src.Memoria/MainWindow.axaml.cs:24`). ↳ 3.4
- **Motor FEA: tokens `NaN` rompen `JsonDocument.Parse`** (`MotorFeaService.cs:76`); en lote aborta todo. ↳ 3.6

### ALTA
- **Botones Vigas no se rehabilitan al cambiar selección** (`VigaEditorViewModel.cs:77`). ↳ 3.1
- **`Inercia` no se recalcula al editar `Base/Peralte`** → diagramas obsoletos (`TramoViga.cs:37`). ↳ 3.1
- **ComboBox de nivel de Columnas bindea `Niveles`/`NivelSeleccionado` inexistentes** (`ColumnasEditorView.axaml:42`). ↳ 3.2
- **`NivelActivo` no propaga `Recargar()` a `ColumnasEditor` en ninguna de ~7 rutas** (`MainViewModel.cs:630`). ↳ 3.2
- **Export/guardado `.DL` y `AgregarSistema/EliminarSistema` actúan sobre la fachada `Niveles[0]`** (`MainViewModel.cs:918,1037,996-1027`). ↳ 3.3
- **Setter alias `Sistema` destructivo (`Clear()+Add()`)** (`MainViewModel.cs:705-719`). ↳ 3.3
- **Seleccionar un proyecto reciente lo ABRE de inmediato**: el setter de `ProyectoRecienteSeleccionado` llama `Execute(null)` (no `RaiseCanExecuteChanged`) y `AbrirEnEditorCommand` no es no-op → un clic carga el proyecto y salta al Editor, posiblemente perdiendo cambios (`MainViewModel.cs:109`).
- **Botones de Cargas/Combinaciones no se re-habilitan** (`CargasCombinacionesViewModel.cs:61`).
- **Abrir `.lpx.json` no copia Cargas/Combinaciones/metadata** (`MainViewModel.cs:1593-1628`). ↳ 3.4
- **Undo/Redo no restaura Cargas ni metadata MemoriaPlus** (`MainViewModel.cs:468-514`). ↳ 3.4
- **`EliminarSistema` sin snapshot ni confirmación** (`MainViewModel.cs:1013-1027`). ↳ 3.4
- **Cutover FEA modela toda losa como simplemente apoyada** (`MotorFeaService.cs:171`). ↳ 3.6
- **Hit-test CAD no deshace Escala/Offset del DXF** (`CadCanvasHost.cs:1549-1553`). ↳ 3.7
- **`LayoutSolver.Solve` corre por frame** (`CadCanvasHost.cs:544-545`). ↳ 3.7
- **Icono ya es EstructurasRD pero el texto dice "LosasPlus"** (`MainWindow.axaml:115-120`). ↳ 3.8
- **Strings que mezclan marca con `.exe` reales**: rebrandear sin renombrar AssemblyName pediría un `.exe` inexistente (`MainViewModel.cs:1796-1848`). ↳ 3.8

### MEDIA
- **Editar dimensiones de columna no recalcula el diseño** (`ColumnasEditorViewModel.cs:73`). ↳ 3.2
- **Doble fuente de elevación sin reconciliar** `Nivel.Cota` vs `Sistema.CotaMetros` (`Sistema.MemoriaPlus.cs:254-270`). ↳ 3.3
- **Validación/Búsqueda solo cubren `Niveles[0]`** (`MainViewModel.cs:872,1714,1752`). ↳ 3.3
- **Memoria no puede editar más de un nivel real** (`NivelesView.axaml:51`). ↳ 3.3
- **`GuardarDLAsync` sella `_proyecto.Archivo` con `.dl`** → Ctrl+S escribe JSON en `.dl` (`MainViewModel.cs:1039`). ↳ 3.4
- **Memoria no hace backup antes de guardar** (`src.Memoria/.../MainViewModel.cs:560-601`). ↳ 3.4
- **Atajo Ctrl+L (AgregarLosa) muerto**: configurable y persistido, sin cablear a KeyBinding (`MainWindow.axaml.cs:114-122`; `AtajosConfig.cs:64`). ↳ 3.5
- **Dos shells distintos en el mismo repo** (LosasPlus doble-menú vs MemoriaPlus router) (`MainWindow.axaml:103-166`). ↳ 3.5
- **Botón "Eliminar muro" del CAD nunca se habilita** (`CadEditorViewModel.cs:65`).
- **Cutover FEA: `MSx==MSy==m_apoyo_max`** (`MotorFeaService.cs:157`). ↳ 3.6
- **`CalcularBordesConMotorAsync` es ruta muerta** (`MotorFeaService.cs:210`). ↳ 3.6
- **`fy` hard-codeado a 420 MPa en armado de viga** (`VigaEditorViewModel.cs:729`). ↳ 3.6
- **Grilla/ejes CAD fijos en ±40 m** (`CadCanvasHost.cs:389-406`). ↳ 3.7
- **Muros/losas Y-down vs DXF flip-Y sin correspondencia** (`CadCanvasHost.cs:585-598`). ↳ 3.7
- **Captura PNG sin encuadre cuando no hay DXF** (`CadCanvasHost.cs:350-368`). ↳ 3.7
- **Logo: cuadrado azul-noche opaco sobre barra clara** (PNG/SVG sin transparencia, `EstructurasRD.svg`/`.png`). ↳ 3.9
- **Mismo PNG oscuro en el encabezado Word entregable** (`MemoriaGenerator.cs:721`). ↳ 3.9
- **Checklist "Verificaciones previas" decorativo** (✓ hard-codeados) (`src.Memoria/Views/GenerarView.axaml:24-43`). ↳ 3.10
- **`GenerarMemoria` no avisa si faltan datos Perdomo** → "memoria exitosa" con tablas vacías (`src.Memoria/.../MainViewModel.cs:964-1009`). ↳ 3.10
- **Inconsistencia de marca**: "LosasPlus" (sin espacio) vs "Memoria Plus" (con espacio) (`MainViewModel.cs:71` vs `src.Memoria/.../MainViewModel.cs:426`). ↳ 3.8

### BAJA
- `AgregarApoyo` siempre en X=0.0 (apoyos colapsados) (`VigaEditorViewModel.cs:289`). ↳ 3.1
- `NuevaViga`: apoyo derecho no se mueve al agregar tramos (`VigaEditorViewModel.cs:255`). ↳ 3.1
- Anotaciones de carga (flecha siempre hacia abajo, banda de altura fija) (`VigaEditorViewModel.cs:573`). ↳ 3.1
- Texto "Columnas (primer nivel)" contradice multinivel (`ColumnasEditorView.axaml:35`). ↳ 3.2
- "Tomar Pu del descenso" reparte por igual sin áreas tributarias (`ColumnasEditorViewModel.cs:52`). ↳ 3.2
- `CantidadNiveles` cuenta sistemas, no niveles (`ProyectoSerializer.cs:294`). ↳ 3.3
- Columna "SISTEMAS" bindea `{Binding Niveles}` (vocabulario cruzado) (`MainWindow.axaml:425`). ↳ 3.3
- `EliminarVigaCommand` deja botones engañosos habilitados con `_vigaActiva=null` (`VigaEditorViewModel.cs:272`). ↳ 3.1
- `AgregarSistemaCommand` (Memoria) sin `RaiseCanExecuteChanged` (latente; hoy `ProyectoActivo` nunca null) (`src.Memoria/.../MainViewModel.cs:60`).
- `TxtParser`/`DLFileService` parsean con `int.Parse`/`double.Parse` crudos (frágiles; hoy protegidos por try/catch del caller) (`TxtParser.cs:297-298`; `DLFileService.cs:246-247`).
- `ZapataDisenador` propaga NaN/Inf si la columna tiene un lado 0 (`ZapataDisenador.cs:86`).
- `TipoLosa.Bordes` asume length==4 sin validar invariante (`Sistema.cs:508`).
- Handlers huérfanos `OnLaunchLosasExeClick/OnBrowseLosasExe` + comentarios stale (líneas 19, 248) (`MainWindow.axaml.cs`). ↳ 3.5
- Dos superficies de atajos desincronizadas; "Pegar de Excel" duplicado (`KeyboardShortcutsWindow.axaml`; `MainWindow.axaml:263`). ↳ 3.5
- Asset PNG duplicado byte-idéntico (`src/Resources/branding/` y `src.Core/Resources/`); SVG de 984KB con C2PA inflando el binario; Memoria sin `Window.Icon`. ↳ 3.9
- `VersionMotor` vacío → "Losas v" colgante; refresco de contadores Perdomo frágil (`NivelesView.axaml:328`; `src.Memoria/.../MainViewModel.cs:931`). ↳ 3.10
- `OnApplyDLText` no borra el temp file si `ReadAll` lanza (fuga de archivo temporal) (`MainWindow.axaml.cs:466-469`).

---

## 5. Qué reconstruir y cómo (interfaces con veredicto de reescritura)

Todas son **reescrituras parciales** (VMs/servicios/chrome); ninguna interfaz se rehace de cero.

### 5.1 `VigaEditorViewModel`
- **VMs:** un VM colgando de `MainViewModel`, migrado a `CommunityToolkit.Mvvm`. `[ObservableProperty]` para `VigaActiva/TramoSeleccionado/ApoyoSeleccionado/CargaSeleccionada`; comandos `[RelayCommand]` con `[NotifyCanExecuteChangedFor]`.
- **Modelo:** `TramoViga.Base/Peralte` recalculan `Inercia=b·h³/12`; columna "I" readonly.
- **Vistas:** `.axaml` casi intacta (3 DataGrids + 4 PlotView).
- **Flujo:** selección de fila → `[ObservableProperty]` → comando rehabilitado; edición de celda → `PropertyChanged` del modelo → recálculo async cancelable → series. Suscripción a `NivelActivo` que resetea `VigaActiva`.

### 5.2 `ColumnasEditorViewModel` + `ColumnasEditorView.axaml`
- **VMs:** `Niveles` y `NivelSeleccionado` como propiedades reales; setter de `NivelSeleccionado` reevalúa `Columnas`, limpia `Seleccionada`, re-suscribe `CollectionChanged`. Setter de `Seleccionada` suscribe `Columna.PropertyChanged` → `RecalcularDiseno`. Sincronía con `MainViewModel.NivelActivo`.
- **Vistas:** el ComboBox `Niveles/NivelSeleccionado` pasa a funcionar; `x:CompileBindings=True`; `OnAgregar/OnEliminar`→Commands; título neutral.
- **Flujo:** cambiar nivel → `Columnas` notifica → DataGrid se llena → seleccionar fila → sección + P-M.

### 5.3 VMs de Niveles/Sistemas (`MainViewModel` LosasPlus + Memoria)
- **Dominio:** mover `Uso`/`Cota` de `Sistema` a `Nivel` (migración v3→v4). `Proyecto.Sistemas` `[Obsolete]` solo-lectura.
- **VMs:** único par `NivelActivo`/`SistemaActivo`; borrar setter alias destructivo; `AgregarSistema/EliminarSistema`→`NivelActivo.Sistemas`; `EnumerarSistemas(edificio)` para export/validación/búsqueda; recordar `SistemaActivo` por nivel.
- **Vistas:** Memoria introduce `NivelActivo` real y corrige vocabulario nivel/sistema.

### 5.4 Servicio de persistencia unificado
- **Servicio:** un solo serializer del árbol completo con magic-header; carga completa; undo total; backup simétrico.
- **VMs:** `IsDirty` + handler `Closing` cancelable (Guardar/Descartar/Cancelar); `EliminarSistema` con snapshot+confirmación; separar identidad `.dl`/`.lpx.json`.

### 5.5 Shell de LosasPlus (chrome)
- **VMs:** `CurrentView` (UserControl) que notifica al setear `ModoActivo`.
- **Vistas:** un sidebar (RadioButtons agrupados) + `<ContentControl Content="{Binding CurrentView}"/>` que reemplaza los 19 `IsVisible`; top-bar contextual con Archivo / **Calcular** / **Generar geometría** / Exportar. Reutilizar **todas** las sub-vistas. 100% español.

### 5.6 `MotorFeaService`
- **Servicio:** `AllowNamedFloatingPointLiterals`; degradar "sección insuficiente" sin abortar lote; mapeo `Tipo`→bordes; `MSx`/`MSy` por dirección; `fy`/`fc` desde `Sistema`; conectar o retirar la ruta de acero de borde.

### 5.7 `CadCanvasHost`
- **Host:** matriz de coordenadas única reversible (render = inverse-hit); cachear `LayoutResult` por `RevisionSistema`; grilla por viewport; encuadre por losas como fallback; convención Y unificada.

---

## 6. Plan por fases (con dependencias)

> Orden por riesgo/valor: primero detener la pérdida de datos, luego coherencia de dominio, luego branding/UX, luego reescrituras profundas. Las fases A y B comparten núcleo (la fachada `Niveles[0]`), así que conviene atacarlas casi juntas.

### Fase A — Desbloquear funcional y **detener pérdida de datos** (prioridad máxima)
*Sin dependencias previas. Habilita todo lo demás.*
- **A1.** Persistencia: dirty-check + `Closing` cancelable en ambas apps; confirmación + snapshot en `EliminarSistema`. (Independiente.)
- **A2.** Motor FEA: `AllowNamedFloatingPointLiterals` + degradado de "sección insuficiente". (Independiente; corrige crash.)
- **A3.** Recientes: el setter de `ProyectoRecienteSeleccionado` deja de llamar `Execute(null)`; pasa a `RaiseCanExecuteChanged`. (Independiente.)
- **A4.** CanExecute: `RaiseCanExecuteChanged` en Vigas, Columnas, Cargas/Combinaciones, CAD muro. (Independiente; o se absorbe en D al migrar a Toolkit.)
- **A5.** Columnas: parche mínimo `ColumnasEditor?.Recargar()` en setter de `NivelActivo`, restore y `AbrirDL`. (Independiente; desbloquea la tabla.)

### Fase B — Coherencia de dominio (Nivel/Sistema)
*Depende de A1 (dirty-check evita perder datos mientras se migra) y comparte raíz con A5.*
- **B1.** Erradicar la fachada como destino de **mutaciones**: `AgregarSistema/EliminarSistema`→`NivelActivo.Sistemas`; borrar setter alias destructivo. (Depende de tener tests de niveles — crear primero.)
- **B2.** `EnumerarSistemas(edificio)` en export `.DL`/validación/búsqueda; guardado del árbol completo en `ProyectoService`/`ProyectoSerializer` unificado. (Depende de B1 y de la decisión de producto .DL por-nivel vs por-edificio.)
- **B3.** Mover `Uso`/`Cota` a `Nivel` + migración v3→v4; `CantidadNiveles` correcto. (Depende de B1/B2.)
- **B4.** Carga completa (Cargas/Combinaciones/metadata) + undo total. (Depende de B2.)

### Fase C — Branding / Logo / UX
*Mayormente independiente; puede ir en paralelo. C2 depende del rename de assembly (fase 2 del rebrand) — diferible.*
- **C1.** Logo: PNG transparente multiescala, reemplazar `<svg:Svg>` por `<Image>`, sobrescribir ambos PNG (incl. encabezado Word), borrar SVG de 984KB. (Independiente.)
- **C2 (display).** Reemplazar ~20 literales de marca por "EstructurasRD" + constante central `Branding.Producto`; **no** tocar namespaces/AssemblyName/rutas/`%AppData%`/URIs/`.lpx.json`. (Independiente; los strings mixtos con `.exe` esperan al rename coordinado.)
- **C3.** Checklist real en `GenerarView` + aviso de datos Perdomo faltantes en `GenerarMemoria`; label F. Perdomo refinado. (Independiente.)
- **C4.** Atajo Ctrl+L cableado; deduplicar atajos/"Pegar de Excel"; limpiar handlers huérfanos y comentarios stale. (Mejor junto a D-shell.)

### Fase D — Reescrituras (profundas)
*Dependen de A (CanExecute estabilizado) y B (dominio coherente) para no reescribir sobre arena.*
- **D1.** Migrar `VigaEditorViewModel` a `CommunityToolkit.Mvvm` + acoplar `Inercia`↔sección + suscripción a nivel. (Depende de A4; absorbe A4 para Vigas.)
- **D2.** Reescribir `ColumnasEditorViewModel` autónomo + alinear `ColumnasEditorView` + `x:CompileBindings=True`. (Depende de A5/B1.)
- **D3.** Shell de LosasPlus → sidebar + router `CurrentView` (blueprint MemoriaPlus). (Depende de C4; sub-vistas intactas.)
- **D4.** CAD: matriz de coordenadas única + caché de `LayoutResult` + grilla por viewport. (Independiente de A/B; alto riesgo de regresión visual → requiere verificación manual.)
- **D5.** FEA: mapeo `Tipo`→bordes, `MSx`/`MSy` por dirección, `fy`/`fc` desde `Sistema`, ruta de acero de borde conectada o retirada. (Depende de A2.)

**Dependencias resumidas:** A es raíz de todo. B depende de A1 y de crear tests de niveles. D1/D2 dependen de A4/A5/B1. D3 depende de C4. C y D4/D5 son ampliamente paralelizables. **Hito de seguridad:** completar A1+A2+B1+B2 antes de cualquier release elimina los 4 bugs críticos (pérdida de datos x3 + crash FEA).

---

### Nota de confianza
- **Alta:** áreas 3.1-3.6, 3.8-3.10 (verificadas contra `.axaml`/código real por los diagnósticos; FEA reproducido empíricamente).
- **Media:** 3.7 CAD (lectura de código, app no compilada/ejecutada; varios "Confianza media" en el propio sweep) y las rutas no revisadas a fondo (`PlantaCanvas`, `CadCanvasHost` render/pointer, exporters SAF/IFC, IA Qwen, plugins).
- La numeración de líneas proviene de los insumos; los **paths** se verificaron como existentes en el repo, pero las **líneas exactas** no se re-auditaron una a una.
