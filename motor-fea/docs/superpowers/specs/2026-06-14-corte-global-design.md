# Diseño — Corte global (plano de sección) (#4b)

**Fecha:** 2026-06-14
**Estado:** aprobado en brainstorming; spec para ejecutar (writing-plans → subagent-driven).
**Motivación:** #4 ("vista en secciones") se descompuso en **#4a** (corte transversal de **un**
miembro elegido por pick — **HECHO y mergeado**) y **#4b** (esta spec) — un **plano de corte
global** que **rebana el modelo 3D** y muestra la **rebanada 2D** del conjunto. #4b es **100%
front** sobre los DTOs existentes (`escena` + `esfuerzos` + `diseño`/`armado`): el server, `core/`,
`contrato.py` y el resto de `viz/` **no se tocan**.

Construye sobre #4a ([`2026-06-14-vista-secciones-design.md`](2026-06-14-vista-secciones-design.md))
y el patrón de modos overlay de #2/#3. Arranque enmarcado en
[`../2026-06-14-corte-global-kickoff.md`](../2026-06-14-corte-global-kickoff.md).

---

## 1. Alcance (MVP confirmado)

**Dentro:** un modo `"corte"` en el `<select>` de estado que rebana el modelo con un **plano
eje-alineado** controlado por **dropdown de orientación + slider de posición**, y muestra:
- un **esquema 2D** (planta o elevación) con **cada miembro que cruza el plano** dibujado como un
  rectángulo `b×h` (o punto) en su posición **proyectada** sobre el plano, coloreable por un esfuerzo
  en el cruce;
- al **tocar un miembro cortado** en el esquema, su **sección transversal completa** (corte `b×h` +
  armado + 6 esfuerzos en la estación del cruce) vía `seccionSVG` (reuso de #4a) — **híbrido**;
- un **marcador 3D** del plano (quad semitransparente) + marcadores en los cruces;
- **export** del esquema a **SVG y PNG**.

Funciona en **modo-ejemplo** y **modo-custom**.

**Fuera (confirmado):**
- **Plano arbitrario** (normal en cualquier dirección) — sólo ejes-alineados (planta/elevación);
  el arbitrario sería mejora futura.
- **Gizmo 3D arrastrable** — el control es dropdown + slider (consistente, barato, sin riesgo).
- Cambios en server / `core` / `contrato` (front-only; la intersección segmento-plano es trivial).
- Miembros **contenidos** en el plano (paralelos, `|denom| < ε`) — se omiten en v1 (ver §9/§10).
- Export del **detalle** por-miembro aquí — ya es exportable en el modo **sección** (#4a); no se
  duplica (YAGNI). El export de #4b es del **esquema** (el "plano de corte").
- Runner de tests JS (YAGNI; el proyecto es Python).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Orientación del plano | **Ejes-alineados**: planta (z=cte) o elevación (x=cte / y=cte). Cubre planta/elevación. |
| Control del plano | **Dropdown de orientación + slider de posición** (reusa el patrón del slider, como `#sec-s`). |
| Contenido de la rebanada | **Híbrido**: esquema 2D de los cruces + pick de un miembro → su `seccionSVG`. |
| Esfuerzos en el cruce | **Anotar N/V/M** en cada cruce vía `esfuerzosEnEstacion(el, s_cruce)` (interp. exacta de #4a). |
| Front vs server | **Front-only**: intersección y proyección 100% en el front; sin endpoint nuevo. |
| Marcador 3D | **Quad semitransparente** en el plano + marcadores en los cruces; miembro elegido resaltado. |
| Export | **SVG y PNG** del esquema (reusa `descargarSVG`/`descargarPNG`). |
| Render del esquema | **SVG inline** (vendorless), como #3/#4a; vía `nodo()` de `svgutil`. |

## 3. Arquitectura

6.ª instancia del patrón overlay (losa → refuerzo → diseño → diagramas → sección → **corte**).
Todo vive en el front, sobre `esfuerzos` (poblado por `renderEscena`) + `escena.barras`/`basePos`
+ `diseno`/`armado` cuando existen. Piezas con una responsabilidad cada una:

```
┌─ index.html ─────────────────────────────────────┐
│  #panel (estado, exag, diseño) [de #2/#3]         │
│  #diag (diagramas) [de #3] · #sec (sección) [#4a] │
│  #corte (NUEVO): dropdown orient + slider pos      │  ← panel de corte global
│                  + host esquema + host detalle     │
│                  + botones SVG/PNG                  │
└───────────────┬───────────────────────────────────┘
   app.js        │
   ├─ estado esfuerzos / diseno / armado [previos]   │
   ├─ esfuerzosEnEstacion(el, s) / datosSeccion [#4a]│  (reuso directo)
   ├─ modo "corte" → entrarCorte / reconstruirCorte  │
   ├─ orient/slider → recalcula cruces + plano 3D     │
   ├─ pick del esquema (data-id) → detalle seccionSVG │
   └─ construirPlanoCorte / disposePlanoCorte         │
                  │
   corte2d.js     │  intersectarPlano(segmentos, k, c) → cruces   (pura, sin three.js)
                  │  corteSVG(cruces, opts) → SVGElement          (pura, usa nodo())
   seccion2d.js   │  seccionSVG(...)  [#4a, reuso para el detalle]
   svgutil.js     │  nodo(), descargarSVG(), descargarPNG()  [compartidos]
```

- **`corte2d.js`** (nuevo): dos funciones **puras**, sin three.js ni DOM global.
  - `intersectarPlano(segmentos, k, c) -> cruces[]` — geometría pura sobre coords planas (ver §5).
  - `corteSVG(cruces, opts) -> SVGElement` — arma el esquema 2D (ver §6); usa `nodo` de `svgutil`.
- **`app.js`**: adaptador escena→segmentos, modo `"corte"`, control (orient + slider), pick del
  esquema, marcador 3D y export. Helpers nuevos siguiendo el patrón de overlays:
  `entrarCorte`, `reconstruirCorte`, `construirPlanoCorte`/`disposePlanoCorte`, teardown.
- **`index.html`**: contenedor `#corte`. CSS mínimo en el `<style>` inline existente.
- **`seccion2d.js` / `svgutil.js`**: sin cambios; se **reusan** (`seccionSVG`, `nodo`, export).

## 4. Datos (recordatorio de los DTOs, no se modifican)

- **`escena`** (de #2): `nodos[]` `{id, p:[x,y,z]}`; `barras[]` `{id, i, j, b, h, tipo}`;
  `bbox {min:[x,y,z], max:[x,y,z]}`. En el front: `basePos[id]` = `THREE.Vector3`; `barras[]` =
  `{mesh, i, j, id, b, h}` (b/h ya guardados desde #4a). **Z es el eje vertical** (server:
  `_clasificar` → columna = Δz domina; la losa confirma: footprint XY, deflexión en Z).
- **`esfuerzos`** (de #1/#2): `elementos[]` `{id, longitud, extremo_i, extremo_j, diagrama}`;
  `diagrama` = filas `[s, N, Vy, Vz, T, My, Mz]`. Interp. lineal exacta ya disponible como
  `esfuerzosEnEstacion(el, s)` (cargas nodales → N/V constantes, M lineal).
- **`diseño`/`armado`** (modo-ejemplo): `elementos[]` por `id` con `long:[{x,y,d}]` +
  `estribo:{d,s,w,h}` (+ diseño: `tipo,designacion,cumple`). `datosSeccion(id,s,L)` ya resuelve la
  fuente del armado (diseño → armado → bare).
- Unidades: posiciones/dimensiones en **m**; fuerzas en **N**, momentos en **N·m**. Mostrar fuerzas
  en **kN** y momentos en **kN·m** (`seccionSVG` y `colorDeCampo` ya lo manejan).

## 5. Geometría — `corte2d.js :: intersectarPlano(segmentos, k, c)`

Función pura. `segmentos` = lista de `{ id, pi:[x,y,z], pj:[x,y,z], longitud, b, h, tipo }`
(construida en `app.js` desde `barras` + `basePos` + `esfuerzos`). `k` = eje normal (0=X, 1=Y,
2=Z). `c` = posición del plano en ese eje. Devuelve `cruces[]`:

Para cada segmento:
- `den = pj[k] − pi[k]`; si `|den| < ε` (≈ paralelo / contenido) → **se omite** (§9/§10).
- `f = (c − pi[k]) / den`; si `f ∉ [0,1]` → no cruza, se omite.
- Cruza en `P = lerp(pi, pj, f)` (3D); estación `s = f · longitud`.
- Proyección 2D `(u, v)` = las **dos coords no-normales** de `P`, con `v` = la coord vertical
  cuando exista (ver tabla abajo) para que las elevaciones queden con Z arriba.

| Normal `k` | Esquema | `u` (horiz) | `v` (vert) |
|---|---|---|---|
| 2 (Z) → planta | planta | `P.x` | `P.y` |
| 0 (X) → elevación X | elevación | `P.y` | `P.z` |
| 1 (Y) → elevación Y | elevación | `P.x` | `P.z` |

Cada cruce: `{ id, u, v, P, s, b, h, tipo }`. Las **fuerzas** en el cruce las calcula `app.js`
(`esfuerzosEnEstacion(el, s)`) y las adjunta antes de `corteSVG`, manteniendo `intersectarPlano`
puramente geométrica (sin conocer `esfuerzos`).

## 6. Esquema 2D — `corte2d.js :: corteSVG(cruces, opts)`

Función **pura** que arma el SVG del plano de corte:
- **Auto-fit:** calcula el rango de `(u, v)` de los cruces y escala a un viewBox con margen
  (mismo enfoque de auto-escala que `seccionSVG`); `v` hacia arriba (SVG y invertido).
- **Miembro cortado:** cada cruce se dibuja como un **rectángulo `b×h`** centrado en `(u,v)` a la
  escala del esquema (orientación aprox. eje-alineada; ver §9). Si el rect resulta sub-pixel, cae a
  un **punto** (círculo mínimo). Cada nodo SVG lleva `data-id` = id del miembro (para el pick).
- **Color por esfuerzo (opcional):** `opts.comp` (0..5) + `opts.maxAbs` → color divergente por
  signo (equivalente a `colorDeCampo`); por defecto, relleno neutro. (v1: color fijo o por
  componente; mantener simple.)
- **Etiqueta:** encabezado con orientación + posición `c` y el número de cruces.
- **Cero cruces:** SVG con sólo el mensaje "0 cortes" (sin rects), sin lanzar.

Construye con `nodo()` de `svgutil`. `opts` da tamaño/colores/escala con defaults.

## 7. Modo 3D + interacción (en `app.js`)

### 7.1 Entrada del modo
- Una entrada **"corte"** en `selEstado`, añadida en `renderEscena` cuando hay `esfuerzos`
  (junto a "diagramas" y "sección").
- `entrarCorte()`: activa el modo (estático — barras en base, sin deformar), muestra `#corte`,
  fija orientación por defecto = **planta (z)**, posición = **centro del rango z** del bbox,
  construye el esquema + el plano 3D, y deja el detalle vacío hasta el primer pick. `encuadrar(bbox)`.

### 7.2 Control: orientación + posición
- Dropdown `#corte-orient`: `planta (z)` / `elevación (x)` / `elevación (y)`. En `change`:
  reconfigura el slider al rango del bbox en el nuevo eje normal (`min/max/step`, valor = centro) y
  llama `reconstruirCorte()`.
- Slider `#corte-pos` (`input range`, propio — **no** reusa `exag`): en `input` actualiza `c` y
  llama `reconstruirCorte()`.
- `reconstruirCorte()`: arma `segmentos` desde `barras`/`basePos`/`esfuerzos`, calcula
  `cruces = intersectarPlano(...)`, adjunta `fuerzas` por cruce, redibuja el esquema
  (`corteSVG`), reposiciona el plano/markers 3D, y limpia el detalle si el miembro previo ya no
  cruza. `info` = "N cortes — <orientación> @ c = … m".

### 7.3 Pick del esquema → detalle
`pointerdown`/`click` sobre el host del esquema: si el target lleva `data-id`, fija el miembro y
dibuja el detalle con `seccionSVG(datosSeccion(id, s_cruce, L))` en `#corte-det`, y resalta su
marcador 3D. `s_cruce` y `L` salen del cruce/`esfuerzos`. (Pick **2D** sobre el SVG, no raycaster
3D — el esquema es un panel; esto no toca la lógica de pick 3D existente.)

### 7.4 Marcador 3D del plano
`construirPlanoCorte()`: un `THREE.Mesh` (`PlaneGeometry`) semitransparente
(`transparent, opacity≈0.15, side: DoubleSide, depthWrite:false`), dimensionado al bbox en los dos
ejes no-normales, posicionado en `c` a lo largo del eje normal y orientado con su normal = eje del
plano. Más marcadores pequeños (p.ej. esferas/círculos) en cada cruce; el del miembro elegido se
resalta (color/escala). `reconstruirCorte` reposiciona/regenera; `disposePlanoCorte` libera
geometría/material (patrón del anillo de #4a).

### 7.5 Fuente del armado (adaptativo)
El detalle reusa `datosSeccion(id, s, L)` (#4a): `long`/`estribo`/`designacion`/`cumple` de
`diseno` por `id` → si no, `armado` → si no, sólo `b×h` + esfuerzos. El `b×h` siempre de la barra.

### 7.6 Teardown
`resetOverlays` y `limpiarEscena` ocultan/eliminan el plano 3D y los markers (`dispose`), vacían
`#corte-svg` y `#corte-det`, limpian el miembro seleccionado y desactivan el modo (`corteActivo`),
igual que las cintas (#3) y el anillo (#4a). Cargar otro modelo deja la escena/paneles limpios.

## 8. Export (en `app.js` + `svgutil.js`)

Dos botones en `#corte`: **SVG** (`#corte-svg-dl`) y **PNG** (`#corte-png-dl`), que actúan sobre el
**esquema** actual (`corteSvgActual`):
- SVG: `descargarSVG(corteSvgActual, 'corte.svg')`.
- PNG: `descargarPNG(corteSvgActual, 'corte.png')`.
- Sin esquema (no debería ocurrir; siempre hay esquema al entrar) o cero cruces → exporta el SVG tal
  cual (incluido el mensaje "0 cortes"); nunca no-op silencioso roto.

## 9. Limitación documentada (igual que #3 §6.3 / #4a §9)

La **orientación** de cada sección dibujada en el esquema (y del cue de tracción en el detalle) usa
los ejes **dibujados** / eje-alineados, no el **triedro local real** del elemento (el front no
recibe `vector_referencia` en el DTO `escena`). Los **valores** (posición del cruce, estación `s`,
6 esfuerzos) son **exactos**; sólo la orientación del rect/cue es una aproximación de visualización.
Además, los miembros **contenidos en el plano** (paralelos) se **omiten** en v1 (no se dibujan como
línea). Arreglo exacto de la orientación = añadir el triedro local al `escena` DTO (cambio de
server, fuera de #4b — lo mismo que habilitaría el plano exacto de las cintas de #3 y el cue de #4a).

## 10. Manejo de errores / casos borde

| Situación | Respuesta |
|---|---|
| Sin `esfuerzos` (fetch falló) | Entrada "corte" no aparece (igual que diagramas/sección). |
| Miembro paralelo/contenido (`\|den\| < ε`) | Se omite del esquema (no se dibuja). |
| Miembro no cruza (`f ∉ [0,1]`) | Se omite. |
| Cero cruces (plano entre miembros) | Esquema con mensaje "0 cortes"; detalle vacío; sin crash. |
| Plano fuera del bbox | El slider está clamped al rango del bbox → no ocurre; si ocurriera, 0 cruces. |
| Pick en zona del esquema sin `data-id` | No hace nada (detalle intacto). |
| `longitud ≤ 0` de un miembro | `s = f·longitud = 0`; el cruce se dibuja; las fuerzas usan clamp de `esfuerzosEnEstacion`. |
| Sin diseño ni armado (custom) | El detalle muestra sólo `b×h` + esfuerzos. |

## 11. Testing

### 11.1 Sin runner JS (consistente con el proyecto)
No se añade infra de tests JS (YAGNI). `corte2d.js` se escribe **puro y acotado**
(`intersectarPlano` + `corteSVG`); la verificación es **manual en navegador real** (Playwright),
como #2/#3/#4a. Los 225 tests Python deben seguir verde (no se toca Python).

### 11.2 Checklist manual (navegador)
1. **Arranque (ejemplo):** render idéntico; el `<select>` gana la entrada "corte".
2. **Entrar a corte:** aparece `#corte`; orientación = planta, slider centrado; el esquema muestra
   los miembros que cruzan el plano horizontal (las **columnas** del pórtico como rects en su (x,y)),
   y el plano 3D semitransparente aparece en la escena a esa altura.
3. **Slider de posición:** mover `c` recalcula los cruces y reposiciona el plano 3D en vivo; a
   alturas sin columnas el esquema muestra "0 cortes".
4. **Cambio de orientación:** elevación (x) / (y) → el esquema cambia a vista de elevación (Z
   arriba), distintos miembros cruzan; el plano 3D rota a la nueva orientación.
5. **Pick → detalle:** tocar un rect del esquema dibuja su `seccionSVG` (corte b×h + armado +
   6 esfuerzos en la estación del cruce) en `#corte-det`; el marcador 3D del miembro se resalta.
   **Caso de control:** los esfuerzos en el cruce coinciden con `esfuerzosEnEstacion` (mismo valor
   que el modo sección a esa `s`).
6. **Export:** "SVG" descarga un `.svg` abrible del esquema; "PNG" un `.png` con el mismo dibujo.
7. **Adaptativo (custom):** cargar un modelo propio (shell) → corte funciona; el detalle degrada a
   sólo b×h + esfuerzos (sin armado).
8. **Teardown:** cambiar de "corte" a otro estado y cargar otro modelo deja escena/paneles limpios
   (sin plano ni markers residuales, sin SVG viejo, sin fugas en consola).
9. **Sin errores** en consola (salvo `favicon.ico` 404 conocido / artefactos de eventos sintéticos
   `setPointerCapture` de OrbitControls; los picks del esquema son sobre el SVG, no generan esos).

**Criterio de aceptación:** en el pórtico de ejemplo, una planta a media altura corta las columnas y
las dibuja en su posición (x,y); el pick reproduce la sección/esfuerzos del miembro; cambio de
orientación da una elevación coherente; SVG y PNG descargan; teardown limpio.

## 12. Archivos afectados

**Crear:**
- `src/motor_fea/viz/static/corte2d.js` (funciones puras `intersectarPlano` + `corteSVG`).

**Modificar:**
- `src/motor_fea/viz/static/app.js` (adaptador escena→segmentos; modo "corte" + `entrarCorte`/
  `reconstruirCorte`/teardown; control orient + slider; pick del esquema → detalle; marcador 3D
  `construirPlanoCorte`/`disposePlanoCorte`; export; refs DOM).
- `src/motor_fea/viz/static/index.html` (contenedor `#corte`: dropdown orient + slider pos + host
  esquema + host detalle + 2 botones; CSS mínimo).

`core/`, `normativa/`, `api/` (servidor + contrato), `seccion2d.js`, `svgutil.js` y el resto de
`viz/` **no se tocan** — #4b es puramente front sobre los DTOs y helpers existentes.

## 13. Roadmap habilitado

| Item | Reusa de #4b |
|---|---|
| (futuro) plano arbitrario | `intersectarPlano` generalizable a normal arbitraria + proyección a base del plano |
| (futuro) gizmo 3D arrastrable | el marcador 3D del plano ya existe; faltaría el drag |
| (futuro) cue/orientación exactos | añadir el triedro local al `escena` DTO (cambio de server) |
| (futuro) export DXF/medidas | el esquema 2D ya tiene las coords proyectadas de cada cruce |
