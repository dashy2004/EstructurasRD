# Diseño — Vista de sección de miembro (#4a)

**Fecha:** 2026-06-14
**Estado:** aprobado en brainstorming; spec para ejecutar (writing-plans → subagent-driven).
**Motivación:** #3 dejó los diagramas P/V/M (panel 2D + cintas 3D) sobre el DTO `esfuerzos`.
#4 ("vista en secciones") se **descompone** en dos sub-proyectos: **#4a** (esta spec) —
el **corte transversal de un miembro** en una estación `s`, con armado y esfuerzos; y **#4b**
(spec aparte, futuro) — un **plano de corte global** que rebana el pórtico. #4a es **100% front**:
el server, `core/`, `contrato.py` y el resto de `viz/` no se tocan.

Construye sobre #3 ([`2026-06-14-diagramas-pvm-design.md`](2026-06-14-diagramas-pvm-design.md))
y #2 (estado `esfuerzos` en el front, modos ejemplo y custom).

---

## 1. Alcance (MVP confirmado)

**Dentro:** un modo `"sección"` en el `<select>` de estado que, al **tocar una barra**, dibuja en
un **panel 2D** el **corte transversal** del miembro en una **estación `s`** (slider `0…L`):
- rectángulo de la sección `b×h`,
- **armado** (barras longitudinales + estribo) cuando hay diseño/armado cargado (**adaptativo**),
- los **6 esfuerzos** `N, Vy, Vz, T, My, Mz` **interpolados en `s`** (numérico),
- un **cue ligero**: flecha del momento resultante + marca de la cara en tracción,
- un **anillo 3D** sobre la barra en la posición `s` (sigue al slider),
- **export** del panel a **SVG y PNG**.

Funciona en **modo-ejemplo** y **modo-custom**.

**Fuera (confirmado):**
- **#4b plano de corte global** (rebanar el modelo 3D con un plano) — sub-proyecto aparte.
- Cambios en server / `core` / `contrato` (no hacen falta: todos los datos ya están en el front).
- Análisis de sección nuevo (bloque de Whitney / eje neutro / tensiones) — sería su propio
  sub-proyecto; #4a se queda en geometría + armado + esfuerzos numéricos + cue ligero.
- Combinaciones/factores de carga (los esfuerzos son la demanda combinada sin factorar, ver
  limitación de #1).
- Runner de tests JS (YAGNI; el proyecto es Python).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Descomposición de #4 | **#4a sección de miembro** ahora; **#4b corte global** en spec aparte. |
| Contenido de la sección | **Adaptativo**: con diseño/armado → b×h + armado + esfuerzos; sin (custom) → b×h + esfuerzos. |
| Fuente del armado | **`diseño` por id → si no, `armado` → si no, solo b×h**. |
| Interpretación | **Numérico + cue ligero** (flecha de momento + cara en tracción). Sin análisis de sección. |
| Export | **Ambos: SVG y PNG** (PNG por rasterizado nativo en canvas, vendorless). |
| Interacción | **Modo `"sección"` dedicado** en el `<select>` + panel 2D + slider de estación + anillo 3D. |
| Estación `s` | **Slider continuo `0…L`**, interpolación **lineal** del diagrama (exacta, lineal por tramos). |
| Render del panel 2D | **SVG inline** (vendorless), como #3. |

## 3. Arquitectura

Todo vive en el front, sobre el estado `esfuerzos` (poblado por `renderEscena` en ambos modos)
y los overlays `diseno`/`armado` cuando existen. Piezas nuevas con una responsabilidad cada una:

```
┌─ index.html ─────────────────────────────────┐
│  #panel (estado, exag, diseño) [de #2/#3]     │
│  #diag  (diagramas) [de #3]                   │
│  #sec  (NUEVO): host SVG + slider s + export  │  ← panel de sección
└───────────────┬───────────────────────────────┘
   app.js        │
   ├─ estado esfuerzos / diseno / armado [previos]│
   ├─ esfuerzosEnEstacion(el, s)  (interp. lineal)│
   ├─ modo "sección" → entrarSeccion / teardown   │
   ├─ pick → seleccionar miembro                  │
   ├─ slider s → redibuja panel + mueve anillo 3D │
   └─ construirAnilloSeccion / dispose (eje barra) │
                  │
   seccion2d.js   │  seccionSVG({b,h,long,estribo,fuerzas,...}) → SVGElement  (pura, sin three.js)
   svgutil.js     │  nodo(), descargarSVG(), descargarPNG()  (compartidos con diagramas2d)
```

- **`svgutil.js`** (nuevo): helpers compartidos sin estado.
  - `nodo(tag, attrs) -> SVGElement` — extraído del helper que hoy vive en `diagramas2d.js`
    (el review de #3 ya lo señaló como candidato a compartir). `diagramas2d.js` pasa a importarlo.
  - `descargarSVG(svg, nombre)` — serializa el `SVGElement` (`XMLSerializer`) → `Blob` → descarga
    (mismo patrón que el botón "descargar .json" de `shell.js`).
  - `descargarPNG(svg, nombre, escala=2)` — serializa el SVG a data-URL, lo carga en un `Image`,
    lo dibuja en un `<canvas>` (×escala) y descarga vía `canvas.toBlob`. Nativo, sin librerías.
- **`seccion2d.js`** (nuevo): función **pura** `seccionSVG(datos, opts) -> SVGElement`. No conoce
  three.js ni el DOM global; recibe datos y devuelve nodos SVG (usa `nodo` de `svgutil`).
- **`app.js`**: interpolación, modo `"sección"`, pick, slider, anillo 3D y export (helpers nuevos
  siguiendo el patrón de overlays de #3: `entrarSeccion`, `construirAnilloSeccion`, teardown).
- **`index.html`**: contenedor `#sec`. CSS mínimo en el `<style>` inline existente.

## 4. Datos (recordatorio de los DTOs, no se modifican)

- **`esfuerzos`** (de #1/#2): `elementos[]` con `{ id, longitud, extremo_i, extremo_j, diagrama }`;
  `diagrama` = filas `[s, N, Vy, Vz, T, My, Mz]` (índice 0 = `s`; N=1, Vy=2, Vz=3, T=4, My=5, Mz=6).
- **`escena.barras`** (de #2): cada barra trae `b`, `h`, `i`, `j`, `id` (hoy `addBarra` usa `b.b`,
  `b.h` para el `BoxGeometry` pero **no** los guarda en el array `barras`; #4a los guardará).
- **`diseño`** (`GET /diseno`, modo-ejemplo): `elementos[]` con `{ id, i, j, tipo, long, estribo,
  designacion, demanda, muy, muz, utilizacion, combo, estribo_txt, cumple }`.
- **`armado`** (`GET /armado`, modo-ejemplo): `elementos[]` con `{ id, i, j, tipo, long, estribo }`.
  - En ambos: `long` = `[{ x, y, d }]` (posiciones de barras en el plano de la sección, metros);
    `estribo` = `{ d, s, w, h }` (diámetro, separación, ancho y alto del núcleo, metros).
- Unidades: posiciones/dimensiones en **m**; fuerzas en **N**, momentos en **N·m**.
  Mostrar fuerzas en **kN** (`/1000`) y momentos en **kN·m**.

## 5. Interpolación — `esfuerzosEnEstacion(el, s)` (en `app.js`)

Devuelve `[N, Vy, Vz, T, My, Mz]` del elemento `el` en la posición `s ∈ [0, longitud]`:
- Busca las dos estaciones del `diagrama` que bracketean `s` (`filas` ordenadas por `s`).
- **Interpolación lineal** entre ellas, componente a componente. Exacta porque el modelo solo tiene
  cargas **nodales** → dentro del elemento N y V son constantes y M es lineal (ver #3 §4).
- `s ≤ filas[0][0]` → primera fila; `s ≥ filas[n-1][0]` → última fila (clamp, sin extrapolar).
- Si `el` no tiene `diagrama` o está vacío → devuelve seis ceros.

## 6. Panel 2D — `seccion2d.js`

### 6.1 `seccionSVG(datos, opts) -> SVGElement`
`datos = { b, h, long, estribo, fuerzas, tipo, designacion, cumple, s, L }` donde `fuerzas` =
`[N, Vy, Vz, T, My, Mz]` (en `s`); `long`/`estribo` pueden ser `null`/vacíos (modo sin diseño).
Función pura que arma un SVG con:
- **Rectángulo `b×h`** centrado, a escala que llene el área de dibujo (auto-fit con margen).
- **Estribo** (si `estribo`): rectángulo interior de `estribo.w × estribo.h`.
- **Barras longitudinales** (si `long`): un círculo por barra en `(x, y)` con radio `d/2`,
  mapeadas del plano de la sección (metros, origen al centro) a coordenadas SVG.
- **Cue ligero** (si hay momento): flecha del **momento resultante transversal** y **sombreado de
  la cara en tracción** (My ↔ caras horizontales, Mz ↔ caras verticales, según los ejes **dibujados**
  de la sección; ver limitación §9). Si los momentos son ~0, se omite el cue.
- **Bloque de texto** con los 6 esfuerzos en `s`: `N` (con `compr`/`tracc`), `Vy`, `Vz`, `T`,
  `My`, `Mz` en kN / kN·m; encabezado con `designacion` y `cumple` si vienen, y la posición
  `s = … m (s/L = …)`.
- Convención de signo: esfuerzo **interno** de sección (tracción +), tal cual (coherente con #3).

Construye con `nodo()` de `svgutil`. `opts` da parámetros mínimos (tamaño, colores) con defaults.

### 6.2 Mapeo de coordenadas
El plano de la sección usa `x` (ancho, ligado a `b`) horizontal y `y` (alto, ligado a `h`) vertical,
con origen al centro — el mismo sistema en que `armado`/`diseño` expresan `long` (ver `armado.py`),
así que las barras caen donde deben sobre el rectángulo `b×h`. Eje `y` del SVG invertido (arriba = +y).

## 7. Sección 3D + interacción (en `app.js`)

### 7.1 Entrada del modo
- Una entrada **"sección"** en `selEstado`, añadida en `renderEscena` cuando hay `esfuerzos`
  (igual que "diagramas" de #3, en ambos modos).
- `entrarSeccion()`: activa el modo (estático, como los overlays — barras en base, sin deformar),
  muestra `#sec`, pone `info` = "toca una barra para ver su sección", y deja el panel/slider vacíos
  hasta el primer pick. Sin pick previo no dibuja nada.

### 7.2 Pick → seleccionar miembro
En modo sección, el `pointerdown` resuelve la barra tocada a su `id`, fija el miembro
seleccionado, configura el slider (`min=0`, `max=L`, `value=L/2`) y dibuja la sección en `s=L/2`
(con su anillo 3D). `L` = `el.longitud` del elemento en `esfuerzos`. Si la barra no está en
`esfuerzos`, no hace nada (panel intacto).

### 7.3 Slider de estación
`#sec-s` (`input range`): en `input`, recalcula `fuerzas = esfuerzosEnEstacion(el, s)`, redibuja
el panel (`seccionSVG`) y mueve el anillo 3D a la posición `s`. Sin reconstruir la geometría del
anillo (solo su posición).

### 7.4 Anillo 3D
`construirAnilloSeccion()` crea un anillo (`THREE.TorusGeometry` fino) que marca el plano de corte:
- posición = `lerp(pos[i], pos[j], s/L)` sobre el eje del miembro (de `basePos`, como #3);
- orientado con el **eje del miembro como normal** (reusa la matemática de eje de `construirCintas`
  de #3: `axis = normalize(pos[j]-pos[i])`, orientar el toro con `lookAt`/quaternion al eje).
- Se agrega a la escena, visible solo en modo sección. Tamaño ligado a `max(b,h)` del miembro.

### 7.5 Fuente del armado (adaptativo)
Para el miembro `id`: `long`/`estribo`/`tipo`/`designacion`/`cumple` salen de
`diseno.elementos` por `id` si existe; si no, de `armado.elementos` por `id`; si ninguno, `long` y
`estribo` van `null` (solo b×h + esfuerzos). El rectángulo `b×h` siempre sale de la barra
(`escena.barras`, guardado en `addBarra`).

### 7.6 Teardown
`limpiarEscena` y `resetOverlays` ocultan/eliminan el anillo (`dispose` de su geometría), vacían
`#sec-svg`, limpian el miembro seleccionado y desactivan el modo, igual que las cintas de #3.
Cargar otro modelo deja la escena y el panel limpios.

## 8. Export (en `app.js` + `svgutil.js`)

Dos botones en `#sec`: **SVG** (`#sec-svg-dl`) y **PNG** (`#sec-png-dl`).
- SVG: `descargarSVG(svgActual, 'seccion.svg')`.
- PNG: `descargarPNG(svgActual, 'seccion.png')`.
- Sin sección dibujada (sin pick) → no-op (los botones no hacen nada).

## 9. Limitación documentada (igual que #3 §6.3)

El **cue de cara en tracción** mapea `My`/`Mz` a los ejes **dibujados** de la sección, que pueden
no coincidir con los ejes principales reales cuando el `vector_referencia` del elemento no es
trivial (el front no recibe el triedro local en el DTO `escena`). Los **valores** de los 6
esfuerzos son **correctos**; solo la **orientación del cue** es una aproximación de visualización.
El **dibujo del armado** (posiciones `long`) es correcto siempre (viene del DTO en coordenadas de
sección). Exactitud del cue = mejora futura (añadir el triedro local al `escena` DTO, server, fuera
de #4a — lo mismo que habilitaría el plano exacto de las cintas de #3).

## 10. Manejo de errores / casos borde

| Situación | Respuesta |
|---|---|
| Sin `esfuerzos` (fetch falló) | Entrada "sección" inerte; panel vacío, sin error. |
| Pick de barra sin entrada en `esfuerzos` | No hace nada (panel/slider intactos). |
| Sin diseño ni armado (custom) | Solo rectángulo `b×h` + esfuerzos (sin barras ni estribo). |
| Momentos ~0 en `s` | Se omite el cue (sin flecha ni cara en tracción); resto normal. |
| `longitud ≤ 0` | El miembro se ignora (no se puede ubicar `s` ni el anillo). |
| Export sin sección dibujada | No-op. |

## 11. Testing

### 11.1 Sin runner JS (consistente con el proyecto)
No se añade infra de tests JS (YAGNI). `seccion2d.js` y `svgutil.js` se escriben **puros y
acotados**; la verificación de #4a es **manual en navegador real** (Playwright), como #2/#3.

### 11.2 Checklist manual (navegador)
1. **Arranque (ejemplo):** render idéntico; el `<select>` gana la entrada "sección".
2. **Modo sección + pick:** entrar → "toca una barra"; click en una barra dibuja el corte b×h con
   barras + estribo (modo-ejemplo) y el bloque de 6 esfuerzos; aparece el anillo 3D en la barra.
3. **Slider de estación:** mover `s` recalcula los esfuerzos y mueve el anillo. **Caso de control**
   (voladizo, carga P en el extremo, largo L): el momento crece lineal → en `s≈L` el momento de
   flexión ≈ `P·L`; en `s≈0` ≈ 0; N y V constantes a lo largo de `s`.
4. **Cue:** la cara en tracción y la flecha de momento cambian de lado al cambiar el signo del
   momento dominante a lo largo de `s`.
5. **Export:** "SVG" descarga un `.svg` abrible; "PNG" descarga un `.png` con el mismo dibujo.
6. **Adaptativo (custom):** cargar un modelo propio (shell) → la sección muestra solo b×h +
   esfuerzos (sin armado), pick + slider + export funcionan.
7. **Teardown:** cambiar de "sección" a otro estado y cargar otro modelo deja la escena/panel
   limpios (sin anillo residual, sin SVG viejo, sin fugas en consola).
8. **Sin errores** en consola (salvo el `favicon.ico` 404 conocido / artefactos de eventos sintéticos).

**Criterio de aceptación:** en el voladizo de control, el panel reproduce N/V constantes y M lineal
a `P·L`; el armado se dibuja en modo-ejemplo y degrada a solo b×h en custom; SVG y PNG descargan.

## 12. Archivos afectados

**Crear:**
- `src/motor_fea/viz/static/svgutil.js` (`nodo`, `descargarSVG`, `descargarPNG`).
- `src/motor_fea/viz/static/seccion2d.js` (función pura `seccionSVG`).

**Modificar:**
- `src/motor_fea/viz/static/diagramas2d.js` (importar `nodo` de `svgutil`; quitar su copia local).
- `src/motor_fea/viz/static/app.js` (guardar `b,h` en `addBarra`; `esfuerzosEnEstacion`; modo
  "sección" + `entrarSeccion`/teardown; pick→miembro; slider; `construirAnilloSeccion`/dispose;
  fuente adaptativa diseño→armado→bare; export).
- `src/motor_fea/viz/static/index.html` (contenedor `#sec`: host SVG + slider + 2 botones; CSS).

`core/`, `normativa/`, `api/` (servidor + contrato) y el resto de `viz/` **no se tocan** — #4a es
puramente front sobre los DTOs existentes.

## 13. Roadmap habilitado

| Item | Reusa de #4a |
|---|---|
| #4b plano de corte global | el panel 2D + `svgutil` (export) + (posiblemente) `seccionSVG` por miembro cortado |
| (futuro) bloque de esfuerzos / eje neutro | la geometría de sección + armado + esfuerzos en `s` |
| (futuro) cue exacto de tracción | añadir el triedro local al `escena` DTO (cambio de server) |
