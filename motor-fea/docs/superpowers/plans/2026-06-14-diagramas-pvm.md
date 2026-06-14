# Diagramas P/V/M Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the `esfuerzos` DTO already in the front-end state, draw the 6 P/V/M diagrams (N, Vy, Vz, T, My, Mz) of a picked element as a 2D SVG panel, plus a 3D "ribbon" overlay of one selectable component over every bar in the scene.

**Architecture:** Pure front-end, building on #2 (`esfuerzos` is already loaded by `renderEscena` in both ejemplo and custom modes). One new vendorless module `diagramas2d.js` exports a pure `diagramaSVG(elemento)` returning an `SVGElement`. `app.js` gains: a pick→`dibujarDiagramas2D` hook, a `"diagramas"` overlay mode with `construirCintas`/`disposeCintas` (mirroring `construirJaula`/`disposeDiseno`), a component dropdown, and `exag` reuse. `index.html` gains a `#diag` block (`<select id="diag-comp">` + `<div id="diag-svg">` host). The server, `core/`, `contrato.py`, and the rest of `viz/` are NOT touched.

**Tech Stack:** Vanilla ES modules, three.js (vendorized, import map), SVG via `document.createElementNS`. No JS test runner (project is Python; spec §9.1 — YAGNI). Verification is manual in a real browser via Playwright, exactly like #2.

**TDD note:** This plan deviates from the skill's default TDD loop because there is no JS test runner and the spec (§9) explicitly defines verification as a manual browser checklist. `diagramaSVG` is written pure and bounded so a unit test *could* be added later, but the acceptance gate is Task 5 (Playwright).

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `src/motor_fea/viz/static/diagramas2d.js` | Pure `diagramaSVG(elemento, opts)` → stacked 6-component SVG. No three.js, no global DOM mutation. | **Create** |
| `src/motor_fea/viz/static/index.html` | Add `#diag` block (component `<select>` + SVG host) inside `#panel`; minimal CSS. | **Modify** |
| `src/motor_fea/viz/static/app.js` | Import `diagramaSVG`; pick→`dibujarDiagramas2D`; `"diagramas"` mode (`entrarDiagramas`, `construirCintas`, `disposeCintas`); dropdown + `exag` wiring; teardown hooks. | **Modify** |

DTO recap (NOT modified). `esfuerzos`:
```
{ "orden_componentes": ["N","Vy","Vz","T","My","Mz"],
  "elementos": [ { "id", "longitud", "extremo_i":[...], "extremo_j":[...],
                   "diagrama": [ [s, N, Vy, Vz, T, My, Mz], ... ] } ] }
```
Row indices: `s=0, N=1, Vy=2, Vz=3, T=4, My=5, Mz=6`. Units: forces N (→kN, `/1000`), moments N·m (→kN·m). `esfuerzos` elements carry `id` only — join to `barras` (`{mesh,i,j,id}`) by `id` to reach `basePos`.

---

### Task 1: `index.html` — `#diag` block (component select + SVG host)

**Files:**
- Modify: `src/motor_fea/viz/static/index.html` (CSS in `<style>`; markup at end of `#panel`)

- [ ] **Step 1: Add CSS for the diag block**

In the `<style>` block, after the `#info` rule (currently `#info { min-height: 16px; opacity: .85; }`), add:

```css
    #diag-svg { margin-top: 4px; max-height: 56vh; overflow-y: auto; }
    #diag-svg svg { background: rgba(255,255,255,.04); border-radius: 4px; }
```

- [ ] **Step 2: Add the component dropdown + SVG host inside `#panel`**

In `#panel`, replace the lone `<span id="info"></span>` line with:

```html
    <div class="fila">
      <label for="diag-comp">cinta</label>
      <select id="diag-comp" aria-label="componente de cinta 3D">
        <option value="0">N</option>
        <option value="1">Vy</option>
        <option value="2">Vz</option>
        <option value="3">T</option>
        <option value="4">My</option>
        <option value="5">Mz</option>
      </select>
    </div>
    <span id="info"></span>
    <div id="diag-svg"></div>
```

- [ ] **Step 3: Visually confirm the page still loads (no JS change yet)**

Run the viz server and open the page (see Task 5, Step 1 for the exact launch command). Expected: panel now shows a "cinta" dropdown with N…Mz; everything else renders as before; `#diag-svg` is empty. No new console errors.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/index.html
git commit -m "feat(viz): index.html — bloque #diag (select componente + host SVG)"
```

---

### Task 2: `diagramas2d.js` — pure `diagramaSVG(elemento, opts)`

**Files:**
- Create: `src/motor_fea/viz/static/diagramas2d.js`

- [ ] **Step 1: Write the module**

Create `src/motor_fea/viz/static/diagramas2d.js` with exactly:

```javascript
// Panel 2D de diagramas P/V/M: función pura que arma un SVG con los 6
// mini-diagramas (N, Vy, Vz, T, My, Mz) apilados de un elemento del DTO
// `esfuerzos`. No conoce three.js; no muta el DOM global (solo crea nodos).
const SVGNS = 'http://www.w3.org/2000/svg';

// k = índice de la columna del valor en una fila del diagrama [s, N, Vy, Vz, T, My, Mz].
// momento → unidad kN·m; fuerza → kN. Ambos se dividen por 1000.
const COMP = [
  { k: 1, nombre: 'N',  unidad: 'kN',   momento: false },
  { k: 2, nombre: 'Vy', unidad: 'kN',   momento: false },
  { k: 3, nombre: 'Vz', unidad: 'kN',   momento: false },
  { k: 4, nombre: 'T',  unidad: 'kN·m', momento: true  },
  { k: 5, nombre: 'My', unidad: 'kN·m', momento: true  },
  { k: 6, nombre: 'Mz', unidad: 'kN·m', momento: true  },
];

function nodo(tag, attrs) {
  const n = document.createElementNS(SVGNS, tag);
  for (const k in attrs) n.setAttribute(k, attrs[k]);
  return n;
}

// elemento: { longitud, diagrama: [[s,N,Vy,Vz,T,My,Mz], ...] }
export function diagramaSVG(elemento, opts = {}) {
  const W = opts.ancho || 230;
  const H = opts.alto || 40;        // alto del área de trazado por componente
  const gap = 22;                   // espacio para la etiqueta encima
  const pad = 6;
  const colPos = opts.colorPos || '#ff4444';
  const colNeg = opts.colorNeg || '#4488ff';
  const filas = elemento.diagrama || [];
  const Ln = elemento.longitud || 1;
  const total = COMP.length * (H + gap);

  const svg = nodo('svg', {
    width: W, height: total, viewBox: `0 0 ${W} ${total}`, xmlns: SVGNS,
  });

  COMP.forEach((c, ci) => {
    const y0 = ci * (H + gap) + gap;   // borde superior del trazado
    const mid = y0 + H / 2;            // línea base (cero)

    let m = 0;
    for (const f of filas) m = Math.max(m, Math.abs(f[c.k]));
    const esc = m > 0 ? (H / 2 - 2) / m : 0;   // auto-escala; evita /0

    const pico = (m / 1000).toFixed(c.momento ? 1 : 0);
    const etq = nodo('text', { x: pad, y: y0 - 6, fill: '#fff', 'font-size': 11 });
    etq.textContent = `${c.nombre}  |máx| = ${pico} ${c.unidad}`;
    svg.appendChild(etq);

    svg.appendChild(nodo('line', {
      x1: pad, y1: mid, x2: W - pad, y2: mid, stroke: '#888', 'stroke-width': 1,
    }));

    if (!filas.length) return;
    const X = (s) => pad + (s / Ln) * (W - 2 * pad);
    const Y = (v) => mid - v * esc;

    // Relleno por signo: un trapecio por tramo, coloreado por el signo del
    // valor medio del tramo (diagramas lineales por tramos → exacto).
    for (let i = 0; i < filas.length - 1; i++) {
      const f0 = filas[i], f1 = filas[i + 1];
      const v0 = f0[c.k], v1 = f1[c.k];
      const fill = (v0 + v1) >= 0 ? colPos : colNeg;
      svg.appendChild(nodo('polygon', {
        points: `${X(f0[0])},${mid} ${X(f0[0])},${Y(v0)} ${X(f1[0])},${Y(v1)} ${X(f1[0])},${mid}`,
        fill, 'fill-opacity': 0.35, stroke: 'none',
      }));
    }

    svg.appendChild(nodo('polyline', {
      points: filas.map((f) => `${X(f[0])},${Y(f[c.k])}`).join(' '),
      fill: 'none', stroke: '#fff', 'stroke-width': 1.5,
    }));
  });

  return svg;
}
```

- [ ] **Step 2: Sanity-check the module parses (Node syntax check)**

Run: `node --check src/motor_fea/viz/static/diagramas2d.js`
Expected: no output, exit 0 (valid ES module syntax). `document` is not referenced at top level, so `--check` (parse only) passes without a DOM.

- [ ] **Step 3: Commit**

```bash
git add src/motor_fea/viz/static/diagramas2d.js
git commit -m "feat(viz): diagramas2d.js — diagramaSVG puro (6 mini-diagramas P/V/M)"
```

---

### Task 3: `app.js` — pick → 2D panel

**Files:**
- Modify: `src/motor_fea/viz/static/app.js` (import at top; pick handler ~line 355; new `dibujarDiagramas2D`; `diagComp` state + dropdown listener)

- [ ] **Step 1: Import the pure function**

At the top of `app.js`, after the `import { crearShell } from './shell.js';` line, add:

```javascript
import { diagramaSVG } from './diagramas2d.js';
```

- [ ] **Step 2: Add diag state + dropdown handle**

In the `// --- Estado ---` section, immediately after the `let esfuerzos = null;` line, add:

```javascript
let cintasGroup = null;      // overlay de cintas 3D (Group de Meshes)
let diagActivo = false;
let diagComp = 0;            // componente activo de la cinta: N=0 … Mz=5
```

Then, in the block that grabs panel elements (after `const btnRedi = document.getElementById('redisenar');`), add:

```javascript
const selDiagComp = document.getElementById('diag-comp');
const diagSvg = document.getElementById('diag-svg');
```

- [ ] **Step 3: Add `dibujarDiagramas2D`**

Immediately after the `resumenEsfuerzos(id)` function (ends at the `}` after its `return` ~line 400), add:

```javascript
function dibujarDiagramas2D(id) {
  if (!esfuerzos || !diagSvg) return;
  const el = esfuerzos.elementos.find((e) => e.id === id);
  if (!el) return;                      // id no encontrado: panel intacto
  diagSvg.replaceChildren(diagramaSVG(el));
}
```

- [ ] **Step 4: Call it from the pick handler**

In the `pointerdown` handler, find the `else if (esfuerzos) {` branch. It currently ends:

```javascript
    const txt = bar && resumenEsfuerzos(bar.id);
    if (txt) info.textContent = txt;
  }
```

Change it to also draw the panel:

```javascript
    if (!bar) return;
    const txt = resumenEsfuerzos(bar.id);
    if (txt) info.textContent = txt;
    dibujarDiagramas2D(bar.id);
  }
```

- [ ] **Step 5: Wire the component dropdown (state only for now)**

After the existing `exagInput.addEventListener('input', ...)` listener, add:

```javascript
selDiagComp.addEventListener('change', () => {
  diagComp = parseInt(selDiagComp.value, 10);
  if (diagActivo) reconstruirCintas();   // definido en la Task 4
});
```

NOTE: `reconstruirCintas` is added in Task 4. Until then the dropdown only updates `diagComp`; `diagActivo` is always false (no `"diagramas"` mode exists yet), so the call is never reached. This task stays runnable on its own.

- [ ] **Step 6: Verify the 2D panel draws on pick**

Launch (Task 5 Step 1) and open the page in ejemplo mode. Click a bar.
Expected: `#diag-svg` fills with 6 stacked mini-diagrams (N, Vy, Vz, T, My, Mz), each with a label and a divergent-colored shape; the one-line readout still appears in `#info`. No console errors.

- [ ] **Step 7: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): pick → dibujarDiagramas2D (panel 2D de 6 diagramas)"
```

---

### Task 4: `app.js` — 3D ribbons (`"diagramas"` mode)

**Files:**
- Modify: `src/motor_fea/viz/static/app.js` (`construirCintas`/`disposeCintas`/`reconstruirCintas`; `entrarDiagramas`; hooks in `setEstado`, `resetOverlays`, `limpiarEscena`, `renderEscena`, `exag` listener)

- [ ] **Step 1: Add `construirCintas` / `disposeCintas` / `reconstruirCintas`**

After `construirJaula` (ends ~line 237, the `}` after `return grupo;`), add:

```javascript
// --- Cintas 3D de diagramas (overlay) ---
// Para el componente c (0=N … 5=Mz), una tira de triángulos por barra entre la
// polilínea base (eje del miembro) y la polilínea desplazada (valor × escala).
// Dirección de despliegue derivada del eje + up global (aprox. de orientación,
// ver spec §6.3): Mz,Vy,N,T → t1 ; Vz,My → t2.
function construirCintas(c) {
  const grupo = new THREE.Group();
  grupo.visible = false;
  if (!esfuerzos) { scene.add(grupo); return grupo; }

  let maxAbs = 0;
  for (const el of esfuerzos.elementos)
    for (const fila of el.diagrama) maxAbs = Math.max(maxAbs, Math.abs(fila[c + 1]));
  const norm = maxAbs > 0 ? exag / maxAbs : 0;   // valor pico → offset = exag (m)

  const up = new THREE.Vector3(0, 1, 0);
  const altUp = new THREE.Vector3(1, 0, 0);

  for (const el of esfuerzos.elementos) {
    const bar = barras.find((b) => b.id === el.id);
    if (!bar) continue;
    const vi = basePos[bar.i], vj = basePos[bar.j];
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    if (L === 0) continue;                         // largo 0: se omite

    const axis = vj.clone().sub(vi).normalize();
    let t1 = new THREE.Vector3().crossVectors(axis, up);
    if (t1.lengthSq() < 1e-6) t1 = new THREE.Vector3().crossVectors(axis, altUp);
    t1.normalize();
    const t2 = new THREE.Vector3().crossVectors(axis, t1).normalize();
    const dir = (c === 2 || c === 4) ? t2 : t1;    // Vz, My → t2 ; resto → t1

    const filas = el.diagrama;
    const n = filas.length;
    const pos = new Float32Array(n * 2 * 3);
    const col = new Float32Array(n * 2 * 3);
    for (let k = 0; k < n; k++) {
      const s = filas[k][0];
      const val = filas[k][c + 1];
      const base = vi.clone().lerp(vj, el.longitud ? s / el.longitud : 0);
      const off = base.clone().add(dir.clone().multiplyScalar(val * norm));
      pos[k * 6 + 0] = base.x; pos[k * 6 + 1] = base.y; pos[k * 6 + 2] = base.z;
      pos[k * 6 + 3] = off.x;  pos[k * 6 + 4] = off.y;  pos[k * 6 + 5] = off.z;
      const cc = colorDeCampo('diagrama', val, -maxAbs, maxAbs);   // divergente por signo
      col[k * 6 + 0] = cc.r; col[k * 6 + 1] = cc.g; col[k * 6 + 2] = cc.b;
      col[k * 6 + 3] = cc.r; col[k * 6 + 4] = cc.g; col[k * 6 + 5] = cc.b;
    }
    const idx = [];
    for (let k = 0; k < n - 1; k++) {
      const b0 = k * 2, o0 = k * 2 + 1, b1 = (k + 1) * 2, o1 = (k + 1) * 2 + 1;
      idx.push(b0, o0, o1, b0, o1, b1);
    }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    geo.setIndex(idx);
    const mat = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide });
    grupo.add(new THREE.Mesh(geo, mat));
  }
  scene.add(grupo);
  return grupo;
}

function disposeCintas() {
  if (!cintasGroup) return;
  scene.remove(cintasGroup);
  cintasGroup.traverse((o) => {
    if (o.geometry) o.geometry.dispose();
    if (o.material) o.material.dispose();
  });
  cintasGroup = null;
}

function reconstruirCintas() {
  disposeCintas();
  cintasGroup = construirCintas(diagComp);
  cintasGroup.visible = true;
}
```

- [ ] **Step 2: Add `entrarDiagramas`**

After `entrarDiseno` (ends ~line 306), add:

```javascript
function entrarDiagramas() {
  diagActivo = true;
  // las barras quedan en su posición base (estado 'diagramas' → despNodo = 0);
  // la escala exag controla la altura de las cintas.
  const span = frameBbox
    ? new THREE.Vector3(...frameBbox.max).distanceTo(new THREE.Vector3(...frameBbox.min))
    : 10;
  exagInput.min = 0; exagInput.max = span; exagInput.step = span / 100;
  exagInput.value = span * 0.25; exag = span * 0.25;
  reconstruirCintas();
  const nombres = esfuerzos ? esfuerzos.orden_componentes : ['N','Vy','Vz','T','My','Mz'];
  info.textContent = `diagramas 3D — ${nombres[diagComp]}`;
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}
```

- [ ] **Step 3: Hook teardown into `resetOverlays`**

In `resetOverlays`, after the `disenoActivo = false;` line add `diagActivo = false;`, and after `if (disenoGroup) disenoGroup.visible = false;` add `if (cintasGroup) cintasGroup.visible = false;`. Result:

```javascript
function resetOverlays() {
  losaActiva = false;
  refuerzoActivo = false;
  disenoActivo = false;
  diagActivo = false;
  if (losaMesh) losaMesh.visible = false;
  if (armadoGroup) armadoGroup.visible = false;
  if (disenoGroup) disenoGroup.visible = false;
  if (cintasGroup) cintasGroup.visible = false;
  fantasma(false);
  for (const bar of barras) bar.mesh.visible = true;
}
```

- [ ] **Step 4: Hook the mode into `setEstado`**

In `setEstado`, update `veniaEspecial` to include `diagActivo`, and add the `"diagramas"` branch next to the others:

```javascript
function setEstado(nuevo) {
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo || diagActivo;
  estado = nuevo;
  resetOverlays();
  if (nuevo.startsWith('losa-')) { entrarLosa(nuevo); return; }
  if (nuevo === 'refuerzo') { entrarRefuerzo(); return; }
  if (nuevo === 'diseno') { entrarDiseno(); return; }
  if (nuevo === 'diagramas') { entrarDiagramas(); return; }
  if (veniaEspecial && frameBbox) encuadrar(frameBbox.min, frameBbox.max);
```

(Leave the rest of `setEstado` unchanged.)

- [ ] **Step 5: Rebuild cintas live on `exag` change**

Replace the existing `exag` listener:

```javascript
exagInput.addEventListener('input', () => { exag = parseFloat(exagInput.value); });
```

with:

```javascript
exagInput.addEventListener('input', () => {
  exag = parseFloat(exagInput.value);
  if (diagActivo) reconstruirCintas();
});
```

- [ ] **Step 6: Add the `"diagramas"` entry to the `<select>`**

In `renderEscena`, replace the final `if (esf) esfuerzos = esf;` with:

```javascript
  if (esf) {
    esfuerzos = esf;
    selEstado.add(new Option('diagramas', 'diagramas'));
  }
```

This runs in both ejemplo and custom modes, so the entry appears whenever `esfuerzos` is present.

- [ ] **Step 7: Dispose cintas in `limpiarEscena`**

In `limpiarEscena`, after the `disposeDiseno();` call, add `disposeCintas();`. Also add `diagActivo = false;` alongside the other `*Activo = false;` resets. Result (the relevant lines):

```javascript
  disposeDiseno();
  disposeCintas();

  resultados = null; esfuerzos = null; frameBbox = null;
  losa = null; armado = null; diseno = null;
  losaActiva = false; refuerzoActivo = false; disenoActivo = false; diagActivo = false;
```

- [ ] **Step 8: Syntax-check the module**

Run: `node --check src/motor_fea/viz/static/app.js`
Expected: no output, exit 0. (Parse-only check; bare ES `import` of three is fine for `--check`.)

- [ ] **Step 9: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): modo diagramas — cintas 3D (construirCintas + teardown + exag)"
```

---

### Task 5: Manual browser verification (Playwright) — acceptance gate

**Files:** none (verification only). This replaces unit tests per spec §9.

- [ ] **Step 1: Launch the viz server**

Run (background): `python -m motor_fea.viz` — if that entrypoint differs, discover it with `grep -rn "run\|uvicorn\|app.run\|HTTPServer" src/motor_fea/viz/*.py` and use the correct launch command. Note the URL/port it prints (e.g. `http://127.0.0.1:8000`).

- [ ] **Step 2: Drive the checklist with Playwright**

Use the Playwright MCP browser tools to navigate to the server URL and verify, capturing a screenshot and the console log at each step:

1. **Arranque (ejemplo):** page renders the frame; the `estado` `<select>` now contains a `"diagramas"` entry; the `cinta` dropdown shows N…Mz; `#diag-svg` is empty.
2. **Pick → panel 2D:** click a bar → `#diag-svg` shows 6 mini-diagrams with labels (kN / kN·m) and divergent fills; `#info` keeps the one-line readout.
   - **Control case (cantilever, point load P at the free tip, length L):** `N` flat at 0; the bending component is a straight ramp reaching `P·L` at the support; the shear component is constant `= P`.
3. **Modo "diagramas" (cintas 3D):** select `diagramas` in `estado` → ribbons appear on every bar; changing the `cinta` dropdown (N/Vy/Vz/T/My/Mz) rebuilds them; dragging `exag` scales ribbon height; color is by sign.
4. **Ambos modos:** load a custom model via the shell (paste/file) and repeat pick + cintas in custom mode.
5. **Teardown:** switch from `diagramas` to another `estado`, then load another model → scene is clean (no leftover ribbons, no console leaks).
6. **No errors** in console except the known `favicon.ico` 404 / synthetic-event artifacts.

- [ ] **Step 3: Record the result**

If all checks pass, the acceptance criterion (spec §9.2) is met: the control cantilever reproduces N=0 / V=P / M linear to `P·L` in the 2D panel, and `"diagramas"` mode draws the chosen component over all bars. If any check fails, fix the responsible task's code, re-run `node --check`, and re-verify before proceeding.

- [ ] **Step 4: Close the browser and stop the server**

Close the Playwright browser; terminate the background server process.

---

## Self-Review

**Spec coverage:**
- §5.1 `diagramaSVG` pure, 6 stacked, auto-scaled, base line, sign fill, label with peak, signs as-is → Task 2. ✓
- §5.2 pick → `dibujarDiagramas2D`, keeps `#info` readout, no-op on missing id/esfuerzos → Task 3. ✓
- §6.1 single `"diagramas"` select entry + `diag-comp` dropdown → Task 1 (markup) + Task 4 Step 6 (entry) + Task 3/4 (dropdown). ✓
- §6.2 ribbon geometry (axis + t1/t2, component→direction map, station offset, triangle strip, vertex colors, hidden until mode) → Task 4 Step 1. ✓
- §6.3 orientation approximation — documented in code comment on `construirCintas`. ✓
- §6.4 teardown + static bars in mode → Task 4 Steps 3/7 + the `despNodo`-returns-zero behavior in `'diagramas'` estado. ✓
- §7 both modes, dropdown controls 3D / panel shows all 6 → Task 3/4. ✓
- §8 edge cases (no esfuerzos, id not found, all-zero, L=0) → guards in Tasks 2 & 4. ✓
- §9 manual Playwright checklist incl. cantilever control → Task 5. ✓
- §10 files: create `diagramas2d.js`, modify `app.js` + `index.html`, nothing else → matches. ✓

**Placeholder scan:** No TBD/"handle errors"/"similar to" — all code shown in full. `reconstruirCintas` is forward-referenced in Task 3 Step 5 with an explicit note that it's unreachable until Task 4 (`diagActivo` stays false), so each task is independently runnable.

**Type/name consistency:** `cintasGroup`, `diagActivo`, `diagComp`, `selDiagComp`, `diagSvg`, `construirCintas`, `disposeCintas`, `reconstruirCintas`, `entrarDiagramas`, `dibujarDiagramas2D`, `diagramaSVG` — used identically across tasks. Component index convention `c` (0..5) maps to diagram row `c+1` everywhere. `colorDeCampo(name, v, min, max)` called with a non-`'deflexion'` name in both Task 2 (implicit via fills) and Task 4 (`'diagrama'`).
