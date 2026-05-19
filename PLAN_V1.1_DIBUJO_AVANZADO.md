# Plan v1.1 — Dibujo Avanzado (rev. 2 — "Esqueleto primero")

Arquitectura de la ergonomía de dibujo y la sincronización de UI de LosasPlus.
Esta revisión reorganiza el plan tras la revisión del Stakeholder Principal.

## 1. Contexto y motivo de la revisión

LosasPlus v1.0 (MVP) está en producción: editor CAD `CadCanvasHost`
(retained-mode, 4 capas de `DrawingVisual`), `SnappingEngine`, Undo/Redo,
mapeo de polígonos DXF → losas y chips de adyacencia (Fases 1-4 del
`PLAN_CAD_V1.md`).

La **rev.1** de este plan organizaba v1.1 en 4 "pilares" que **mezclaban**
lógica de dominio (Core) con interfaz (WPF). El Stakeholder Principal exige un
rediseño bajo la regla:

> **"Esqueleto primero, Interfaz después."**

Y suma cuatro requerimientos nuevos:

1. **Origen y contexto DXF** — el lienzo carece de sistema de coordenadas
   visual. La Capa 0 debe dibujar los ejes X e Y (origen 0,0) con grosores
   distintos y mostrar el *bounding box* del DXF si existe.
2. **Conexiones parciales (offset adjacency)** — las losas se desplazan
   libremente y se conectan en caras compartidas de forma **parcial** (Losa 2
   conecta con Losa 1 desplazada una distancia d₁ de la esquina).
3. **Acotado dinámico relativo** — mientras se dibuja/mueve, generar cotas
   dinámicas (distancias ortogonales d₁, d₂, d₃…) entre la losa activa y sus
   vecinas, como en un CAD moderno.
4. **Reestructuración del layout** — las pestañas laterales roban espacio
   horizontal; migrar a navegación superior (Top Tabs). *Fase posterior.*

**Reorganización.** El documento se divide en dos fases de ejecución
estrictamente ordenadas:

- **FASE A — El Esqueleto.** Toda la lógica de dominio en `src.Core`: geometría,
  topología de adyacencia parcial y el motor de cotas. Puro, sin UI, testeable
  de forma exhaustiva. **Se completa y se prueba antes de tocar la UI.**
- **FASE B — La Interfaz.** Todo `src/` (WPF): el sistema de coordenadas visual,
  el renderizado de cotas, la sincronización con la tabla, el Dynamic Input y la
  migración del layout a Top Tabs.

## 2. Principios de arquitectura (invariantes)

- **Esqueleto primero.** Ninguna sección de FASE B se implementa antes de que
  su dependencia de FASE A esté completa, probada y con la suite verde. Las
  features de FASE B consumen tipos y motores puros de FASE A.
- **Aislamiento del Core.** `src.Core` no referencia `System.Windows.*`. Toda la
  matemática nueva vive en Core como funciones puras; el dibujo y el input
  (mouse/teclado) viven en `src/`.
- **Modelo rect-only = invariante "Pieper-Martens".** Una `Losa` es, por
  construcción, un rectángulo ortogonal (`PosX, PosY, Lx, Ly`). No hay
  representación de rotación ni de polígono libre. Toda edición opera sobre esos
  4 escalares → es estructuralmente imposible producir una forma no-ortogonal.
- **Topología de cálculo inmutable.** El `.DL` que consume `Losas.exe` emite por
  cada borde exactamente tres campos —`B-I`, `B-J`, `BALANCEO`— (verificado en
  `DLFileService.cs`). El motor de cálculo deduce el acoplamiento de bordes a
  partir de la geometría de cada losa. **v1.1 no añade campos a `BordeAdic` ni
  al formato `.DL`** (ver §4.3).
- **SSOT.** La tabla del Editor (`LosasGrid` → `LosasFiltradas`) y el lienzo CAD
  (`CadCanvasHost.Sistema`) observan la misma `Sistema.Losas`.
- **MVVM sin input crudo.** El ViewModel nunca recibe `MouseEventArgs`/
  `KeyEventArgs`; el host resuelve el gesto y entrega DTOs de dominio.

## 3. Mapa de las dos fases

| Requerimiento | FASE A — Esqueleto (`src.Core`, puro) | FASE B — Interfaz (`src/`, WPF) |
|---|---|---|
| Sistema de coordenadas DXF | — (datos ya existen en `PlanoReferencia`) | B1 — dibujar ejes 0,0 + bounding box |
| Conexiones parciales (offset) | A3 — topología + matemática de offsets dₙ | (se visualiza vía B2) |
| Acotado dinámico | A4 — motor de cotas (cálculo) | B2 — renderizado de cotas |
| Edición restringida | A2 — `GeometriaEdicion` (resize/move) | B4 — la usa el Dynamic Input |
| Smart Tracking | A5 — `SnappingEngine.RastrearAlineacion` | B5 — renderizado de guías |
| Selección Esquema↔Tabla | — | B3 — `LosaSeleccionada` + sync |
| Dynamic Input | — | B4 — HUD superpuesto |
| Top Tabs | — | B6 — migración de layout |

---

# FASE A — EL ESQUELETO

Todo `src.Core`, ns `LosasPlus.Services`. Funciones puras, sin dependencias de
UI, con cobertura de tests exhaustiva. Es la base que FASE B consume.

## 4.1 · A1 — Primitivas geométricas comunes

Dos *value types* inmutables, en metros, sistema Y-descendente (igual que el
lienzo y que `PosX/PosY`):

| Tipo | Campos | Derivados |
|---|---|---|
| `PuntoM` | `X`, `Y` (double) | — |
| `RectM` | `X`, `Y`, `Ancho`, `Alto` (double) | `Der = X+Ancho`, `Inf = Y+Alto` |

Convención de bordes de un `RectM`: `Izq = X`, `Der = X+Ancho`, `Sup = Y`,
`Inf = Y+Alto`. Estos tipos son la moneda común de A2, A3, A4 y A5 — el host
los obtiene convirtiendo px↔metros y desde `PosX/PosY/Lx/Ly` de cada `Losa`.

## 4.2 · A2 — Geometría de edición restringida (Pieper-Martens safe)

Clase pura `GeometriaEdicion` con dos operaciones, ambas producen siempre un
rectángulo ortogonal:

- **`Mover(original, dx, dy)`** → traslada la esquina superior-izquierda;
  `Ancho/Alto` intactos.
- **`Redimensionar(original, asa, destino, ladoMin)`** → el "asa" arrastrada
  (una de 8: 4 esquinas + 4 puntos medios) define qué bordes se mueven; el
  borde/esquina **opuesto queda anclado**.

**Matemática del anclaje.** Sea el rect `(Izq, Sup, Der, Inf)`:
- Asa **inferior-derecha**: `Izq` y `Sup` anclados ⟹ `PosX, PosY` **no cambian**.
  `Der' = max(destino.X, Izq+ladoMin)`, `Inf' = max(destino.Y, Sup+ladoMin)` ⟹
  `RectM(Izq, Sup, Der'−Izq, Inf'−Sup)`. Sólo crecen/encogen `Lx, Ly`.
- Asa **superior-izquierda**: `Der` e `Inf` anclados. `Izq' = min(destino.X,
  Der−ladoMin)`, `Sup' = min(destino.Y, Inf−ladoMin)` ⟹ cambian `PosX, PosY`
  **y** `Lx, Ly` de forma compensada, manteniendo fija la esquina opuesta.
- Asas de **borde**: mueven un solo borde; el resto fijos.

El clamp a `ladoMin` impide el "flip" (que un borde móvil cruce al fijo).

Hoy esta matemática vive —en px y privada— en `CadCanvasHost.CalcularRectRedimensionado`
(Fase 3). FASE A la **extrae** a Core puro para testearla y para que el Dynamic
Input (B4) la reutilice con una dimensión tecleada en lugar del cursor.

## 4.3 · A3 — Topología de adyacencia parcial — la matemática de los offsets dₙ ★

Esta es la sección central del rediseño: cómo se modela que dos losas se
conecten en una **fracción** de su cara, con un desplazamiento.

### Decisión de modelo — el offset es DERIVADO, no almacenado

La topología de cálculo es inmutable (§2). `BordeAdic` lleva sólo `BI/BJ/Balanceo`
y `DLFileService` emite por borde exactamente `{B-I, B-J, BALANCEO}`. Una
conexión parcial **sigue siendo** topológicamente "A y B son adyacentes en una
cara X" — lo que cambia es **dónde** a lo largo de la cara, y eso queda
completamente determinado por `PosX/PosY/Lx/Ly` de ambas losas.

⟹ **El offset dₙ es una función pura de las posiciones absolutas.** No se
guarda en `BordeAdic`, no se serializa, no toca el `.DL`. La geometría es el
SSOT; el offset es una *medición* de ella. Esto mantiene el motor de cálculo y
el formato de archivo intactos.

### Notación

Una losa es un `RectM`. Para losas A y B con bordes `Izq/Der/Sup/Inf` y
tolerancia de contacto τ.

### Detección de cara compartida vertical (adyacencia X)

A y B comparten una cara vertical cuando el borde derecho de una toca el
izquierdo de la otra y sus rangos en Y se solapan:

- **Contacto:** `|A.Der − B.Izq| ≤ τ`  (A a la izquierda, B a la derecha).
- **Solape en Y:** `yₒ = max(A.Sup, B.Sup)`, `y₁ = min(A.Inf, B.Inf)`.
  Longitud de la cara compartida `L = y₁ − yₒ`. Existe cara real ⟺ `L > τ`
  (si `L ≤ 0` es un toque de esquina, no una cara).

### Los offsets dₙ — el corazón de la "adyacencia parcial"

La cara compartida `[yₒ, y₁]` es un **subconjunto** del borde derecho de A
(largo `A.Alto`) y del izquierdo de B (largo `B.Alto`). Los offsets describen
*dónde* se sitúa:

- `d_sup = B.Sup − A.Sup` — desplazamiento vertical del borde superior de B
  respecto al de A (con signo; > 0 ⟹ B corrida hacia abajo). **Éste es el "d₁"
  del ejemplo del Stakeholder.**
- `d_inf = B.Inf − A.Inf` — ídem para el borde inferior.
- "Entradas" desde cada esquina (siempre ≥ 0): `e_A↑ = yₒ − A.Sup`,
  `e_B↑ = yₒ − B.Sup`, `e_A↓ = A.Inf − y₁`, `e_B↓ = B.Inf − y₁`.
  Identidad: exactamente uno de `{e_A↑, e_B↑}` es 0 (la losa de `Sup` mayor
  define `yₒ`); ídem para el par inferior.

**Clasificación:** adyacencia **total** ⟺ `d_sup = 0 ∧ d_inf = 0`; **parcial**
⟺ algún offset ≠ 0.

**Caso con holgura.** Si A y B no se tocan (`B.Izq − A.Der > τ`), el motor
reporta la separación `g = B.Izq − A.Der` como offset de conexión — el acotado
(A4) la muestra igualmente, como un CAD muestra el gap al acercar dos objetos.

### Adyacencia horizontal (Y)

Simétrica, intercambiando X↔Y e `Izq/Der`↔`Sup/Inf`: la cara compartida es un
solape en X y los offsets `d_izq`, `d_der` son desplazamientos horizontales.

### Integración con lo existente

- **Movimiento libre.** Las losas del CAD están "ancladas" (`PosX/PosY`
  explícitos, Fase 2-3): ya se posicionan libremente. El `LayoutSolver` legacy
  —que asume adyacencia de cara completa en su BFS— sólo gobierna las losas
  *flotantes* del editor tabular; no se toca.
- **`AdyacenciaDetector` (Fase 4)** ya detecta el solape parcial (su prueba es
  `solape > τ`, no exige caras iguales). v1.1 **enriquece su resultado** para que,
  además del par `BI/BJ`, reporte la cara compartida `[yₒ, y₁]` y los offsets
  `d_sup/d_inf`. Sin cambios de topología.

### Non-goals y pregunta abierta

- **Non-goal (explícito):** v1.1 NO añade restricciones paramétricas — mover la
  Losa A **no** arrastra a la Losa B. Las posiciones son independientes; la
  adyacencia y los offsets se **re-derivan** tras cada movimiento. Un solver de
  restricciones es una feature aparte, mucho mayor.
- **Pregunta abierta para el Stakeholder:** la arquitectura trata el offset como
  metadato geométrico/visual. Si un modelo de cálculo futuro necesitara
  representar continuidad parcial, eso exigiría extender `BordeAdic` + un *bump*
  del formato `.DL` — fuera del alcance de v1.1, se señala explícitamente.

## 4.4 · A4 — Motor de cotas dinámicas

Clase pura `MotorCotas`. Dada la losa activa y las demás, produce el conjunto de
dimensiones ortogonales a dibujar.

**El tipo `Cota`** (value type):

| Campo | Significado |
|---|---|
| `Eje` | `Horizontal` o `Vertical` — orientación de la medida |
| `Valor` | distancia medida (double, metros) |
| `Desde`, `Hasta` | los dos `PuntoM` de referencia que se miden |
| `LineaCotaPerp` | coordenada perpendicular de la línea de cota (escalona cotas paralelas para que no se solapen) |
| `Etiqueta` | "d₁", "d₂", … — asignada en orden de la lista |

**`MotorCotas.Calcular(activa, otras, contexto)` → lista de `Cota`:**

1. Para cada losa de `otras`, evaluar su relación con `activa` con el análisis
   de A3 (cara compartida, parcial, o con holgura).
2. Una vecina es **relevante** si comparte cara o si un borde de `activa` está
   dentro de una banda de proximidad (algunos × el umbral de snap) de un borde
   suyo — así el acotado "aparece" al acercarse, como en un CAD.
3. Por cada vecina relevante, emitir:
   - la cota del **eje de conexión** (el gap, 0 si en contacto);
   - la(s) cota(s) **perpendicular(es)** — los offsets `d_sup`/`d_inf` de A3:
     las distancias d₁, d₂, …
4. Opcionalmente, cotas de la losa activa al **origen (0,0)** y/o al
   **bounding box del DXF** (`contexto`) — su posición absoluta.
5. Escalonar las líneas de cota paralelas vía `LineaCotaPerp` para evitar solape.

`MotorCotas` es puro ⟹ totalmente testeable sin UI. El renderizado es B2.

## 4.5 · A5 — Motor de alineación (Smart Tracking)

Ampliación de `SnappingEngine` (Core puro): el método `RastrearAlineacion`
proyecta ortogonalmente los bordes/centros de las otras losas; si el cursor cae
dentro del umbral de una proyección X o Y, lo engancha y devuelve, además del
punto ajustado, las **guías** que lo justifican.

| Tipo nuevo | Contenido |
|---|---|
| `EjeSnap` | enum `X` / `Y` |
| `GuiaAlineacion` | `Eje`, `Coordenada`, `DesdePerp`, `HastaPerp` — un segmento ortogonal en metros |
| `ResultadoTracking` | el `PuntoM` ajustado + lista de `GuiaAlineacion` |

A4 (cotas) y A5 (tracking) comparten la primitiva de "proyección de bordes de
vecinas"; conviene factorizarla una vez en A1/A3 y reutilizarla.

## 4.6 · A6 — Estrategia de pruebas de FASE A

Todo FASE A es puro ⟹ se cubre con tests unitarios exhaustivos en
`tests/LosasPlus.Tests/Services/` antes de pasar a FASE B:

- `GeometriaEdicionTests` — anclaje de la esquina opuesta, clamp de lado
  mínimo, sin flip, las 8 asas.
- `AdyacenciaOffsetTests` — cara total vs parcial, `d_sup/d_inf` con signo,
  toque de esquina (sin cara), caso con holgura, simetría X/Y.
- `MotorCotasTests` — selección de vecinas relevantes, gap, offsets, escalonado.
- `SnappingEngineTests` ampliado — rastreo X / Y / ambos / ninguno.

**Hito de FASE A:** build 0 warnings + suite completa verde. Recién entonces
empieza FASE B.

---

# FASE B — LA INTERFAZ

Todo `src/` (WPF): `CadCanvasHost`, `CadView`, `CadEditorViewModel`,
`MainWindow`, `MainViewModel`. Consume los motores puros de FASE A.

## 5.1 · B1 — Sistema de coordenadas visual (Capa 0)

Hoy `RedibujarGrilla` (Capa 0) usa dos plumas: fina (líneas cada 1 m) y media
(cada 5 m). El origen (0,0) se dibuja con la misma pluma que cualquier línea de
5 m — **no se distingue**.

**Cambios:**

- **Ejes X e Y.** Una tercera pluma `penEjes`, de grosor y color claramente
  distintos, para las líneas `x = 0` e `y = 0`. Marcador del origen (0,0): una
  pequeña cruz o círculo. Jerarquía visual (de menor a mayor peso): grilla fina
  (1 m) < grilla media (5 m) < ejes (0,0).
- **Bounding box del DXF.** Cuando hay un `Plano` no vacío, dibujar el rectángulo
  `[MinX, MinY] – [MaxX, MaxY]` (datos ya presentes en `PlanoReferencia`:
  `MinX/MinY/MaxX/MaxY`, `Ancho`, `Alto`, `EstaVacio`) con una pluma propia
  `penBBox` (p. ej. punteada). **Caveat de coordenadas:** el plano se dibuja
  (Capa 1) con flip-Y (`MaxY − y`); el bounding box debe trazarse en ese mismo
  marco para envolver el dibujo. La Capa 0 recibe el `Plano` y aplica el flip al
  trazar el bbox.

## 5.2 · B2 — Renderizado de cotas dinámicas

El overlay (`_capaOverlay`, Capa 3) dibuja cada `Cota` que devuelve `MotorCotas`
(A4). Anatomía de una cota dibujada:

- **Línea de cota** — paralela al eje medido, desplazada hacia afuera por
  `LineaCotaPerp`.
- **Líneas de extensión (testigo)** — perpendiculares, desde `Desde` y `Hasta`
  hasta la línea de cota.
- **Terminadores** — flechas o ticks en los extremos.
- **Texto** — el valor y la etiqueta ("d₁ = 1.25 m"), centrado sobre la línea.

**Dinámico:** durante un `Mover`/`Redimensionar`, cada `MouseMove` re-consulta
`MotorCotas.Calcular(...)` y redibuja las cotas junto al fantasma. Al soltar, las
cotas se limpian (coherente con el fantasma de la Fase 3). `MotorCotas` es O(n)
por movimiento — trivial para decenas de losas; si hubiera cientos, cachear los
`RectM` de las vecinas al iniciar el arrastre.

## 5.3 · B3 — Sincronización bidireccional (Esquema ↔ Tabla)

Estado actual: `MainViewModel` sólo modela multi-selección (`LosasSeleccionadas`);
`LosasGrid` es `SelectionMode=Extended` sin binding de `SelectedItem`; Editor y
CAD son vistas mutuamente excluyentes (`DataTrigger` sobre `ModoActivo`).

**Diseño** — una **propiedad compartida** `MainViewModel.LosaSeleccionada`
(`Losa?`, `INotifyPropertyChanged`), la "losa activa" del shell, distinta de la
multi-selección. Su setter es **idempotente** (si el valor no cambia, retorna
sin notificar) — defensa primaria contra la re-entrada de la sync bidireccional.

- **Canal hacia el lienzo:** `CadCanvasHost` gana una `DependencyProperty`
  `LosaSeleccionada` bindeada TwoWay; reemplaza el campo privado de la Fase 3.
  Su callback redibuja el overlay de selección y, opcionalmente, centra la
  cámara (`CentrarEnLosa` — sólo paneo, sin alterar el zoom).
- **Tabla → Lienzo:** `OnLosasSelectionChanged` setea `LosaSeleccionada`; el
  binding actualiza el DP del host → redibujo.
- **Lienzo → Tabla:** un clic en el lienzo escribe el DP (`SetCurrentValue`); el
  binding TwoWay actualiza la propiedad; el code-behind observa el
  `PropertyChanged` y hace `LosasGrid.SelectedItem = …` + `ScrollIntoView`.
- **Re-entrada:** el setter idempotente corta el bucle; una bandera
  `_sincronizandoSeleccion` en el code-behind además evita el `ScrollIntoView`
  redundante cuando el origen es la propia tabla.

## 5.4 · B4 — Dynamic Input (HUD)

`CadCanvasHost` (un `FrameworkElement` que dibuja `DrawingVisual` y sobreescribe
`VisualChildrenCount`/`GetVisualChild`) **no puede hospedar `UIElement` hijos** →
un control de texto debe **superponerse** como hermano.

- **`CadView.xaml`:** el `Border` que hoy contiene sólo el host pasa a contener
  un `Grid` con el host (capa 0) + un `Canvas` HUD (capa 1, `IsHitTestVisible=False`)
  con un panel de display flotante (oculto por defecto).
- **La captura de teclado la hace el host, no el HUD.** Durante un arrastre el
  host ya tiene `CaptureMouse()` y el foco; las teclas (dígitos, `.`,
  `Backspace`, `Enter`, `Esc`) llegan al host, que mantiene un **buffer**. El
  HUD es un display pasivo — sin foco, sin contienda con el mouse capturado.
- Con el buffer vacío la dimensión la da el mouse; al teclear, ese valor exacto
  alimenta el eje en edición recalculando el fantasma con `GeometriaEdicion`
  (A2). `Enter` confirma vía `ActualizarLosaCommand`; `Esc` descarta el buffer.
- El host emite un evento `HudActualizado` (con un DTO de capa vista que lleva
  el texto y un ancla de pantalla — vive en `src/`, no en Core); `CadView.xaml.cs`
  posiciona y actualiza el HUD.

## 5.5 · B5 — Renderizado de guías de tracking

El host, durante el arrastre, llama `SnappingEngine.RastrearAlineacion` (A5) con
los `RectM` de las otras losas, usa el punto enganchado para el fantasma y
dibuja las `GuiaAlineacion` devueltas como **líneas punteadas** en `_capaOverlay`.
Se borran al soltar.

## 5.6 · B6 — Reestructuración de layout (Top Tabs) — *Fase posterior*

**Estado actual** (`MainWindow.xaml`): `Grid` de nivel superior con dos
columnas — `240px` (sidebar) + `*` (contenido). La sidebar es un `Border` en la
columna 0 con un `Grid` interno de 5 filas: branding, navegación, encabezado
"SISTEMAS", `ListBox` de sistemas y footer. La navegación son **11 `RadioButton`**
en un `StackPanel` vertical (`GroupName="SidebarNav"`), cada uno con
`IsChecked` ligado a `MainViewModel.ModoActivo` vía `EnumToBoolConverter` +
`ConverterParameter`.

**Migración:**
- Reestructurar el `Grid` superior: eliminar la columna de 240px → una sola
  columna de contenido; la navegación pasa a una **fila superior** (la barra de
  acciones existente, o una nueva).
- Mover los 11 `RadioButton` a un `StackPanel`/`WrapPanel` **horizontal** en la
  barra superior, reestilados como pestañas. **El mecanismo de binding
  (`EnumToBoolConverter` + `ConverterParameter` + `ModoActivo`) no cambia** — la
  migración es de layout, no de lógica.
- **Punto no trivial:** la columna izquierda alberga **más que navegación** — el
  branding, el `ListBox` de sistemas y el footer. "Liberar el espacio
  horizontal" obliga a re-alojar esos tres: el branding va a una esquina de la
  barra superior; el `ListBox` de sistemas se integra en el contenido del modo
  `Explorador` o en una franja lateral colapsable; el footer pasa a una barra de
  estado inferior. Esta re-ubicación es el grueso del trabajo de B6.
- Es la **última** sección de FASE B (el Stakeholder la marcó "Fase posterior").

## 5.7 · B7 — Estrategia de pruebas de FASE B

FASE B es WPF (no unit-testeable sin arnés de UI): se verifica con build 0
warnings + smoke test de arranque + checklist manual (ver §10).

---

## 6. Modificaciones consolidadas por componente

### `CadCanvasHost` (`src/Views/Cad/`)
- Capa 0: ejes 0,0 con pluma propia, marcador de origen, bounding box del DXF (B1).
- Capa 3 (overlay): renderizado de cotas (B2) y de guías de tracking (B5).
- `DependencyProperty` `LosaSeleccionada` (reemplaza el campo privado de Fase 3) +
  callback de redibujo; método público `CentrarEnLosa` (B3).
- Buffer de teclado + evento `HudActualizado` (B4).
- `CalcularRectRedimensionado` → wrapper delgado de `GeometriaEdicion` (A2).
- En el arrastre: llamadas a `MotorCotas` (A4) y `RastrearAlineacion` (A5).

### `CadEditorViewModel` (`src/ViewModels/`)
- Toggles de feature: `CentrarCamaraEnSeleccion`, `DynamicInputHabilitado`,
  `SmartTrackingHabilitado`, `AcotadoDinamicoHabilitado`.
- **Sin más cambios:** `ActualizarLosaCommand` (Fase 3) sigue siendo el canal de
  commit que reutilizan todas las features.

### `MainViewModel` / `MainWindow` (`src/`)
- `MainViewModel`: + `LosaSeleccionada` (propiedad compartida, setter idempotente).
- `MainWindow.xaml(.cs)`: wiring de la sync de selección (B3); migración del
  layout a Top Tabs y re-alojo de branding / lista de sistemas / footer (B6).

### `CadView.xaml(.cs)`
- Host envuelto en `Grid` + `Canvas` HUD; binding TwoWay de `LosaSeleccionada`;
  code-behind que posiciona el HUD ante `HudActualizado`.

### `src.Core` — **sólo se agrega, nada se rompe**
- Nuevos: `PuntoM`, `RectM`, `GeometriaEdicion`, `MotorCotas` + `Cota`,
  `EjeSnap`/`GuiaAlineacion`/`ResultadoTracking`, ampliación de `SnappingEngine`
  y `AdyacenciaDetector`. `BordeAdic`, `Sistema`, `DLFileService` **no se tocan**.

## 7. DTOs / tipos / eventos nuevos

| Nuevo | Capa | Para |
|---|---|---|
| `PuntoM`, `RectM` | **Core** | A1 — primitivas comunes |
| `GeometriaEdicion` (clase pura) | **Core** | A2 |
| `Cota`, `MotorCotas` | **Core** | A4 |
| `EjeSnap`, `GuiaAlineacion`, `ResultadoTracking` | **Core** | A5 |
| Enriquecimiento de `AdyacenciaDetector` (cara + offsets) | **Core** | A3 |
| `MainViewModel.LosaSeleccionada` (propiedad) | VM (`src/`) | B3 |
| `CadCanvasHost.LosaSeleccionada` (DP) | View (`src/`) | B3 |
| `HudDimensionInfo` (DTO; lleva un punto de pantalla) | View (`src/`) | B4 |
| `CadCanvasHost.HudActualizado` (evento) | View (`src/`) | B4 |
| 4 toggles en `CadEditorViewModel` | VM (`src/`) | B1-B5 |

Todos los tipos de Core son value types / funciones puras — **sin
`System.Windows`**. El único DTO con tipo de UI vive en `src/`. El aislamiento
del Core se mantiene.

## 8. Riesgos y decisiones abiertas

- **Offset persistido vs derivado** — decidido: derivado (§4.3). Pregunta
  abierta señalada al Stakeholder por si el motor de cálculo necesitara
  continuidad parcial explícita (sería un cambio de `BordeAdic` + `.DL`).
- **Restricciones paramétricas** — non-goal explícito de v1.1.
- **Bordes obsoletos** — un `BordeAdic` creado y luego separado geométricamente
  queda "colgado"; el `AdyacenciaDetector` enriquecido podría además detectar
  bordes cuyos losas ya no se tocan. Mejora menor, anotada.
- **Top Tabs re-alojo** — el verdadero costo de B6 no es mover los `RadioButton`
  sino re-ubicar la lista de sistemas, el branding y el footer (§5.6).
- **Vistas mutuamente excluyentes** — Editor y CAD nunca se ven a la vez; la
  sync se observa al cambiar de modo. Un split-view sería un rework de layout
  posterior; el diseño de `LosaSeleccionada` ya lo soportaría.

## 9. Orden de implementación

**FASE A (completar y testear antes de tocar UI):**
1. A1 primitivas → A2 `GeometriaEdicion` → A3 adyacencia/offsets → A4 `MotorCotas`
   → A5 tracking. 2. A6 — suite Core verde. Hito: build 0 warnings.

**FASE B (cada sección consume su dependencia de FASE A):**
3. B1 sistema de coordenadas (independiente, bajo riesgo).
4. B3 sincronización de selección.
5. B2 renderizado de cotas (depende de A4) + B5 guías (depende de A5).
6. B4 Dynamic Input (depende de A2).
7. B6 Top Tabs — **última** ("fase posterior").

## 10. Verificación end-to-end

- **FASE A:** `dotnet build` 0 warnings; `dotnet test` verde con los nuevos
  `GeometriaEdicionTests`, `AdyacenciaOffsetTests`, `MotorCotasTests` y
  `SnappingEngineTests` ampliado.
- **B1:** importar un `.dxf`; verificar ejes 0,0 destacados, marcador de origen y
  bounding box punteado envolviendo el plano.
- **A3+B2:** mover la Losa 2 contra la Losa 1 con un desfase → aparece la cota d₁
  con el valor del offset; alejarlas → la cota muestra la holgura.
- **B3:** seleccionar en la tabla → resaltado (y centrado) en el lienzo; clic en
  el lienzo → fila seleccionada y visible en la tabla.
- **B4:** arrastrar un asa, teclear `3.5`, `Enter` → la losa toma 3.5 m exactos.
- **B5:** arrastrar cerca de la proyección de una losa distante → guía punteada
  + enganche.
- **B6:** la navegación queda arriba; el área de contenido gana el ancho de los
  240 px liberados; los modos siguen conmutando vía `ModoActivo`.
