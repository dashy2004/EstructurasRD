# Diseño — Diagramas P/V/M: panel 2D + cintas 3D (#3)

**Fecha:** 2026-06-14
**Estado:** aprobado en brainstorming; spec escrito para **ejecutar en una nueva sesión**
(writing-plans → subagent-driven). Pendiente: plan de implementación.
**Motivación:** #2 dejó el DTO `esfuerzos` en el estado del front (en modo-ejemplo y
modo-custom) y un *pick readout* mínimo (axial + |M|máx al tocar una barra). #3 convierte
el `diagrama` de cada elemento en **diagramas P/V/M completos**: un **panel 2D** del elemento
seleccionado y **cintas 3D** sobre las barras en la escena. **Todo es front** — el server,
`core/`, `contrato.py` y el resto de `viz/` no se tocan.

Construye sobre #2 ([`2026-06-14-shell-web-webxr-design.md`](2026-06-14-shell-web-webxr-design.md))
y habilita #4 (vista en secciones), que reusa la misma geometría + `diagrama`.

---

## 1. Alcance (MVP confirmado)

**Dentro:** a partir del `esfuerzos` DTO ya cargado, (a) un **panel 2D** que al hacer pick en
una barra dibuja sus **6 diagramas** (N, Vy, Vz, T, My, Mz) a lo largo del miembro, y (b) un
**modo de cintas 3D** que dibuja, para **un componente seleccionable**, la distribución de ese
esfuerzo sobre todas las barras de la escena. Funciona en modo-ejemplo y modo-custom.

**Fuera (confirmado):** cambios en el server / `core` / `contrato` (no hacen falta: el `diagrama`
ya viene completo); diagramas de losa; combinaciones/factores de carga (los esfuerzos son la
demanda combinada sin factorar, ver limitación de #1); exportar/descargar los diagramas como
imagen; runner de tests JS (YAGNI, el proyecto es Python).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Presentación | **Ambos**: panel 2D del elemento (primario) **y** cintas 3D (overlay). |
| Componentes | **Los 6**: N, Vy, Vz, T, My, Mz. |
| Origen de datos | `esfuerzos` DTO ya en el estado del front (de #2). **Sin cambio de server.** |
| Render del panel 2D | **SVG inline** (vendorless, nítido, polilíneas triviales). |
| Selección de componente (cintas 3D) | **Una** entrada "diagramas" en el `<select>` + **dropdown de componente** en el panel (no 6 entradas). |
| Escala de las cintas 3D | Reusar el slider **`exag`** existente. |

## 3. Arquitectura

Todo vive en el front. Se reusa el estado `esfuerzos` (poblado por `renderEscena` en ambos
modos). Dos piezas nuevas con una sola responsabilidad cada una:

```
┌─ index.html ─────────────────────────────────┐
│  #panel (estado, exag, diseño)                │
│  #diag  (NUEVO): <select componente> + SVG    │  ← panel 2D
│  #shell (cargar/pegar/descargar) [de #2]      │
└───────────────┬───────────────────────────────┘
   app.js        │
   ├─ estado esfuerzos  [de #2]                  │
   ├─ pick (pointerdown) → dibujarDiagramas2D(el) │ ← reusa diagramas2d.js
   ├─ modo "diagramas" → construirCintas(comp)    │ ← cintas 3D (BufferGeometry)
   └─ limpiarEscena/resetOverlays → teardown      │
                  │
   diagramas2d.js │  diagramaSVG(elemento) → SVGElement   (función pura, sin three.js)
```

- **`diagramas2d.js`** (nuevo): exporta una función **pura** `diagramaSVG(elemento, opts)` que
  recibe un elemento del DTO (`{id, longitud, extremo_i, extremo_j, diagrama}`) y devuelve un
  `SVGElement` con los 6 mini-diagramas apilados. No conoce three.js ni el DOM global; recibe
  los datos y devuelve nodos SVG. Acotada y testeable en aislamiento (aunque no haya runner JS).
- **Cintas 3D** en `app.js`: helpers `construirCintas(componenteIndex)` / teardown, siguiendo el
  patrón de los overlays existentes (`construirJaula`, `disposeDiseno`, `resetOverlays`).
- **`index.html`**: contenedor `#diag` (un `<select id="diag-comp">` de componente + un host para
  el SVG). CSS mínimo en el `<style>` inline existente.

## 4. Datos (recordatorio del DTO, no se modifica)

`esfuerzos` (de `GET /esfuerzos` y de `POST /visor`):

```
{
  "orden_componentes": ["N", "Vy", "Vz", "T", "My", "Mz"],
  "elementos": [
    { "id", "longitud",
      "extremo_i": [N, Vy, Vz, T, My, Mz],          // fuerzas nodales de extremo (crudas)
      "extremo_j": [N, Vy, Vz, T, My, Mz],
      "diagrama":  [ [s, N, Vy, Vz, T, My, Mz], ... ] }   // n estaciones; índice 0 = s (posición)
  ]
}
```

- Cada estación es `[s, N, Vy, Vz, T, My, Mz]` (7 valores; `s∈[0, longitud]`).
- Índices de componente en una fila del diagrama: **N=1, Vy=2, Vz=3, T=4, My=5, Mz=6** (el índice
  0 es `s`). Equivalentemente, `orden_componentes[k]` ↔ `fila[k+1]`.
- **Diagramas lineales por tramos:** el modelo solo tiene cargas **nodales** (no de vano), así que
  dentro de un elemento N y V son constantes y M es lineal. Conectar las estaciones con segmentos
  rectos es exacto; `n=11` está sobre-muestreado. **No se aumenta `n`.**
- Unidades: fuerzas en N, momentos en N·m. Mostrar en **kN** (`/1000`) y **kN·m**.

## 5. Panel 2D — `diagramas2d.js` + integración por pick

### 5.1 `diagramaSVG(elemento, opts) -> SVGElement`
Función pura que arma un SVG con **6 mini-diagramas apilados** (uno por componente, en el orden
N, Vy, Vz, T, My, Mz). Para cada componente:
- Eje x = `s / longitud` ∈ [0, 1] (normalizado al largo del miembro).
- Eje y = valor del componente en cada estación, **auto-escalado** al máximo |valor| de ese
  componente (si todo es 0 → línea plana en la base, sin dividir por cero).
- **Línea base en cero** (eje horizontal) dibujada.
- **Polilínea** uniendo las estaciones; **relleno por signo** entre la polilínea y la base con el
  esquema divergente (positivo ↔ un color, negativo ↔ otro), reutilizando la idea de
  `colorDeCampo` de la losa (blanco→rojo / blanco→azul).
- Etiqueta por mini-diagrama: nombre del componente + unidad + valor pico (p.ej. `My  |máx| = 3.0 kN·m`).
- Convención de signo: el `diagrama` es el esfuerzo **interno** de sección (tracción +); se grafica
  tal cual (sin re-derivar), coherente con el readout de #2 (`N = -extremo_i[0]`).

Construye con `document.createElementNS('http://www.w3.org/2000/svg', …)` (vendorless). `opts`
permite parámetros mínimos (ancho/alto por mini-diagrama, colores); con defaults sensatos.

### 5.2 Integración por pick (en `app.js`)
El handler `pointerdown` (que ya resuelve la barra tocada a su `id` y muestra `resumenEsfuerzos`
en `#info`) **además** llama a una función `dibujarDiagramas2D(id)` que: busca el elemento en
`esfuerzos.elementos`, llama a `diagramaSVG(el)`, y reemplaza el contenido del host SVG de `#diag`.
Se **conserva** el readout de una línea en `#info`. Disponible en los modos no-overlay
(sin-deformar / deformada / modo-N), igual que el readout de #2. Si no hay `esfuerzos` o el id no
está, el panel queda vacío (sin error).

## 6. Cintas 3D — modo overlay en la escena

### 6.1 Entrada en el `<select>` + dropdown de componente
- Una entrada nueva **"diagramas"** en `selEstado` (el `<select>` de estado), añadida donde
  corresponda según el modo (en ambos modos, ya que `esfuerzos` está presente).
- Un `<select id="diag-comp">` en `#diag` con los 6 componentes; su `change` re-construye las
  cintas con el nuevo componente cuando el modo activo es "diagramas".

### 6.2 Geometría de la cinta (por elemento)
Para cada barra con `esfuerzos` del elemento correspondiente:
- Eje del miembro: `axis = normalize(pos[j] - pos[i])` (de `basePos`).
- **Dirección transversal** (en la que se "levanta" la cinta): derivada del eje + un *up* global
  (igual heurística que orienta las cajas hoy vía `lookAt`): `t1 = normalize(cross(axis, up))`
  con fallback a otro eje si `axis` es casi vertical; `t2 = cross(axis, t1)`.
  - Componentes del plano local 1 (**Mz, Vy**) → desplazan a lo largo de `t1`.
  - Componentes del plano local 2 (**My, Vz**) → desplazan a lo largo de `t2`.
  - **N, T** (sin plano natural) → desplazan a lo largo de `t1` (eje de despliegue neutro, solo
    para visualizar magnitud).
- Por estación `s`: punto base sobre el eje del miembro a fracción `s/longitud`; punto desplazado =
  base + dir × (valor × escala). La **escala** sale del slider `exag` (reusado), normalizada por el
  máximo |valor| del componente sobre todo el modelo para que llene un rango visible.
- **Tira de triángulos** (`THREE.BufferGeometry` con posiciones + índices) entre la polilínea base
  y la polilínea desplazada; **color por signo** (vertex colors divergentes, idea de `colorDeCampo`).
- El grupo de cintas se agrega a la escena con `visible=false` hasta entrar al modo, como los demás
  overlays.

### 6.3 Limitación documentada (aprox. de orientación)
La dirección transversal se deriva del eje del miembro + *up* global, **no** del
`vector_referencia` real del elemento (que el front no recibe en el `escena` DTO). Por tanto el
**plano** en que se dibuja la cinta puede no coincidir con el eje principal real de la sección
cuando se usa un `vector_referencia` no trivial. Los **valores** son correctos; solo la orientación
del despliegue es una aproximación de visualización. Exactitud del plano = mejora futura (añadir el
triedro local al `escena` DTO, un cambio de server fuera de #3).

### 6.4 Teardown
`construirCintas` agrega un grupo (como `armadoGroup`/`disenoGroup`); `limpiarEscena` y
`resetOverlays` lo ocultan/eliminan y hacen `dispose` de sus geometrías, igual que los overlays
existentes. Entrar a "diagramas" pausa la animación de deformada (estático), como losa/refuerzo/diseño.

## 7. Interacción / integración

- **Panel 2D**: dirigido por pick, en modos no-overlay (igual que el readout de #2, pero rico).
- **Cintas 3D**: modo del `<select>` (como losa/refuerzo/diseño). El `<select id="diag-comp">`
  controla qué componente se dibuja en 3D; el panel 2D siempre muestra los 6.
- **Ambos modos** (ejemplo y custom): `esfuerzos` se carga en los dos; las cintas no dependen de
  overlays de ejemplo (losa/armado/diseño), así que también funcionan en modo-custom.

## 8. Manejo de errores / casos borde

| Situación | Respuesta |
|---|---|
| Sin `esfuerzos` (fetch falló) | Panel 2D vacío; entrada "diagramas" inerte (sin cintas, sin error). |
| Elemento no encontrado por id | `dibujarDiagramas2D` no hace nada (panel intacto). |
| Componente todo-cero (típico T o N) | Mini-diagrama y/o cinta como línea/cinta plana en la base (auto-escala evita /0). |
| Barra de largo 0 | Se omite de las cintas (igual que en `construirJaula`). |

## 9. Testing

### 9.1 Sin runner JS (consistente con el proyecto)
No se añade infra de tests JS (YAGNI). `diagramaSVG` se escribe **pura y acotada** por si se añade
un test luego, pero la verificación de #3 es **manual en navegador real** (Playwright), como #2.

### 9.2 Checklist manual (navegador)
1. **Arranque (ejemplo):** render idéntico; el `<select>` gana la entrada "diagramas".
2. **Pick → panel 2D:** click en una barra dibuja 6 mini-diagramas con formas correctas. Caso de
   control (voladizo P en el extremo, L): **N** plano en 0; **M** rampa lineal a `P·L` en el apoyo;
   **V** constante = `P`. Etiquetas con kN / kN·m correctas.
3. **Modo "diagramas" (cintas 3D):** entrar muestra cintas por barra; el dropdown de componente
   cambia N/Vy/Vz/T/My/Mz y reconstruye; el slider `exag` escala la altura; color por signo.
4. **Ambos modos:** repetir pick + cintas tras cargar un modelo propio (modo-custom).
5. **Teardown:** cambiar de "diagramas" a otro estado, y cargar otro modelo, deja la escena limpia
   (sin cintas residuales, sin fugas en consola).
6. **Sin errores** en consola (salvo el `favicon.ico` 404 conocido / artefactos de eventos sintéticos).

**Criterio de aceptación:** en el voladizo de control, el panel 2D reproduce N=0 / V=P / M lineal a
`P·L`, y el modo cintas 3D dibuja la distribución del componente elegido sobre todas las barras.

## 10. Archivos afectados

**Crear:**
- `src/motor_fea/viz/static/diagramas2d.js` (función pura `diagramaSVG`).

**Modificar:**
- `src/motor_fea/viz/static/app.js` (estado/pick → `dibujarDiagramas2D`; modo "diagramas" +
  `construirCintas`/teardown; dropdown de componente; reuso de `exag`).
- `src/motor_fea/viz/static/index.html` (contenedor `#diag` con `<select id="diag-comp">` + host SVG; CSS mínimo).

`core/`, `normativa/`, `api/` (servidor + contrato) y el resto de `viz/` **no se tocan** — #3 es
puramente front sobre el `esfuerzos` DTO existente.

## 11. Roadmap habilitado

| Item | Reusa de #3 |
|---|---|
| #4 vista en secciones | geometría + `esfuerzos.diagrama` + (posiblemente) `diagramaSVG` por sección |
| (futuro) plano exacto de cintas | añadir el triedro local al `escena` DTO (cambio de server) |
