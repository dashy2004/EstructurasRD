# Fase A — Detener la pérdida de datos + desbloquear funcional — Plan

> **Ejecución:** subagent-driven-development **SECUENCIAL** (tareas acopladas por archivo: A1/A3/A5 tocan `src/ViewModels/MainViewModel.cs`). Compuerta tras CADA tarea: `dotnet test` ≥ **1009** verde.

**Spec / diagnóstico fuente:** `docs/ANALISIS_UI_v2.md` (§3.1–3.6, §4 CRÍTICA/ALTA, §6 Fase A).

**Goal:** Eliminar los 4 bugs críticos (pérdida de datos ×3 + crash FEA) y desbloquear el editor de Columnas, sin regresión (1009 tests verdes).

**Toolchain (Linux):**
```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd /home/gdc/Downloads/EstructurasRD-engine
dotnet build LosasPlus.Linux.sln --no-incremental     # SIEMPRE --no-incremental (oculta AVLN2000 si no)
dotnet test tests/LosasPlus.Tests/LosasPlus.Tests.csproj   # baseline: 1009/1009 verde
```
Pin crítico: **Avalonia.Svg.Skia 11.2.0.2** (no 11.3.x). Diálogos: servicio propio (AppServices). Commands: `CanExecuteChanged` propio + `RaiseCanExecuteChanged()` (Avalonia NO tiene auto-requery WPF).

**Orden (minimiza acoplamiento):** A2 (aislado) → A4 (VMs separados) → A5 → A3 → A1 (A5/A3/A1 comparten `MainViewModel.cs`, por eso van en serie y al final).

---

## Task 1 — A2: crash del motor FEA con losa fina (CRÍTICA)

**Archivos:** `src.Core/Services/MotorFeaService.cs`; Test `tests/LosasPlus.Tests/` (nuevo o ampliar `MotorFeaCutoverTests`).
**Diagnóstico (§3.6):** `JsonDocument.Parse`/deserialización sin `AllowNamedFloatingPointLiterals` → al diseñar una losa sub-dimensionada el motor Python emite `NaN` y el parser C# lanza `JsonReaderException`; en lote (`CalcularConMotorAsync`) una losa insuficiente aborta todo.

**Fix:**
1. Leé `MotorFeaService.cs` y localizá el/los `JsonDocument.Parse(...)` / `JsonSerializer.Deserialize(...)` (≈ línea 76).
2. Pasá `JsonDocumentOptions`/`JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }` en TODO punto de parseo del resultado del motor.
3. Degradado: cuando una franja venga con `NaN`/`seccion_insuficiente`, mapeala a un resultado "SECCIÓN INSUFICIENTE" (bandera/centinela en el modelo de resultado) en vez de propagar `NaN`/excepción. En `CalcularConMotorAsync` (lote), **continuá** con las demás losas (capturá por-losa, no abortes el lote).

**Test:** parseá un JSON de resultado del motor que contenga `NaN` (string con `"mu_x": NaN, ...` o `seccion_insuficiente: true`) → `ParsearResultado` NO lanza y marca la franja insuficiente. (Si reproducir el CLI es caro, basta un JSON sintético con `NaN`.)

**Gate:** `dotnet test` ≥ 1010 verde. **Commit:** `fix(motor-fea): parsear NaN (AllowNamedFloatingPointLiterals) y degradar seccion insuficiente sin abortar`.

---

## Task 2 — A4: CanExecute congelado en los editores (ALTA)

**Archivos:** `src/ViewModels/Vigas/VigaEditorViewModel.cs`, `src/ViewModels/ColumnasEditorViewModel.cs`, `src/ViewModels/CargasCombinacionesViewModel.cs`, `src/ViewModels/Cad/CadEditorViewModel.cs` (verificá el path del CAD). Tests: ampliar los `*ViewModelTests`.
**Diagnóstico (§3.1/§3.2/§4):** los setters de las propiedades de selección/estado no llaman `RaiseCanExecuteChanged()`, y Avalonia no reevalúa CanExecute solo → botones (Eliminar/Agregar) quedan congelados al cambiar la selección.

**Fix (por VM):**
1. Leé cada VM; identificá los comandos cuyo `CanExecute` depende de estado mutable (p.ej. `_vigaActiva`, `_tramoSeleccionado`, `_apoyoSeleccionado`, `_cargaSeleccionada` en Vigas; `Seleccionada` en Columnas; selección en Cargas/Combinaciones y CAD muro).
2. En el **setter** de cada propiedad de la que dependen, tras notificar la propiedad, llamá `RaiseCanExecuteChanged()` sobre los comandos afectados. Para eso, guardá los comandos como el tipo concreto `RelayCommand` (no `ICommand`) si hace falta para acceder al método.
3. NO cambies la lógica de los comandos ni la vista; es solo el disparo de la reevaluación.

**Test:** por VM, un test que: setea la propiedad de selección a un item válido → el comando dependiente `CanExecute(null)` pasa a `true` Y `CanExecuteChanged` se disparó (suscribí al evento y verificá que se levantó). Cubre el gate que los tests viejos saltaban (llamaban `Execute(null)` directo).

**Gate:** `dotnet test` verde (suma los nuevos). **Commit:** `fix(ui): disparar RaiseCanExecuteChanged en los setters de seleccion (Vigas/Columnas/Cargas/CAD)`.

---

## Task 3 — A5: editor de Columnas vacío (ALTA)

**Archivos:** `src/ViewModels/MainViewModel.cs`. Test: `tests/LosasPlus.Tests/` (VM-level).
**Diagnóstico (§3.2):** `ColumnasEditorViewModel.Recargar()` solo se llama en el ctor (cuando `NivelActivo` aún es null) → la tabla bindea a null y nunca se llena. El setter de `NivelActivo` no propaga `Recargar()` (contraste: `SistemaActivo` sí llama `Aceros?.Recargar()`).

**Fix:**
1. Leé el setter de `NivelActivo` (≈ L630) y los puntos de restore (`RestoreSnapshot`) y de apertura `.DL` (`AbrirDL`/`AbrirDLAsync`).
2. Agregá `ColumnasEditor?.Recargar()` en: (a) el setter de `NivelActivo` (tras fijar el nivel), (b) el restore de snapshot, (c) tras abrir `.DL`. (Mismo patrón que `Aceros?.Recargar()`.)

**Test:** crear un `MainViewModel` con un edificio que tenga columnas en un nivel, fijar `NivelActivo` a ese nivel, y verificar que `ColumnasEditor.Columnas` deja de ser null/vacío (refleja las columnas del nivel).

**Gate:** `dotnet test` verde. **Commit:** `fix(columnas): recargar el editor al cambiar NivelActivo / restore / abrir .DL`.

---

## Task 4 — A3: seleccionar un reciente lo abre y puede perder cambios (ALTA)

**Archivos:** `src/ViewModels/MainViewModel.cs`.
**Diagnóstico (§4 ALTA):** el setter de `ProyectoRecienteSeleccionado` (≈ L109) llama `AbrirEnEditorCommand.Execute(null)` → un solo clic en la lista de recientes **abre** el proyecto de inmediato (salta al editor, puede descartar cambios).

**Fix:**
1. Leé el setter de `ProyectoRecienteSeleccionado`.
2. Quitá la llamada a `Execute(null)`; el setter solo fija la selección (y a lo sumo `RaiseCanExecuteChanged()` del comando de abrir). La apertura queda para la acción explícita (doble-clic / botón Abrir), que ya existe.

**Test:** setear `ProyectoRecienteSeleccionado` NO cambia el modo/editor activo ni dispara la carga (verificá que el proyecto activo no cambió).

**Gate:** `dotnet test` verde. **Commit:** `fix(recientes): seleccionar un reciente ya no lo abre de inmediato`.

---

## Task 5 — A1: pérdida de datos al cerrar/nuevo/abrir + EliminarSistema sin confirmación (CRÍTICA)

**Archivos:** `src/ViewModels/MainViewModel.cs`, `src/MainWindow.axaml.cs`; `src.Memoria/ViewModels/MainViewModel.cs`, `src.Memoria/MainWindow.axaml.cs`. Tests: VM-level donde aplique.
**Diagnóstico (§3.4/§4):** ninguna ventana registra `Closing` cancelable; no hay `IsDirty`; cerrar/Nuevo/Abrir descarta trabajo sin aviso. `EliminarSistema` (LosasPlus) borra sin snapshot ni confirmación.

**Fix (decisiones tomadas — opción correcta):**
1. **`IsDirty`** en ambos `MainViewModel`: agregá una propiedad `bool IsDirty`. Localizá el chokepoint de mutación con undo (`RegistrarSnapshot`/`PushUndo`/equivalente, ya que existe `RestoreSnapshot`) y seteá `IsDirty = true` ahí; si no hay chokepoint único, seteala en los comandos de mutación principales (agregar/eliminar/editar). Reseteala a `false` en Guardar/Abrir/Nuevo (tras éxito).
2. **Closing cancelable** en ambas `MainWindow.axaml.cs`: en el handler `Closing`, si `IsDirty`, cancelá el cierre (`e.Cancel = true`), mostrá un diálogo (servicio AppServices) **Guardar / Descartar / Cancelar**; Guardar→guardar y cerrar, Descartar→cerrar, Cancelar→permanecer. (Usá el patrón async de Avalonia: cancelar, await diálogo, cerrar programáticamente si corresponde.)
3. **Nuevo/Abrir** con `IsDirty`: el mismo diálogo de confirmación antes de descartar.
4. **`EliminarSistema`** (LosasPlus `MainViewModel`, ≈ L1013): registrar snapshot (undo) + confirmación antes de borrar (Memoria ya confirma — replicá ese patrón).

**Test:** VM-level — tras una mutación `IsDirty==true`; tras guardar/abrir/nuevo `IsDirty==false`. `EliminarSistema` registra snapshot (el undo restaura el sistema). (El `Closing`/diálogo, al ser UI, se verifica con build verde + lógica del VM testeada; smoke manual opcional.)

**Gate:** `dotnet test` verde. **Commit:** `fix(persistencia): IsDirty + Closing cancelable (ambas apps) + EliminarSistema con snapshot/confirmacion`.

---

## Cierre
- Tras las 5 tareas: `dotnet build LosasPlus.Linux.sln --no-incremental` 0/0 + `dotnet test` ≥ 1009 (+ los nuevos) verde.
- Revisión adversarial final (los 4 críticos cerrados, sin regresión, sin pérdida de datos residual).
- Smoke opcional: `timeout 8 ./src/bin/Debug/net8.0/LosasPlus` (exit 124 = arrancó OK) en ambas apps.
- PR contra `avalonia-linux`.

## Criterio de aceptación
1. Suite verde (≥ 1009, sin regresión).
2. Guardar/abrir multinivel ya no pierde niveles silenciosamente (la raíz se cierra en Fase B; aquí se **frena la pérdida silenciosa** con dirty-check + confirmaciones).
3. El motor FEA no crashea con losa insuficiente (degrada).
4. El editor de Columnas se llena al elegir nivel.
5. Seleccionar un reciente no abre/descarta sin querer.
