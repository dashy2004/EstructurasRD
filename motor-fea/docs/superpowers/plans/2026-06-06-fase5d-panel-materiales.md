# Fase 5D: panel de materiales editables — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`).

**Goal:** Editar f'c/fy/recubrimiento desde el visor y re-diseñar en vivo (`/diseno` con query params).

**Architecture:** `/diseno` gana query params `fc/fy/rec`; el panel del visor gana 3 inputs + botón "rediseñar"; `app.js` re-fetch con params y reconstruye la jaula. La lógica de diseño no cambia.

**Spec:** `docs/superpowers/specs/2026-06-06-fase5d-panel-materiales-design.md`

---

## Task 1: `/diseno` con query params (endpoint)

**Files:** Modify `src/motor_fea/api/servidor.py`; Test `tests/test_servidor.py`

- [ ] **Step 1: Test que falla** — añadir al final de `tests/test_servidor.py`:
```python
def test_diseno_query_params():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/diseno?fc=35&fy=500&rec=0.05")
    assert r.status_code == 200 and len(r.json()["elementos"]) == 8
    assert cli.get("/diseno?fc=-1").status_code == 400          # fc inválido → 400
    assert cli.get("/diseno").status_code == 200                # defaults
```

- [ ] **Step 2: Correr — FAIL** (los params no se aceptan / fc=-1 no da 400):
`PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_diseno_query_params -q` (use `.venv/bin/pytest` si falta).

- [ ] **Step 3: Implementar** — en `src/motor_fea/api/servidor.py`, reemplazar el endpoint `/diseno` actual:
```python
    @app.get("/diseno")
    def diseno():
        try:
            return calcular_diseno(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```
por:
```python
    @app.get("/diseno")
    def diseno(fc: float = 21.0, fy: float = 420.0, rec: float = 0.04):
        try:
            return calcular_diseno(modelo, fc, fy, rec)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 4: Correr los tests del servidor — PASS**: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`

- [ ] **Step 5: Commit**
```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): /diseno acepta fc/fy/rec como query params

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Panel de materiales + reconstrucción (visor)

> Sin unit test (smoke). `index.html` (panel) + `app.js` (fetch con params + rebuild). Validación estática + suite.

**Files:** Modify `src/motor_fea/viz/static/index.html`, `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: `index.html`** — dentro de `<div id="panel">`, justo ANTES de `<span id="info"></span>`, insertar:
```html
    <div class="fila">
      <label for="fc">f'c</label><input type="number" id="fc" value="21" min="1" step="1" style="width:3.2em">
      <label for="fy">fy</label><input type="number" id="fy" value="420" min="1" step="10" style="width:3.8em">
      <label for="rec">rec</label><input type="number" id="rec" value="0.04" min="0.01" step="0.005" style="width:3.8em">
      <button id="redisenar" type="button">rediseñar</button>
    </div>
```

- [ ] **Step 2: `app.js`** — `Read` el archivo y aplicar TRES cambios con `Edit`:

(a) Junto a los otros refs del DOM (donde están `const selEstado`/`exagInput`/`btnPlay`/`info`), añadir:
```javascript
const inpFc = document.getElementById('fc');
const inpFy = document.getElementById('fy');
const inpRec = document.getElementById('rec');
const btnRedi = document.getElementById('redisenar');
```

(b) REEMPLAZAR la función `cargarDiseno` actual:
```javascript
async function cargarDiseno() {
  try {
    const r = await fetch('./diseno');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    diseno = await r.json();
  } catch (e) {
    return;   // sin diseño: no se agrega el estado
  }
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  selEstado.add(new Option('diseño: armado', 'diseno'));
}
```
por (agrega `fetchDisenoUrl`, `disposeDiseno`, `redisenar`, y el listener del botón):
```javascript
function fetchDisenoUrl() {
  const fc = parseFloat(inpFc.value) || 21;
  const fy = parseFloat(inpFy.value) || 420;
  const rec = parseFloat(inpRec.value) || 0.04;
  return `./diseno?fc=${fc}&fy=${fy}&rec=${rec}`;
}

function disposeDiseno() {
  if (!disenoGroup) return;
  scene.remove(disenoGroup);
  disenoGroup.traverse((o) => { if (o.geometry) o.geometry.dispose(); });
  disenoGroup = null;
}

async function cargarDiseno() {
  try {
    const r = await fetch(fetchDisenoUrl());
    if (!r.ok) throw new Error('HTTP ' + r.status);
    diseno = await r.json();
  } catch (e) {
    return;   // sin diseño: no se agrega el estado
  }
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  selEstado.add(new Option('diseño: armado', 'diseno'));
}

async function redisenar() {
  let nuevo;
  try {
    const r = await fetch(fetchDisenoUrl());
    if (!r.ok) throw new Error('HTTP ' + r.status);
    nuevo = await r.json();
  } catch (e) {
    info.textContent = 'rediseño: error (' + e.message + ')';
    return;   // mantiene la jaula previa
  }
  diseno = nuevo;
  disposeDiseno();
  disenoGroup = construirJaula(diseno, (el) => (el.cumple ? MAT_OK : MAT_FALLA));
  if (disenoActivo) entrarDiseno();
}

btnRedi.addEventListener('click', redisenar);
```

- [ ] **Step 3: Validación estática**
1. `cp src/motor_fea/viz/static/app.js /tmp/app5d.mjs && node --check /tmp/app5d.mjs` → exit 0.
2. `grep -n 'redisenar\|fetchDisenoUrl' src/motor_fea/viz/static/app.js` (presentes).
3. ids del panel: `grep -o 'id="[^"]*"' src/motor_fea/viz/static/index.html` (incluye `fc`, `fy`, `rec`, `redisenar`).

- [ ] **Step 4: Suite completa** — `PYTHONPATH=src:tests python -m pytest -q`
Expected: ~203 passed (la lógica Python no cambia; solo el endpoint params de Task 1). Reportar el conteo; si algo falla, STOP/BLOCKED.

- [ ] **Step 5: Smoke manual** — `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`; en `http://127.0.0.1:8000/`, estado `diseño: armado`: cambiar f'c a 35, click "rediseñar" → la jaula se reconstruye; poner f'c=-1 → "rediseño: error" sin romper. Ctrl-C.

- [ ] **Step 6: Commit**
```bash
git add src/motor_fea/viz/static/index.html src/motor_fea/viz/static/app.js
git commit -m "feat(viz): panel de materiales (fc/fy/rec) + redisenar en vivo

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación
1. Suite verde (~203); sin regresión.
2. `GET /diseno?fc=&fy=&rec=` re-diseña; sin params usa defaults; fc inválido → 400.
3. El panel tiene inputs f'c/fy/rec + botón "rediseñar" que reconstruye la jaula.

## Notas de revisión
- **La lógica de diseño no cambia**: `calcular_diseno` ya parametriza fc/fy/rec; 5D solo expone los params en el endpoint y la UI.
- **Reconstrucción segura**: `disposeDiseno` libera las geometrías antes de reconstruir; en error de fetch se mantiene la jaula previa.
- **`index.html`** gana 4 ids nuevos (`fc/fy/rec/redisenar`); el resto del panel intacto.
