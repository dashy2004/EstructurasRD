# Vista de Sección de Miembro (#4a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `"sección"` mode that, on picking a bar, draws its cross-section at a slider-chosen station `s` — the b×h rectangle, rebar (when design/armado is loaded), the 6 internal forces interpolated at `s`, a light moment/tension cue, a 3D ring marker on the bar, and SVG/PNG export.

**Architecture:** Pure front-end, on the existing `esfuerzos` + `diseño`/`armado` DTOs (server/`core`/`contrato` untouched). New `svgutil.js` holds shared SVG helpers (`nodo`, `descargarSVG`, `descargarPNG`); `diagramas2d.js` is refactored to import `nodo` from it. New pure `seccion2d.js` exports `seccionSVG(datos)`. `app.js` adds interpolation, the `"sección"` overlay mode, pick→member, the station slider, the 3D ring, the adaptive rebar source, and export — mirroring the #3 overlay lifecycle. `index.html` gains a `#sec` block.

**Tech Stack:** Vanilla ES modules, three.js (vendorized, import map), SVG via `document.createElementNS`, native canvas for PNG. No JS test runner (Python project; spec §11 — YAGNI). Verification is manual in a real browser via Playwright, like #2/#3.

**TDD note:** Deviates from the skill's default TDD loop — no JS runner; the spec defines verification as a manual browser checklist (Task 5). `seccion2d.js`/`svgutil.js` are written pure so unit tests could be added later; the acceptance gate is Playwright.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/motor_fea/viz/static/svgutil.js` | Shared SVG helpers: `nodo`, `descargarSVG`, `descargarPNG`. No state, no three.js. | **Create** |
| `src/motor_fea/viz/static/seccion2d.js` | Pure `seccionSVG(datos, opts)` → cross-section SVG (rect + rebar + cue + forces). | **Create** |
| `src/motor_fea/viz/static/diagramas2d.js` | Import `nodo` from `svgutil` (remove local copy). | **Modify** |
| `src/motor_fea/viz/static/index.html` | `#sec` block (SVG host + station slider + SVG/PNG buttons) + CSS. | **Modify** |
| `src/motor_fea/viz/static/app.js` | `b,h` in `addBarra`; `esfuerzosEnEstacion`; `"sección"` mode; pick→member; slider; 3D ring; adaptive source; export; teardown. | **Modify** |

DTO recap (NOT modified): `esfuerzos.elementos[]` = `{id, longitud, diagrama:[[s,N,Vy,Vz,T,My,Mz],...]}`; `escena.barras[]` carry `b,h,i,j,id`; `diseño`/`armado` `.elementos[]` keyed by `id` with `long:[{x,y,d}]` + `estribo:{d,s,w,h}` (diseño adds `tipo,designacion,cumple`). Units: m; forces N (→kN), moments N·m (→kN·m). Internal-force sign: tension +.

---

### Task 1: `svgutil.js` + refactor `diagramas2d.js`

**Files:**
- Create: `src/motor_fea/viz/static/svgutil.js`
- Modify: `src/motor_fea/viz/static/diagramas2d.js`

- [ ] **Step 1: Create `svgutil.js`**

```javascript
// Helpers SVG compartidos: creación de nodos y descarga (SVG/PNG).
// Sin estado, sin three.js. Usados por diagramas2d.js y seccion2d.js.
const SVGNS = 'http://www.w3.org/2000/svg';

export function nodo(tag, attrs) {
  const n = document.createElementNS(SVGNS, tag);
  for (const k of Object.keys(attrs)) n.setAttribute(k, attrs[k]);
  return n;
}

function descargarBlob(blob, nombre) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombre;
  a.click();
  URL.revokeObjectURL(url);
}

export function descargarSVG(svg, nombre) {
  if (!svg) return;
  const texto = new XMLSerializer().serializeToString(svg);
  descargarBlob(new Blob([texto], { type: 'image/svg+xml' }), nombre);
}

// Rasteriza el SVG a PNG en un canvas nativo (sin librerías). Fondo opaco
// (el panel es oscuro) para que el PNG no salga transparente.
export function descargarPNG(svg, nombre, escala = 2) {
  if (!svg) return;
  const w = parseInt(svg.getAttribute('width'), 10) || 300;
  const h = parseInt(svg.getAttribute('height'), 10) || 300;
  const texto = new XMLSerializer().serializeToString(svg);
  const url = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(texto);
  const img = new Image();
  img.onload = () => {
    const canvas = document.createElement('canvas');
    canvas.width = w * escala;
    canvas.height = h * escala;
    const ctx = canvas.getContext('2d');
    ctx.fillStyle = '#101418';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    canvas.toBlob((blob) => { if (blob) descargarBlob(blob, nombre); });
  };
  img.src = url;
}
```

- [ ] **Step 2: Refactor `diagramas2d.js` to import `nodo`**

Replace the `SVGNS` constant line (currently `const SVGNS = 'http://www.w3.org/2000/svg';`, just after the header comment) with the import:

```javascript
import { nodo } from './svgutil.js';
```

Then delete the now-unused local `nodo` definition (the whole block, including the blank line before it):

```javascript

function nodo(tag, attrs) {
  const n = document.createElementNS(SVGNS, tag);
  for (const k of Object.keys(attrs)) n.setAttribute(k, attrs[k]);
  return n;
}
```

Leave `COMP` and `diagramaSVG` unchanged. `diagramaSVG` already calls `nodo(...)`, now resolved via the import.

- [ ] **Step 3: Syntax-check both**

Run: `node --check src/motor_fea/viz/static/svgutil.js && node --check src/motor_fea/viz/static/diagramas2d.js`
Expected: no output, exit 0.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/svgutil.js src/motor_fea/viz/static/diagramas2d.js
git commit -m "feat(viz): svgutil.js (nodo + descargas SVG/PNG); diagramas2d usa nodo compartido"
```

---

### Task 2: `seccion2d.js` — pure `seccionSVG`

**Files:**
- Create: `src/motor_fea/viz/static/seccion2d.js`

- [ ] **Step 1: Write the module**

```javascript
// Panel 2D de la sección transversal de un miembro: función pura que arma un SVG
// con el corte b×h, el armado (barras + estribo) si viene, un cue ligero de momento
// (cara en tracción) y un bloque con los 6 esfuerzos en la estación s. No conoce three.js.
import { nodo } from './svgutil.js';

const COMP = ['N', 'Vy', 'Vz', 'T', 'My', 'Mz'];
const ES_MOMENTO = [false, false, false, true, true, true];   // T, My, Mz → kN·m

// datos = { b, h, long, estribo, fuerzas:[N,Vy,Vz,T,My,Mz], designacion, cumple, s, L }
export function seccionSVG(datos, opts = {}) {
  const W = opts.ancho || 240;
  const Hsec = opts.altoSeccion || 200;     // alto del área de dibujo de la sección
  const pad = 14;
  const colBar = opts.colorBarra || '#c0392b';
  const colTracc = opts.colorTraccion || '#e67e22';
  const {
    b, h, long, estribo, fuerzas = [0, 0, 0, 0, 0, 0],
    designacion, cumple, s = 0, L = 1,
  } = datos;

  const dispW = W - 2 * pad, dispH = Hsec - 2 * pad;
  const esc = Math.min(dispW / (b || 1), dispH / (h || 1));   // m → px, llena el área
  const cx = W / 2, cy = pad + dispH / 2;
  const X = (x) => cx + x * esc;          // sección local (m, origen al centro) → SVG
  const Y = (y) => cy - y * esc;          // y arriba = +

  const altoTexto = 16 * (COMP.length + 1) + 12;
  const total = Hsec + altoTexto;
  const svg = nodo('svg', { width: W, height: total, viewBox: `0 0 ${W} ${total}` });

  // --- cue de cara en tracción (Mz → caras verticales; My → horizontales) ---
  const My = fuerzas[4], Mz = fuerzas[5];
  if (Math.abs(Mz) > 1e-6) {
    const xc = Mz > 0 ? X(b / 2) : X(-b / 2);
    svg.appendChild(nodo('line', { x1: xc, y1: Y(h / 2), x2: xc, y2: Y(-h / 2),
      stroke: colTracc, 'stroke-width': 4 }));
  }
  if (Math.abs(My) > 1e-6) {
    const yc = My > 0 ? Y(h / 2) : Y(-h / 2);
    svg.appendChild(nodo('line', { x1: X(-b / 2), y1: yc, x2: X(b / 2), y2: yc,
      stroke: colTracc, 'stroke-width': 4 }));
  }

  // --- rectángulo b×h ---
  svg.appendChild(nodo('rect', { x: X(-b / 2), y: Y(h / 2), width: b * esc, height: h * esc,
    fill: 'none', stroke: '#aaa', 'stroke-width': 1.5 }));

  // --- estribo ---
  if (estribo && estribo.w > 0 && estribo.h > 0) {
    svg.appendChild(nodo('rect', {
      x: X(-estribo.w / 2), y: Y(estribo.h / 2),
      width: estribo.w * esc, height: estribo.h * esc,
      fill: 'none', stroke: '#2ecc71', 'stroke-width': 1 }));
  }

  // --- barras longitudinales ---
  if (Array.isArray(long)) {
    for (const bar of long) {
      svg.appendChild(nodo('circle', {
        cx: X(bar.x), cy: Y(bar.y), r: Math.max(1.5, (bar.d / 2) * esc), fill: colBar }));
    }
  }

  // --- bloque de texto ---
  let ty = Hsec + 12;
  const linea = (t) => {
    const e = nodo('text', { x: pad, y: ty, fill: '#fff', 'font-size': 11, 'font-family': 'sans-serif' });
    e.textContent = t; svg.appendChild(e); ty += 16;
  };
  const enc = designacion
    ? `${designacion}${cumple === undefined ? '' : (cumple ? ' · cumple' : ' · NO cumple')}`
    : 'sección';
  linea(`${enc}   s = ${s.toFixed(2)} m  (s/L = ${(L ? s / L : 0).toFixed(2)})`);
  COMP.forEach((nombre, k) => {
    const v = fuerzas[k] / 1000;
    const u = ES_MOMENTO[k] ? 'kN·m' : 'kN';
    let etq = `${nombre} = ${v.toFixed(ES_MOMENTO[k] ? 1 : 0)} ${u}`;
    if (nombre === 'N') etq += v >= 0 ? '  (tracc)' : '  (compr)';
    linea(etq);
  });

  return svg;
}
```

- [ ] **Step 2: Syntax-check**

Run: `node --check src/motor_fea/viz/static/seccion2d.js`
Expected: no output, exit 0.

- [ ] **Step 3: Commit**

```bash
git add src/motor_fea/viz/static/seccion2d.js
git commit -m "feat(viz): seccion2d.js — seccionSVG puro (corte b×h + armado + esfuerzos + cue)"
```

---

### Task 3: `index.html` — `#sec` block

**Files:**
- Modify: `src/motor_fea/viz/static/index.html`

- [ ] **Step 1: Add CSS**

In the `<style>` block, after the two `#diag-svg` rules added in #3, add:

```css
    #sec { display: none; flex-direction: column; gap: 6px; margin-top: 4px; }
    #sec-svg svg { background: rgba(255,255,255,.04); border-radius: 4px; }
```

- [ ] **Step 2: Add the `#sec` markup**

In `#panel`, immediately after the `<div id="diag-svg"></div>` line, add:

```html
    <div id="sec">
      <div id="sec-svg"></div>
      <div class="fila">
        <label for="sec-s">s</label>
        <input type="range" id="sec-s" min="0" max="1" step="0.01" value="0.5">
      </div>
      <div class="fila">
        <button id="sec-svg-dl" type="button">SVG</button>
        <button id="sec-png-dl" type="button">PNG</button>
      </div>
    </div>
```

- [ ] **Step 3: Visually confirm the page still loads**

Launch (Task 5 Step 1). Expected: page renders as before; `#sec` is hidden (no visible change yet). No new console errors.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/index.html
git commit -m "feat(viz): index.html — bloque #sec (host SVG + slider s + botones SVG/PNG)"
```

---

### Task 4: `app.js` — `"sección"` mode wiring

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Imports**

After `import { diagramaSVG } from './diagramas2d.js';`, add:

```javascript
import { seccionSVG } from './seccion2d.js';
import { descargarSVG, descargarPNG } from './svgutil.js';
```

- [ ] **Step 2: State vars**

After the diag state block (`let cintasGroup = null;` … `let diagComp = 0;`), add:

```javascript
let anilloSeccion = null;    // marcador 3D del plano de corte
let secActivo = false;
let secElId = null;          // id del miembro seleccionado
let secSvgActual = null;     // último SVGElement dibujado (para export)
```

- [ ] **Step 3: DOM handles**

After `const diagSvg = document.getElementById('diag-svg');`, add:

```javascript
const secDiv = document.getElementById('sec');
const secHost = document.getElementById('sec-svg');
const secSlider = document.getElementById('sec-s');
const btnSecSvg = document.getElementById('sec-svg-dl');
const btnSecPng = document.getElementById('sec-png-dl');
```

- [ ] **Step 4: Store `b,h` in `addBarra`**

Replace the `barras.push(...)` line in `addBarra`:

```javascript
  barras.push({ mesh, i: b.i, j: b.j, id: b.id });
```

with:

```javascript
  barras.push({ mesh, i: b.i, j: b.j, id: b.id, b: b.b, h: b.h });
```

- [ ] **Step 5: Add section helpers**

After `dibujarDiagramas2D` (the function added in #3, ends with `}`), add:

```javascript
function esfuerzosEnEstacion(el, s) {
  const filas = el && el.diagrama;
  if (!filas || !filas.length) return [0, 0, 0, 0, 0, 0];
  if (s <= filas[0][0]) return filas[0].slice(1);
  const ult = filas[filas.length - 1];
  if (s >= ult[0]) return ult.slice(1);
  for (let k = 0; k < filas.length - 1; k++) {
    const s0 = filas[k][0], s1 = filas[k + 1][0];
    if (s >= s0 && s <= s1) {
      const t = s1 > s0 ? (s - s0) / (s1 - s0) : 0;
      return filas[k].slice(1).map((v, c) => v + (filas[k + 1][c + 1] - v) * t);
    }
  }
  return ult.slice(1);
}

function datosSeccion(id, s, L) {
  const el = esfuerzos && esfuerzos.elementos.find((e) => e.id === id);
  const bar = barras.find((b) => b.id === id);
  if (!el || !bar) return null;
  const d = (diseno && diseno.elementos.find((e) => e.id === id))
         || (armado && armado.elementos.find((e) => e.id === id));
  return {
    b: bar.b, h: bar.h,
    long: d ? d.long : null,
    estribo: d ? d.estribo : null,
    designacion: d ? d.designacion : undefined,
    cumple: d ? d.cumple : undefined,
    fuerzas: esfuerzosEnEstacion(el, s),
    s, L,
  };
}

function construirAnilloSeccion(el) {
  disposeAnillo();
  const bar = barras.find((b) => b.id === el.id);
  if (!bar) return;
  const r = Math.max(bar.b, bar.h) * 0.7;
  const geo = new THREE.TorusGeometry(r, r * 0.06, 8, 32);
  anilloSeccion = new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ color: 0x00ff88 }));
  scene.add(anilloSeccion);
}

function posicionarAnillo(s, el) {
  if (!anilloSeccion) return;
  const bar = barras.find((b) => b.id === el.id);
  const vi = basePos[bar.i], vj = basePos[bar.j];
  if (!vi || !vj) return;
  const L = el.longitud || vi.distanceTo(vj) || 1;
  anilloSeccion.position.copy(vi).lerp(vj, Math.max(0, Math.min(1, s / L)));
  anilloSeccion.lookAt(vj);   // eje del miembro = normal del toro (plano del corte ⟂ al eje)
}

function disposeAnillo() {
  if (!anilloSeccion) return;
  scene.remove(anilloSeccion);
  anilloSeccion.geometry.dispose();
  anilloSeccion.material.dispose();
  anilloSeccion = null;
}

function dibujarSeccion() {
  if (secElId == null || !esfuerzos) return;
  const el = esfuerzos.elementos.find((e) => e.id === secElId);
  if (!el) return;
  const L = el.longitud || 1;
  const s = parseFloat(secSlider.value);
  const datos = datosSeccion(secElId, s, L);
  if (!datos) return;
  secSvgActual = seccionSVG(datos);
  secHost.replaceChildren(secSvgActual);
  posicionarAnillo(s, el);
}
```

- [ ] **Step 6: Add `entrarSeccion`**

After `entrarDiagramas` (ends with `}`), add:

```javascript
function entrarSeccion() {
  secActivo = true;
  secElId = null;
  secSvgActual = null;
  secHost.replaceChildren();
  if (diagSvg) diagSvg.replaceChildren();   // evita el diagrama viejo encima del panel
  secDiv.style.display = 'flex';
  info.textContent = 'toca una barra para ver su sección';
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}
```

- [ ] **Step 7: Hook teardown into `resetOverlays`**

In `resetOverlays`, add `secActivo = false;` after `diagActivo = false;`, and after `if (cintasGroup) cintasGroup.visible = false;` add:

```javascript
  disposeAnillo();
  if (secDiv) secDiv.style.display = 'none';
```

- [ ] **Step 8: Hook the mode into `setEstado`**

Add `|| secActivo` to `veniaEspecial`, and add the `seccion` branch after the `diagramas` branch:

```javascript
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo || diagActivo || secActivo;
```
```javascript
  if (nuevo === 'diagramas') { entrarDiagramas(); return; }
  if (nuevo === 'seccion') { entrarSeccion(); return; }
```

- [ ] **Step 9: Pick branch + slider + export listeners**

In the `pointerdown` handler, add a `secActivo` branch BEFORE the `else if (esfuerzos && !refuerzoActivo && !diagActivo)` branch, and also exclude `secActivo` from that diagram branch. The chain becomes:

```javascript
  } else if (secActivo && esfuerzos) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    if (!bar) return;
    const el = esfuerzos.elementos.find((e) => e.id === bar.id);
    if (!el) return;
    secElId = bar.id;
    const L = el.longitud || 1;
    secSlider.min = 0; secSlider.max = L; secSlider.step = L / 100; secSlider.value = L / 2;
    construirAnilloSeccion(el);
    dibujarSeccion();
  } else if (esfuerzos && !refuerzoActivo && !diagActivo && !secActivo) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
```

(Only the branch header of the existing diagram branch changes — its body is unchanged.)

Then, after the `selDiagComp.addEventListener('change', ...)` listener, add:

```javascript
secSlider.addEventListener('input', () => { if (secActivo) dibujarSeccion(); });
btnSecSvg.addEventListener('click', () => descargarSVG(secSvgActual, 'seccion.svg'));
btnSecPng.addEventListener('click', () => descargarPNG(secSvgActual, 'seccion.png'));
```

- [ ] **Step 10: Add the `seccion` option in `renderEscena`**

In `renderEscena`, replace the `if (esf) { … }` block with:

```javascript
  if (esf) {
    esfuerzos = esf;
    selEstado.add(new Option('diagramas', 'diagramas'));
    selEstado.add(new Option('sección', 'seccion'));
  }
```

- [ ] **Step 11: Teardown in `limpiarEscena`**

In `limpiarEscena`, after the `disposeCintas();` line (and the `if (diagSvg) diagSvg.replaceChildren();` / `diagComp` lines added in #3), add:

```javascript
  disposeAnillo();
  if (secHost) secHost.replaceChildren();
  if (secDiv) secDiv.style.display = 'none';
  secActivo = false; secElId = null; secSvgActual = null;
```

- [ ] **Step 12: Syntax-check**

Run: `node --check src/motor_fea/viz/static/app.js`
Expected: no output, exit 0.

- [ ] **Step 13: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): modo sección — corte por miembro (interp + anillo 3D + export + teardown)"
```

---

### Task 5: Manual browser verification (Playwright) — acceptance gate

**Files:** none (verification only). Replaces unit tests per spec §11.

- [ ] **Step 1: Launch the viz server**

Run (background): `.venv/bin/python -m motor_fea.api.cli --serve --port 8000` → `http://127.0.0.1:8000/`. (Requires the `[api]` extra / FastAPI, already in `.venv`.)

- [ ] **Step 2: Drive the checklist with Playwright**

Navigate to the URL and verify (screenshot + console each step). Drive mode/slider via `change`/`input` events on the `<select>`/slider and synthesize `pointerdown` on the canvas for picks (note: synthetic picks emit `setPointerCapture` errors from OrbitControls — a harness artifact, not app errors; filter them out):

1. **Arranque (ejemplo):** the `estado` `<select>` gains a `"sección"` entry; `#sec` hidden.
2. **Modo sección + pick:** select `seccion` → `info` = "toca una barra…", `#sec` visible; pick a bar → `#sec-svg` shows the b×h rect + rebar (dots) + stirrup + the 6-force block; a green 3D ring appears on the bar.
3. **Slider:** moving `#sec-s` updates the forces and moves the ring. **Control:** along a member where the bending moment is linear, the moment value scales ~linearly with `s` (and reaches ~`P·L` at the support end of a cantilever); N and V stay constant across `s`.
4. **Cue:** the tension-face line flips side when the dominant moment changes sign across `s`.
5. **Export:** clicking `SVG` downloads `seccion.svg`; `PNG` downloads `seccion.png` (verify via the page's download events or that the click handler runs without error).
6. **Adaptativo (custom):** load a model via the shell → section shows only b×h + forces (no rebar); pick + slider + export work.
7. **Teardown:** switch from `seccion` to another estado, then load another model → no residual ring, `#sec` hidden, `#sec-svg` empty, no console leaks.
8. **No errors** in console except the known `favicon.ico` 404 / synthetic-pointer artifacts.

- [ ] **Step 3: Record the result**

If all pass, the acceptance criterion (spec §11.2) is met. If any fails, fix the responsible task's code, re-run `node --check`, and re-verify.

- [ ] **Step 4: Close the browser and stop the server**

---

## Self-Review

**Spec coverage:**
- §3 `svgutil.js` (nodo + descargas) + diagramas2d refactor → Task 1. ✓
- §6 `seccion2d.js` pure `seccionSVG` (rect + estribo + barras + cue + texto, adaptativo) → Task 2. ✓
- §5 `esfuerzosEnEstacion` linear interp w/ clamp → Task 4 Step 5. ✓
- §7.1 `"sección"` select entry + `entrarSeccion` → Task 4 Steps 6/8/10. ✓
- §7.2 pick→member, slider range `[0,L]`, default `L/2` → Task 4 Step 9. ✓
- §7.3 slider `input` redraw + ring move → Task 4 Steps 5/9. ✓
- §7.4 3D ring (torus, member axis as normal) → Task 4 Step 5. ✓
- §7.5 adaptive source diseño→armado→bare → Task 4 Step 5 (`datosSeccion`). ✓
- §7.6 teardown (`resetOverlays`/`limpiarEscena`) → Task 4 Steps 7/11. ✓
- §8 export SVG+PNG → Task 1 (`descargar*`) + Task 4 Step 9. ✓
- §9 limitation — documented in spec; `seccionSVG` cue tied to drawn axes (no overclaim). ✓
- §10 edge cases (no esfuerzos, bar not in esfuerzos, no design, ~0 moment, export no-op) → guards in Tasks 2 & 4. ✓
- §11 Playwright checklist incl. control case → Task 5. ✓
- §12 files (create svgutil/seccion2d; modify diagramas2d/app/index) → matches. ✓

**Placeholder scan:** No TBD/"handle errors"/"similar to" — all code shown in full. `b,h` stored in `addBarra` before any consumer needs them (`datosSeccion`).

**Type/name consistency:** `secActivo`, `secElId`, `secSvgActual`, `anilloSeccion`, `secDiv`, `secHost`, `secSlider`, `btnSecSvg`, `btnSecPng`, `esfuerzosEnEstacion`, `datosSeccion`, `construirAnilloSeccion`, `posicionarAnillo`, `disposeAnillo`, `dibujarSeccion`, `entrarSeccion`, `seccionSVG` — used identically across tasks. `nodo`/`descargarSVG`/`descargarPNG` exported by `svgutil` and imported by `seccion2d`/`diagramas2d`/`app`. The select value is `"seccion"` (no accent); label `"sección"`.
