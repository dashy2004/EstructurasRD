# Prompts Stitch — Fase 2 (pantallas faltantes + macro screens)

> Extiende `PROMPTS_STITCH.md`. Estos prompts cubren:
> - **1 reintento**: pantalla que Stitch saltó en la primera ronda.
> - **7 nuevas (macro)**: pantallas sistémicas que coordinan los módulos y no estaban en la primera tanda.
>
> Convención: cada prompt es autosuficiente. Prepender el bloque `DESIGN SYSTEM` del `PROMPTS_STITCH.md §0` antes de pegar a Stitch.

---

## ÍNDICE

| # | Pantalla | Slug | Razón |
|---|----------|------|-------|
| 1 | Editor de zapata poligonal (AnaZap) | `editor_de_zapata_poligonal_anazap` | Reintento — Stitch la saltó |
| 2 | Dashboard / Home del edificio | `dashboard_edificio` | Macro — vista resumen |
| 3 | Planta por nivel (2D top-down) | `planta_nivel_2d` | Macro — paso intermedio antes del 3D |
| 4 | Editor visual de planta con drag-and-drop | `editor_planta_drag_drop` | Macro — productividad masiva |
| 5 | Vista de cargas como vectores en 3D | `vista_cargas_3d` | Macro — verificación visual |
| 6 | Comparador de alternativas A vs B | `comparador_alternativas` | Macro — toma de decisión |
| 7 | Onboarding wizard | `onboarding_wizard` | Macro — primera ejecución |
| 8 | Vista de detalle de barras y armaduras (BIM-lite) | `detalle_barras_armaduras` | Macro — para taller |

---

## 1. Editor de zapata poligonal (AnaZap)  (slug: `editor_de_zapata_poligonal_anazap`)

```
TÍTULO: Editor de zapata poligonal — análisis biaxial sobre forma arbitraria (AnaZap)

[Pegar DESIGN SYSTEM del PROMPTS_STITCH.md §0]

CONTEXTO:
Equivalente al binario AnaZap.exe v2.07 de DISEST. Permite definir una
zapata como POLÍGONO de vértices arbitrarios (sentido anti-horario) con
HUECOS opcionales (sentido horario). Calcula propiedades geométricas
automáticamente, permite definir N en posición (Xn, Yn) además de Mx y My,
y reporta presiones bajo el suelo. NO diseña armaduras, solo analiza.

LAYOUT:

Top bar (altura 56 px):
  [← Atrás] · Breadcrumb "Cimentación > Z-Combinada-A"
  Botones: [Importar polígono DXF] [Calcular presiones] [Ver heatmap ↗]

Layout 3 paneles:

Panel izquierdo (320 px) — DEFINICIÓN DEL POLÍGONO:
  
  Card "VÉRTICES (anti-horario)":
    Tabla editable, columnas: #, X [m], Y [m].
    8 filas de ejemplo:
    ┌────┬─────────┬─────────┐
    │  # │  X [m]  │  Y [m]  │
    ├────┼─────────┼─────────┤
    │  1 │  0.00   │  0.00   │
    │  2 │  4.00   │  0.00   │
    │  3 │  4.00   │  3.00   │
    │  4 │  7.00   │  3.00   │
    │  5 │  7.00   │  6.00   │
    │  6 │  0.00   │  6.00   │
    └────┴─────────┴─────────┘
    [+ Vértice] [– Vértice] [↑] [↓] (reordenar)
  
  Card "HUECOS (horario)" — colapsable:
    Lista expandible, cada hueco con su propio mini-DataGrid.
    Inicialmente: "Sin huecos" + botón [+ Agregar hueco].
  
  Card "PROPIEDADES GEOMÉTRICAS" (auto-calculadas):
    Número de vértices:   6
    Área:                 30.00 m²
    Perímetro:            22.00 m
    C.G. Xg:              3.20 m
    C.G. Yg:              3.10 m
    Ix:                   71.4 m⁴
    Iy:                   62.8 m⁴
    Ixy:                  -3.2 m⁴
    Espesor H [m]:        [0.50]

Panel central — VISOR DEL POLÍGONO (vectorial, ~50% ancho):
  Fondo gris oscuro #1E1E1E con grid de 0.5 m.
  Polígono renderizado a escala:
    - Línea perimetral color azul accent #3DB1F2 grosor 2 px
    - Vértices como círculos pequeños numerados (1-6) blancos
    - Flechas anti-horarias sutiles entre vértices indicando sentido
    - Centroide marcado con cruz roja + label "C.G. (3.20, 3.10)"
    - Ejes X-Y de referencia con flechas grandes en esquina inferior izquierda
  Si hay hueco, dibujarlo con línea punteada en color rojo tenue (sentido horario).
  
  Sobre el polígono, dibujar:
    - Posición de la columna como cuadrado pequeño con etiqueta "C-3"
    - Posición de aplicación de N como punto rojo con etiqueta "(Xn, Yn)"
  
  Toolbar arriba del visor:
    [🔍+] [🔍-] [Fit] [Zoom 100%] · Snap: ☑ Grid 0.5 m  ☐ Vértices
  
  Hint inferior: "Click sobre el lienzo para agregar vértice · Doble-click
                  vértice existente para editar · Drag para mover"

Panel derecho (360 px) — CARGAS:
  
  Selector de combinación:
    Combinación activa: [D + L ▾]
    11 opciones (sincronizadas con Cargas y Combinaciones globales)
  
  Card "Cargas internas (auto)":
    Peso propio zapata [ton]:    36.0   (= 30 m² × 0.50 m × 2.4 ton/m³)
    Peso terreno relleno [ton]:   0.0
    Sobrecarga [ton]:             0.0
  
  Card "Cargas externas":
    N total [ton]:        [85.0]
    Mx [ton·m]:           [4.5]
    My [ton·m]:           [3.2]
    Xn (posición N) [m]:  [3.20]   ← por defecto = Xg (centroide)
    Yn (posición N) [m]:  [3.10]   ← por defecto = Yg
    
    Toggle: ☑ N aplicado en centroide (Xn=Xg, Yn=Yg)
            ☐ N aplicado en posición personalizada
  
  Card "Suelo":
    σ adm [kg/cm²]:       [1.50]
    Kcd (corta duración): [1.33]
  
  Card "Coeficientes sísmicos":
    Norma: ◉ DGRS (Ev=0.30·Sds)  ○ ASCE 7-05 (Ev=0.20)
    Sds [g]:              [1.00]
    Ev (calculado):       0.30

Bottom bar (cuando ya se calculó):
  Card grid horizontal 4×1:
    σ_max:          σ_min:        % área en compresión:   Estado:
    0.82 kg/cm²     0.05 kg/cm²   100%                    ✅ OK
  Botones: [Ver heatmap presiones] [Generar reporte AnaZap]
  Chip: "Cumple ASCE 7-05 §2.4 + criterios MOPC RD"

ESTÉTICA:
- Convención clara del sentido de los vértices (flechas)
- Auto-cálculo de propiedades al editar cualquier vértice
- Validación: vértices duplicados, polígono auto-intersectado, huecos fuera del polígono externo → mostrar warning chip rojo
- Soporte para zapatas combinadas (L invertida, T, en H, etc.) — el polígono puede tener formas no convexas

TAMAÑO: 1920×1080
```

---

## 2. Dashboard / Home del edificio  (slug: `dashboard_edificio`)

```
TÍTULO: Dashboard del edificio — vista resumen del proyecto completo

[Pegar DESIGN SYSTEM]

CONTEXTO:
Primera pantalla al abrir un proyecto. Vista panorámica con KPIs de todo el
edificio, alertas normativas, tareas pendientes, y accesos directos a los
elementos críticos. Reemplaza la pantalla "Datos generales" como home por
defecto (Datos generales pasa a ser sub-pestaña accesible desde aquí).

LAYOUT:

Top bar:
  Breadcrumb: "🏢 Torre PS2-A · Bloque A · v1.3"
  Acciones: [Editar datos] [Generar memoria] [Exportar todo] [3D]

Layout grid 3×3 de cards (con tamaños variables):

Fila 1 — KPIs (3 cards iguales, ancho 33% cada una):

  Card "PROYECTO" (icono 🏢):
    Torre PS2-A
    Bloque A · 4 niveles · 320 m²/planta
    Ing. responsable: Juan Pérez · CODIA 12345
    Fecha modelado: 2026-05-18
    Última edición: hace 2 horas
    [Editar →]

  Card "ELEMENTOS ESTRUCTURALES" (icono 📐):
    Cimentación:       12 zapatas
    Columnas:          48 (16 por nivel)
    Vigas:             96 (24 por nivel)
    Losas:             64 (16 por nivel)
    Muros:             8 (perimetrales)
    Total elementos:   228
    [Ver árbol completo →]

  Card "ESTADO DEL DISEÑO" (icono ✅):
    Diseño completo:   87% ████████░░
    Pendiente:
      • 4 columnas sin verificar pandeo
      • 1 zapata sin combinaciones de sismo
      • 8 vigas sin diseño RC final
    [Ver pendientes →]

Fila 2 — Alertas normativas (1 card grande full-width):

  Card "VALIDACIÓN NORMATIVA":
    Chip resumen: ⚠ "2 violaciones · 5 advertencias · 221 elementos OK"
    
    Lista de violaciones (color rojo):
      ✗ Columna C-7 (Nivel 2): esbeltez 67 > crítica 46 — ACI §10.10.1
        [Ver elemento →]
      ✗ Losa L-12 (Nivel 3): aspecto Ly/Lx = 2.45 fuera de tabla Pieper-Martens
        [Ver elemento →]
    
    Lista de advertencias (color naranja):
      ⚠ Viga V-15: cuantía 2.8% cerca del máximo (3.0% ACI §10.3.5)
      ⚠ Sds = 1.00 g asumido — verificar contra mapa MOPC zonificación
      ... (3 más colapsadas)
    
    Botón [Ver lista completa de validaciones →]

Fila 3 — Accesos rápidos a módulos (3 cards):

  Card "MÓDULOS ACTIVOS":
    Acceso directo a las pestañas. Para cada módulo:
      ┌─────────────────────────────────────┐
      │ 📐 LOSAS                             │
      │ 64 losas · método Pieper-Martens     │
      │ Memoria parcial: ✅ generada         │
      │ [Editor →]  [Ver memoria →]          │
      └─────────────────────────────────────┘
    Repetir para: VIGAS, COLUMNAS, ZAPATAS, CARGAS, 3D, EDUCACIÓN.

  Card "ACTIVIDAD RECIENTE" (lista cronológica):
    🕐 hace 2 h    · Modificada viga V-15 (Mu actualizado)
    🕐 hace 3 h    · Nueva zapata Z-12 añadida
    🕐 hace 5 h    · Importadas combinaciones desde Combinaciones.DZP
    🕐 ayer 18:30  · Generada memoria v1.2
    🕐 hace 2 días · Importado proyecto desde DXF
    [Ver historial completo →]

  Card "PRÓXIMA ACCIÓN SUGERIDA":
    Cara grande con icono y CTA:
    ⚠ "Tienes 2 violaciones críticas. Antes de generar memoria final, 
       resuélvelas:"
    
    Botón grande verde: [RESOLVER VIOLACIONES →]
    Botón secundario:   [Ignorar y generar memoria de todos modos]

Bottom bar:
  Métricas globales:
    Volumen total hormigón: 187.3 m³  
    Peso total acero: 8.4 ton (cuantía global 1.45%)
    Costo estimado material: $42,500 USD (aproximado, módulo opcional)

TAMAÑO: 1920×1080
```

---

## 3. Planta por nivel (2D top-down)  (slug: `planta_nivel_2d`)

```
TÍTULO: Vista de planta por nivel — visualización 2D top-down

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista 2D ortográfica de planta de un nivel específico del edificio. Muestra
columnas, vigas, losas, muros en su posición geométrica real. Click en
elementos abre su editor. Útil como paso intermedio entre el árbol y la vista 3D.

LAYOUT:

Top bar:
  Breadcrumb "Edificio > Nivel 2"
  Selector de nivel: [Nivel 1] [Nivel 2 ✓] [Nivel 3] [Nivel 4] [Cubierta]
  Toggles: ☑ Columnas  ☑ Vigas  ☑ Losas (opacidad 30%)  ☐ Muros  ☐ Cotas

Layout 2 paneles:

Panel central (~80% ancho × 100% altura):
  Canvas 2D con la planta:
    - Grid sutil cada 1 m, ejes X-Y en la esquina inferior izquierda
    - Columnas como cuadrados pequeños 30×60 cm (a escala) en azul accent
      con etiqueta C-1, C-2, ... encima
    - Vigas como rectángulos finos en gris oscuro conectando columnas,
      con etiqueta V-1, V-2, ... centrada
    - Losas como polígonos rellenos en color verde tenue (translúcido)
      con etiqueta L-1, L-2, ... en el centro
    - Muros (si activo) como líneas gruesas en marrón
    - Cotas (si activo) en líneas finas grises con valores en metros
  
  Toolbar arriba del canvas:
    [Fit a pantalla] [Zoom 100%] [Imprimir]  ·  Cursor: (3.45, 2.10)
  
  Indicadores de criticidad: si un elemento tiene violación normativa,
  contorno rojo grueso parpadeante.
  
  Tooltip al hacer hover sobre cualquier elemento:
    "L-7 · 4.5×3.5 m · h=12 cm · Tipo 40 · Mu=8.5 ton·m"

Panel derecho (340 px) — Información del elemento seleccionado:
  Cuando NO hay selección, muestra resumen del nivel:
    Card "NIVEL 2":
      Cota:              +3.50 m
      Altura entrepiso:  3.50 m
      # Columnas:        16
      # Vigas:           24
      # Losas:           16
      Área total:        320 m²
      Carga viva típica: 0.250 ton/m² (R-026 oficinas)
  
  Cuando hay un elemento seleccionado:
    Card "L-7 · Losa":
      Lx × Ly:           4.5 × 3.5 m
      H:                 12 cm
      Tipo:              40 (3 bordes continuos)
      Carga Wu:          0.85 ton/m²
      Mfx:               1.42 ton·m
      Mfy:               2.18 ton·m
      Validación:        ✅ OK
      [Abrir en editor →]

Bottom bar:
  Status: "Nivel 2 · 1 elemento seleccionado · 0 violaciones · sin
  modificaciones pendientes"

TAMAÑO: 1920×1080
```

---

## 4. Editor visual de planta con drag-and-drop  (slug: `editor_planta_drag_drop`)

```
TÍTULO: Editor visual de planta — drag-and-drop de elementos estructurales

[Pegar DESIGN SYSTEM]

CONTEXTO:
Modo EDICIÓN de la planta. El usuario arrastra paletas de columnas/vigas/losas
sobre la planta para construir el modelo geométricamente. Snap a grid o a
elementos existentes. Equivalente a la productividad del CadEditor pero
optimizada para crear el modelo estructural, no detallar planos.

LAYOUT:

Top bar:
  Breadcrumb "Edificio > Nivel 2 > Edición"
  Botones: [Validar layout] [Generar elementos] [Cancelar] [Guardar]
  Modo: ◉ Edición ○ Vista (selector)

Layout 3 paneles:

Panel izquierdo (240 px) — Paletas:
  
  Card "📦 ELEMENTOS":
    ┌──────────────────────┐
    │ 🟦 Columna           │ ← drag
    │    Rect 30×60        │
    ├──────────────────────┤
    │ 🟦 Columna           │
    │    Circ Ø50           │
    ├──────────────────────┤
    │ ▭  Viga              │
    │    25×50              │
    ├──────────────────────┤
    │ ▭  Viga T            │
    │    25×50 + 80×12      │
    ├──────────────────────┤
    │ 🟩 Losa maciza       │
    │    H = 12 cm          │
    ├──────────────────────┤
    │ 🟩 Losa vig+bov      │
    │    20+5 (1D)          │
    ├──────────────────────┤
    │ 🟥 Muro estructural  │
    │    H × 15 cm          │
    └──────────────────────┘
    Drag cualquiera al canvas.
  
  Card "📐 HERRAMIENTAS":
    [Línea de cotas]   [Eje de referencia]   [Comentario]
    [Cota inteligente] [Polígono losa]       [Borrar elemento]
  
  Card "⚙ SNAP":
    ☑ Grid 0.5 m
    ☑ Vértices de losas
    ☑ Caras de columnas
    ☑ Caras de muros
    ☐ Ortogonal estricto

Panel central — Canvas de edición (~60% ancho):
  Igual que planta_nivel_2d pero con:
    - Ghost del elemento que se arrastra (semi-transparente)
    - Líneas de snap activas como rayas amarillas
    - Cota dinámica que se actualiza al mover (ej. "3.45 m · 2.10 m")
    - Atajos visibles en esquina:
      "R = rotar 90°  ·  Espacio = ortogonal  ·  Esc = cancelar"

Panel derecho (320 px) — Propiedades del elemento que se está colocando:
  Card "Configuración rápida":
    Cuando se arrastra una columna:
      Cx [cm]: [30]   Cy [cm]: [60]
      f'c [kg/cm²]: [210]   fy [kg/cm²]: [4200]
      Caso de carga predeterminado: usar combinaciones globales
    Cuando se arrastra una losa:
      Tipo Pieper-Martens: [Auto-detectar tras posicionar]
      Espesor: [12] cm
      Carga Wu: [0.85] ton/m²
    Cuando se arrastra una viga:
      B × H: [25] × [50] cm
      Conexión: ◉ Articulada en extremos ○ Empotrada

Bottom bar:
  Status: "Colocando columna C-17 · drag para posicionar · Esc para cancelar"
  Contador: "16 columnas, 24 vigas, 16 losas en el nivel"

TAMAÑO: 1920×1080
```

---

## 5. Vista de cargas como vectores en 3D  (slug: `vista_cargas_3d`)

```
TÍTULO: Vista 3D con cargas como vectores — verificación visual

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista 3D del edificio donde TODAS las cargas aplicadas se renderizan como
flechas vectoriales con magnitud proporcional. Permite al ingeniero verificar
visualmente que las cargas estén donde deben estar, sin valores extraños o
duplicados.

LAYOUT:

Top bar:
  Selector de combinación: [D + L (servicio) ▾]
  Toggle: ☑ Mostrar D  ☑ Mostrar L  ☐ Mostrar E  ☐ Mostrar W
  Toggle: ☑ Cargas distribuidas (flechas múltiples)
          ☑ Cargas puntuales (flechas largas)
          ☑ Momentos aplicados (flechas circulares)
          ☑ Reacciones (flechas hacia arriba en cimentación)

Visor 3D (88% ancho × 85% altura):
  Modelo wireframe del edificio en gris claro (no realista — modo "diagrama").
  Sobre cada elemento, las cargas:
    
    - Cargas distribuidas sobre losas: filas de flechas verticales rojas
      apuntando hacia abajo, con longitud proporcional a la magnitud.
      Etiqueta al centro: "Wu = 0.85 ton/m²"
    
    - Cargas distribuidas sobre vigas (de muros, fachadas): flechas
      horizontales con etiqueta:  "wmuro = 1.5 ton/m"
    
    - Cargas puntuales sobre vigas (de columnas superiores que descargan
      excéntricamente): flechas largas rojas con etiqueta "P = 12.5 ton"
    
    - Momentos aplicados en extremos de vigas: flechas circulares 3D
      (espirales) con etiqueta "M = 4.5 ton·m"
    
    - Reacciones en zapatas: flechas verticales hacia arriba (color verde)
      con etiqueta "R = 38.4 ton"
  
  Las magnitudes están a escala lineal con un factor configurable (slider).
  Los colores diferencian D (gris-azul), L (verde), E (rojo), W (cyan), T (amarillo).

Panel derecho (320 px):
  Card "Escala de vectores":
    Slider [────●─────] (0.5x a 5.0x) · Actual: 2.0x
  
  Card "Visibilidad por elemento":
    Selector múltiple con todas las losas/vigas/etc.
    Default: TODOS visibles.
    Botones [Mostrar todo] [Ocultar todo].
  
  Card "Resumen de cargas activas":
    Combinación D + L (servicio):
      Σ N descendente:    320 ton
      Σ Reacciones:       320 ton  ✅ Equilibrio
      Σ Mx (sobre origen): 0.0 ton·m ✅
      Σ My (sobre origen): 0.0 ton·m ✅
    Si NO hay equilibrio, mostrar chip rojo "⚠ Desbalance: Σ N ≠ Σ R"
  
  Card "Cargas que parecen sospechosas" (heurística):
    ⚠ V-15: Wu = 5.2 ton/m (>3σ del promedio del nivel)
    ⚠ L-7: Wu < 0.15 ton/m² (muy bajo para uso oficina)
    Click en cada item resalta el elemento en el visor.

Bottom bar:
  Hint: "Las flechas son a escala — ajusta el slider si no se ven bien.
  Click sobre una flecha para editar la carga del elemento."

TAMAÑO: 1920×1080
```

---

## 6. Comparador de alternativas A vs B  (slug: `comparador_alternativas`)

```
TÍTULO: Comparador de alternativas — diseño A vs diseño B

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pantalla split que permite comparar dos versiones del proyecto lado a lado.
Cada versión es un snapshot completo (clonable como "rama de diseño"). Útil
para evaluar trade-offs (ej. columnas 30×60 vs 40×50 mantiendo igual rigidez,
o losa maciza 15 cm vs losa nervada 20+5).

LAYOUT:

Top bar:
  Selector A: [Versión inicial v1.0 ▾]
  vs
  Selector B: [Optimizada v1.2 (act) ▾]
  Botones: [Sincronizar vistas] [Generar reporte de comparación]
           [Promover A a actual] [Promover B a actual]

Layout 2 columnas iguales (cada una 50% ancho):

Columna izquierda — Versión A:
  Mini-header: "v1.0 · Inicial · creada 2026-05-15"
  Mini visor 3D del edificio (versión A) — sólo display.
  Tabla resumida de elementos:
  ┌──────────────────┬─────────┐
  │ Métrica          │ Valor   │
  ├──────────────────┼─────────┤
  │ Total hormigón   │ 198 m³  │
  │ Total acero      │ 9.2 t   │
  │ Cuantía global   │ 1.62%   │
  │ Costo estimado   │ $48,200 │
  │ Violaciones      │ 3       │
  └──────────────────┴─────────┘

Columna derecha — Versión B:
  Mini-header: "v1.2 · Optimizada · creada 2026-05-21"
  Mini visor 3D del edificio (versión B).
  Tabla resumida (idéntico formato):
  ┌──────────────────┬─────────┐
  │ Métrica          │ Valor   │
  ├──────────────────┼─────────┤
  │ Total hormigón   │ 187 m³  │  ← -5.6%
  │ Total acero      │ 8.4 t   │  ← -8.7%
  │ Cuantía global   │ 1.45%   │
  │ Costo estimado   │ $42,500 │  ← -$5,700
  │ Violaciones      │ 0       │  ← ✅
  └──────────────────┴─────────┘

Panel inferior (full-width, ~30% altura) — Diff detallado:
  Tabs internas: [Por elemento ✓]  [Por nivel]  [Solicitaciones]  [Memoria]
  
  Tab "Por elemento" — DataGrid con columnas:
    Elemento | Atributo | Valor A | Valor B | Diff
    C-1 | Cx | 30 cm | 30 cm | =
    C-1 | Cy | 60 cm | 50 cm | -10 cm
    C-1 | As | 19.5 cm² | 16.4 cm² | -3.1
    C-7 | Esbeltez X | 67 ⚠ | 38 ✅ | RESUELTO
    L-12 | Aspecto | 2.45 ⚠ | 1.95 ✅ | RESUELTO
    ... 
    Filtros: [Solo cambios] [Solo violaciones]
  
  Resumen del diff:
    "Versión B reduce 5.6% el volumen de hormigón, elimina las 3 violaciones
    normativas, y reduce el costo estimado en $5,700 manteniendo igual
    capacidad. Recomendación automática: PROMOVER B."

TAMAÑO: 1920×1080
```

---

## 7. Onboarding wizard  (slug: `onboarding_wizard`)

```
TÍTULO: Onboarding — primera ejecución de LosasPlus 2.0

[Pegar DESIGN SYSTEM]

CONTEXTO:
Wizard de 5 pasos que aparece la primera vez que se abre la aplicación o
cuando el usuario hace "Archivo > Nuevo proyecto guiado". Configura el perfil
del ingeniero, detecta DISEST, crea el primer proyecto y ejecuta un
test de verificación con un caso conocido.

LAYOUT (modal full-screen con overlay oscuro):

Header centrado:
  [Logo LosasPlus] · Bienvenido
  Stepper visual horizontal:
    [① Bienvenida] — [② Perfil] — [③ DISEST] — [④ Proyecto] — [⑤ Test]
  Stepper muestra paso actual resaltado en azul.

Cuerpo central (anclado, ~80% ancho × 60% altura):

Paso 1 — BIENVENIDA:
  Title: "LosasPlus 2.0 — Suite estructural"
  Sub: "En 5 minutos configuramos tu perfil, detectamos DISEST si está
       instalado, y verificamos que el motor de cálculo funciona."
  Lista de capacidades destacadas:
    ✅ 6 módulos: Losas · Vigas · Columnas · Diseño RC · Zapatas · 3D
    ✅ Motor propio verificado contra ACI 318-08 + ASCE 7-05
    ✅ Compatible con .DL/.TXT/.DZP/.CEZ de DISEST
    ✅ Memoria .docx automática multi-elemento
    ✅ Módulo educativo con citas a normas y bibliografía
  Botones: [Saltar wizard] [Empezar →]

Paso 2 — PERFIL DEL INGENIERO:
  Form:
    Nombre completo:        [_________________]
    Email:                  [_________________]
    CODIA:                  [_________________]
    Empresa (opcional):     [_________________]
    Sello/firma (drag-drop): [+ Subir imagen]
  Hint: "Estos datos se incluyen automáticamente en cada memoria generada."
  Botones: [← Atrás] [Siguiente →]

Paso 3 — DETECCIÓN DE DISEST (opcional):
  Card grande:
    "¿Tienes la suite DISEST original instalada?
     Si la tienes, LosasPlus puede usar Losas.exe, VigaContinua.exe,
     Columna.exe, Zapata.exe como motor pluggable. Si no, usaremos el
     motor propio (verificado contra ACI/ASCE)."
  
  Botón grande: [🔍 Buscar DISEST automáticamente]
  
  Después de buscar, mostrar resultado:
    ✅ Detectado: C:\Users\emilg\Downloads\Setups\DISSET\Disest windows\
        - Losas.exe v5.20      ✅
        - VigaContinua.exe v7.10 ✅
        - Columna.exe v2.00     ✅
        - Zapata.exe v6.10      ✅
        - AnaZap.exe v2.07      ✅
    
  Toggle: ☑ Usar DISEST cuando esté disponible (recomendado)
          ☐ Usar siempre motor propio
  
  Si no se detectó: mostrar "No se encontró DISEST. Se usará motor propio."
  
  Botones: [← Atrás] [Siguiente →]

Paso 4 — CREAR PRIMER PROYECTO:
  Opciones grandes (3 cards seleccionables):
    
    ┌─ NUEVO ───────────┐  ┌─ ABRIR ───────────┐  ┌─ IMPORTAR ────────┐
    │                    │  │                    │  │                    │
    │ Proyecto en blanco │  │ Proyecto .lpx.json │  │ Desde .DL legacy   │
    │ (configurar manual)│  │ existente          │  │ + .xlsx cargas     │
    │                    │  │                    │  │                    │
    │ [Crear →]          │  │ [Buscar...]        │  │ [Importar...]      │
    └────────────────────┘  └────────────────────┘  └────────────────────┘

Paso 5 — TEST DE VERIFICACIÓN:
  Card:
    "Vamos a correr un caso conocido para verificar que todo funciona."
    
    Caso: viga simplemente apoyada L=5m, w=1 ton/m
    Resultado esperado (analítico): Mmax = wL²/8 = 3.125 ton·m
    
    Botón grande: [▶ Ejecutar test]
    
    Resultado (después de ejecutar):
      Motor propio:    Mmax = 3.1250 ton·m  ✅ exacto
      Motor DISEST:    Mmax = 3.125 ton·m   ✅ exacto
      Diferencia:      0.000%
      
    Chip verde: "Todo listo. Click en Terminar para abrir tu proyecto."
  
  Botones: [← Atrás] [✅ Terminar]

Bottom bar (footer del modal):
  "Puedes acceder al onboarding nuevamente desde Ayuda > Wizard de inicio"

TAMAÑO: 1920×1080 (modal full-screen)
```

---

## 8. Vista de detalle de barras y armaduras (BIM-lite)  (slug: `detalle_barras_armaduras`)

```
TÍTULO: Detalle de barras y armaduras — listado para taller (BIM-lite)

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista exportable que lista TODAS las barras de acero del proyecto con sus
diámetros, longitudes, ganchos, dobleces, posición y conteo. Es el output
para el taller de armado/ferralla. Reemplaza la planilla Excel manual que
los ingenieros suelen hacer al final del proyecto.

LAYOUT:

Top bar:
  Filtros rápidos:
    Por elemento: [Todas ▾] [Losas] [Vigas] [Columnas] [Zapatas]
    Por nivel: [Todos ▾] [Nivel 1] [Nivel 2] ...
    Por diámetro: ☑ #3 ☑ #4 ☑ #5 ☑ #6 ☑ #7 ☑ #8
  Botones: [Exportar Excel] [Exportar PDF] [Exportar DXF] [Imprimir]

Layout 2 paneles:

Panel central (~70% ancho) — TABLA DE BARRAS (DataGrid grande):
  Columnas:
    # | Elemento | Posición | Ø | L total [m] | Forma | Ganchos | Cantidad | Peso [kg]
  
  ~50 filas de ejemplo:
  ┌────┬──────────┬──────────────┬────┬─────────┬──────────┬─────────┬─────┬───────┐
  │ 1  │ C-1 N1   │ Long. esquina│ #6 │ 3.65    │ Recta    │ 2 stand │ 4   │ 32.4  │
  │ 2  │ C-1 N1   │ Estribo      │ #3 │ 1.45    │ Cerrado  │ 4 90°   │ 30  │ 18.6  │
  │ 3  │ V-1 N1   │ Inferior     │ #5 │ 5.20    │ Recta    │ 2 stand │ 4   │ 41.2  │
  │ 4  │ V-1 N1   │ Superior     │ #5 │ 1.80    │ L        │ 1 90°   │ 4   │ 14.3  │
  │ 5  │ V-1 N1   │ Camellado    │ #5 │ 4.30    │ Camell.  │ 0       │ 2   │ 17.0  │
  │ 6  │ V-1 N1   │ Estribo      │ #3 │ 1.10    │ Cerrado  │ 4 135°  │ 22  │ 10.4  │
  │ 7  │ L-1 N1   │ Princ X inf  │ #4 │ 4.50    │ Recta    │ 0       │ 20  │ 89.6  │
  │ 8  │ L-1 N1   │ Princ Y inf  │ #4 │ 3.50    │ Recta    │ 0       │ 25  │ 87.1  │
  │ 9  │ L-1 N1   │ Adic borde X │ #4 │ 1.50    │ L        │ 1 90°   │ 16  │ 23.9  │
  │ ...│ ...      │ ...          │ ...│ ...     │ ...      │ ...     │ ... │ ...   │
  └────┴──────────┴──────────────┴────┴─────────┴──────────┴─────────┴─────┴───────┘
  
  Filas seleccionables (multi-select). 
  
  Visualización de la forma de cada barra como icono diminuto:
    Recta: línea horizontal —
    L:     línea con doblez —⌐
    U:     —⊐—  (camellado típico)
    Cerrado estribo: rectángulo cerrado □
    Z:     línea Z
  
  Footer del DataGrid:
    Total barras: 1,247
    Peso total: 8,432 kg
    Diámetros usados: #3, #4, #5, #6 (sin #7, #8)
    Longitud total: 4,123 m

Panel derecho (380 px) — RESUMEN POR DIÁMETRO:
  
  Card "Por diámetro":
    Tabla pivote:
    ┌────┬─────────┬─────────┬──────────┬─────────┐
    │ Ø  │ Cantidad│ L [m]   │ Peso [kg]│ Costo*  │
    ├────┼─────────┼─────────┼──────────┼─────────┤
    │ #3 │ 845     │ 1,012   │ 562      │ $1,124  │
    │ #4 │ 234     │ 968     │ 968      │ $1,936  │
    │ #5 │ 105     │ 945     │ 1,514    │ $3,028  │
    │ #6 │ 63      │ 920     │ 2,166    │ $4,332  │
    │TOT │ 1,247   │ 3,845   │ 5,210    │ $10,420 │
    └────┴─────────┴─────────┴──────────┴─────────┘
    * Costo estimado al precio actual de mercado RD.
  
  Card "Por elemento":
    Listado plegable:
    ▾ Columnas: 1,200 kg (23%)
       C-1: 51 kg · C-2: 51 kg · ...
    ▾ Vigas: 2,400 kg (46%)
    ▾ Losas: 1,400 kg (27%)
    ▸ Zapatas: 210 kg (4%)
  
  Card "Optimización sugerida":
    💡 "Si reemplazas 200 barras #5 de 0.85 m por #4 de 1.20 m, ahorrarías
     ~80 kg de acero manteniendo capacidad. ¿Aplicar?"
    [Ver detalles] [Aplicar] [Ignorar]

Bottom bar:
  Note: "Las longitudes incluyen ganchos según ACI 318-08 §7.1 y §12.5.
  Verificar con plano de armado antes de pedir a la ferretería."

TAMAÑO: 1920×1080
```

---

## Notas finales sobre estos prompts

- **Idioma**: español dominicano. Términos técnicos: ferralla, taller de armado, ganchos, dobleces, recubrimiento, vigueta, bovedilla.
- **Datos realistas**: tomados de proyectos típicos de RD (edificio de oficinas / residencial 4-5 niveles de hormigón armado).
- **No duplicar pantallas existentes**: cuando un módulo ya tiene su mockup en `docs/referencia/ui-design/`, NO regenerar. Si el operador no está satisfecho con un mockup, abrir un follow-up específico ("Reajustar editor_de_viga_continua_v_3 — cambiar paleta a tema claro").
- **Stitch puede saltar pantallas** complejas (ver caso zapata poligonal). Si vuelve a fallar, dividir en dos prompts más simples: uno para la geometría del polígono, otro para la integración con cargas.

---

**Fin del documento.** 8 prompts copy-pasteables (1 reintento + 7 macro nuevas) para completar la suite visual de LosasPlus 2.0.
