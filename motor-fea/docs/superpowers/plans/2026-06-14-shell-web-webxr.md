# Shell web/WebXR (#2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convertir el visor demo en una app real: cargar un modelo propio (archivo .json o pegado), analizarlo en el server y renderizar geometría + deformada + modos + readout de esfuerzos al hacer pick.

**Architecture:** Un endpoint nuevo `POST /visor` reusa `exportar_escena` + `calcular_resultados` + esfuerzos y devuelve los **mismos DTOs** que el visor ya pinta (paridad visual, cambio de front mínimo). El front gana un módulo `shell.js` (UI de carga) y un **modo-custom**; `app.js` se refactoriza extrayendo `limpiarEscena()` y `renderEscena(bundle)` de la función `cargar()` actual, sin reescribir el render. `core/` no se toca.

**Tech Stack:** Python 3 · FastAPI · pytest (`fastapi.testclient`, requiere `httpx`) · three.js vendorizado (ES modules, sin bundler) · WebXR.

**Spec:** [`docs/superpowers/specs/2026-06-14-shell-web-webxr-design.md`](../specs/2026-06-14-shell-web-webxr-design.md)

---

## File Structure

**Crear:**
- `src/motor_fea/viz/static/shell.js` — UI de carga (archivo + pegado + analizar + descargar). Una responsabilidad: tomar texto JSON, `POST /visor`, e invocar `onModelo(bundle, modeloJson)`.

**Modificar:**
- `src/motor_fea/api/contrato.py` — añade `visor_dict(modelo_dict, n)` + imports de `viz`.
- `src/motor_fea/api/servidor.py` — añade `POST /visor` dentro de `crear_app` (antes del `app.mount`); actualiza docstring.
- `src/motor_fea/viz/static/app.js` — extrae `limpiarEscena()` y `renderEscena(bundle)`; modo-ejemplo pasa a usarlos + `GET /esfuerzos`; añade estado `esfuerzos`, pick readout y cableado de `shell.js` (modo-custom).
- `src/motor_fea/viz/static/index.html` — contenedor `#shell` para la UI de carga + CSS mínimo.
- `tests/test_contrato.py` — test de `visor_dict` (aditivo).
- `tests/test_servidor.py` — tests de `POST /visor` (aditivo).

`core/`, `normativa/` y el resto de `viz/` (escena/resultados/etc.) **no se tocan**.

---

## PARTE A — Server (TDD estricto: rojo → verde → commit)

### Task 1: `contrato.visor_dict`

**Files:**
- Modify: `src/motor_fea/api/contrato.py` (imports tras la línea 41; función nueva tras `esfuerzos_modelo_dict`, ~línea 124)
- Test: `tests/test_contrato.py` (aditivo, al final del archivo)

- [ ] **Step 1: Escribir el test que falla**

Añadir al final de `tests/test_contrato.py` (usa el helper existente `_voladizo_dict()`):

```python
# ---------------------------------------------------------------------------
# #2: visor_dict — DTOs que el visor necesita para un modelo propio
# ---------------------------------------------------------------------------
def test_visor_dict_estructura_y_coherencia():
    md = _voladizo_dict()
    d = contrato.visor_dict(md, n=11)

    assert set(d) == {"escena", "resultados", "esfuerzos"}
    assert set(d["escena"]) == {"unidades", "bbox", "nodos", "barras", "losas"}
    assert set(d["resultados"]) == {"deformada", "modos"}
    assert set(d["esfuerzos"]) == {"orden_componentes", "elementos"}

    modelo = contrato.modelo_desde_dict(md)
    # coherencia: esfuerzos del visor == esfuerzos_modelo_dict directo (mismo solve lógico)
    assert d["esfuerzos"] == contrato.esfuerzos_modelo_dict(modelo, 11)


def test_visor_dict_rechaza_n_menor_2():
    with pytest.raises(ValueError):
        contrato.visor_dict(_voladizo_dict(), n=1)
```

- [ ] **Step 2: Correr el test para ver que falla**

Run: `pytest tests/test_contrato.py::test_visor_dict_estructura_y_coherencia -v`
Expected: FAIL con `AttributeError: module 'motor_fea.api.contrato' has no attribute 'visor_dict'`.

- [ ] **Step 3: Implementar lo mínimo**

En `src/motor_fea/api/contrato.py`, añadir los imports de `viz` justo después de la línea 41 (`from motor_fea.core.solver import ...`):

```python
from motor_fea.viz.escena import exportar_escena
from motor_fea.viz.resultados import calcular_resultados
```

(Dependencia **hacia abajo** desde la frontera JSON: `viz.escena`/`viz.resultados` solo importan `core`, nunca `contrato` → sin ciclo.)

Y añadir la función después de `esfuerzos_modelo_dict` (tras la línea 123):

```python
def visor_dict(modelo_dict: dict, n: int = 11) -> dict:
    """Pipeline dict→dict: los DTOs que el visor necesita para un modelo propio.

    Compone los mismos DTOs que el visor ya pinta (escena + deformada/modos +
    esfuerzos). Nota: ``calcular_resultados`` y ``esfuerzos_modelo_dict`` resuelven
    el modelo por separado → 2 solves por request; aceptable para el MVP (modelos
    pequeños). Compartir un único solve es optimización futura.
    """
    modelo = modelo_desde_dict(modelo_dict)
    return {
        "escena": exportar_escena(modelo),
        "resultados": calcular_resultados(modelo),
        "esfuerzos": esfuerzos_modelo_dict(modelo, n),
    }
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `pytest tests/test_contrato.py -v -k visor_dict`
Expected: PASS (ambos tests).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/api/contrato.py tests/test_contrato.py
git commit -m "feat(api): contrato.visor_dict — DTOs (escena+resultados+esfuerzos) para modelo propio"
```

---

### Task 2: `POST /visor` en `servidor.py`

**Files:**
- Modify: `src/motor_fea/api/servidor.py` (import línea 18; endpoint dentro de `crear_app` tras `/analizar`, antes del `app.mount` línea 121; docstring líneas 1-9)
- Test: `tests/test_servidor.py` (aditivo, al final del archivo)

- [ ] **Step 1: Escribir los tests que fallan**

Añadir al final de `tests/test_servidor.py` (reusa `modelo_ejemplo`, `modelo_a_dict`, `TestClient` ya importados):

```python
def test_visor_post_ok_coincide_con_gets():
    m = modelo_ejemplo()
    cli = TestClient(crear_app(m))
    r = cli.post("/visor", json=modelo_a_dict(m))
    assert r.status_code == 200
    data = r.json()
    assert set(data) == {"escena", "resultados", "esfuerzos"}
    assert len(data["escena"]["barras"]) == 8
    assert len(data["esfuerzos"]["elementos"]) == 8
    # round-trip: el modelo de ejemplo serializado reproduce los GET del mismo modelo
    assert data["escena"] == cli.get("/escena").json()
    assert data["esfuerzos"] == cli.get("/esfuerzos").json()


def test_visor_post_modelo_invalido_da_400():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.post("/visor", json={"nodos": [], "elementos": [{"id": 1, "nodo_i": 1,
                 "nodo_j": 2, "material_id": 1, "seccion_id": 1}]})
    assert r.status_code == 400


def test_visor_post_n_invalido_da_422():
    cli = TestClient(crear_app(modelo_ejemplo()))
    md = modelo_a_dict(modelo_ejemplo())
    assert cli.post("/visor?n=1", json=md).status_code == 422
```

- [ ] **Step 2: Correr los tests para ver que fallan**

Run: `pytest tests/test_servidor.py -v -k visor_post`
Expected: FAIL — `/visor` da 404 (ruta no registrada → cae al `StaticFiles` mount).

- [ ] **Step 3: Implementar lo mínimo**

En `src/motor_fea/api/servidor.py`, ampliar el import de `contrato` (línea 18) para incluir `visor_dict`:

```python
from motor_fea.api.contrato import analizar_completo_dict, esfuerzos_modelo_dict, modelo_desde_dict, visor_dict
```

Y registrar el endpoint **dentro de `crear_app`**, justo después del `@app.post("/analizar")` (después de la línea 118) y **antes** del comentario `# Montar al final:` (línea 120):

```python
    @app.post("/visor")
    def visor(modelo_dict: dict = Body(...), n: int = Query(11, ge=2)):
        try:
            return visor_dict(modelo_dict, n)
        except (ValueError, KeyError, TypeError) as ex:
            raise HTTPException(status_code=400, detail=f"Modelo inválido: {ex}")
```

Actualizar el docstring del módulo (líneas 4-8) para mencionar el endpoint nuevo. Reemplazar el fragmento `POST /analizar (analiza un modelo propio, stateless → resultados + esfuerzos), y` por:

```
POST /analizar (analiza un modelo propio, stateless → resultados + esfuerzos),
POST /visor (modelo propio → escena + resultados + esfuerzos, para el visor), y
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `pytest tests/test_servidor.py -v -k visor_post`
Expected: PASS (los 3 tests).

- [ ] **Step 5: Suite completa del server (regresión)**

Run: `pytest tests/test_contrato.py tests/test_servidor.py -q`
Expected: todo verde (incluye `test_index_se_sirve`, que confirma que `index.html` se sigue sirviendo en `/`).

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): POST /visor — modelo propio → DTOs del visor (escena+resultados+esfuerzos)"
```

---

## PARTE B — Front (implementar + verificación manual)

> **Nota de método:** el proyecto **no tiene runner JS** (es Python) y el spec lo deja fuera a propósito (YAGNI). Estas tareas no son red-green: se implementan y se verifican con el checklist manual de la Task 6. Cada tarea sigue cargando el visor sin errores en consola.

### Task 3: `app.js` — extraer `limpiarEscena()` y `renderEscena(bundle)`

**Files:**
- Modify: `src/motor_fea/viz/static/app.js` (estado ~líneas 38-62; función `cargar`/`cargarResultados` líneas 380-417)

Objetivo: separar el **render** (reutilizable por ambos modos) del **fetch** (específico de cada modo), y añadir el estado `esfuerzos`. Sin cambiar el comportamiento visible en arranque.

- [ ] **Step 1: Añadir el estado `esfuerzos`**

En el bloque `// --- Estado ---`, después de `let resultados = null;` (línea 41), añadir:

```javascript
let esfuerzos = null;        // DTO de esfuerzos por elemento (para el pick readout)
```

- [ ] **Step 2: Añadir `limpiarEscena()` (teardown para cambiar de modelo)**

Insertar esta función justo antes de `// --- Carga ---` (antes de la línea 379). Reusa `disposeDiseno()` (ya existe, líneas 452-457):

```javascript
// --- Teardown: limpiar la escena para cargar otro modelo ---
function limpiarEscena() {
  for (const bar of barras) { scene.remove(bar.mesh); bar.mesh.geometry.dispose(); }
  barras.length = 0;
  for (const k of Object.keys(basePos)) delete basePos[k];

  if (losaMesh) { scene.remove(losaMesh); losaMesh.geometry.dispose(); losaMesh = null; }
  if (armadoGroup) {
    scene.remove(armadoGroup);
    armadoGroup.traverse((o) => { if (o.geometry) o.geometry.dispose(); });
    armadoGroup = null;
  }
  disposeDiseno();

  resultados = null; esfuerzos = null;
  losa = null; armado = null; diseno = null;
  losaActiva = false; refuerzoActivo = false; disenoActivo = false;

  // Reconstruir el <select> dejando solo sin-deformar.
  selEstado.length = 0;
  selEstado.add(new Option('sin deformar', 'sin-deformar'));
  estado = 'sin-deformar';
}
```

- [ ] **Step 3: Añadir `renderEscena(bundle)` (construye barras + habilita deformada/modos)**

Insertar justo después de `limpiarEscena()`. Toma un bundle `{escena, resultados, esfuerzos}` ya obtenido (no hace `fetch`):

```javascript
// --- Render: construir barras + deformada/modos desde los DTOs ya obtenidos ---
function renderEscena({ escena, resultados: res, esfuerzos: esf }) {
  for (const n of escena.nodos) basePos[n.id] = new THREE.Vector3(n.p[0], n.p[1], n.p[2]);
  for (const b of escena.barras) {
    if (basePos[b.i] && basePos[b.j]) addBarra(b);
  }
  frameBbox = escena.bbox;
  encuadrar(escena.bbox.min, escena.bbox.max);
  setMsg(`${escena.barras.length} barras · ${escena.nodos.length} nodos`);

  if (res) {
    resultados = res;
    selEstado.add(new Option('deformada', 'deformada'));
    for (const m of resultados.modos) {
      selEstado.add(new Option('modo ' + m.indice, 'modo-' + m.indice));
    }
  }
  if (esf) esfuerzos = esf;
}
```

- [ ] **Step 4: Reescribir `cargar()` (modo-ejemplo) para usar `renderEscena` + `GET /esfuerzos`**

Reemplazar la función `cargar()` actual (líneas 380-402) por esta versión, que obtiene escena+resultados+esfuerzos y delega el render. Mantiene los overlays de ejemplo (losa/armado/diseño):

```javascript
// --- Carga (modo-ejemplo: el modelo del server) ---
async function cargar() {
  let escena;
  try {
    const r = await fetch('./escena');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    escena = await r.json();
  } catch (e) {
    setMsg('Error cargando /escena: ' + e.message);
    return;
  }

  let res = null;
  try {
    const r = await fetch('./resultados');
    if (r.ok) res = await r.json();
  } catch (e) { /* sin resultados: se muestra solo geometría */ }

  let esf = null;
  try {
    const r = await fetch('./esfuerzos');
    if (r.ok) esf = await r.json();
  } catch (e) { /* sin esfuerzos: el pick readout queda inactivo */ }

  renderEscena({ escena, resultados: res, esfuerzos: esf });

  await cargarLosa();
  await cargarArmado();
  await cargarDiseno();
}
```

- [ ] **Step 5: Borrar `cargarResultados()` (su lógica vive ahora en `renderEscena`)**

Eliminar la función `cargarResultados()` completa (líneas 404-417). Ningún otro sitio la llama tras el Step 4 (verificar con búsqueda: no debe quedar ninguna referencia a `cargarResultados`).

- [ ] **Step 6: Verificación manual (arranque idéntico a hoy)**

Run: `python -m motor_fea.api.cli serve` (o el comando habitual del proyecto), abrir `http://127.0.0.1:8000/`.
Expected: el modelo de ejemplo renderiza **idéntico a antes** — barras, y el `<select>` ofrece sin-deformar + deformada + modos + losa + refuerzo + diseño. Consola del navegador sin errores.

- [ ] **Step 7: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "refactor(viz): extraer limpiarEscena/renderEscena de cargar(); modo-ejemplo carga /esfuerzos"
```

---

### Task 4: `app.js` — pick readout de esfuerzos

**Files:**
- Modify: `src/motor_fea/viz/static/app.js` (handler `pointerdown` líneas 339-354; función nueva tras `mostrarDiseno`, ~línea 377)

- [ ] **Step 1: Añadir `resumenEsfuerzos(id)`**

Insertar después de la función `mostrarDiseno` (después de la línea 377). `esfuerzos.elementos[i].diagrama` son estaciones `[s, N, Vy, Vz, T, My, Mz]`; `extremo_i` es `[N, Vy, Vz, T, My, Mz]`. Axial con tracción + = `-extremo_i[0]`; `|M|máx` = máximo de `max(|My|,|Mz|)` sobre el diagrama (componentes 5 y 6 de cada estación):

```javascript
function resumenEsfuerzos(id) {
  if (!esfuerzos) return null;
  const el = esfuerzos.elementos.find((e) => e.id === id);
  if (!el) return null;
  const N = -el.extremo_i[0];                       // tracción +
  const signo = N >= 0 ? 'tracción' : 'compresión';
  let mmax = 0;
  for (const fila of el.diagrama) {
    mmax = Math.max(mmax, Math.abs(fila[5]), Math.abs(fila[6]));   // |My|, |Mz|
  }
  const kN = (n) => (n / 1000).toFixed(0);
  const kNm = (n) => (n / 1000).toFixed(1);
  return `N = ${kN(Math.abs(N))} kN (${signo}) · |M|máx = ${kNm(mmax)} kN·m`;
}
```

- [ ] **Step 2: Extender el handler `pointerdown` con el readout**

En el listener `pointerdown` (líneas 339-354), añadir una rama final tras el `else if (disenoActivo && diseno)`. Queda así (la cabecera y las dos ramas previas no cambian):

```javascript
  if (losaActiva && losaMesh) {
    const hits = punteroRay.intersectObject(losaMesh);
    if (hits.length) mostrarValorEnPunto(hits[0].point.x, hits[0].point.y);
  } else if (disenoActivo && diseno) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    const el = bar && diseno.elementos.find((e) => e.id === bar.id);
    if (el) mostrarDiseno(el);
  } else if (esfuerzos) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    const txt = bar && resumenEsfuerzos(bar.id);
    if (txt) info.textContent = txt;
  }
```

- [ ] **Step 3: Verificación manual**

Recargar el visor. En modo sin-deformar/deformada/modo, hacer click sobre una barra.
Expected: `#info` muestra `N = … kN (tracción/compresión) · |M|máx = … kN·m`. (En modo losa/diseño el comportamiento previo se mantiene: el readout solo actúa fuera de esos modos.)

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): pick readout de esfuerzos (axial + |M|máx) al tocar una barra"
```

---

### Task 5: `shell.js` — módulo de carga (nuevo)

**Files:**
- Create: `src/motor_fea/viz/static/shell.js`

Responsabilidad única: construir/cablear la UI de carga dentro del contenedor `#shell`, postear a `/visor` e invocar `onModelo(bundle, modeloJson)`. En error conserva la escena (no llama a `onModelo`).

- [ ] **Step 1: Crear `shell.js`**

```javascript
// shell.js — UI de carga de un modelo propio. POST /visor → onModelo(bundle, modeloJson).
// No conoce three.js: solo DOM + fetch. El render lo hace el callback onModelo.
export function crearShell({ onModelo }) {
  const cont = document.getElementById('shell');

  const file = document.createElement('input');
  file.type = 'file';
  file.accept = '.json,application/json';
  file.setAttribute('aria-label', 'cargar modelo .json');

  const textarea = document.createElement('textarea');
  textarea.placeholder = 'pega aquí el JSON del modelo';
  textarea.rows = 4;
  textarea.style.width = '16em';

  const btnAnalizar = document.createElement('button');
  btnAnalizar.type = 'button';
  btnAnalizar.textContent = 'analizar';

  const btnDescargar = document.createElement('button');
  btnDescargar.type = 'button';
  btnDescargar.textContent = 'descargar .json';
  btnDescargar.disabled = true;

  const estado = document.createElement('span');
  estado.id = 'shell-estado';

  cont.append(file, textarea, btnAnalizar, btnDescargar, estado);

  let ultimoModelo = null;   // el último JSON cargado con éxito (para descargar)

  async function analizar(texto) {
    let modeloJson;
    try {
      modeloJson = JSON.parse(texto);
    } catch (e) {
      estado.textContent = 'JSON inválido: ' + e.message;
      return;   // no postea, no toca la escena
    }
    let bundle;
    try {
      const r = await fetch('./visor', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(modeloJson),
      });
      if (!r.ok) {
        const d = await r.json().catch(() => ({}));
        throw new Error(d.detail || ('HTTP ' + r.status));
      }
      bundle = await r.json();
    } catch (e) {
      estado.textContent = 'Error: ' + e.message;
      return;   // conserva la escena actual
    }
    ultimoModelo = modeloJson;
    btnDescargar.disabled = false;
    estado.textContent = 'modelo cargado';
    onModelo(bundle, modeloJson);
  }

  file.addEventListener('change', () => {
    const f = file.files && file.files[0];
    if (!f) return;
    const lector = new FileReader();
    lector.onload = () => analizar(String(lector.result));
    lector.onerror = () => { estado.textContent = 'No se pudo leer el archivo'; };
    lector.readAsText(f);
  });

  btnAnalizar.addEventListener('click', () => analizar(textarea.value));

  btnDescargar.addEventListener('click', () => {
    if (!ultimoModelo) return;
    const blob = new Blob([JSON.stringify(ultimoModelo, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'modelo.json';
    a.click();
    URL.revokeObjectURL(url);
  });
}
```

- [ ] **Step 2: Commit**

```bash
git add src/motor_fea/viz/static/shell.js
git commit -m "feat(viz): shell.js — UI de carga (archivo/pegado) → POST /visor → onModelo"
```

---

### Task 6: `index.html` + cableado del modo-custom en `app.js`

**Files:**
- Modify: `src/motor_fea/viz/static/index.html` (CSS en `<style>`; markup tras `#panel`)
- Modify: `src/motor_fea/viz/static/app.js` (import al inicio; cableado tras `cargar()`)

- [ ] **Step 1: Añadir el contenedor `#shell` y su CSS en `index.html`**

En el `<style>` (antes de `#info { ... }`, línea 17), añadir:

```css
    #shell { position: fixed; bottom: 8px; left: 8px; padding: 8px 10px;
             background: rgba(0,0,0,.6); color: #fff; border-radius: 6px;
             font-size: 13px; display: flex; flex-direction: column; gap: 6px; }
    #shell textarea { font-family: monospace; font-size: 12px; }
    #shell-estado { min-height: 16px; opacity: .85; }
```

En el `<body>`, añadir el contenedor justo después de cerrar `</div>` de `#panel` (después de la línea 52) y antes del `<script ...>`:

```html
  <div id="shell"></div>
```

(No hace falta `<script>` extra: `shell.js` se importa desde `app.js`.)

- [ ] **Step 2: Importar y cablear `crearShell` en `app.js`**

Al inicio de `app.js`, después de los imports de three (después de la línea 3), añadir:

```javascript
import { crearShell } from './shell.js';
```

Al final del archivo, **después** de la llamada `cargar();` (línea 569), añadir el cableado del modo-custom. `limpiarEscena()` resetea, `renderEscena(bundle)` pinta, y se fuerza el estado a sin-deformar:

```javascript
// Modo-custom: cargar un modelo propio reemplaza la escena (sin overlays de ejemplo).
crearShell({
  onModelo: (bundle) => {
    limpiarEscena();
    renderEscena(bundle);
    selEstado.value = 'sin-deformar';
    setEstado('sin-deformar');
  },
});
```

- [ ] **Step 3: Verificación manual (checklist completo del spec §8.2)**

Recargar `http://127.0.0.1:8000/` y verificar:

1. **Arranque:** el modelo de ejemplo renderiza idéntico a hoy (barras, deformada, modos, losa, armado, diseño).
2. **Cargar `.json`:** elegir un archivo de modelo válido (p. ej. exportar primero con "descargar" tras pegar, o un modelo del esquema de `contrato.py`) → la escena se reconstruye con esa geometría + deformada/modos; el `<select>` muestra solo sin-deformar/deformada/modos (sin losa/armado/diseño).
3. **Pegar JSON + analizar:** pegar el mismo JSON y pulsar "analizar" → mismo resultado.
4. **Errores:** pegar JSON malformado → `#shell-estado` muestra "JSON inválido…"; pegar un modelo inválido (refs colgantes) → "Error: Modelo inválido…" y la escena previa se conserva.
5. **Pick:** click sobre un elemento → `#info` muestra `N = … (tracción/compresión) · |M|máx = …`.
6. **Descargar:** pulsar "descargar .json" → baja el modelo cargado.

Consola del navegador sin errores en ninguno de los pasos.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/index.html src/motor_fea/viz/static/app.js
git commit -m "feat(viz): modo-custom — shell de carga cableada a limpiarEscena/renderEscena"
```

---

## Cierre

- [ ] **Suite Python completa (regresión final):**

Run: `pytest -q`
Expected: todo verde. Criterio de aceptación del spec: `POST /visor` del ejemplo serializado reproduce `GET /escena` y `GET /esfuerzos` del mismo modelo (cubierto por `test_visor_post_ok_coincide_con_gets`).

- [ ] **Checklist manual del front (Task 6, Step 3) ejecutado y OK.**

---

## Self-Review (cobertura del spec)

| Requisito del spec | Task que lo implementa |
|---|---|
| §4.1 `contrato.visor_dict(modelo_dict, n)` | Task 1 |
| §4.2 `POST /visor?n=11` | Task 2 |
| §4.3 limitación 2-solves documentada | Task 1 (docstring de `visor_dict`) |
| §5.1 `shell.js` (archivo + pegado + analizar + descargar) | Task 5 |
| §5.2 `limpiarEscena()` + `renderEscena(bundle)` | Task 3 |
| §5.2 modo-ejemplo usa renderEscena + GET /esfuerzos | Task 3 |
| §5.2 modo-custom: limpiar → POST /visor → render | Tasks 5 + 6 |
| §5.3 controles de la shell en index.html | Task 6 |
| §6 pick readout `resumenEsfuerzos(id)` | Task 4 |
| §7 manejo de errores (400/JSON malformado/n) | Tasks 2 (server) + 5 (front) |
| §8.1 tests server (TDD) | Tasks 1 + 2 |
| §8.2 checklist manual front | Task 6, Step 3 |

**Desviaciones del spec (intencionales, documentadas):**
- `/visor` se registra **dentro de `crear_app`** (closure sobre `modelo`), no en un `app` de módulo (el spec mostró `@app.post` suelto; aquí todas las rutas son closures).
- La clave `esfuerzos` de `visor_dict` usa `esfuerzos_modelo_dict(modelo, n)` (ya existente) en vez de `esfuerzos_a_dict(modelo, resolver(modelo), n)` — mismo resultado, DRY, y hace trivial el round-trip `== GET /esfuerzos`.
- Las tareas de front no son red-green TDD: el proyecto no tiene runner JS y el spec lo excluye (YAGNI). Se verifican con el checklist manual.
