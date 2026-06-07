# Visor estructural WebXR (Fase 2: deformada + modos) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extender el visor para mostrar la deformada estática (peso propio + cargas) y las formas modales 1–3 oscilando, con un panel que cambia de estado, escala la exageración y muestra el período real.

**Architecture:** Una unidad pura nueva `viz/resultados.py` deriva el peso propio, arma la deformada (reusa `solver.resolver`) y los modos (reusa `modal.modos`) en un `ResultadosDTO`. El endpoint `GET /resultados` lo sirve. El visor (`app.js`/`index.html`) refactoriza las barras a "caja unitaria escalable" y anima por frame en el cliente con período de display fijo. `core/` y `normativa/` no se tocan.

**Tech Stack:** Python 3.11 + stdlib (`resultados.py`), FastAPI (endpoint), three.js vendorizado (visor, sin build).

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-visor-webxr-fase2-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/viz/resultados.py` (nuevo) | **Puro.** `calcular_resultados(modelo, n_modos=3) -> dict`: peso propio, masa sísmica, deformada, modos, factores sugeridos. |
| `tests/test_resultados.py` (nuevo) | Tests puros con `modelos_ref.voladizo()`. |
| `src/motor_fea/api/servidor.py` (mod) | Endpoint `GET /resultados`; carga lateral en `modelo_ejemplo()`. |
| `tests/test_servidor.py` (mod) | +1 test de `/resultados`. |
| `src/motor_fea/viz/static/index.html` (mod) | Panel de control (selector, slider, play, info). |
| `src/motor_fea/viz/static/app.js` (mod) | Refactor caja unitaria + fetch `/resultados` + bucle de animación. |
| `README.md` (mod) | Mención de la vista de resultados. |

**Contrato `ResultadosDTO`** (lo que `calcular_resultados` devuelve y `/resultados` sirve):

```jsonc
{
  "deformada": {
    "factor_sugerido": 120.0,
    "desplazamientos": { "5": [ux, uy, uz] }   // solo traslaciones, clave = nodo_id (str)
  },
  "modos": [
    { "indice": 1, "periodo": 0.42, "frecuencia": 2.38, "omega": 14.9,
      "factor_sugerido": 80.0, "forma": { "5": [ux, uy, uz] } }
    // hasta 3; [] si no hay masa en GDL libres
  ]
}
```

---

## Task 1: Cálculo de resultados (puro)

**Files:**
- Create: `src/motor_fea/viz/resultados.py`
- Test: `tests/test_resultados.py`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/test_resultados.py` con el contenido completo:

```python
"""Tests puros del cálculo de resultados del visor (peso propio, deformada, modos)."""
import math

import pytest

import modelos_ref
from motor_fea.core.modelo import Apoyo, ElementoFrame, ModeloEstructural
from motor_fea.viz import resultados


def test_peso_propio_reparte_masa_mitad_a_cada_nodo():
    # voladizo: densidad 2400 · A 0.09 · L 3 = 648 kg → 324 kg en cada extremo.
    masa, cargas = resultados._peso_propio(modelos_ref.voladizo())
    assert masa[1] == pytest.approx(324.0)
    assert masa[2] == pytest.approx(324.0)
    # carga gravitatoria fz = -m·g/2 en cada nodo (signo negativo = hacia abajo).
    assert all(c.fz < 0 for c in cargas)


def test_deformada_punta_baja_por_peso_propio():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    uz_punta = res["deformada"]["desplazamientos"]["2"][2]
    assert uz_punta < 0.0


def test_modos_periodo_positivo_y_omega_ascendente():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    modos = res["modos"]
    assert len(modos) >= 1
    assert all(m["periodo"] > 0.0 for m in modos)
    omegas = [m["omega"] for m in modos]
    assert omegas == sorted(omegas)          # modal devuelve ω ascendente (modo 1 = fundamental)


def test_factor_sugerido_finito_y_positivo():
    res = resultados.calcular_resultados(modelos_ref.voladizo())
    fs_def = res["deformada"]["factor_sugerido"]
    assert fs_def > 0.0 and math.isfinite(fs_def)
    for m in res["modos"]:
        assert m["factor_sugerido"] > 0.0 and math.isfinite(m["factor_sugerido"])


def test_todo_empotrado_sin_modos_pero_con_deformada():
    m = modelos_ref.voladizo()
    m.apoyos.append(Apoyo.empotrado(2))      # ahora ambos nodos fijos
    res = resultados.calcular_resultados(m)
    assert res["modos"] == []
    assert "desplazamientos" in res["deformada"]


def test_modelo_invalido_lanza_valueerror():
    m = ModeloEstructural(elementos=[ElementoFrame(1, 1, 2, 1, 1)])  # refs inexistentes
    with pytest.raises(ValueError):
        resultados.calcular_resultados(m)
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_resultados.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.viz.resultados'`.

- [ ] **Step 3: Implementar `resultados.py`**

Crear `src/motor_fea/viz/resultados.py` con el contenido completo:

```python
"""Cálculo de resultados estructurales para el visor (capa frontera).

Deriva, reusando los motores de ``core`` (solver y modal), los estados que el
visor anima: la **deformada** estática bajo peso propio + cargas del modelo y
las **formas modales** 1..n. Función pura: solo usa ``core``; no toca HTTP ni
three.js, así que se prueba con asserts normales.

Convención de unidades: SI (metros, newtons, kilogramos). El peso propio de un
elemento es ``densidad·area·L``; se reparte mitad a cada nodo, como masa (para
el análisis modal) y como carga gravitatoria ``fz = −m·g/2`` (para la deformada).
"""
from __future__ import annotations

import math
from dataclasses import replace

from motor_fea.core import modal, solver
from motor_fea.core.modelo import CargaNodal, ModeloEstructural

G = 9.81  # aceleración de la gravedad (m/s²)


def _longitud(ni, nj) -> float:
    return math.sqrt((nj.x - ni.x) ** 2 + (nj.y - ni.y) ** 2 + (nj.z - ni.z) ** 2)


def _peso_propio(modelo: ModeloEstructural) -> tuple[dict[int, float], list[CargaNodal]]:
    """Masa nodal {id: kg} y cargas gravitatorias por peso propio (mitad a cada nodo)."""
    nodos = {n.id: n for n in modelo.nodos}
    mats = {m.id: m for m in modelo.materiales}
    secs = {s.id: s for s in modelo.secciones}
    masa: dict[int, float] = {}
    fz: dict[int, float] = {}
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        m_el = mats[e.material_id].densidad * secs[e.seccion_id].area * _longitud(ni, nj)
        for nid in (e.nodo_i, e.nodo_j):
            masa[nid] = masa.get(nid, 0.0) + m_el / 2.0
            fz[nid] = fz.get(nid, 0.0) - m_el * G / 2.0
    cargas = [CargaNodal(nid, fz=v) for nid, v in fz.items()]
    return masa, cargas


def _agregar_masa_sismica(modelo: ModeloEstructural, masa: dict[int, float]) -> None:
    """Suma la masa sísmica de las cargas verticales del modelo: |fz|/g."""
    for c in modelo.cargas:
        if c.fz != 0.0:
            masa[c.nodo_id] = masa.get(c.nodo_id, 0.0) + abs(c.fz) / G


def _diagonal_bbox(modelo: ModeloEstructural) -> float:
    if not modelo.nodos:
        return 1.0
    xs = [n.x for n in modelo.nodos]
    ys = [n.y for n in modelo.nodos]
    zs = [n.z for n in modelo.nodos]
    d = math.sqrt((max(xs) - min(xs)) ** 2 + (max(ys) - min(ys)) ** 2 + (max(zs) - min(zs)) ** 2)
    return d if d > 0.0 else 1.0


def _factor_sugerido(despl, diag: float) -> float:
    """0.08·diagonal / max|desplazamiento|; 1.0 si el máximo es 0 (no divide por cero)."""
    maxd = 0.0
    for v in despl.values():
        mag = math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2)
        if mag > maxd:
            maxd = mag
    return 0.08 * diag / maxd if maxd > 0.0 else 1.0


def calcular_resultados(modelo: ModeloEstructural, n_modos: int = 3) -> dict:
    """ResultadosDTO: deformada (peso propio + cargas) y modos 1..n. ValueError si inválido."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    diag = _diagonal_bbox(modelo)
    masa, cargas_pp = _peso_propio(modelo)
    _agregar_masa_sismica(modelo, masa)

    # Deformada: modelo + cargas de peso propio (sin mutar el original).
    modelo_def = replace(modelo, cargas=list(modelo.cargas) + cargas_pp)
    res = solver.resolver(modelo_def)
    despl = {nid: u[:3] for nid, u in res.desplazamientos.items()}
    deformada = {
        "factor_sugerido": _factor_sugerido(despl, diag),
        "desplazamientos": {str(nid): list(u) for nid, u in despl.items()},
    }

    # Modos: si no hay masa en GDL libres, modal lanza ValueError → modos=[].
    masas = {nid: m for nid, m in masa.items() if m > 0.0}
    try:
        modales = modal.modos(modelo, masas, n_modos=n_modos)
    except ValueError:
        modales = []
    modos = []
    for i, rm in enumerate(modales, start=1):
        modos.append({
            "indice": i,
            "periodo": rm.periodo,
            "frecuencia": rm.frecuencia,
            "omega": rm.omega,
            "factor_sugerido": _factor_sugerido(rm.forma, diag),
            "forma": {str(nid): list(v) for nid, v in rm.forma.items()},
        })

    return {"deformada": deformada, "modos": modos}
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_resultados.py -q`
Expected: PASS (6 passed).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/resultados.py tests/test_resultados.py
git commit -m "feat(viz): calcular_resultados (deformada + modos) reusando solver/modal

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Endpoint `/resultados` + carga lateral en el ejemplo

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir el test que falla**

Añadir al final de `tests/test_servidor.py`:

```python
def test_resultados_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/resultados")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"deformada", "modos"}
    assert "desplazamientos" in data["deformada"]
    assert len(data["modos"]) <= 3
    assert len(data["modos"]) == 3          # los 4 nodos superiores tienen masa de peso propio
    assert all(m["periodo"] > 0 for m in data["modos"])
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_resultados_ok -q`
Expected: FAIL con HTTP 404 (la ruta `/resultados` aún no existe; la sirve `StaticFiles` y no la encuentra).

- [ ] **Step 3: Añadir el import de `CargaNodal` y `calcular_resultados`**

En `src/motor_fea/api/servidor.py`, reemplazar el bloque de imports de modelo y añadir el de resultados:

```python
from motor_fea.api.contrato import modelo_desde_dict
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz.escena import exportar_escena
from motor_fea.viz.resultados import calcular_resultados
```

- [ ] **Step 4: Añadir la carga lateral al pórtico de ejemplo**

En `modelo_ejemplo()`, reemplazar el bucle de apoyos y el `return m` finales por (añade la carga lateral antes de retornar):

```python
    for n in (1, 2, 3, 4):
        m.apoyos.append(Apoyo.empotrado(n))
    for n in (5, 6, 7, 8):                      # carga lateral en +X (deformada visible)
        m.cargas.append(CargaNodal(n, fx=10000.0))
    return m
```

- [ ] **Step 5: Registrar el endpoint `/resultados`**

En `crear_app`, después del endpoint `escena()` y antes de `app.mount(...)`, añadir:

```python
    @app.get("/resultados")
    def resultados():
        try:
            return calcular_resultados(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 6: Correr el test para verificar que pasa**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`
Expected: PASS (todos los tests del servidor, incluido `test_resultados_ok`).

- [ ] **Step 7: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): endpoint GET /resultados + carga lateral en el portico de ejemplo

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Panel de control y animación en el visor

> Sin unit test (decisión del spec §8): smoke manual. Los pasos producen el HTML
> y el JS completos; la verificación es visual con `motor-fea --serve`.

**Files:**
- Modify: `src/motor_fea/viz/static/index.html`
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Añadir el panel de control al `index.html`**

Reemplazar el `<style>…</style>` por (añade los estilos de `#panel`):

```html
  <style>
    html, body { margin: 0; height: 100%; overflow: hidden; background: #101418;
                 font-family: system-ui, sans-serif; }
    #msg { position: fixed; top: 8px; left: 8px; padding: 6px 10px;
           background: rgba(0,0,0,.6); color: #fff; border-radius: 6px; font-size: 14px; }
    #panel { position: fixed; top: 8px; right: 8px; padding: 8px 10px;
             background: rgba(0,0,0,.6); color: #fff; border-radius: 6px;
             font-size: 13px; display: flex; flex-direction: column; gap: 6px; }
    #panel .fila { display: flex; align-items: center; gap: 6px; }
    #panel select, #panel button { font-size: 13px; }
    #info { min-height: 16px; opacity: .85; }
  </style>
```

Y reemplazar el `<body>…</body>` por (añade el `<div id="panel">`):

```html
<body>
  <div id="msg">Cargando…</div>
  <div id="panel">
    <div class="fila">
      <select id="estado">
        <option value="sin-deformar">sin deformar</option>
      </select>
      <button id="play" type="button">⏸</button>
    </div>
    <div class="fila">
      <label for="exag">exag</label>
      <input type="range" id="exag" min="0" max="100" step="1" value="0">
    </div>
    <span id="info"></span>
  </div>
  <script type="module" src="./app.js"></script>
</body>
```

- [ ] **Step 2: Reescribir `app.js` con el refactor de caja unitaria + animación**

Reemplazar el contenido completo de `src/motor_fea/viz/static/app.js` por:

```javascript
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { VRButton } from 'three/addons/webxr/VRButton.js';

const msg = document.getElementById('msg');
const setMsg = (t) => { msg.textContent = t; };

// --- Escena base ---
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101418);
scene.add(new THREE.GridHelper(50, 50, 0x444444, 0x222222));
scene.add(new THREE.AxesHelper(2));
scene.add(new THREE.HemisphereLight(0xffffff, 0x303030, 1.3));

const camera = new THREE.PerspectiveCamera(60, innerWidth / innerHeight, 0.05, 1000);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(devicePixelRatio);
renderer.xr.enabled = true;
document.body.appendChild(renderer.domElement);

// Rig: movemos este grupo para teletransportarnos en VR.
const rig = new THREE.Group();
rig.add(camera);
scene.add(rig);

const controls = new OrbitControls(camera, renderer.domElement);

const MAT = {
  columna: new THREE.MeshStandardMaterial({ color: 0x4a90d9 }),
  viga:    new THREE.MeshStandardMaterial({ color: 0xd98a4a }),
};

// --- Estado del modelo y de la animación ---
const basePos = {};          // id -> THREE.Vector3 (posición sin deformar)
const barras = [];           // { mesh, i, j } con caja unitaria en Z (escalable)
let resultados = null;       // DTO de /resultados (deformada + modos)

let estado = 'sin-deformar';
let exag = 0;
let playing = true;
let tAcum = 0;
let lastT = null;
const T_DISPLAY = 2.0;       // periodo de display (s) — NO el ω real (se muestra como texto)

const selEstado = document.getElementById('estado');
const exagInput = document.getElementById('exag');
const btnPlay = document.getElementById('play');
const info = document.getElementById('info');

// --- Barras como cajas unitarias reposicionables cada frame ---
function addBarra(b) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, 1), MAT[b.tipo] || MAT.viga);
  scene.add(mesh);
  barras.push({ mesh, i: b.i, j: b.j });
}

// Desplazamiento del nodo en el estado activo (THREE.Vector3 a sumar a la base).
function despNodo(id, fase) {
  if (!resultados) return new THREE.Vector3();
  if (estado === 'deformada') {
    const d = resultados.deformada.desplazamientos[id];
    if (d) return new THREE.Vector3(d[0], d[1], d[2]).multiplyScalar(exag);
  } else if (estado.startsWith('modo-')) {
    const m = resultados.modos[parseInt(estado.slice(5), 10) - 1];
    if (m) {
      const f = m.forma[id];
      if (f) return new THREE.Vector3(f[0], f[1], f[2]).multiplyScalar(exag * fase);
    }
  }
  return new THREE.Vector3();
}

function posDef(id, fase) {
  const base = basePos[id];
  if (!base) return null;
  return base.clone().add(despNodo(id, fase));
}

function actualizarBarras(fase) {
  for (const bar of barras) {
    const vi = posDef(bar.i, fase);
    const vj = posDef(bar.j, fase);
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    bar.mesh.position.copy(vi).lerp(vj, 0.5);
    bar.mesh.lookAt(vj);                 // orienta el lado +Z hacia el nodo j
    bar.mesh.scale.z = L === 0 ? 1e-6 : L;
  }
}

// --- Panel de control ---
function fsDe(est) {
  if (!resultados) return 1;
  if (est === 'deformada') return resultados.deformada.factor_sugerido;
  if (est.startsWith('modo-')) {
    const m = resultados.modos[parseInt(est.slice(5), 10) - 1];
    return m ? m.factor_sugerido : 1;
  }
  return 1;
}

function setEstado(nuevo) {
  estado = nuevo;
  const fs = fsDe(estado);
  exagInput.min = 0;
  exagInput.max = fs * 5;                // rango 0 … factor_sugerido×5
  exagInput.step = fs / 100;
  exagInput.value = estado === 'sin-deformar' ? 0 : fs;
  exag = parseFloat(exagInput.value);
  if (estado.startsWith('modo-')) {
    const m = resultados.modos[parseInt(estado.slice(5), 10) - 1];
    info.textContent = m ? `T = ${m.periodo.toFixed(2)} s` : '';
  } else if (estado === 'deformada') {
    info.textContent = 'estático';
  } else {
    info.textContent = '';
  }
}

selEstado.addEventListener('change', () => setEstado(selEstado.value));
exagInput.addEventListener('input', () => { exag = parseFloat(exagInput.value); });
btnPlay.addEventListener('click', () => {
  playing = !playing;
  btnPlay.textContent = playing ? '⏸' : '▶';
});

// --- Carga de geometría (/escena) ---
async function cargar() {
  let data;
  try {
    const r = await fetch('./escena');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    data = await r.json();
  } catch (e) {
    setMsg('Error cargando /escena: ' + e.message);
    return;
  }
  for (const n of data.nodos) basePos[n.id] = new THREE.Vector3(n.p[0], n.p[1], n.p[2]);
  for (const b of data.barras) {
    if (basePos[b.i] && basePos[b.j]) addBarra(b);
  }

  // Auto-encuadre con bbox.
  const mn = new THREE.Vector3(data.bbox.min[0], data.bbox.min[1], data.bbox.min[2]);
  const mx = new THREE.Vector3(data.bbox.max[0], data.bbox.max[1], data.bbox.max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);

  await cargarResultados();
}

// --- Carga de resultados (/resultados) ---
async function cargarResultados() {
  try {
    const r = await fetch('./resultados');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    resultados = await r.json();
  } catch (e) {
    setMsg(msg.textContent + ' · sin resultados (' + e.message + ')');
    return;
  }
  selEstado.add(new Option('deformada', 'deformada'));
  for (const m of resultados.modos) {
    selEstado.add(new Option('modo ' + m.indice, 'modo-' + m.indice));
  }
}

// --- WebXR: botón solo si hay soporte ---
if (navigator.xr && navigator.xr.isSessionSupported) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}

// --- Teletransporte en VR (idéntico a Fase 1) ---
const piso = new THREE.Mesh(
  new THREE.PlaneGeometry(500, 500).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ visible: false }));
scene.add(piso);

const marca = new THREE.Mesh(
  new THREE.CircleGeometry(0.25, 32).rotateX(-Math.PI / 2),
  new THREE.MeshBasicMaterial({ color: 0x00ff88 }));
marca.visible = false;
scene.add(marca);

const raycaster = new THREE.Raycaster();
const rotMatrix = new THREE.Matrix4();
let destino = null;

function crearControl(i) {
  const c = renderer.xr.getController(i);
  c.addEventListener('selectstart', () => { c.userData.activo = true; });
  c.addEventListener('selectend', () => {
    c.userData.activo = false;
    if (destino) {
      const cabeza = new THREE.Vector3().setFromMatrixPosition(camera.matrixWorld);
      rig.position.x += destino.x - cabeza.x;
      rig.position.z += destino.z - cabeza.z;
    }
  });
  c.add(new THREE.Line(
    new THREE.BufferGeometry().setFromPoints([new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, -5)]),
    new THREE.LineBasicMaterial({ color: 0x00ff88 })));
  rig.add(c);
  return c;
}
const xrControls = [crearControl(0), crearControl(1)];

function actualizarTeletransporte() {
  destino = null;
  marca.visible = false;
  for (const c of xrControls) {
    if (!c.userData.activo) continue;
    rotMatrix.identity().extractRotation(c.matrixWorld);
    raycaster.ray.origin.setFromMatrixPosition(c.matrixWorld);
    raycaster.ray.direction.set(0, 0, -1).applyMatrix4(rotMatrix);
    const hits = raycaster.intersectObject(piso);
    if (hits.length) {
      destino = hits[0].point;
      marca.position.copy(destino);
      marca.visible = true;
    }
  }
}

addEventListener('resize', () => {
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(innerWidth, innerHeight);
});

renderer.setAnimationLoop((time) => {
  const now = (time || 0) / 1000;
  if (lastT === null) lastT = now;
  const dt = now - lastT;
  lastT = now;
  if (playing) tAcum += dt;
  const fase = Math.sin((2 * Math.PI * tAcum) / T_DISPLAY);

  actualizarBarras(fase);

  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();
```

- [ ] **Step 3: Smoke manual del visor**

Run: `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`
(Si `--serve` requiere el extra `api`: `pip install -e '.[api]'` primero.)

Abrir `http://127.0.0.1:8000/` y verificar:
- El selector muestra `sin deformar`, `deformada`, `modo 1`, `modo 2`, `modo 3`.
- En `deformada`, mover el slider escala la deformación; `info` dice `estático`.
- En `modo 1..3`, la estructura oscila suavemente; `info` muestra `T = … s`.
- El botón `⏸/▶` pausa y reanuda (congela la fase).
- Las barras se mantienen rectas y conectadas al deformarse.

Detener el servidor (Ctrl-C).

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/index.html src/motor_fea/viz/static/app.js
git commit -m "feat(viz): panel de estados + animacion deformada/modos (caja unitaria)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Documentación y verificación final

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Documentar la vista de resultados en el README**

Localizar la sección donde la Fase 1 documenta `motor-fea --serve` y añadir, justo debajo de su párrafo, este texto:

```markdown
Además de la geometría, el visor obtiene `GET /resultados` y ofrece un panel
(arriba a la derecha) para alternar entre **sin deformar**, la **deformada** bajo
peso propio + cargas, y los **modos 1–3**. Un slider exagera el desplazamiento y
se muestra el período real `T` de cada modo; el cálculo ocurre en el servidor
(reusa el solver y el análisis modal) y el visor solo anima en el cliente.
```

- [ ] **Step 2: Correr la suite completa**

Run: `PYTHONPATH=src:tests python -m pytest -q`
Expected: PASS, ~124 tests (117 de Fase 1 + 6 de resultados + 1 de servidor).

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(viz): documentar la vista de resultados (deformada + modos) del visor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §8)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~124 tests).
2. `GET /resultados` del pórtico de ejemplo devuelve `deformada` con desplazamientos
   no nulos y `modos` con 3 entradas de período positivo.
3. En el visor: el selector cambia el estado, el slider escala la exageración, los
   modos oscilan suavemente y se muestra el período real.

## Notas de revisión (plan vs. spec)

- **Orden de modos:** el spec §8 dice "períodos ascendentes"; físicamente el modo 1
  (fundamental) tiene el **período más largo**, así que los períodos *descienden*
  con el índice. El motor `modal.modos` devuelve **ω ascendente** (su contrato
  real), y por eso el test afirma `omegas == sorted(omegas)` en vez de períodos
  ascendentes. Equivalente y correcto.
- **Pureza:** `resultados.py` solo importa de `motor_fea.core`; no toca HTTP ni
  three.js. El endpoint y el visor son las únicas piezas con I/O.
- **No-mutación:** `calcular_resultados` usa `dataclasses.replace(...)` con una lista
  de cargas nueva, de modo que el modelo del caller no se altera al añadir el peso
  propio para la deformada.
