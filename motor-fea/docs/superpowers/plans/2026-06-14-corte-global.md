# Corte Global (Plano de Sección) (#4b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `"corte"` mode that slices the 3D model with an axis-aligned plane (plan / elevation), chosen by an orientation dropdown + position slider, and shows a 2D schematic of every member crossing the plane (each as a `b×h` rect at its projected position); picking a member draws its full `seccionSVG` (cross-section + rebar + the 6 forces at the crossing station), plus a translucent 3D plane marker with crossing markers, and SVG/PNG export of the schematic.

**Architecture:** Pure front-end on the existing `escena`/`esfuerzos`/`diseño`/`armado` DTOs (server/`core`/`contrato` untouched). New pure `corte2d.js` exports `ORIENTACIONES`, `intersectarPlano(segmentos, orient, c)` (segment-vs-plane geometry over plain coords) and `corteSVG(cruces, opts)` (the schematic, using `nodo` from `svgutil`). `app.js` adds the scene→segments adapter, the `"corte"` overlay mode (orientation/slider control, schematic SVG pick→detail reusing `seccionSVG`/`datosSeccion`, the 3D plane marker, export, teardown) — mirroring the #3/#4a overlay lifecycle. `index.html` gains a `#corte` block.

**Tech Stack:** Vanilla ES modules, three.js (vendorized, import map), SVG via `document.createElementNS`, native canvas for PNG. No JS test runner (Python project; spec §11 — YAGNI). Verification is manual in a real browser via Playwright, like #2/#3/#4a.

**TDD note:** Deviates from the skill's default TDD loop — no JS runner; the spec defines verification as a manual browser checklist (Task 4). `corte2d.js` is written pure so unit tests could be added later; the acceptance gate is Playwright. Each code task ends with `node --check` (syntax gate) + a commit.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/motor_fea/viz/static/corte2d.js` | Pure `ORIENTACIONES` + `intersectarPlano` (segment∩plane) + `corteSVG` (2D schematic). No three.js, no global DOM. | **Create** |
| `src/motor_fea/viz/static/index.html` | `#corte` block (orientation dropdown + position slider + schematic host + detail host + SVG/PNG buttons) + CSS. | **Modify** |
| `src/motor_fea/viz/static/app.js` | `tipo` in `addBarra`; `"corte"` mode (`entrarCorte`/`reconstruirCorte`/teardown); scene→segments adapter; 3D plane marker (`construirPlanoCorte`/`disposePlanoCorte`); schematic pick→detail (`dibujarDetalleCorte`, reuses `datosSeccion`/`seccionSVG`); export; listeners. | **Modify** |

DTO recap (NOT modified): `escena.nodos[].p=[x,y,z]`, `escena.barras[]={id,i,j,b,h,tipo}`, `escena.bbox={min:[x,y,z],max:[x,y,z]}`; `esfuerzos.elementos[]={id,longitud,diagrama:[[s,N,Vy,Vz,T,My,Mz],...]}`; `diseño`/`armado.elementos[]` keyed by `id` with `long:[{x,y,d}]`+`estribo:{d,s,w,h}` (diseño adds `tipo,designacion,cumple`). **Z is the vertical axis** (server `_clasificar`: columna = Δz dominates). Units: m; forces N (→kN), moments N·m (→kN·m). Reuses from #4a: `esfuerzosEnEstacion`, `datosSeccion`, `seccionSVG`, `descargarSVG`/`descargarPNG`, `nodo`.

---

### Task 1: `corte2d.js` — pure `intersectarPlano` + `corteSVG`

**Files:**
- Create: `src/motor_fea/viz/static/corte2d.js`

- [ ] **Step 1: Write the module**

```javascript
// Corte global: funciones puras para rebanar el modelo con un plano eje-alineado y
// dibujar el esquema 2D de los miembros cortados. No conocen three.js ni el DOM global
// (sólo crean nodos SVG vía nodo()). Z es el eje vertical del modelo.
import { nodo } from './svgutil.js';

// Orientaciones del plano: eje normal k (0=X,1=Y,2=Z) y los dos ejes proyectados
// (u = horizontal del dibujo, v = vertical del dibujo). Las elevaciones llevan Z (vertical) en v.
//   planta (z):     normal Z → (u,v) = (X, Y)   — vista en planta
//   elevación (x):  normal X → (u,v) = (Y, Z)   — Z arriba
//   elevación (y):  normal Y → (u,v) = (X, Z)   — Z arriba
export const ORIENTACIONES = {
  planta: { k: 2, u: 0, v: 1, etq: 'planta (z)' },
  elev_x: { k: 0, u: 1, v: 2, etq: 'elevación (x)' },
  elev_y: { k: 1, u: 0, v: 2, etq: 'elevación (y)' },
};

const EPS = 1e-9;
const COLOR_TIPO = { columna: '#4a90d9', viga: '#d98a4a' };   // mismo palette que el 3D

// segmentos = [{ id, pi:[x,y,z], pj:[x,y,z], longitud, b, h, tipo }]
// Devuelve cruces = [{ id, u, v, P:[x,y,z], s, b, h, tipo }] para los que cruzan el plano k=c.
export function intersectarPlano(segmentos, orient, c) {
  const { k, u, v } = orient;
  const cruces = [];
  for (const seg of segmentos) {
    const den = seg.pj[k] - seg.pi[k];
    if (Math.abs(den) < EPS) continue;            // paralelo/contenido en el plano: se omite
    const f = (c - seg.pi[k]) / den;
    if (f < 0 || f > 1) continue;                 // no cruza el segmento
    const P = [
      seg.pi[0] + (seg.pj[0] - seg.pi[0]) * f,
      seg.pi[1] + (seg.pj[1] - seg.pi[1]) * f,
      seg.pi[2] + (seg.pj[2] - seg.pi[2]) * f,
    ];
    cruces.push({
      id: seg.id, u: P[u], v: P[v], P,
      s: f * (seg.longitud || 0),
      b: seg.b, h: seg.h, tipo: seg.tipo,
    });
  }
  return cruces;
}

// Color divergente (blanco→azul para v<0, blanco→rojo para v>0). Sólo se usa si opts.comp.
function colorDivergente(val, maxAbs) {
  const s = Math.max(-1, Math.min(1, val / (maxAbs || 1)));
  const m = Math.abs(s);
  const r = s > 0 ? 255 : Math.round(255 * (1 - m));
  const g = Math.round(255 * (1 - m));
  const b = s < 0 ? 255 : Math.round(255 * (1 - m));
  return `rgb(${r},${g},${b})`;
}

// cruces = salida de intersectarPlano (opcionalmente con .fuerzas adjuntas).
// opts = { orientEtq, c, ancho, alto, comp, maxAbs }. Devuelve un SVGElement.
// Cada miembro cortado es un rect b×h con data-id (para el pick del esquema).
export function corteSVG(cruces, opts = {}) {
  const W = opts.ancho || 260;
  const H = opts.alto || 220;
  const pad = 18;
  const total = H + 28;
  const cc = (opts.c != null ? opts.c : 0);
  const svg = nodo('svg', { width: W, height: total, viewBox: `0 0 ${W} ${total}` });

  const etiqueta = (t) => {
    const e = nodo('text', { x: pad, y: H + 18, fill: '#fff', 'font-size': 11, 'font-family': 'sans-serif' });
    e.textContent = t; svg.appendChild(e);
  };

  if (!cruces.length) {
    const e = nodo('text', { x: W / 2, y: H / 2, fill: '#888', 'font-size': 13,
      'font-family': 'sans-serif', 'text-anchor': 'middle' });
    e.textContent = '0 cortes';
    svg.appendChild(e);
    etiqueta(`${opts.orientEtq || ''}  @ ${cc.toFixed(2)} m · 0 cortes`);
    return svg;
  }

  // Auto-fit: rango de (u,v) incluyendo el tamaño de cada rect, con margen.
  let uMin = Infinity, uMax = -Infinity, vMin = Infinity, vMax = -Infinity;
  for (const cr of cruces) {
    const hb = Math.max(cr.b, cr.h) / 2;
    uMin = Math.min(uMin, cr.u - hb); uMax = Math.max(uMax, cr.u + hb);
    vMin = Math.min(vMin, cr.v - hb); vMax = Math.max(vMax, cr.v + hb);
  }
  const du = (uMax - uMin) || 1, dv = (vMax - vMin) || 1;
  const dispW = W - 2 * pad, dispH = H - 2 * pad;
  const esc = Math.min(dispW / du, dispH / dv);
  const offU = pad + (dispW - du * esc) / 2;
  const offV = pad + (dispH - dv * esc) / 2;
  const X = (uu) => offU + (uu - uMin) * esc;
  const Y = (vv) => offV + (vMax - vv) * esc;   // v hacia arriba (SVG y invertido)

  for (const cr of cruces) {
    const w = Math.max(2, cr.b * esc);
    const h = Math.max(2, cr.h * esc);
    let fill = COLOR_TIPO[cr.tipo] || '#888';
    if (opts.comp != null && Array.isArray(cr.fuerzas) && opts.maxAbs > 0) {
      fill = colorDivergente(cr.fuerzas[opts.comp], opts.maxAbs);
    }
    svg.appendChild(nodo('rect', {
      x: X(cr.u) - w / 2, y: Y(cr.v) - h / 2, width: w, height: h,
      fill, stroke: '#fff', 'stroke-width': 0.75, 'data-id': cr.id }));
  }
  etiqueta(`${opts.orientEtq || ''}  @ ${cc.toFixed(2)} m · ${cruces.length} cortes`);
  return svg;
}
```

- [ ] **Step 2: Syntax-check**

Run: `node --check src/motor_fea/viz/static/corte2d.js`
Expected: no output, exit 0.

- [ ] **Step 3: Commit**

```bash
git add src/motor_fea/viz/static/corte2d.js
git commit -m "feat(viz): corte2d.js — intersectarPlano + corteSVG puros (esquema del plano de corte)"
```

---

### Task 2: `index.html` — `#corte` block

**Files:**
- Modify: `src/motor_fea/viz/static/index.html`

- [ ] **Step 1: Add CSS**

In the `<style>` block, immediately after the two `#sec` rules (`#sec { ... }` and `#sec-svg svg { ... }`, currently the last two style lines before `</style>`), add:

```css
    #corte { display: none; flex-direction: column; gap: 6px; margin-top: 4px; }
    #corte-svg svg, #corte-det svg { background: rgba(255,255,255,.04); border-radius: 4px; }
    #corte-svg rect { cursor: pointer; }
    #corte-det { max-height: 40vh; overflow-y: auto; }
```

- [ ] **Step 2: Add the `#corte` markup**

In `#panel`, immediately after the closing `</div>` of the `#sec` block (the line `    </div>` that closes `<div id="sec">`, which is the last child of `#panel` before `</div>` closes `#panel`), add:

```html
    <div id="corte">
      <div class="fila">
        <label for="corte-orient">corte</label>
        <select id="corte-orient" aria-label="orientación del plano de corte">
          <option value="planta">planta (z)</option>
          <option value="elev_x">elevación (x)</option>
          <option value="elev_y">elevación (y)</option>
        </select>
      </div>
      <div class="fila">
        <label for="corte-pos">pos</label>
        <input type="range" id="corte-pos" min="0" max="1" step="0.01" value="0.5">
      </div>
      <div id="corte-svg"></div>
      <div id="corte-det"></div>
      <div class="fila">
        <button id="corte-svg-dl" type="button">SVG</button>
        <button id="corte-png-dl" type="button">PNG</button>
      </div>
    </div>
```

(For reference, the resulting tail of `#panel` is: `<div id="sec">…</div>` then `<div id="corte">…</div>`, then `</div>` closes `#panel`.)

- [ ] **Step 3: Visually confirm the page still loads**

Launch (Task 4 Step 1). Expected: page renders as before; `#corte` is hidden (no visible change yet). No new console errors.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/index.html
git commit -m "feat(viz): index.html — bloque #corte (orient + slider pos + hosts esquema/detalle + SVG/PNG)"
```

---

### Task 3: `app.js` — `"corte"` mode wiring

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Import `corte2d`**

After `import { descargarSVG, descargarPNG } from './svgutil.js';` (line 7), add:

```javascript
import { intersectarPlano, corteSVG, ORIENTACIONES } from './corte2d.js';
```

- [ ] **Step 2: State vars**

After the section state block (the four `let anilloSeccion … let secSvgActual = null;` lines), add:

```javascript
let planoCorte = null;       // marcador 3D del plano (quad semitransparente)
let cruceMarkers = null;     // Group de marcadores 3D en los cruces
let cruceActuales = [];      // últimos cruces calculados (para el pick del esquema)
let corteActivo = false;
let corteOrient = 'planta';  // clave en ORIENTACIONES
let corteC = 0;              // posición del plano en el eje normal (m)
let corteElId = null;        // miembro seleccionado en el esquema (para el detalle)
let corteSvgActual = null;   // esquema SVG actual (para export)
```

- [ ] **Step 3: DOM handles**

After `const btnSecPng = document.getElementById('sec-png-dl');`, add:

```javascript
const corteDiv = document.getElementById('corte');
const corteOrientSel = document.getElementById('corte-orient');
const corteSlider = document.getElementById('corte-pos');
const corteHost = document.getElementById('corte-svg');
const corteDetHost = document.getElementById('corte-det');
const btnCorteSvg = document.getElementById('corte-svg-dl');
const btnCortePng = document.getElementById('corte-png-dl');
```

- [ ] **Step 4: Store `tipo` in `addBarra`**

Replace the `barras.push(...)` line in `addBarra`:

```javascript
  barras.push({ mesh, i: b.i, j: b.j, id: b.id, b: b.b, h: b.h });
```

with:

```javascript
  barras.push({ mesh, i: b.i, j: b.j, id: b.id, b: b.b, h: b.h, tipo: b.tipo });
```

- [ ] **Step 5: Add corte helpers**

After `dibujarSeccion` (the function ending at the line `  posicionarAnillo(s, el);` then `}`, immediately before the `// --- Teardown` comment), add:

```javascript
// --- Corte global (plano de sección) ---
// segmentos para intersectarPlano desde el estado actual (barras + basePos + esfuerzos).
function segmentosCorte() {
  const segs = [];
  for (const bar of barras) {
    const vi = basePos[bar.i], vj = basePos[bar.j];
    if (!vi || !vj) continue;
    const el = esfuerzos && esfuerzos.elementos.find((e) => e.id === bar.id);
    segs.push({
      id: bar.id,
      pi: [vi.x, vi.y, vi.z], pj: [vj.x, vj.y, vj.z],
      longitud: el ? el.longitud : vi.distanceTo(vj),
      b: bar.b, h: bar.h, tipo: bar.tipo,
    });
  }
  return segs;
}

// Configura el slider de posición al rango del bbox en el eje normal de la orientación actual.
function configurarSliderCorte() {
  const k = ORIENTACIONES[corteOrient].k;
  const lo = frameBbox ? frameBbox.min[k] : 0;
  const hi = frameBbox ? frameBbox.max[k] : 1;
  const span = (hi - lo) || 1;
  corteSlider.min = lo; corteSlider.max = hi; corteSlider.step = span / 200;
  corteSlider.value = (lo + hi) / 2;
  corteC = parseFloat(corteSlider.value);
}

function reconstruirCorte() {
  if (!corteActivo) return;
  const orient = ORIENTACIONES[corteOrient];
  corteC = parseFloat(corteSlider.value);
  cruceActuales = intersectarPlano(segmentosCorte(), orient, corteC);
  corteSvgActual = corteSVG(cruceActuales, { orientEtq: orient.etq, c: corteC });
  corteHost.replaceChildren(corteSvgActual);
  // si el miembro del detalle ya no cruza, limpiar el detalle
  if (corteElId != null && !cruceActuales.some((cr) => cr.id === corteElId)) {
    corteElId = null;
    corteDetHost.replaceChildren();
  }
  construirPlanoCorte(orient, corteC, cruceActuales);
  info.textContent = `${cruceActuales.length} cortes — ${orient.etq} @ ${corteC.toFixed(2)} m`;
}

function construirPlanoCorte(orient, c, cruces) {
  disposePlanoCorte();
  if (!frameBbox) return;
  const k = orient.k;
  const otros = [0, 1, 2].filter((a) => a !== k);
  const min = frameBbox.min, max = frameBbox.max;
  const w = (max[otros[0]] - min[otros[0]]) || 1;
  const h = (max[otros[1]] - min[otros[1]]) || 1;
  const mat = new THREE.MeshBasicMaterial({ color: 0x00ff88, transparent: true,
    opacity: 0.15, side: THREE.DoubleSide, depthWrite: false });
  planoCorte = new THREE.Mesh(new THREE.PlaneGeometry(w, h), mat);
  const centro = [0, 0, 0];
  centro[k] = c;
  centro[otros[0]] = (min[otros[0]] + max[otros[0]]) / 2;
  centro[otros[1]] = (min[otros[1]] + max[otros[1]]) / 2;
  planoCorte.position.set(centro[0], centro[1], centro[2]);
  const normal = new THREE.Vector3(0, 0, 0); normal.setComponent(k, 1);
  planoCorte.quaternion.setFromUnitVectors(new THREE.Vector3(0, 0, 1), normal);
  scene.add(planoCorte);

  cruceMarkers = new THREE.Group();
  for (const cr of cruces) {
    const r = Math.max(cr.b, cr.h) * 0.35 || 0.08;
    const m = new THREE.Mesh(new THREE.SphereGeometry(r, 8, 8),
      new THREE.MeshBasicMaterial({ color: cr.id === corteElId ? 0xffff00 : 0x00ff88 }));
    m.position.set(cr.P[0], cr.P[1], cr.P[2]);
    m.userData.id = cr.id;
    cruceMarkers.add(m);
  }
  scene.add(cruceMarkers);
}

function disposePlanoCorte() {
  if (planoCorte) {
    scene.remove(planoCorte);
    planoCorte.geometry.dispose(); planoCorte.material.dispose();
    planoCorte = null;
  }
  if (cruceMarkers) {
    scene.remove(cruceMarkers);
    cruceMarkers.traverse((o) => { if (o.geometry) o.geometry.dispose(); if (o.material) o.material.dispose(); });
    cruceMarkers = null;
  }
}

function dibujarDetalleCorte(idStr) {
  const cr = cruceActuales.find((c) => String(c.id) === String(idStr));
  if (!cr || !esfuerzos) return;
  corteElId = cr.id;
  const el = esfuerzos.elementos.find((e) => e.id === cr.id);
  const L = el ? (el.longitud || 1) : 1;
  const datos = datosSeccion(cr.id, cr.s, L);
  if (!datos) return;
  corteDetHost.replaceChildren(seccionSVG(datos));
  if (cruceMarkers) {
    for (const m of cruceMarkers.children) {
      m.material.color.set(m.userData.id === cr.id ? 0xffff00 : 0x00ff88);
    }
  }
}

function entrarCorte() {
  corteActivo = true;
  corteElId = null;
  corteSvgActual = null;
  if (corteHost) corteHost.replaceChildren();
  if (corteDetHost) corteDetHost.replaceChildren();
  if (diagSvg) diagSvg.replaceChildren();   // evita el diagrama viejo encima del panel
  if (corteDiv) corteDiv.style.display = 'flex';
  corteOrient = 'planta';
  corteOrientSel.value = 'planta';
  configurarSliderCorte();
  reconstruirCorte();
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}
```

- [ ] **Step 6: Hook teardown into `resetOverlays`**

In `resetOverlays`, add `corteActivo = false;` immediately after `secActivo = false;`. Then, immediately after the existing `secElId = null; secSvgActual = null;` line, add:

```javascript
  disposePlanoCorte();
  if (corteDiv) corteDiv.style.display = 'none';
  corteElId = null; corteSvgActual = null; cruceActuales = [];
```

- [ ] **Step 7: Hook the mode into `setEstado`**

In `setEstado`, add `|| corteActivo` to `veniaEspecial`:

```javascript
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo || diagActivo || secActivo || corteActivo;
```

and add the `corte` branch immediately after the `seccion` branch:

```javascript
  if (nuevo === 'seccion') { entrarSeccion(); return; }
  if (nuevo === 'corte') { entrarCorte(); return; }
```

- [ ] **Step 8: Exclude corte from the 3D pick fallback**

In the `pointerdown` handler, the last `else if` branch (the 2D diagram panel) currently reads:

```javascript
  } else if (esfuerzos && !refuerzoActivo && !diagActivo && !secActivo) {   // panel 2D solo en modos no-overlay (spec §5.2/§7)
```

Replace its header with (add `&& !corteActivo`):

```javascript
  } else if (esfuerzos && !refuerzoActivo && !diagActivo && !secActivo && !corteActivo) {   // panel 2D solo en modos no-overlay (spec §5.2/§7)
```

(The branch body is unchanged. Corte picks happen on the schematic SVG, not the 3D canvas.)

- [ ] **Step 9: Listeners**

After the three section listeners (`secSlider.addEventListener(...)`, `btnSecSvg...`, `btnSecPng...`), add:

```javascript
corteOrientSel.addEventListener('change', () => {
  corteOrient = corteOrientSel.value;
  configurarSliderCorte();
  reconstruirCorte();
});
corteSlider.addEventListener('input', () => { if (corteActivo) reconstruirCorte(); });
corteHost.addEventListener('click', (ev) => {
  if (!corteActivo) return;
  const id = ev.target && ev.target.getAttribute ? ev.target.getAttribute('data-id') : null;
  if (id == null) return;
  dibujarDetalleCorte(id);
});
btnCorteSvg.addEventListener('click', () => descargarSVG(corteSvgActual, 'corte.svg'));
btnCortePng.addEventListener('click', () => descargarPNG(corteSvgActual, 'corte.png'));
```

- [ ] **Step 10: Add the `corte` option in `renderEscena`**

In `renderEscena`, replace the `if (esf) { … }` block with:

```javascript
  if (esf) {
    esfuerzos = esf;
    selEstado.add(new Option('diagramas', 'diagramas'));
    selEstado.add(new Option('sección', 'seccion'));
    selEstado.add(new Option('corte', 'corte'));
  }
```

- [ ] **Step 11: Teardown in `limpiarEscena`**

In `limpiarEscena`, immediately after the existing section teardown line `  secActivo = false; secElId = null; secSvgActual = null;`, add:

```javascript
  disposePlanoCorte();
  if (corteHost) corteHost.replaceChildren();
  if (corteDetHost) corteDetHost.replaceChildren();
  if (corteDiv) corteDiv.style.display = 'none';
  corteActivo = false; corteElId = null; corteSvgActual = null; cruceActuales = [];
  corteOrient = 'planta';
```

- [ ] **Step 12: Syntax-check**

Run: `node --check src/motor_fea/viz/static/app.js`
Expected: no output, exit 0.

- [ ] **Step 13: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): modo corte global — plano eje-alineado + esquema 2D + pick→sección + marcador 3D + export"
```

---

### Task 4: Manual browser verification (Playwright) — acceptance gate

**Files:** none (verification only). Replaces unit tests per spec §11.

- [ ] **Step 1: Launch the viz server**

Run (background): `.venv/bin/python -m motor_fea.api.cli --serve --port 8000` → `http://127.0.0.1:8000/`. (Requires the `[api]` extra / FastAPI, already in `.venv`.) Append `?v=N` to the URL to bypass the static-file cache after edits.

- [ ] **Step 2: Drive the checklist with Playwright**

Navigate to the URL and verify (screenshot + console each step). Drive orientation/slider via `change`/`input` events on the `<select>`/slider; for the **schematic pick**, dispatch a `click` on a `#corte-svg rect` element (it carries `data-id`) — these are real SVG clicks, NOT canvas raycaster picks, so they do **not** emit the `setPointerCapture` OrbitControls artifacts. (Switching modes via the `estado` `<select>` likewise uses `change`.)

1. **Arranque (ejemplo):** the `estado` `<select>` gains a `"corte"` entry; `#corte` hidden.
2. **Entrar a corte:** select `corte` → `#corte` visible, orientation = `planta (z)`, slider centered; `#corte-svg` shows rects for the members crossing the horizontal plane (the **columns** of the example pórtico, in their (x,y) positions, blue); a translucent green plane appears in the 3D scene at that height; `info` = "N cortes — planta (z) @ … m".
3. **Slider de posición:** moving `#corte-pos` recomputes the crossings and repositions the 3D plane live; at a height with no members the schematic shows "0 cortes".
4. **Cambio de orientación:** `elevación (x)` / `elevación (y)` → schematic switches to an elevation view (Z up), different members cross (beams), and the 3D plane rotates to the new orientation.
5. **Pick → detalle:** clicking a rect in `#corte-svg` draws that member's `seccionSVG` (b×h + rebar + the 6 forces at the crossing station) in `#corte-det`; the member's 3D crossing marker turns yellow. **Control:** the forces shown equal `esfuerzosEnEstacion` at the crossing `s` (same as the sección mode at that station).
6. **Export:** clicking `SVG` downloads `corte.svg`; `PNG` downloads `corte.png` (verify the handlers run without error / via download events).
7. **Adaptativo (custom):** load a model via the shell → corte works; the detail degrades to b×h + forces only (no rebar).
8. **Teardown:** switch from `corte` to another estado, then load another model → no residual 3D plane/markers, `#corte` hidden, `#corte-svg`/`#corte-det` empty, no console leaks.
9. **No errors** in console except the known `favicon.ico` 404. (Schematic picks are SVG clicks → no `setPointerCapture` artifacts.)

- [ ] **Step 3: Record the result**

If all pass, the acceptance criterion (spec §11.2) is met. If any fails, fix the responsible task's code, re-run `node --check`, and re-verify.

- [ ] **Step 4: Close the browser and stop the server**

---

## Self-Review

**Spec coverage:**
- §2 axis-aligned plane, dropdown+slider, hybrid, esfuerzos at crossing, front-only, 3D marker, SVG+PNG, inline SVG → Tasks 1–3. ✓
- §3 architecture: new `corte2d.js` (pure), `app.js` mode, `index.html` block; `seccion2d`/`svgutil` reused untouched → File Structure + Tasks 1/2/3. ✓
- §5 `intersectarPlano` (segment∩plane, omit `|den|<ε` and `f∉[0,1]`, projection table) → Task 1 Step 1 + `ORIENTACIONES`. ✓
- §6 `corteSVG` (auto-fit, rect-per-crossing with `data-id`, optional comp color, "0 cortes" label) → Task 1 Step 1. ✓
- §7.1 `"corte"` select entry + `entrarCorte` (planta default, centered slider) → Task 3 Steps 5/7/10. ✓
- §7.2 orientation dropdown reconfigures slider; slider `input` → `reconstruirCorte` → Task 3 Steps 5/9. ✓
- §7.3 schematic pick (`data-id`) → `seccionSVG(datosSeccion(...))` in `#corte-det` + 3D highlight → Task 3 Steps 5/9. ✓
- §7.4 3D plane quad (translucent, oriented to normal) + crossing markers, selected highlighted → Task 3 Step 5 (`construirPlanoCorte`). ✓
- §7.5 adaptive rebar via reused `datosSeccion` → Task 3 Step 5 (`dibujarDetalleCorte`). ✓
- §7.6 teardown in `resetOverlays`/`limpiarEscena` → Task 3 Steps 6/11. ✓
- §8 export SVG+PNG of schematic → Task 3 Step 9 + reused `descargar*`. ✓
- §9 limitation — documented in spec; schematic rects axis-aligned, contained members omitted (no overclaim). ✓
- §10 edge cases (no esfuerzos→no option; parallel/no-cross omitted; 0 crossings label; clamped slider; pick w/o data-id no-op; no design→b×h only) → guards in Tasks 1 & 3. ✓
- §11 Playwright checklist incl. control case → Task 4. ✓
- §12 files (create corte2d; modify app/index; seccion2d/svgutil untouched) → matches. ✓

**Placeholder scan:** No TBD/"handle errors"/"similar to" — all code shown in full. `tipo` stored in `addBarra` (Task 3 Step 4) before `segmentosCorte` consumes it.

**Type/name consistency:** `corteActivo`, `corteOrient`, `corteC`, `corteElId`, `corteSvgActual`, `cruceActuales`, `planoCorte`, `cruceMarkers`, `corteDiv`, `corteOrientSel`, `corteSlider`, `corteHost`, `corteDetHost`, `btnCorteSvg`, `btnCortePng`, `segmentosCorte`, `configurarSliderCorte`, `reconstruirCorte`, `construirPlanoCorte`, `disposePlanoCorte`, `dibujarDetalleCorte`, `entrarCorte` — used identically across steps. `intersectarPlano`/`corteSVG`/`ORIENTACIONES` exported by `corte2d` and imported by `app`. Reused from #4a: `datosSeccion`, `seccionSVG`, `descargarSVG`, `descargarPNG`. Select value `"corte"`; orientation keys `planta`/`elev_x`/`elev_y` match `ORIENTACIONES` and the `<option value>`s. The cruce object shape `{id,u,v,P,s,b,h,tipo}` is produced by `intersectarPlano` and consumed by `corteSVG`/`construirPlanoCorte`/`dibujarDetalleCorte` identically.
