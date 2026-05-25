# Notas de Lanzamiento — EstructurasRD v0.8.0

### Liga B (UI Optimization) + Liga C (Planta Estructural) + Fase 3D-II MVP

Este release consolida tres bloques mayores de trabajo sobre la suite
estructural **EstructurasRD** — la evolución del histórico
*LosasPlus*:

1. **Liga B — UI Optimization**: una capa de presentación moderna con
   panel persistente del elemento activo, suscripción débil al servicio
   de selección, throttle anti-saturación y ratio Demanda/Capacidad
   asíncrono.
2. **Liga C — Vista Planta Estructural**: nuevo modo 2D con render
   integrado de grillas + losas + muros + columnas + vigas + zapatas y
   edición por arrastre + creación + borrado, con snap a grilla
   automático.
3. **Fase 3D-II MVP**: visor tridimensional HelixToolkit con cinco
   tipos de elemento estructural, selección bidireccional con paneles
   2D y banner persistente del modo de autoría.

> Build: **0 warnings / 0 errors**. Suite: **853 / 853 tests verdes**.
> Rama de release: `feat/saf-export-interop1`.

---

## 1. Liga B — UI Optimization

### B5 · Banner persistente del modo de autoría 3D
Banner naranja en la parte superior del shell, visible **sólo** cuando
`Viewport3D.HerramientaActiva ≠ Selección`. Indica al ingeniero el modo
actual y le recuerda que `ESC` aborta y vuelve a Selección — atajo
estándar CAD/BIM. Implementado con DataTriggers para que sea declarativo
y observable sin code-behind adicional.

### B2 · Panel persistente "Elemento Activo"
Panel flotante en la esquina superior-derecha del área de contenido que
muestra el elemento estructural seleccionado actualmente: **tipo · Id ·
nombre · dimensiones · ratio D/C**. Cuatro garantías de ingeniería:

- **Suscripción débil (`WeakEventManager`)** al servicio de selección —
  el ciclo de vida del `MainViewModel` no es retenido por el servicio
  global.
- **DTO read-only inmutable (`ElementoActivoViewModel`)** — la UI
  nunca posee referencias mutables al dominio. Toda mutación pasa por
  comandos transaccionales.
- **Throttle 30 ms con `DispatcherTimer`** — coalesce ráfagas de
  selección (Shift+click, selección por ventana) en una sola resolución
  para no inanir el `ThreadPool`.
- **Null Object pattern** — la `DomainKey` huérfana o concurrentemente
  eliminada degrada al singleton `Vacio` sin propagar
  `NullReferenceException` al binding tree.

### B3 · Toolbar 3D extendida + comandos de mutación resilientes
La toolbar del visor 3D pasa de 3 a 4 herramientas: 🔍 Selección, 🟨
Crear Columna, 🟧 Crear Viga (2 clicks con preview line live), 🗑
Borrar elemento. Cuatro directivas estrictas:

- **Idempotencia por `TransactionId` (Guid)** con HashSet acotado a 256
  entradas y eviction simple.
- **Ghost Deletion semántica** — `IAutoria3DService.BorrarElementoPorKey`
  retorna `false` ante Id huérfano, tipo `Muro` o cualquier excepción
  interna, **sin propagar** `NullReferenceException`.
- **Desacoplamiento toolbar↔Viewport3D** vía `IAutoria3DService`
  abstracta — la UI invoca comandos del `MainViewModel`, nunca métodos
  directos del sub-VM gráfico.
- **Sweep de ghost artifacts** ante cualquier fallo de mutación
  (`Seleccion.LimpiarSeleccion(this)` + `RegenerarEscenaAsync`) — el
  viewport nunca queda con `Element3D` referenciando entidades
  removidas del dominio.

### B4 · Integración asíncrona del ratio D/C
La propiedad `RatioDC` del panel "Elemento Activo" se computa **en el
ThreadPool** vía `IRatioCalculatorService` con coordinador de ciclo de
vida (`RatioComputacionCoordinator`) basado en `CancellationTokenSource`
secuenciales — cuando el `ElementoActivo` cambia, la tarea anterior se
cancela inmediatamente, sin riesgo de payloads tardíos corrompiendo el
estado visual. NaN/error → `"—"` en la UI vía `NaNToTextConverter`.

---

## 2. Liga C — Vista Planta Estructural

Nuevo modo de navegación **"📐 Planta Estructural"** al lado del visor
3D. Es la evolución natural del Lienzo CAD legacy: render unificado
de la planta estructural 2D del nivel activo, con edición por
arrastre + creación + borrado.

### C1 · Canvas 2D read-only + selector de nivel
- Canvas custom (`FrameworkElement` con `DrawingVisual` layers +
  `TransformGroup` para zoom/pan) — rendimiento DirectComposition-grade.
- Render de **grillas A/B/C × 1/2/3** con etiquetas circulares en los
  extremos.
- Render de **columnas** (cuadrados azules), **vigas** (líneas
  naranjas), **zapatas** (rectángulos punteados grises debajo de
  columnas).
- Convención clásica de planos estructurales: Y crece hacia arriba.
- Selector de nivel en la toolbar (combobox sincronizado con
  `MainViewModel.NivelActivo`) permite cambiar de piso sin afectar el
  resto del shell.

### C1.5 · Render losas + muros del Sistema activo
La Planta Estructural incorpora ahora el render que antes vivía sólo
en el Lienzo CAD legacy:

- **Losas** dibujadas con `LayoutSolver.Solve(sistema)` (mismo motor
  que el CAD) — rectángulos verdes semi-transparentes con etiqueta
  centrada de 3 líneas (Id / Lx×Ly / Tipo).
- **Muros** como líneas gruesas grises cuyo espesor en píxeles es
  `Espesor × PxPorMetro` (mínimo 2 px). Caps redondeados.
- Click sobre losa/muro publica vía `SeleccionService` y el panel
  "Elemento Activo" se actualiza.

### C2 · Edición de planta — drag + crear + borrar con snap
Toolbar flotante de 4 herramientas (mismo patrón UX que el visor 3D):

- 🔍 **Selección / Mover**: click selecciona; **arrastrar** columna o
  zapata mueve su `PosX/PosY` con snap a grilla. La zapata pareada
  (mismo Id en nivel base) se arrastra junto con la columna.
- 🟨 **Crear Columna**: 1 click. Snap automático a la intersección de
  grilla más cercana. Si nivel base → crea zapata pareada
  automáticamente.
- 🟧 **Crear Viga**: 2 clicks (inicio + fin) con snap en ambos
  endpoints. Crea viga con 1 tramo + 2 apoyos fijos.
- 🗑 **Borrar elemento**: click sobre target lo elimina (Ghost
  Deletion). Muros y losas blindados.
- **Tecla Delete** borra el elemento actualmente seleccionado.

Snap a grilla **activo por defecto** (radio 0.80 m). **Shift**
presionado durante drag/crear desactiva el snap temporalmente —
convención CAD clásica.

---

## 3. Fase 3D-II MVP — Resumen acumulado

Para contexto del release v0.8.0, los hitos de la Fase 3D-II ya
integrados:

- **Módulo 1**: Losas renderizadas como prismas extruidos
  posicionados por `LayoutSolver`.
- **Módulo 2 A/B/C**: Dominio de grillas estructurales + motor de snap
  + creación interactiva de columnas en 3D con zapata pareada
  automática en cota base.
- **Banner naranja persistente** (B5) + ESC para abortar.
- **Visor read-only** con cinco tipos de elemento (losas + muros +
  columnas + vigas + zapatas) + grillas A/B/C × 1/2/3.

---

## 4. Estado de los módulos estructurales (importante)

> **Todos los módulos estructurales están operativos pero en fase de
> prueba.** No se han verificado en un proyecto de ingeniería real con
> condiciones de carga complejas. Se recomienda validar resultados
> contra otro software comercial (ETABS, SAFE, RFEM) antes de aprobar
> cualquier diseño para construcción.

### 🧪 Columnas (en prueba)
- ✅ Editor de geometría rectangular + capas de acero.
- ✅ Diagrama de interacción P-M uniaxial 2D según **ACI 318-19**.
- ✅ Verificación punto-a-punto con `RcDesignEngine`.
- ✅ Crear/Borrar/Mover desde la Planta + Vista 3D.
- ⚠️ **Pendiente**: detallado de acero comercial (convertir áreas crudas
  a barras `#N` con disposición de capas + estribos).

### 🧪 Vigas continuas (en prueba)
- ✅ Motor de **Rigidez Directa** (12-GDL) para vigas multi-tramo.
- ✅ Diagramas V(x), M(x), δ(x) en OxyPlot.
- ✅ Diseño RC ACI 318-19 (bloque de Whitney) + verificación D/C.
- ⚠️ **Pendiente**: detallado refuerzo comercial; conexión visual
  viga↔columna en planta (hoy las vigas se renderizan como líneas
  placeholder).

### 🧪 Losas (en prueba — heredado de LosasPlus)
- ✅ Motor **Pieper-Martens** legacy operativo.
- ✅ Importación de salida `.TXT` y mapeo a momentos por dirección.
- ⚠️ **Sin transmisión automática de cargas** a vigas — cada elemento
  se analiza con sus propias cargas declaradas independientes. La
  visión es un flujo integrado losa→viga→columna→zapata.

### 🧪 Zapatas aisladas (en prueba)
- ✅ Presiones de contacto biaxiales (4 esquinas).
- ✅ Verificación estructural ACI 318-19 (cortante uni- y
  bidireccional + flexión).
- ⚠️ **Pendiente**: evaluación del comportamiento bajo cargas reales
  con columnas conectadas y diagramas de presiones; detallado de
  bastones + ganchos.

### ⚠️ Muros (en prueba con restricción)
- ✅ **Dibujar** muros desde el Lienzo CAD legacy (segmento recto con
  espesor físico). Render en la Planta Estructural + visor 3D.
- ✅ Análisis con `AnalisisMuros` (resumen de envolvente).
- 🚫 **NO se pueden eliminar muros desde la aplicación**: la
  inmutabilidad de muros está declarada como invariante de dominio.
  La herramienta "🗑 Borrar elemento" rechaza explícitamente
  `TipoElemento.Muro` como No-Op declarado. Para remover un muro hay
  que editar el JSON `.lpx.json` manualmente.
- ⚠️ La funcionalidad de **dibujar muros está en prueba** — pueden
  existir edge cases en la lógica de intersección y validación
  geométrica que se descubrirán con uso real.

---

## 5. Visión del producto

EstructurasRD se posiciona como una **suite de diseño estructural
integrada para Dominicana**, no como una calculadora por elemento. La
hoja de ruta busca cerrar progresivamente la brecha entre análisis
puntual y modelo estructural completo:

### 🎯 Modelo estructural integrado
Flujo de cargas automático **losa → viga → columna → zapata** con
áreas tributarias calculadas a partir del grafo proyectado. El
ingeniero define cargas globales por entrepiso/techo y el sistema
propaga las reacciones por la jerarquía estructural.

### 🎯 Detallado de acero comercial
Convertir todas las áreas de acero crudas (cm²) a barras estándar
ACI 318 (`4#6`, `2#5+1#4`, etc.) con disposición de capas, estribos y
ganchos, persistido en el JSON v4 con migración silenciosa desde v3.

### 🎯 Diagramas M/V/Δ 3D reales
Cintas reales de momento/cortante/deflexión superpuestas a las vigas
en el visor 3D, alimentadas por `EnvolventeViga.Puntos` (no parábolas
estéticas). Vectores de reacción rojos saliendo de las zapatas
proporcionales a `P_u`.

### 🎯 Interoperabilidad BIM
- **SAF 2.2.0** export ya operativo (Fase INTEROP-I1).
- **SAF 2.2.0** import pendiente.
- **IFC 4** y **Revit** vía SAF AutoConverter como objetivos
  de iteración posterior.

### 🎯 **Adopción del nuevo Código de Construcción dominicano**
La versión actual implementa **ACI 318-19** combinado con el
**Reglamento Dominicano R-001**. Se planea **incorporar el nuevo
Código de Construcción dominicano** cuando se publique oficialmente
— la arquitectura modular del motor de diseño (`RcDesignEngine`,
`ZapataDesignEngine`, `ColumnaDesignEngine`) permite agregar el
reglamento como una capa adicional sin reescribir el dominio.

---

## 6. Limitaciones conocidas

- ⚠️ **Muros no eliminables desde la app** (ver sección 4).
- ⚠️ **Sin transmisión automática de cargas** entre elementos.
- ⚠️ **Vigas en planta como placeholder** — falta el concepto de
  endpoints planos en el dominio; las vigas se renderizan como líneas
  apiladas en filas arbitrarias hasta que C3 introduzca la conexión
  viga↔columna.
- ⚠️ **Plano PDF/DXF de referencia** sólo en el Lienzo CAD legacy, no
  en la Planta Estructural (pendiente de migración).
- ⚠️ **Vista de cortes/elevaciones** no implementada.
- ⚠️ **Sin push automático al remoto** — el commit queda local en la
  rama; el usuario decide cuándo publicar.

---

## 7. Cómo abrir y probar

1. **Ejecutable**:
   ```
   C:\Users\emilg\LosasPlus\src\bin\Debug\net8.0-windows\LosasPlus.exe
   ```
2. **Modo recomendado para explorar**: navegar al modo **"📐 Planta
   Estructural"** — es la vista que más cambió en v0.8.0.
3. **Vista 3D**: navegar al modo **"🧊 Vista 3D"**. Probar Crear
   Columna + Crear Viga + Borrar elemento desde la toolbar flotante.
4. **Panel "Elemento Activo"**: visible en todos los modos en la
   esquina superior-derecha; reacciona a cualquier click sobre un
   elemento estructural.
5. **ESC** vuelve a Selección desde cualquier modo de autoría.

---

## 8. Crédito y firma técnica

- Marca paraguas: **EstructurasRD**
- Módulo histórico: **LosasPlus** (motor Pieper-Martens de F. Perdomo)
- Stack: WPF .NET 8 + MVVM puro + OxyPlot 2.2.0 +
  HelixToolkit.Wpf.SharpDX 3.1.2 + StructuralAnalysisFormat SDK 1.7.3
- Build: 0 warnings / 0 errors / 853 tests verdes
- Rama: `feat/saf-export-interop1`
