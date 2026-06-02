# Prompts para Stitch — interfaces de la suite LosasPlus 2.0

> Documento de referencia para generar mockups de UI con Stitch (https://stitch.withgoogle.com).
> Cada sección contiene un prompt independiente, copy-pasteable.
> Convención: los outputs van a `docs/referencia/ui-design/{slug}/{code.html, screen.png}`.
> Las pantallas existentes de v0.7 ya están en esa carpeta — estos prompts son para las **nuevas** propuestas en `PROPUESTA_UPDATE_v1.md`.

---

## 0. Sistema de diseño compartido (incluir al inicio de cada prompt)

```
DESIGN SYSTEM (mantener consistente en TODAS las pantallas):

Tema: dark mode profesional para software de ingeniería estructural.
Paleta:
  Fondo principal:   #1E1E1E
  Fondo secundario:  #252526
  Fondo terciario:   #2D2D30
  Texto principal:   #E8E8E8
  Texto muted:       #A0A0A0
  Acento:            #0E7DB8 (azul ingeniería)
  Acento brillante:  #3DB1F2
  Warning:           #E2B93B
  Error:             #E54B4B
  OK:                #4CAF50
  Bordes:            #3F3F46

Tipografía:
  UI:        Segoe UI 13 px regular / 14 px semi-bold para títulos
  Mono:      Consolas / Cascadia Mono 12 px (código, datos numéricos)
  Tabular:   Segoe UI 13 px regular con números alineados a la derecha

Tabs top-level del MainWindow (mantener en el orden propuesto):
  [Datos]  [Niveles]  [Losas]  [Vigas]  [Columnas]  [Zapatas]  [3D]
  [Cargas y Combinaciones]  [Reglamento]  [Educación]  [Plugins]  [Acerca]

Iconografía: minimalista tipo Lucide o Material Icons Outlined.
Idioma: español dominicano (ingeniero civil).
Resolución target: 1920×1080.
Stack tecnológico real: WPF/.NET 8 — no Tailwind ni Web; mockup en HTML/CSS sirve como referencia visual.
Tono profesional, denso de información, sin animaciones decorativas.
```

---

## ÍNDICE

| # | Pantalla | Módulo | Slug |
|---|----------|--------|------|
| 1 | Editor de viga continua | Vigas | `vigas_editor` |
| 2 | Diagramas M(x) y V(x) | Vigas | `vigas_diagramas_mv` |
| 3 | Diseño de sección RC por sección crítica | Vigas/Diseño | `diseno_seccion_rc` |
| 4 | Editor de columna (sección + pandeo) | Columnas | `columnas_editor` |
| 5 | Diagrama de interacción P-M 2D | Columnas | `columnas_pm_2d` |
| 6 | Diagrama de interacción P-M 3D | Columnas | `columnas_pm_3d` |
| 7 | Sección con eje neutro animado | Columnas | `columnas_seccion_eje_neutro` |
| 8 | Editor de zapata rectangular | Zapatas | `zapatas_rectangular` |
| 9 | Editor de zapata poligonal (AnaZap) | Zapatas | `zapatas_poligonal` |
| 10 | Heatmap de presiones bajo zapata | Zapatas | `zapatas_heatmap` |
| 11 | Vista 3D del edificio completo | 3D | `vista_3d_edificio` |
| 12 | Editor de casos de carga y combinaciones | Cargas | `cargas_combinaciones_editor` |
| 13 | Importador `.DZP` / `.CEZ` | Cargas | `cargas_importer_dzp` |
| 14 | Panel educativo — listado de artículos | Educación | `educacion_listado` |
| 15 | Vista de artículo educativo (Pieper-Martens) | Educación | `educacion_articulo` |
| 16 | Popover "Ver derivación" | Educación | `educacion_popover_derivacion` |
| 17 | Memoria unificada — vista preview | Memoria | `memoria_preview_unificada` |
| 18 | Selector de motor (DISEST vs propio) | Settings | `settings_selector_motor` |

---

## 1. Editor de viga continua  (slug: `vigas_editor`)

```
TÍTULO: Editor de viga continua — pestaña "Vigas" de LosasPlus 2.0

[Pegar aquí el DESIGN SYSTEM del §0]

CONTEXTO:
Pantalla principal del módulo Vigas. El usuario define una viga continua de
hasta 20 tramos sobre apoyos (articulados o empotrados), opcionalmente con
voladizos en los extremos. Cada tramo tiene una longitud, una rigidez
relativa I/Ic, y se subdivide en sub-tramos con carga distribuida lineal
(W inicial, W final) y carga puntual P en el extremo final.

LAYOUT:
- Top bar: nombre del proyecto, breadcrumb "Edificio > Nivel 2 > V-3", botones
  [Calcular] (azul accent), [Importar VigaContinua.exe], [Generar memoria].
- Panel izquierdo (240 px): árbol del edificio con expandable nodes
  (Edificio > Nivel 1 > V-1, V-2, V-3; Nivel 2 > V-4, V-5, ...). La viga
  activa está resaltada en azul.
- Panel central (60% del ancho restante):
  * Esquema gráfico de la viga: línea horizontal con triángulos en cada
    apoyo (articulados) y cuadrados en los empotrados. Los voladizos se
    dibujan a la izquierda/derecha sin apoyo en el extremo. Cargas
    distribuidas representadas con flechas verticales agrupadas.
    Apoyos numerados 1, 2, 3... Tramos etiquetados T1, T2, T3...
  * Debajo del esquema: paneles colapsables "Tramo 1", "Tramo 2", "Tramo 3"
    seleccionables por click. El tramo activo se expande mostrando sus
    sub-tramos.
- Panel derecho (380 px): formulario del tramo seleccionado:
  * Longitud [m]:  [4.500]
  * I/Ic [adim]:   [1.000]
  * Tabla de sub-tramos con columnas: #, L [m], W inic [ton/m], W fin
    [ton/m], P [ton]. Botones [+ Sub-tramo] [– Sub-tramo].
- Bottom bar: 
  - Status: "3 tramos, 6 sub-tramos, viga continua válida"
  - Validación normativa: chip verde "OK R-001 + ACI 318"

DATOS DE EJEMPLO REALISTAS (usar):
- Nombre viga: V-3
- 3 tramos: L = 4.5 m, 5.0 m, 4.0 m
- I/Ic: 1.0, 1.2, 1.0
- Voladizo izquierdo activo, voladizo derecho inactivo
- Apoyo 1: articulado, Apoyo 2: articulado, Apoyo 3: empotrado, Apoyo 4: articulado
- Tramo 1: sub-tramo único, W inic = W fin = 2.5 ton/m, P = 0
- Tramo 2: 2 sub-tramos: (L=2.5, W=2.5/3.5), (L=2.5, W=3.5/3.5, P=4 ton)
- Tramo 3: sub-tramo único, W = 2.0 ton/m

REFERENCIA DE CONVENCIONES (mostrar en tooltip o panel de ayuda):
- Cargas positivas hacia abajo
- Momento positivo = tracción en fibra inferior
- I/Ic con Ic = tramo No. 1 (referencia)

TAMAÑO: 1920×1080
```

---

## 2. Diagramas M(x) y V(x)  (slug: `vigas_diagramas_mv`)

```
TÍTULO: Diagramas de momento flector y cortante — módulo Vigas

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista de resultados del análisis de la viga continua. Muestra simultáneamente
M(x) y V(x) a lo largo de toda la viga, alineados con el esquema arriba para
que el usuario lea las posiciones críticas.

LAYOUT (3 paneles verticales apilados):

Panel 1 — Esquema de la viga (altura 120 px):
  Igual que en pantalla 1 (esquema + apoyos + cargas), pero más compacto.
  Etiquetas de posición x = 0, 4.5, 9.5, 13.5 m bajo cada apoyo.

Panel 2 — Diagrama de momento M(x) en ton·m (altura 300 px):
  Curva continua dibujada bajo la línea cero (convención: M positivo abajo
  para indicar tracción en fibra inferior). 
  Marcadores rojos en máximos y mínimos con valor numérico:
    "Mmax+ = 8.42 ton·m @ x = 2.34 m"
    "Mmin (sobre apoyo 2) = -4.85 ton·m"
  Línea de referencia M=0 en gris claro.
  Eje Y etiquetado de -6 a +10 ton·m. Eje X compartido con paneles 1 y 3.

Panel 3 — Diagrama de cortante V(x) en ton (altura 200 px):
  Curva escalonada / lineal a tramos. Marcadores en saltos (apoyos)
  y en valores extremos:
    "Vmax = +6.25 ton @ apoyo 2 (derecha)"
    "Vmin = -5.45 ton @ apoyo 3 (izquierda)"

Top bar de toolbar (sobre los 3 paneles):
  [Vista Datos] [Vista Diagramas ✓] [Vista Diseño RC]
  ─────────────────────────────────────────
  Botones: [Exportar PNG] [Exportar CSV] [Imprimir]

Panel derecho colapsable (320 px) — Tabla de secciones críticas:
  Tabla con columnas: Posición [m], M [ton·m], V [ton], Tramo, Tipo
    (vano / apoyo / voladizo).
  6-8 filas con valores numéricos. Click en una fila resalta la posición
  en los diagramas.

Status bar abajo: "Análisis completado · 32 puntos por tramo · método matricial
src.Core/Calculo/VigaContinuaEngine.cs"

ESTÉTICA:
- Curvas en azul accent #3DB1F2 (M) y verde OK #4CAF50 (V).
- Grid sutil con líneas grises tenues.
- Cuando el usuario pasa el mouse sobre un punto, mostrar tooltip:
  "x = 2.34 m, M = 8.42 ton·m, V = 0.00 ton (cambio de signo)".

TAMAÑO: 1920×1080
```

---

## 3. Diseño de sección RC por sección crítica  (slug: `diseno_seccion_rc`)

```
TÍTULO: Diseño RC de sección — flexión + cortante + torsión + viga-T

[Pegar DESIGN SYSTEM]

CONTEXTO:
Sub-vista dentro del módulo Vigas (y también accesible standalone como
"Diseñar sección" en el menú). Calcula armaduras requeridas para una
sección de hormigón armado bajo combinación general de solicitaciones.
Reimplementación independiente del módulo Diseno.exe de DISEST.

LAYOUT — 2 columnas:

Columna izquierda (480 px):
  Card "MATERIALES":
    f'c [kg/cm²]:  [210]
    fy  [kg/cm²]:  [4200]   (longitudinal)
    fyv [kg/cm²]:  [4200]   (transversal / estribos)
  Card "SECCIÓN":
    Tipo: ◉ Rectangular  ○ Sección T
    B  [cm]:  [25.0]   (ancho del alma)
    Bf [cm]:  [80.0]   (ancho del ala — solo T)
    H  [cm]:  [55.0]
    Hf [cm]:  [12.0]   (espesor del ala — solo T)
    D' [cm]:  [5.0]    (recubrimiento al centro de As)
    A's/As:  [0.30]
  Card "SOLICITACIONES":
    Mu  [ton·m]:  [12.5]
    Nu  [ton]:    [0.0]    (+ compresión)
    Vu  [ton]:    [8.4]
    Tu  [ton·m]:  [1.2]
    ☐ Primaria  ☑ Sismo

Columna derecha (resto del ancho):
  Visualización vectorial de la sección con:
    - Contorno del polígono de la sección (T o rectangular)
    - Eje neutro horizontal en la profundidad calculada (línea
      punteada amarilla)
    - Barras longitudinales como círculos llenos: tensión (rojo)
      y compresión (azul)
    - Estribos como rectángulo perimetral (rosa tenue)
    - Cotas: B, H, Bf, Hf, D' anotadas con líneas finas grises
  
  Debajo, tarjetas de resultado (grid 2×3):
    [As req]       [As prov]      [A's req]
     8.45 cm²       4 #6 + 2 #5    2.53 cm²
                    (9.83 cm²)     
    [Av/s]         [At/s]         [ρ]
     0.0145         0.0032         0.0061
     cm²/cm         cm²/cm         (0.61%)
  
  Card de verificación con chips:
    ✅ Mu/φ ≤ Mn               ACI 318 §10.3
    ✅ ρ entre ρ_min y ρ_max   ACI 318 §10.5
    ✅ Vu/φ ≤ Vc + Vs          ACI 318 §11.2
    ⚠ Torsión moderada         ACI 318 §11.5 — verificar detallado
  
  Footer: botón [Ver derivación paso a paso →] que abre el popover
  del módulo educativo (ver pantalla 16).

DATOS REALISTAS DEL EJEMPLO:
- Viga T 25×55 cm con ala 80×12
- Concreto 210 kg/cm², acero 4200 kg/cm²
- Mu=12.5 ton·m, Vu=8.4 ton, Tu=1.2 ton·m
- Resultado: 4 #6 inferiores + 2 #5 superiores

TAMAÑO: 1920×1080
```

---

## 4. Editor de columna — sección + pandeo  (slug: `columnas_editor`)

```
TÍTULO: Editor de columna — flexión biaxial + análisis de esbeltez

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pantalla principal del módulo Columnas. Diseño de columnas de hormigón
armado bajo Nu + Mux + Muy con opción de análisis de esbeltez ACI §10.10
para sistemas indesplazables. Reimplementación independiente de Columna.exe.

LAYOUT:

Top bar: [Calcular] [Importar de Columna.exe] [Ver Diagrama P-M ↗]
         (último abre pantalla #5 en otra pestaña)

Panel izquierdo (240 px): árbol del edificio con columnas. Columna activa
                          resaltada (C-12 en nivel 3 por ejemplo).

Panel central — 2 sub-paneles verticales:

  Sub-panel A: SECCIÓN (60% altura)
    Card "Materiales":
      f'c [kg/cm²]: [210]    fy [kg/cm²]: [4200]
      ◉ Estribos  ○ Zuncho     (afecta φ: 0.65 vs 0.75)
    Card "Geometría":
      Tipo: ◉ Rectangular  ○ Circular
      Cx [cm]: [30]   Cy [cm]: [60]
      b' [cm]: [5.0]  h' [cm]: [5.0]
      D [cm]:  [—]    d' [cm]: [—]    (solo circular)
    Card "Armaduras":
      Barras En dir X (por cara): [2]
      Barras En dir Y (por cara): [2]
      Total barras: 4
      Diámetro propuesto: [#6 ▾]  (selector con #3..#11)
    Visualización adjunta (250×250 px):
      Sección 30×60 con 4 barras en esquinas, cotas, ejes X-Y.

  Sub-panel B: PANDEO (expandible, colapsado por defecto)
    ☐ Activar análisis de esbeltez §10.10 ACI 318-08
    Cuando se activa:
      Por dirección X y Y separadamente:
        Lu [m]:   [3.0]
        Kr:       [1.00]
      Combinación factorizada Nu se calcula desde Nd + Nl
        (1.2D + 1.6L según ASCE 7-05 §2.3.2)
      Esbeltez calculada:
        Dir X: 34.64 (< crítica 46 → δns = 1.0 OK)
        Dir Y: 17.32 (< crítica 46 → δns = 1.0 OK)
      Chip verde "Columna corta en ambas direcciones".

Panel derecho (380 px): SOLICITACIONES por caso

  Tabla 11 filas × 4 columnas:
  ┌─────────────────┬───────┬────────┬────────┐
  │ Caso            │ Nu    │ Mux    │ Muy    │
  │                 │ [ton] │ [ton·m]│ [ton·m]│
  ├─────────────────┼───────┼────────┼────────┤
  │ 1. Muerta       │ 60.0  │ 6.0    │ 6.0    │
  │ 2. Viva         │ 25.0  │ 4.0    │ 4.0    │
  │ 3. Techo        │ 5.0   │ 0.5    │ 0.5    │
  │ 4. Sismo X1     │ 0.0   │ 12.0   │ 0.0    │
  │ 5. Sismo X2     │ 0.0   │ -12.0  │ 0.0    │
  │ 6. Sismo Y1     │ 0.0   │ 0.0    │ 12.0   │
  │ 7. Sismo Y2     │ 0.0   │ 0.0    │ -12.0  │
  │ 8. Viento X     │ 0.0   │ 4.0    │ 0.0    │
  │ 9. Viento Y     │ 0.0   │ 0.0    │ 4.0    │
  │ 10. Def. X      │ 0.0   │ 0.5    │ 0.0    │
  │ 11. Def. Y      │ 0.0   │ 0.0    │ 0.5    │
  └─────────────────┴───────┴────────┴────────┘
  
  Botón [Sincronizar con Combinaciones globales]

Bottom bar:
  Card de resumen (cuando ya se calculó):
    As total: 19.53 cm²   φ = 0.65   ρ = 1.09%
    Disposición: 4 #6 + estribos #3 @ 20 cm
    [Ver Diagrama P-M] [Ver derivación] [Generar memoria]

TAMAÑO: 1920×1080
```

---

## 5. Diagrama de interacción P-M 2D  (slug: `columnas_pm_2d`)

```
TÍTULO: Diagrama de interacción P-M 2D — N vs Mx (uniaxial)

[Pegar DESIGN SYSTEM]

CONTEXTO:
Visualización del diagrama de interacción uniaxial. La envolvente representa
la frontera de capacidad de la sección con su armadura. Los puntos de
solicitación (Nu, Mux) de cada combinación se marcan sobre el plano. Si
caen DENTRO de la envolvente → OK; fuera → falla.

LAYOUT:

Top bar: 
  Tabs: [N vs Mx ✓]  [N vs My]  [Mx vs My @ N fijo]  [3D]
  Selector: "Sección activa: C-12 (30×60, 4 #6, f'c=210, fy=4200)"

Plano principal (centro, ~70% ancho × ~80% altura):
  Eje X horizontal: Mx [ton·m] desde -20 a +20
  Eje Y vertical: N [ton] desde -50 (tracción) a +250 (compresión)
  Líneas grises tenues como grid
  CURVA DE ENVOLVENTE φP-φM dibujada como cierre cerrado simétrico,
    color azul accent grueso (3 px)
    Etiquetas en puntos críticos:
      "Pn0 = 321 ton (compresión pura)"
      "Balance: P=144, M=15.5"
      "Pn = 0 (flexión pura): M = 12.0"
      "Tn = -38 ton (tracción pura)"
  PUNTOS DE COMBINACIONES (~30 marcadores círculos):
    Verdes (✓): dentro de envolvente
    Amarillos (⚠): margen < 10%
    Rojos (✗): fuera (falla)
    Cada marcador con label corto: "C-3" "C-7" "C-12"
  Cuando se hace hover sobre un punto:
    Tooltip "Comb. 7: Nu = 152.0, Mux = 18.0 ton·m, factor uso = 95%"

Panel derecho (320 px):
  Lista de combinaciones con su factor de utilización ordenados de
  mayor a menor:
    ┌────────────────────────────────────┐
    │ Comb 7  · 1.2D+1.0Ex1+0.3Ey1+0.5L  │  95% ⚠
    │ Comb 12 · 1.2D+1.0Ex2+0.3Ey1+0.5L  │  87% ✓
    │ Comb 3  · 1.4D                     │  42% ✓
    │ Comb 1  · 1.0D                     │  35% ✓
    │ ...                                │
    └────────────────────────────────────┘
  Click sobre una combinación resalta su punto en el plano y muestra:
    "Eje neutro c = 18.4 cm   β = 0°   ρ_calc = 0.61%"

Bottom bar:
  Botón [Exportar diagrama PNG] [Generar tabla de puntos CSV]
  Indicador: 32 puntos de envolvente · método de iteración de c
  (deformaciones)
  
TAMAÑO: 1920×1080
```

---

## 6. Diagrama de interacción P-M 3D  (slug: `columnas_pm_3d`)

```
TÍTULO: Diagrama de interacción P-Mx-My 3D — flexión biaxial

[Pegar DESIGN SYSTEM]

CONTEXTO:
Superficie de interacción tridimensional. Eje vertical N (axial), ejes
horizontales Mx y My. La superficie es la envolvente cerrada de
capacidad. Los puntos (Nu, Mux, Muy) de cada combinación deben caer
DENTRO del volumen para que la columna sea segura.

LAYOUT:

Top bar igual que pantalla 5 con tab "[3D ✓]" activo.

Visor 3D (centro, ~75% ancho × 80% altura):
  Fondo gris oscuro #1E1E1E con grid 3D tenue.
  Ejes etiquetados:
    Eje vertical Z: N [ton]
    Eje horizontal X: Mx [ton·m]
    Eje horizontal Y: My [ton·m]
  Superficie de interacción renderizada como malla wireframe semi-transparente
    color azul accent con resaltado en las "rebanadas" cardinales (N-Mx en
    My=0 plano y N-My en Mx=0).
  Puntos de combinaciones como esferas pequeñas coloreadas igual que
    pantalla 5 (verde/amarillo/rojo).
  Vista isométrica por defecto, rotable con el mouse.

Panel derecho (320 px):
  Controles de cámara:
    [Planta]   [Alzado XZ]  [Alzado YZ]  [Isométrico ✓]
  Slider de transparencia de la superficie: [───●─────] 60%
  Toggle "Mostrar rebanadas cardinales" (líneas N-Mx y N-My en planos
    coordenados)
  Lista de combinaciones igual que pantalla 5.

Bottom bar:
  Hint: "Arrastrar para rotar · Shift+Arrastrar para pan · Rueda para zoom"
  Status: "Superficie generada con 31 puntos por dirección × 16 ángulos β"

TAMAÑO: 1920×1080
```

---

## 7. Sección con eje neutro animado  (slug: `columnas_seccion_eje_neutro`)

```
TÍTULO: Sección con bloque de Whitney y eje neutro — animación educativa

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista educativa que muestra para un punto seleccionado del diagrama P-M
cómo se ubica el eje neutro en la sección, qué barras están comprimidas
y cuáles traccionadas, y cómo se calcula la capacidad de la sección.

LAYOUT:

Top bar: Breadcrumb "C-12 > Diagrama P-M > Combinación 7 (Nu=152, Mux=18)"

Plano principal (50% ancho × 80% altura, lado izquierdo):
  Sección rectangular 30×60 cm renderizada a escala con:
    - Contorno gris claro con cotas
    - 4 barras como círculos:
       2 en cara superior (rojo si tracción, azul si compresión, etiqueta
       "#6 d=4.85 cm²")
       2 en cara inferior idem
    - Bloque de Whitney rellenado en gris compresión con etiqueta
      "0.85 f'c · b · a = ..."
    - Eje neutro como línea horizontal punteada amarilla con etiqueta
      "c = 18.40 cm"
    - Bloque comprimido (parte superior, hasta a = β1·c) con sombreado
      diagonal
  Cotas y etiquetas:
    - "a = 0.85·c = 15.64 cm"
    - "d (centroide As tracc.) = 55 cm"
    - "Tensiones de barras"
  Animación implícita: al cambiar el punto en el diagrama P-M (panel
  derecho), el eje neutro y los colores de las barras se actualizan.

Panel derecho (50% ancho):
  Card "Estado de cada barra":
    ┌────┬─────────┬──────────┬───────┬────────┐
    │ Br │ Pos     │ εs       │ Tensión│ Fuerza │
    │    │ (cm)    │          │ (kg/cm²)│ (ton) │
    ├────┼─────────┼──────────┼───────┼────────┤
    │ 1  │ (4, 56) │ -0.0024  │ -4200 │ -20.5  │ ← comp
    │ 2  │ (26,56) │ -0.0024  │ -4200 │ -20.5  │ ← comp
    │ 3  │ (4, 4)  │ +0.0048  │ +4200 │ +20.5  │ ← tracc
    │ 4  │ (26, 4) │ +0.0048  │ +4200 │ +20.5  │ ← tracc
    └────┴─────────┴──────────┴───────┴────────┘
  
  Card "Equilibrio":
    Cc (bloque concreto) =  0.85·210·30·15.64 = 83.7 ton
    Σ Fs (barras)        = -20.5-20.5+20.5+20.5 = 0 ton
    Pn = Cc + Σ Fs       = 83.7 + 0 = 83.7 ton
    
    Mn (alrededor del centroide) = ...
    
  Card "Citas":
    ACI 318-08 §10.2.7 — bloque equivalente de Whitney
    ACI 318-08 §10.3.4 — sección controlada por compresión / tensión
    Park & Paulay 1975 §5.3 — análisis de columnas a flexocompresión

Bottom bar:
  [Anterior punto ←]  Punto 17 de 32  [Siguiente →]
  Botón [Ver derivación completa] (abre artículo educativo)

TAMAÑO: 1920×1080
```

---

## 8. Editor de zapata rectangular  (slug: `zapatas_rectangular`)

```
TÍTULO: Editor de zapata rectangular aislada — pestaña "Zapatas"

[Pegar DESIGN SYSTEM]

CONTEXTO:
Diseño de zapata rectangular bajo columna centrada con flexión biaxial.
Reimplementación independiente de Zapata.exe v6.10. Criterios de área
en compresión 100% / 50% / 25% según tipo de carga.

LAYOUT:

Top bar: 
  [Calcular dimensiones] [Importar de Zapata.exe] [Ver Heatmap ↗]

Panel izquierdo (240 px): árbol del edificio con cimentación seleccionada.

Centro — Grid de 4 cards (2x2):

  Card "MATERIALES Y TERRENO":
    f'c [kg/cm²]:        [210]
    fy [kg/cm²]:         [4200]
    σ adm terreno:       [1.50]   kg/cm²
    Recubrimiento:       [7.50]   cm
    Peso relleno:        [1.60]   ton/m³
    Peso hormigón:       [2.40]   ton/m³

  Card "COEFICIENTES SÍSMICOS":
    Norma sísmica: ◉ DGRS (Ev=0.30·Sds)  ○ ASCE 7-05 (Ev=0.20)
    Rho:                 [1.00]
    Sds:                 [1.00]   g
    Ev (calculado):      0.30     (fracción de Sds)

  Card "COLUMNA SOBRE ZAPATA":
    Cx [m]:  [0.30]   Cy [m]:  [0.30]
    Excentricidad relativa al centro de la zapata:
      ex [m]: [0.00]   ey [m]: [0.00]

  Card "ZAPATA":
    Relación Ly/Lx:      [1.00]   (si 0 → auto Cy/Cx)
    Bmin [m]:            [1.000]
    Hmin [m]:            [0.300]
    Espesor relleno sobre la zapata [m]: [0.000]
    Sobrecarga sobre el relleno [ton/m²]: [0.000]

Panel derecho (380 px) — CARGAS POR CASO (igual estructura que columnas):
  Tabla 11 filas × 3 columnas (N, Mx, My)

Bottom dock — Resultados (cuando ya se calculó):
  Card grid 2×3:
    Lx [m]      Ly [m]      H [m]
    2.20         2.20        0.45
    σ_max bajo D+L [kg/cm²]    Área en compresión bajo combinaciones servicio
    1.42 (95% del adm)         100% / 100% / 87% / 65% (4 chequeos)
    As según X [cm²/m]         As según Y [cm²/m]
    8.5  (#5 @ 20 cm)          8.5  (#5 @ 20 cm)
  
  Botones: [Ver Heatmap presiones] [Generar memoria] [Exportar a DXF]
  Chip de validación: ✅ "Cumple ACI 318-08 §15 + ASCE 7-05"

TAMAÑO: 1920×1080
```

---

## 9. Editor de zapata poligonal (AnaZap)  (slug: `zapatas_poligonal`)

```
TÍTULO: Análisis de zapata poligonal — flexión biaxial sobre forma arbitraria

[Pegar DESIGN SYSTEM]

CONTEXTO:
Equivalente a AnaZap.exe v2.07. Zapata de forma poligonal arbitraria
(vértices anti-horarios, huecos horarios). Sólo analiza presiones,
NO diseña armaduras. Útil para zapatas combinadas o de forma irregular.

LAYOUT:

Top bar: [Importar polígono DXF] [Calcular presiones] [Ver heatmap ↗]

Panel izquierdo (320 px) — Definición del polígono:
  Card "VÉRTICES" (DataGrid editable):
    ┌────┬─────────┬─────────┐
    │  # │  X [m]  │  Y [m]  │
    ├────┼─────────┼─────────┤
    │  1 │  0.00   │  0.00   │
    │  2 │  3.00   │  0.00   │
    │  3 │  3.00   │  2.00   │
    │  4 │  5.00   │  2.00   │
    │  5 │  5.00   │  4.00   │
    │  6 │  0.00   │  4.00   │
    └────┴─────────┴─────────┘
    [+ Vértice] [– Vértice] [Reordenar]
  
  Card "HUECOS" (lista expandible, en sentido horario):
    Hueco 1: 4 vértices (vacío inicial)
    [+ Hueco]
  
  Card "PROPIEDADES GEOMÉTRICAS" (calculadas automáticamente):
    Número de vértices:   6
    Área:                 16.00 m²
    C.G. Xg:              2.13 m
    C.G. Yg:              1.83 m
    Ix:                   34.6 m⁴
    Iy:                   28.2 m⁴

Panel central — Visor del polígono (vectorial):
  Polígono dibujado a escala con:
    - Vértices numerados (círculos pequeños con #)
    - Sentido anti-horario indicado con flechas
    - Centroide marcado con cruz "C.G. (2.13, 1.83)"
    - Eje X-Y de referencia con flechas
    - Click sobre un vértice → resalta en la tabla
    - Grid sutil cada 0.5 m
  
  Below: indicador de selección de combinación
    "Combinación activa: D + L  ·  Σ N = 85.0 ton  ·  ΣMx=4.5  ·  ΣMy=3.2"
    Selector dropdown para cambiar combinación.

Panel derecho (340 px) — CARGAS TOTALES (por combinación seleccionada):
  Sub-card "Cargas externas":
    N total [ton]:        [85.0]
    Mx total [ton·m]:     [4.5]
    My total [ton·m]:     [3.2]
    Xn (posición N) [m]:  [2.13]
    Yn (posición N) [m]:  [1.83]
  
  Sub-card "Cargas internas":
    Peso propio zapata [ton]:     12.8  (auto)
    Peso terreno relleno [ton]:    0.0
    Sobrecarga [ton]:              0.0
  
  Botón [Calcular presiones]

Bottom bar:
  Chip resultado: "σ_max = 0.82 kg/cm² (55% del adm 1.50) · 100% en compresión"

TAMAÑO: 1920×1080
```

---

## 10. Heatmap de presiones bajo zapata  (slug: `zapatas_heatmap`)

```
TÍTULO: Heatmap de presiones bajo la zapata — visualización térmica

[Pegar DESIGN SYSTEM]

CONTEXTO:
Visualización de la distribución de presiones sobre el suelo bajo la
zapata (rectangular o poligonal). Heatmap con gradiente de color que
permite identificar zonas críticas, zonas en despegue (si las hay) y
verificar visualmente el porcentaje en compresión.

LAYOUT:

Top bar: [Combinación: D + L ▾] [Vista 2D ✓] [Vista 3D] [Generar isolíneas]

Centro — Visor de heatmap (75% ancho × 80% altura):
  Polígono de la zapata renderizado con gradiente de color por píxel:
    Azul oscuro (compresión alta) → cyan → verde → amarillo → naranja → rojo
    Banda blanca en σ = 0 (eje neutro)
    Zonas con σ < 0 mostradas con patrón rayado gris para indicar despegue
  Barra de color a la derecha con escala 0 a 1.50 kg/cm² (σ_adm)
  Isolíneas opcionales superpuestas (líneas blancas finas)
  Cotas y ejes en la periferia
  Anotaciones flotantes:
    "σ_max = 0.82 @ (3.20, 0.30)"
    "σ_min = 0.05 @ (0.30, 3.70)"
    "Sin despegue: 100% en compresión"

Panel derecho (320 px):
  Card "Estadísticas":
    Combinación:           D + L (servicio)
    σ_max:                 0.82 kg/cm² (55% adm)
    σ_min:                 0.05 kg/cm²
    σ_promedio:            0.39 kg/cm²
    Área en compresión:    100%
    Eje neutro:            fuera del polígono
  Card "Por combinación" (mini-gráficos):
    Tabla con todas las combinaciones, σ_max y % en compresión:
    ┌─────────────────────┬──────┬─────┐
    │ Comb                │ σmax │ %A  │
    ├─────────────────────┼──────┼─────┤
    │ D                   │ 0.45 │ 100 │
    │ D + L               │ 0.82 │ 100 │
    │ D + L + Ex1         │ 1.35 │ 85  │ ⚠
    │ D + 0.7Ex1          │ 1.48 │ 65  │ ⚠
    │ ...                 │ ...  │ ... │
    └─────────────────────┴──────┴─────┘
    Click en una fila actualiza el heatmap.

Bottom bar:
  Botones [Exportar PNG] [Exportar isolíneas DXF] [Incluir en memoria]
  Status: "Mallado: 50×50 puntos · método de eje neutro iterado"

TAMAÑO: 1920×1080
```

---

## 11. Vista 3D del edificio completo  (slug: `vista_3d_edificio`)

```
TÍTULO: Vista 3D del modelo estructural — edificio completo navegable

[Pegar DESIGN SYSTEM]

CONTEXTO:
Visualización 3D ensamblada del modelo: zapatas → columnas → vigas →
losas → muros. Click sobre cualquier elemento abre su editor. Tecnología
de referencia: HelixToolkit.Wpf (el mockup HTML sirve sólo como guía
visual; la implementación real es WPF 3D).

LAYOUT:

Top bar:
  [Vista normal ✓] [Por utilización] [Por nivel] [Wireframe] [Cortes]
  [Restablecer cámara]  ·  Cámara: ◉ Isométrico ○ Planta ○ Alzado X ○ Alzado Y

Visor 3D (87% ancho × 85% altura):
  Modelo del edificio renderizado en proyección isométrica:
    - 4 niveles (planta baja + 3 niveles superiores)
    - Cimentación visible (zapatas como prismas anchos en cota -1.5 m)
    - Columnas como prismas rectangulares verticales (30×60 típicas)
    - Vigas como prismas horizontales conectando columnas
    - Losas como placas planas semi-transparentes (35% opacidad)
    - Muros opcionalmente visibles según toggle
  Coloreo "Vista normal":
    Hormigón: gris claro #B0B0A8 con borde fino más oscuro
    Acero expuesto (donde aplique): naranja tenue
    Vidrio (placeholder de cerramientos): cyan 20% opacidad
  Coloreo "Por utilización" (alternativo):
    Verde (< 70%), amarillo (70-90%), naranja (90-100%), rojo (>100%)
  Grid del piso (cota 0.00) sutil
  Sol direccional para sombras suaves
  Etiquetas flotantes sobre elementos seleccionados con datos:
    "C-12 · 30×60 · ρ=1.09% · Util=87%"

Panel derecho (320 px):
  Card "Árbol del modelo" (jerárquico, todos colapsables):
    ▾ Edificio · Bloque A
       ▸ Cimentación (4 zapatas)
       ▾ Nivel 1
          ▸ 4 columnas (C-1..C-4)
          ▸ 8 vigas (V-1..V-8)
          ▸ 1 sistema de losas (12 losas)
       ▸ Nivel 2
       ▸ Nivel 3
       ▸ Nivel 4
  Card "Filtros":
    ☑ Mostrar cimentación
    ☑ Mostrar columnas
    ☑ Mostrar vigas
    ☑ Mostrar losas (opacidad slider: 35%)
    ☐ Mostrar muros
    ☐ Mostrar cargas como vectores
  Card "Vista de corte":
    Plano de corte horizontal en cota: [3.00 m]
    Slider para mover el corte verticalmente.

Bottom bar:
  Cursor info: "Hover: V-5 (Nivel 2) · L=4.5 m · 25×50"
  Click info: "Doble-click para abrir editor"
  Atajos: "R: reset · F: fit · 1-4: vistas predefinidas"

TAMAÑO: 1920×1080
```

---

## 12. Editor de casos de carga y combinaciones  (slug: `cargas_combinaciones_editor`)

```
TÍTULO: Cargas y Combinaciones del proyecto — librería transversal

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pestaña única donde el usuario define los casos de carga (D, L, Lr, Ex,
Ey, Wx, Wy, Tx, Ty…) y las combinaciones (servicio + últimas) que TODOS
los módulos de diseño (losas, vigas, columnas, zapatas) reutilizan.
Reemplaza la duplicación de "11 casos × 3 componentes" hoy hardcoded en
cada módulo.

LAYOUT:

Top bar:
  [Importar .DZP] [Importar .CEZ] [Exportar .DZP] [Normalizar ASCE/DGRS]
  Selector de norma base: ◉ ASCE 7-05  ○ ASCE 7-22  ○ R-026  ○ Custom

Layout vertical en 2 secciones:

Sección A — CASOS DE CARGA (40% altura):
  DataGrid con columnas:
  ┌──┬───────────────┬──────┬──────┬──────┐
  │# │ Nombre        │ Tipo │ FCE  │ FSIS │
  ├──┼───────────────┼──────┼──────┼──────┤
  │ 1│ Carga Muerta  │ D    │ 0    │ 0    │
  │ 2│ Carga Viva    │ L    │ 0    │ 0    │
  │ 3│ Carga Techo   │ Lr   │ 0    │ 0    │
  │ 4│ Sismo X1      │ E    │ 1    │ 1    │
  │ 5│ Sismo X2      │ E    │ 1    │ 1    │
  │ 6│ Sismo Y1      │ E    │ 1    │ 1    │
  │ 7│ Sismo Y2      │ E    │ 1    │ 1    │
  │ 8│ Viento X      │ W    │ 1    │ 0    │
  │ 9│ Viento Y      │ W    │ 1    │ 0    │
  │10│ Deform. X     │ T    │ 0    │ 0    │
  │11│ Deform. Y     │ T    │ 0    │ 0    │
  └──┴───────────────┴──────┴──────┴──────┘
  Total: 11 casos · NCC ≤ 11 (límite DISEST)
  Botones [+ Caso] [– Caso] [Reordenar]

Sección B — COMBINACIONES (60% altura):
  Tabs sub-internas: [Servicio (verificación)]  [Últimas (diseño)]
  
  DataGrid grande con todas las combinaciones; columnas:
  IC | Grupo | FS1(D) | FS2(L) | FS3(Lr) | FS4(Ex1) | FS5(Ex2) | FS6(Ey1) | FS7(Ey2) | FS8(Wx) | FS9(Wy) | FS10(Tx) | FS11(Ty)
  
  Algunas filas de ejemplo (servicio ASCE 7-05):
   1  1   1.0    0      0      0       0       0       0       0    0     0    0
   2  2   1.0    1.0    1.0    0       0       0       0       0    0     0    0
   3  3   1.0    0.75   0      0.525   0       0.158   0       0    0     0    0
   ...
   
  NCOMB actual: 86  ·  NCOMB ≤ 100 (límite DISEST)
  
  Indicadores visuales:
    Cellas en verde si pasan validación (factores en rangos esperados)
    Celdas en naranja si exceden norma seleccionada
  
  Footer de la sección:
    [Generar combinaciones ASCE 7-05 (vacía y rellena)]
    [Validar contra norma seleccionada]

Right panel (300 px):
  Card "Resumen":
    11 casos · 86 combinaciones servicio · 86 combinaciones últimas
    Norma: ASCE 7-05
    DGRS compatible: ✓ (Ev=0.30)
  Card "Casos no usados":
    Lista de combinaciones donde algún caso tiene factor 0 en TODAS las
    combinaciones (potencialmente eliminable).
  Card "Compatibilidad con módulos":
    Losas:    11/11 casos requeridos ✓
    Vigas:    7/11 (sismo Y no aplica) ✓
    Columnas: 11/11 ✓
    Zapatas:  11/11 ✓

TAMAÑO: 1920×1080
```

---

## 13. Importador `.DZP` / `.CEZ`  (slug: `cargas_importer_dzp`)

```
TÍTULO: Importar archivo de combinaciones DISEST (.DZP / .CEZ)

[Pegar DESIGN SYSTEM]

CONTEXTO:
Modal o vista a pantalla completa que muestra el diff entre las
combinaciones actuales del proyecto y un archivo `.DZP` o `.CEZ`
externo (formato DISEST). Permite mergear o sobrescribir.

LAYOUT (modal centrado, 1400×800):

Header:
  "Importar combinaciones desde DISEST"
  Sub: "Archivo: Combinaciones.DZP · NCC=11 · NCOMB=86"
  Botones esquina: [X Cerrar]

Cuerpo — 3 columnas:

  Columna izquierda (40%) — Proyecto actual:
    Lista de casos actuales con preview de factores
    Lista de combinaciones actuales (resumen)
  
  Columna central (20%) — Acciones:
    Radio button:
      ◉ Reemplazar TODO con .DZP
      ○ Merge inteligente (mantener casos extra del proyecto)
      ○ Solo agregar combinaciones nuevas (no tocar las que coinciden)
      ○ Solo importar casos (no combinaciones)
    
    Botones verticales:
      [→ Aplicar acción seleccionada]
      [← Re-leer archivo .DZP]
      [Cancelar]
  
  Columna derecha (40%) — Archivo .DZP:
    Lista de casos del archivo (que coinciden marcados verdes, nuevos
    en amarillo)
    Preview de combinaciones del archivo
  
Footer:
  Card de validación:
    "Diff detectado: 0 casos diferentes, 2 combinaciones nuevas
     (NCOMB en .DZP es 86, NCOMB actual es 84). Los factores de
     sismo Ev coinciden con DGRS (0.30)."
  
  Chip de advertencia:
    ⚠ El archivo .DZP fue generado para "programa ZAPATA Ver. 6.00".
      Las combinaciones pueden contener casos no relevantes para
      módulos no-zapata.

TAMAÑO: 1400×800 (modal)
```

---

## 14. Panel educativo — listado de artículos  (slug: `educacion_listado`)

```
TÍTULO: Apartado educativo — biblioteca de artículos técnicos

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pestaña "Educación" accesible desde la barra principal. Lista los
artículos disponibles con búsqueda y filtros por módulo. Cada artículo
puede abrirse en lectura completa (pantalla 15).

LAYOUT:

Top bar:
  Search box [🔍 Buscar artículos por título, palabra clave o cita]
  Filtros: [Todos] [Losas] [Vigas] [Columnas] [Zapatas] [Cargas/Combinaciones]
           [Bibliografía]

Cuerpo — Grid de 3 columnas × N filas (cards):

  Card típica:
    [Icono] [Categoría chip]
    Título del artículo
    "Subtítulo / abstract de 2 líneas máximo"
    [Autor/Norma · 3 citas · 8 min lectura]
    
  Ejemplos:
  
  Card 1:
    [📐] [Losas]
    Método de Pieper-Martens para losas continuas
    Procedimiento simplificado basado en coeficientes
    de las TU Braunschweig (1973).
    Pieper-Martens · 3 citas · 12 min
  
  Card 2:
    [📊] [Losas]
    αfm según ACI 318 §9.5.3.3
    Cálculo del promedio de rigidez de vigas para
    determinar espesor mínimo de losa.
    ACI 318-08 · 2 citas · 6 min
  
  Card 3:
    [📏] [Vigas]
    Método matricial de la rigidez en vigas continuas
    Ensamblaje de K y F, condiciones de borde y resolución
    del sistema.
    MacGregor 2005 · 5 citas · 18 min
  
  Card 4:
    [🏛️] [Columnas]
    Diagrama de interacción P-M de columnas RC
    Construcción punto por punto mediante iteración de
    profundidad del eje neutro.
    Nilson 2004 · 4 citas · 15 min
  
  Card 5:
    [📍] [Columnas]
    Análisis de esbeltez §10.10 ACI 318-08
    Sistemas indesplazables, amplificación de momentos δns,
    fórmula de Cm.
    ACI 318-08 · 3 citas · 10 min
  
  Card 6:
    [🟫] [Zapatas]
    Presión bajo zapata rígida — fórmula de Navier
    Cálculo de σ(x,y) con eje neutro fuera del polígono.
    Bowles 1996 · 2 citas · 7 min
  
  Card 7:
    [⚠️] [Zapatas]
    Punzonamiento en zapatas — ACI 318 §11.11
    Sección crítica a d/2 del borde de la columna.
    Cálculo de β, αs, b0.
    ACI 318-08 · 2 citas · 9 min
  
  Card 8:
    [⚙️] [Cargas]
    Combinaciones ASCE 7-05 vs R-026 vs DGRS
    Diferencias en factores de carga sísmica y Ev.
    ASCE 7-05 · R-026 · 4 citas · 11 min
  
  ... (resto de los ~25 artículos planeados)

Bottom bar:
  Indicador: "8 artículos visibles de 27 · Filtros activos: Losas, Vigas"
  Botón [Sugerir nuevo artículo →] (envía issue al repo)

TAMAÑO: 1920×1080
```

---

## 15. Vista de artículo educativo (Pieper-Martens)  (slug: `educacion_articulo`)

```
TÍTULO: Vista de artículo — "Método de Pieper-Martens para losas continuas"

[Pegar DESIGN SYSTEM]

CONTEXTO:
Vista de lectura inmersiva de un artículo educativo. Texto en español
con LaTeX renderizado (vía MathJax embebido), citas verificables al
margen, navegación de secciones, botón para volver al listado.

LAYOUT:

Top bar:
  [← Volver al listado]  ·  Pieper-Martens  ·  12 min lectura  ·  3 citas
  Acciones: [🔗 Copiar enlace] [📄 Imprimir] [⭐ Favorito]

Cuerpo en 3 columnas:

Columna izquierda (220 px) — Tabla de contenidos:
  1. Origen del método
  2. Hipótesis fundamentales
  3. Fórmulas
  4. Procedimiento de balanceo
  5. Limitaciones declaradas
  6. Comparación con métodos alternativos
  7. Referencias
  Sección activa resaltada con barra azul al lado.

Columna central (60% ancho) — Texto del artículo:
  Título principal "Método de Pieper-Martens para losas continuas en
  dos direcciones" en 24 px bold.
  
  Subtítulos en 18 px semi-bold.
  
  Cuerpo en Segoe UI 14 px, line-height 1.6, color #E8E8E8.
  
  Bloques de cita textual con borde izquierdo azul accent y fondo
  ligeramente diferente:
  
    "Pieper, K. y Martens, P. Bemessung von durchlaufenden Platten
     ohne Stützenraster [Diseño de losas continuas sin marco de
     columnas]. Beton- und Stahlbetonbau, 1973."
  
  Fórmulas LaTeX renderizadas centradas:
  
    M_{fx} = (q · L_x²) / F_x
    M_{fy} = (q · L_y²) / F_y
  
  Tablas con datos del paper (los coeficientes Fx, Fy, Sx, Sy de las
  TABLAS 1 a 12) mostradas con grid sutil.
  
  Imágenes/diagramas (placeholders): "Figura 1 — Nudo de 4 lados",
  "Figura 2 — Nudo de 3 lados" con leyenda.

Columna derecha (240 px) — Citas al margen:
  Lista de citas/referencias laterales con números clickables:
  
  [¹] Pieper, K. & Martens, P., Beton- und Stahlbetonbau, 1973.
      Paper original del método.
      
  [²] Perdomo, F., Losas continuas apoyadas en tres y cuatro bordes,
      manual del módulo Losas v5.20, 2011.
      Adaptación al español publicada por F. Perdomo.
      
  [³] ACI Committee 318, Building Code Requirements for Structural
      Concrete (ACI 318-08), American Concrete Institute, 2008.
      Verificación de apoyos rígidos §13.6.1.6.
      
  [⁴] Nilson, A. H., Darwin, D., Dolan, C. W., Design of Concrete
      Structures, 13ª ed., McGraw-Hill, 2004, Cap. 12.
      Métodos clásicos de análisis de losas.

Bottom dock — Sugerencias:
  "Continuar con…":
  Card [αfm según ACI §9.5.3.3]  Card [Balanceo de momentos en apoyos]
                                  Card [Casos especiales: voladizos]

TAMAÑO: 1920×1080
```

---

## 16. Popover "Ver derivación"  (slug: `educacion_popover_derivacion`)

```
TÍTULO: Popover "Ver derivación" — derivación paso a paso de un cálculo

[Pegar DESIGN SYSTEM]

CONTEXTO:
Popover modal que se abre al hacer click en el icono 🛈 al lado de un
resultado calculado en cualquier pantalla (ej. "Mfx = 0.959 ton·m"). Muestra
la fórmula, sustitución con los valores del caso actual, y citas verificables.

LAYOUT (popover modal centrado, ~700×500):

Header:
  Título: "Cálculo de Mfx — Losa #1"
  Cerrar [×]

Cuerpo:
  
  Sección 1 — Fórmula:
    Pequeño caption "Pieper-Martens 1973, ec. 2.1":
    
    Bloque centrado con LaTeX:
      
      M_{fx} = (q · L_x²) / F_x
    
  Sección 2 — Sustitución con valores del caso actual:
    Tabla 3 columnas:
    ┌────────┬──────────┬─────────────────────────────────────┐
    │ Var    │ Valor    │ Origen                              │
    ├────────┼──────────┼─────────────────────────────────────┤
    │ q      │ 2.000    │ CARGA del .DL (ton/m²)              │
    │ Lx     │ 4.000    │ LX del .DL (m)                      │
    │ Fx     │ 33.95    │ TABLA 6, aspecto Ly/Lx = 0.875,     │
    │        │          │ tipo 40, interpolado entre 0.85     │
    │        │          │ (Fx=52.19) y 0.90 (Fx=45.87)        │
    └────────┴──────────┴─────────────────────────────────────┘
  
  Sección 3 — Resultado:
    
    Mfx = 2.000 · 4.000² / 33.95 = 0.943 ton·m
    
    Comparación con el reporte:
    "El reporte del programa Losas.exe reporta Mfx = 0.959 ton·m.
     Diferencia: 1.7% — atribuible a la interpolación interna o
     a precisión decimal de la tabla utilizada."
  
  Sección 4 — Citas:
    [¹] Pieper-Martens 1973, ec. 2.1
    [²] Perdomo 2011, Losas.hlp
    [³] ACI 318-08 §13.6.1.6 — verificación de apoyos rígidos

Footer:
  [Abrir artículo completo →]  [Copiar al portapapeles]  [Cerrar]

ESTÉTICA:
- Sombra suave sobre el resto de la pantalla (fondo oscurecido al 50%)
- LaTeX renderizado con MathJax embebido (placeholders gráficos en el mockup)
- Bordes redondeados 8 px
- Padding generoso para legibilidad

TAMAÑO: 700×500 (popover)
```

---

## 17. Memoria unificada — vista preview  (slug: `memoria_preview_unificada`)

```
TÍTULO: Generar memoria de cálculo unificada — preview del .docx

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pestaña "Generar" extendida para soportar memoria unificada que cubre
TODO el edificio: cimentación + columnas + vigas + losas + cargas + sismo.
Reemplaza la memoria solo-de-losas actual.

LAYOUT:

Top bar:
  Selector de plantilla: [Memoria_Edificio_Completo.docx ▾] [+ Nueva]
  Botones: [Generar .docx] (verde OK destacado) [Vista previa]
           [Exportar PDF]

Layout 2 columnas:

Columna izquierda (50%) — Configuración:
  Acordeón:
    ▾ Datos del proyecto
        Nombre, código CODIA, ingeniero responsable, firma…
    ▾ Cimentación (4 zapatas)
        ☑ Incluir zapata Z-1
        ☑ Incluir zapata Z-2
        ☑ Incluir zapata Z-3
        ☑ Incluir zapata Z-4
        ☑ Incluir tabla resumen de presiones
        ☑ Incluir heatmaps
    ▾ Columnas (16 columnas)
        ☑ Incluir todas las columnas
        ☑ Incluir diagrama P-M crítico por columna
        ☑ Incluir verificación de esbeltez
    ▾ Vigas (24 vigas)
        ☑ Incluir todas las vigas
        ☑ Incluir diagramas M(x), V(x) por viga
        ☑ Incluir diseño RC por sección crítica
    ▾ Losas (sistema completo)
        ☑ Incluir tabla de momentos y armaduras
        ☑ Incluir esquema del sistema
    ▾ Cargas y combinaciones
        ☑ Incluir casos de carga
        ☑ Incluir combinaciones
        ☑ Incluir mapa sísmico (Sds, R, etc.)
    ▾ Sección educativa adjunta (opcional)
        ☐ Incluir artículo "Método Pieper-Martens" como anexo A
        ☐ Incluir artículo "Esbeltez ACI §10.10" como anexo B
        ...
  Footer:
    [Restablecer todo] [Guardar configuración como preset]

Columna derecha (50%) — Preview del .docx (área de scroll):
  Páginas del documento renderizadas en miniatura, scroll vertical:
    - Página 1: portada con logo, datos del proyecto, firma
    - Página 2: índice
    - Páginas 3-5: cargas y combinaciones
    - Páginas 6-15: cimentación (una zapata por bloque)
    - Páginas 16-30: columnas
    - Páginas 31-50: vigas
    - Páginas 51-65: losas
    - Anexos A, B…
  Total estimado: ~70 páginas
  
Bottom bar:
  Status: "Memoria estimada: 68 páginas · plantilla v2.1 · 47 placeholders
  rellenos / 47 totales · listo para generar"
  Chip: ✅ "Cumple R-001 + R-027 + ACI 318-08"

TAMAÑO: 1920×1080
```

---

## 18. Selector de motor (DISEST vs propio)  (slug: `settings_selector_motor`)

```
TÍTULO: Configuración del motor de cálculo — DISEST vs motor propio

[Pegar DESIGN SYSTEM]

CONTEXTO:
Pantalla de configuración accesible desde "Configuración → Motor de cálculo".
Permite al usuario elegir qué motor usar para cada módulo: el binario DISEST
original (si está instalado en `engine/`) o el motor propio reimplementado
en `src.Core/Calculo/`.

LAYOUT:

Top bar:
  Breadcrumb "Configuración > Motor de cálculo"
  Botones: [Detectar automáticamente] [Guardar]

Cuerpo — Lista de módulos:

Para cada módulo (Losas, Vigas, Columnas, Diseño RC, Zapatas, Interaccion,
AnaZap), una fila tipo card:

  ┌──────────────────────────────────────────────────────────────────┐
  │  LOSAS                                                            │
  │  ──────                                                           │
  │                                                                   │
  │  ◉ Usar motor DISEST                                              │
  │     Binario: ✅ engine/Losas.exe v5.20 detectado                   │
  │     Última verificación: 2026-05-21 (hace 3 días)                 │
  │     Estado: funcional · semi-manual (UI Automation requerida)    │
  │                                                                   │
  │  ○ Usar motor propio (src.Core/Calculo/LosasEngine.cs)            │
  │     Estado: ✅ 31 tests verdes · cobertura ACI 318 + Pieper       │
  │     Velocidad: ~50× más rápido que DISEST (sin UIA)              │
  │     Diferencia esperada con DISEST: < 1% en casos validados      │
  │                                                                   │
  │  [Ver tests de regresión] [Probar con caso conocido]              │
  └──────────────────────────────────────────────────────────────────┘

Repetir el patrón para cada módulo. Status posibles:
  ✅ Detectado y funcional
  ⚠ Detectado pero versión inesperada
  ✗ No detectado (mostrar "Instalar DISEST" link)
  🔒 No redistribuible (caso DISEST binario)

Footer panel (sticky):
  Card "Resumen de configuración":
    Losas:        DISEST v5.20
    Vigas:        Motor propio
    Columnas:     DISEST v2.00 + motor propio (interacción)
    Diseño RC:    Motor propio
    Zapatas:      Motor propio
    Interacción:  Motor propio
    AnaZap:       Motor propio
  
  Chip de advertencia si DISEST no está disponible:
    "ℹ Los binarios DISEST no se encuentran en engine/. LosasPlus usará
     motor propio para todos los módulos. Las fórmulas implementadas
     siguen ACI 318-08 + ASCE 7-05 + DGRS y han sido verificadas con
     casos analíticos exactos."

TAMAÑO: 1920×1080
```

---

## Convenciones finales (recordatorio)

- **Idioma de UI**: español dominicano. Términos técnicos en español (ej. "esfuerzo admisible", no "allowable stress").
- **Unidades**: kg/cm² para f'c, fy; ton para fuerzas; ton·m para momentos; cm o m para longitudes según convención DISEST.
- **Datos realistas**: tomados de proyectos típicos de edificios de hormigón armado de 3-5 niveles en RD.
- **NO incluir publicidad de Stitch**, NO logos de Google, NO copy de placeholder Lorem Ipsum.
- **Iconografía**: el mockup HTML puede usar Material Icons o Lucide; la implementación final en WPF usará iconos SVG propios (ya disponibles en `src.UI.Shared/Resources/icons/`).
- **Capturas finales**: el mockup HTML de Stitch sirve como referencia. El equipo de desarrollo lo traducirá a XAML real con bindings al ViewModel correspondiente.

---

**Fin del documento.** 18 prompts para Stitch listos para pegarse uno a la vez en https://stitch.withgoogle.com.
