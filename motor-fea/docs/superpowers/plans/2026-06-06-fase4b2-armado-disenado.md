# Fase 4b.2: armado diseñado en el visor — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Servir y dibujar el armado diseñado por fuerzas (`diseno_elemento`) en el visor, coloreado por `cumple`, con la demanda (Pu/Mu/Vu) al tocar cada elemento.

**Architecture:** `viz/armado.py` se refactoriza para exponer la geometría de posiciones; `viz/diseno.py` (nuevo, puro) combina `resolver`+`esfuerzos_elementos`+`diseno_elemento` y la geometría en un `DisenoDTO`; endpoint `GET /diseno`; el visor gana un estado `diseño`.

**Tech Stack:** Python 3.11 + stdlib; FastAPI; three.js vendorizado.

**Spec de referencia:** `docs/superpowers/specs/2026-06-06-fase4b2-armado-disenado-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/viz/armado.py` (mod, refactor) | Extraer `_posiciones_columna`/`_posiciones_viga` (comportamiento idéntico). |
| `src/motor_fea/viz/diseno.py` (nuevo, puro) | `calcular_diseno(modelo, fc, fy, rec) → DisenoDTO`. |
| `tests/test_diseno_visual.py` (nuevo) | Tests puros del DTO. |
| `src/motor_fea/api/servidor.py` (mod) | Endpoint `GET /diseno`. |
| `tests/test_servidor.py` (mod) | +1 test de `/diseno`. |
| `src/motor_fea/viz/static/app.js` (mod) | Estado `diseño` + color por cumple + tocar→etiqueta. |
| `README.md` (mod) | Mención del armado diseñado. |

---

## Task 1: Refactor de `armado.py` (extraer geometría de posiciones)

**Files:**
- Modify: `src/motor_fea/viz/armado.py`

- [ ] **Step 1: Refactorizar `_armado_columna` y `_armado_viga`**

En `src/motor_fea/viz/armado.py`, reemplazar `_armado_columna` por (extrae `_posiciones_columna`):
```python
def _posiciones_columna(b: float, h: float, rec: float, num: int, n: int) -> list[dict]:
    """Posiciones (x,y,d) de n barras #num distribuidas en el perímetro de la sección."""
    d = _diametro_m(num)
    ox, oy = max(0.0, b / 2 - rec - d / 2), max(0.0, h / 2 - rec - d / 2)
    return [{"x": x, "y": y, "d": d} for x, y in _perimetro(n, ox, oy)]


def _armado_columna(b: float, h: float, rec: float) -> tuple[list[dict], int, int, float]:
    as_req = 0.01 * (b * 1000.0) * (h * 1000.0)      # ρ_min = 1% (ACI 10.6.1.1), mm²
    num, n = _barra_columna(as_req)
    return _posiciones_columna(b, h, rec, num, n), num, n, _diametro_m(num)
```

Y reemplazar `_armado_viga` por (extrae `_posiciones_viga`):
```python
def _posiciones_viga(b: float, h: float, rec: float, num: int, n_inf: int) -> list[dict]:
    """Posiciones de n_inf barras #num inferiores + 2 superiores en la sección."""
    d = _diametro_m(num)
    ox = max(0.0, b / 2 - rec - d / 2)
    y_inf, y_sup = -(h / 2 - rec - d / 2), (h / 2 - rec - d / 2)
    long: list[dict] = []
    for k in range(n_inf):                            # fila inferior
        f = k / (n_inf - 1) if n_inf > 1 else 0.5    # n_inf siempre ≥2; el 0.5 es defensivo
        long.append({"x": -ox + 2 * ox * f, "y": y_inf, "d": d})
    long += [{"x": -ox, "y": y_sup, "d": d}, {"x": ox, "y": y_sup, "d": d}]   # 2 sup
    return long


def _armado_viga(b: float, h: float, rec: float, fc: float, fy: float) -> tuple[list[dict], int, float]:
    d_util = h - rec
    as_min = as_minimo_flexion(b * 1000.0, d_util * 1000.0, fc, fy)    # mm²
    n_inf = max(2, math.ceil(as_min / AREAS_BARRA_MM2[5]))
    return _posiciones_viga(b, h, rec, 5, n_inf), n_inf, _diametro_m(5)
```

- [ ] **Step 2: Correr los tests de armado (regresión) — comportamiento idéntico**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_armado.py -q` (use `.venv/bin/pytest` si falta python/pytest).
Expected: PASS (10 passed) — el refactor no cambia las posiciones, diámetros ni conteos.

- [ ] **Step 3: Commit**

```bash
git add src/motor_fea/viz/armado.py
git commit -m "refactor(viz): extraer _posiciones_columna/_posiciones_viga en armado

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `viz/diseno.py` — DisenoDTO por fuerzas (puro)

**Files:**
- Create: `src/motor_fea/viz/diseno.py`
- Test: `tests/test_diseno_visual.py`

- [ ] **Step 1: Escribir los tests que fallan** — crear `tests/test_diseno_visual.py`:

```python
"""Tests puros del armado diseñado por fuerzas para el visor."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz import diseno


def _portico():
    """Pórtico 4×4×3 con carga lateral (igual al modelo de ejemplo del servidor)."""
    m = ModeloEstructural()
    m.nodos += [
        Nodo(1, 0, 0, 0), Nodo(2, 4, 0, 0), Nodo(3, 4, 4, 0), Nodo(4, 0, 4, 0),
        Nodo(5, 0, 0, 3), Nodo(6, 4, 0, 3), Nodo(7, 4, 4, 3), Nodo(8, 0, 4, 3),
    ]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    eid = 1
    for i, j in [(1, 5), (2, 6), (3, 7), (4, 8), (5, 6), (6, 7), (7, 8), (8, 5)]:
        m.elementos.append(ElementoFrame(eid, i, j, 1, 1))
        eid += 1
    for n in (1, 2, 3, 4):
        m.apoyos.append(Apoyo.empotrado(n))
    for n in (5, 6, 7, 8):
        m.cargas.append(CargaNodal(n, fx=10000.0))
    return m


def test_un_diseno_por_elemento():
    dto = diseno.calcular_diseno(_portico())
    assert set(dto) == {"recubrimiento", "elementos"}
    assert len(dto["elementos"]) == 8


def test_columnas_y_vigas_con_armado_y_demanda():
    dto = diseno.calcular_diseno(_portico())
    cols = [e for e in dto["elementos"] if e["tipo"] == "columna"]
    vigas = [e for e in dto["elementos"] if e["tipo"] == "viga"]
    assert len(cols) == 4 and len(vigas) == 4
    for e in dto["elementos"]:
        assert len(e["long"]) >= 2
        assert set(e["demanda"]) == {"pu", "mu", "vu"}
        assert all(v >= 0 for v in e["demanda"].values())
        assert isinstance(e["cumple"], bool)
        assert e["designacion"]
    for c in cols:
        assert len(c["long"]) >= 4


def test_posiciones_dentro_de_la_seccion():
    dto = diseno.calcular_diseno(_portico())
    for e in dto["elementos"]:                  # sección 0.30×0.30 → |x|,|y| ≤ 0.15
        for bar in e["long"]:
            assert abs(bar["x"]) <= 0.15 + 1e-9
            assert abs(bar["y"]) <= 0.15 + 1e-9


def test_estribo_y_recubrimiento():
    dto = diseno.calcular_diseno(_portico())
    assert dto["recubrimiento"] == 0.04
    for e in dto["elementos"]:
        est = e["estribo"]
        assert est["d"] > 0 and est["s"] > 0 and est["w"] > 0 and est["h"] > 0


def test_seccion_insuficiente_marca_no_cumple():
    # columna 0.20×0.20 bajo axial enorme → demanda > capacidad incluso con ρ=8%.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.04, inercia_y=0.2 ** 4 / 12,
                               inercia_z=0.2 ** 4 / 12, constante_torsion=1e-4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-3.0e6), CargaNodal(2, fx=2.0e5)]
    dto = diseno.calcular_diseno(m)
    assert dto["elementos"][0]["cumple"] is False


def test_fc_invalido_lanza_valueerror():
    with pytest.raises(ValueError):
        diseno.calcular_diseno(_portico(), fc=0.0)
```

- [ ] **Step 2: Correr — esperar FAIL** (`ModuleNotFoundError: ... 'motor_fea.viz.diseno'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q`

- [ ] **Step 3: Implementar** — crear `src/motor_fea/viz/diseno.py`:

```python
"""Cálculo del armado DISEÑADO por fuerzas para el visor (capa frontera).

Por cada elemento: resuelve el modelo, extrae los esfuerzos, diseña el refuerzo
(``diseno_elemento``) y empaqueta el armado real + la demanda (Pu/Mu/Vu) + cumple,
reusando la derivación de posiciones de ``viz.armado``. Función pura: usa core
(solver), viz (escena/armado) y diseno_elemento (que envuelve aci318); no toca
HTTP ni three.js.

Unidades del DTO: metros (posiciones, estribo) y N/N·m (demanda), como la escena.
"""
from __future__ import annotations

from motor_fea import diseno_elemento
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.core.solver import EsfuerzosElemento, esfuerzos_elementos, resolver
from motor_fea.viz import armado
from motor_fea.viz.escena import _clasificar, _dimensiones


def _demanda(esf: EsfuerzosElemento) -> dict:
    """Demanda del elemento: pu=|axial|, mu=max|My|,|Mz|, vu=max|Vy|,|Vz| (N, N·m, N)."""
    mu = vu = 0.0
    for _s, _n, vy, vz, _t, my, mz in esf.diagrama(21):
        mu = max(mu, abs(my), abs(mz))
        vu = max(vu, abs(vy), abs(vz))
    return {"pu": abs(esf.axial), "mu": mu, "vu": vu}


def calcular_diseno(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """DisenoDTO: armado diseñado por fuerzas + demanda + cumple por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    esfuerzos = esfuerzos_elementos(modelo, resolver(modelo))
    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = armado._diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        esf = esfuerzos[e.id]
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna(esf, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            s = max(0.05, min(16 * armado._diametro_m(d.numero_barra), 48 * d_est, min(b, h)))
            tipo, designacion, cumple = "columna", d.disponer, d.cumple
        else:
            d = diseno_elemento.disenar_viga(esf, b, h, fc, fy, recubrimiento)
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            s = d.estribo.espaciamiento / 1000.0      # mm → m
            tipo, designacion, cumple = "viga", d.disponer, d.cumple
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion, "demanda": _demanda(esf), "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
```

- [ ] **Step 4: Correr — esperar PASS** (6 passed):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q`

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/diseno.py tests/test_diseno_visual.py
git commit -m "feat(viz): DisenoDTO (armado disenado por fuerzas + demanda + cumple)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Endpoint `GET /diseno`

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir el test que falla** — añadir al final de `tests/test_servidor.py`:

```python
def test_diseno_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/diseno")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"recubrimiento", "elementos"}
    assert len(data["elementos"]) == 8
    e0 = data["elementos"][0]
    assert "demanda" in e0 and "cumple" in e0 and "long" in e0
```

- [ ] **Step 2: Correr — esperar FAIL** (HTTP 404):
`PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_diseno_ok -q`

- [ ] **Step 3: Añadir el import** — en `src/motor_fea/api/servidor.py`, después de `from motor_fea.viz.armado import calcular_armado`, añadir:
```python
from motor_fea.viz.diseno import calcular_diseno
```

- [ ] **Step 4: Registrar el endpoint** — en `crear_app`, después del endpoint `armado()` y antes de `app.mount("/", ...)`, añadir:
```python
    @app.get("/diseno")
    def diseno():
        try:
            return calcular_diseno(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 5: Correr los tests del servidor — esperar PASS**:
`PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): endpoint GET /diseno (armado disenado por fuerzas)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Estado `diseño` en el visor (color por cumple + tocar→etiqueta)

> Sin unit test (smoke manual). Produce el `app.js` completo y una validación estática.
> `index.html` no cambia. La jaula se factoriza en `construirJaula(dto, matLongFn)` que comparten
> el estado `refuerzo` (ejemplo) y el `diseño` (real, coloreado por cumple).

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Reescribir `app.js`** — reemplazar el contenido completo de `src/motor_fea/viz/static/app.js` por:

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

const rig = new THREE.Group();
rig.add(camera);
scene.add(rig);

const controls = new OrbitControls(camera, renderer.domElement);

const MAT = {
  columna: new THREE.MeshStandardMaterial({ color: 0x4a90d9 }),
  viga:    new THREE.MeshStandardMaterial({ color: 0xd98a4a }),
};
const MAT_LONG = new THREE.MeshStandardMaterial({ color: 0xc0392b });   // armado de ejemplo
const MAT_EST = new THREE.LineBasicMaterial({ color: 0x2ecc71 });       // estribo
const MAT_OK = new THREE.MeshStandardMaterial({ color: 0x9aa0a6 });     // diseño: cumple (acero gris)
const MAT_FALLA = new THREE.MeshStandardMaterial({ color: 0xff3b30 });  // diseño: NO cumple (rojo)

// --- Estado ---
const basePos = {};
const barras = [];           // { mesh, i, j, id }
let resultados = null;
let frameBbox = null;

let losa = null;
let losaMesh = null;
let losaActiva = false;
let campoLosa = 'deflexion';

let armado = null;
let armadoGroup = null;
let refuerzoActivo = false;

let diseno = null;
let disenoGroup = null;
let disenoActivo = false;

let estado = 'sin-deformar';
let exag = 0;
let playing = true;
let tAcum = 0;
let lastT = null;
const T_DISPLAY = 2.0;

const selEstado = document.getElementById('estado');
const exagInput = document.getElementById('exag');
const btnPlay = document.getElementById('play');
const info = document.getElementById('info');

// --- Barras ---
function addBarra(b) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, 1), MAT[b.tipo] || MAT.viga);
  scene.add(mesh);
  barras.push({ mesh, i: b.i, j: b.j, id: b.id });
}

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
    bar.mesh.lookAt(vj);
    bar.mesh.scale.z = L === 0 ? 1e-6 : L;
  }
}

function encuadrar(min, max) {
  const mn = new THREE.Vector3(min[0], min[1], min[2]);
  const mx = new THREE.Vector3(max[0], max[1], max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
}

// --- Losa ---
function valorLosa(campoNombre, i, j) {
  return losa.campos[campoNombre].valores[`${i},${j}`];
}

function colorDeCampo(nombre, v, min, max) {
  if (nombre === 'deflexion') {
    const t = max > min ? (v - min) / (max - min) : 0;
    return new THREE.Color().setHSL((1 - t) * 240 / 360, 1, 0.5);
  }
  const M = Math.max(Math.abs(min), Math.abs(max)) || 1;
  const s = v / M;
  const destino = s < 0 ? new THREE.Color(0x2222ff) : new THREE.Color(0xff2222);
  return new THREE.Color(1, 1, 1).lerp(destino, Math.min(1, Math.abs(s)));
}

function construirLosa() {
  const { a, b, nx, ny } = losa;
  const nvx = (nx + 1) * (ny + 1);
  const pos = new Float32Array(nvx * 3);
  const col = new Float32Array(nvx * 3);
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      pos[n * 3] = i * a / nx;
      pos[n * 3 + 1] = j * b / ny;
      pos[n * 3 + 2] = 0;
    }
  }
  const idx = [];
  for (let cj = 0; cj < ny; cj++) {
    for (let ci = 0; ci < nx; ci++) {
      const n00 = cj * (nx + 1) + ci, n10 = cj * (nx + 1) + ci + 1;
      const n11 = (cj + 1) * (nx + 1) + ci + 1, n01 = (cj + 1) * (nx + 1) + ci;
      idx.push(n00, n10, n11, n00, n11, n01);
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  geo.setIndex(idx);
  const mat = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide });
  losaMesh = new THREE.Mesh(geo, mat);
  losaMesh.visible = false;
  scene.add(losaMesh);
}

function colorearLosa(nombre) {
  const campo = losa.campos[nombre];
  const col = losaMesh.geometry.getAttribute('color');
  const { nx, ny } = losa;
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      const c = colorDeCampo(nombre, valorLosa(nombre, i, j), campo.min, campo.max);
      col.setXYZ(n, c.r, c.g, c.b);
    }
  }
  col.needsUpdate = true;
}

function actualizarLosa() {
  const pos = losaMesh.geometry.getAttribute('position');
  const { nx, ny } = losa;
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      const w_m = valorLosa('deflexion', i, j) / 1000;
      pos.setZ(n, -w_m * exag);
    }
  }
  pos.needsUpdate = true;
}

// --- Jaula de armado (ejemplo o diseñada) ---
// Group de Groups por elemento, sin scale.z (los cilindros llevan su largo L).
// matLongFn(el) elige el material de las barras (ej.: por cumple en el diseño).
function construirJaula(dto, matLongFn) {
  const grupo = new THREE.Group();
  grupo.visible = false;
  for (const el of dto.elementos) {
    const vi = basePos[el.i], vj = basePos[el.j];
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    if (L === 0) continue;
    const g = new THREE.Group();
    g.position.copy(vi).lerp(vj, 0.5);
    g.lookAt(vj);
    const matLong = matLongFn(el);
    for (const bar of el.long) {
      const geo = new THREE.CylinderGeometry(bar.d / 2, bar.d / 2, L, 8);
      geo.rotateX(Math.PI / 2);
      const cil = new THREE.Mesh(geo, matLong);
      cil.position.set(bar.x, bar.y, 0);
      g.add(cil);
    }
    const { w, h, s } = el.estribo;
    const pts = [
      new THREE.Vector3(-w / 2, -h / 2, 0), new THREE.Vector3(w / 2, -h / 2, 0),
      new THREE.Vector3(w / 2, h / 2, 0), new THREE.Vector3(-w / 2, h / 2, 0),
    ];
    const loopGeo = new THREE.BufferGeometry().setFromPoints(pts);
    const nTramos = Math.max(2, Math.floor(L / s));
    for (let k = 0; k <= nTramos; k++) {
      const loop = new THREE.LineLoop(loopGeo, MAT_EST);
      loop.position.z = -L / 2 + k * (L / nTramos);
      g.add(loop);
    }
    grupo.add(g);
  }
  scene.add(grupo);
  return grupo;
}

function fantasma(on) {
  for (const m of [MAT.columna, MAT.viga]) {
    m.transparent = on;
    m.opacity = on ? 0.25 : 1.0;
    m.depthWrite = !on;
  }
}

// --- Panel ---
function fsDe(est) {
  if (!resultados) return 1;
  if (est === 'deformada') return resultados.deformada.factor_sugerido;
  if (est.startsWith('modo-')) {
    const m = resultados.modos[parseInt(est.slice(5), 10) - 1];
    return m ? m.factor_sugerido : 1;
  }
  return 1;
}

function resetOverlays() {
  losaActiva = false;
  refuerzoActivo = false;
  disenoActivo = false;
  if (losaMesh) losaMesh.visible = false;
  if (armadoGroup) armadoGroup.visible = false;
  if (disenoGroup) disenoGroup.visible = false;
  fantasma(false);
  for (const bar of barras) bar.mesh.visible = true;
}

function entrarLosa(est) {
  campoLosa = est.slice(5);
  losaActiva = true;
  for (const bar of barras) bar.mesh.visible = false;
  losaMesh.visible = true;
  colorearLosa(campoLosa);
  const fs = losa.factor_sugerido;
  exagInput.min = 0; exagInput.max = fs * 5; exagInput.step = fs / 100;
  exagInput.value = fs; exag = fs;
  const campo = losa.campos[campoLosa];
  const et = { deflexion: 'deflexión', momento_mx: 'momento Mx', momento_my: 'momento My' }[campoLosa];
  info.textContent = `${et}: ${campo.min.toFixed(1)} … ${campo.max.toFixed(1)} ${campo.unidad}`;
  encuadrar([0, 0, 0], [losa.a, losa.b, 0]);
}

function entrarRefuerzo() {
  refuerzoActivo = true;
  fantasma(true);
  if (armadoGroup) armadoGroup.visible = true;
  exagInput.min = 0; exagInput.max = 1; exagInput.step = 1;
  exagInput.value = 0; exag = 0;
  info.textContent = armado
    ? `armado de ejemplo (ρ≈1% col · As_mín viga) — ${armado.elementos.length} elementos`
    : '';
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function entrarDiseno() {
  disenoActivo = true;
  fantasma(true);
  if (disenoGroup) disenoGroup.visible = true;
  exagInput.min = 0; exagInput.max = 1; exagInput.step = 1;
  exagInput.value = 0; exag = 0;
  const n = diseno.elementos.length;
  const ok = diseno.elementos.filter((el) => el.cumple).length;
  info.textContent = `diseño por fuerzas — ${ok}/${n} cumplen`;
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function setEstado(nuevo) {
  const veniaEspecial = losaActiva || refuerzoActivo || disenoActivo;
  estado = nuevo;
  resetOverlays();
  if (nuevo.startsWith('losa-')) { entrarLosa(nuevo); return; }
  if (nuevo === 'refuerzo') { entrarRefuerzo(); return; }
  if (nuevo === 'diseno') { entrarDiseno(); return; }
  if (veniaEspecial && frameBbox) encuadrar(frameBbox.min, frameBbox.max);
  const fs = fsDe(estado);
  exagInput.min = 0; exagInput.max = fs * 5; exagInput.step = fs / 100;
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
  btnPlay.setAttribute('aria-label', playing ? 'pausar' : 'reanudar');
});

// --- Picking: tocar la losa (valor) o un elemento en diseño (etiqueta) ---
const punteroRay = new THREE.Raycaster();
const ndc = new THREE.Vector2();
renderer.domElement.addEventListener('pointerdown', (ev) => {
  if (renderer.xr.isPresenting) return;
  ndc.x = (ev.clientX / innerWidth) * 2 - 1;
  ndc.y = -(ev.clientY / innerHeight) * 2 + 1;
  punteroRay.setFromCamera(ndc, camera);
  if (losaActiva && losaMesh) {
    const hits = punteroRay.intersectObject(losaMesh);
    if (hits.length) mostrarValorEnPunto(hits[0].point.x, hits[0].point.y);
  } else if (disenoActivo && diseno) {
    const hits = punteroRay.intersectObjects(barras.map((b) => b.mesh));
    if (!hits.length) return;
    const bar = barras.find((b) => b.mesh === hits[0].object);
    const el = bar && diseno.elementos.find((e) => e.id === bar.id);
    if (el) mostrarDiseno(el);
  }
});

function mostrarValorEnPunto(x, y) {
  const { a, b, nx, ny } = losa;
  const lx = a / nx, ly = b / ny;
  const ci = Math.max(0, Math.min(nx - 1, Math.floor(x / lx)));
  const cj = Math.max(0, Math.min(ny - 1, Math.floor(y / ly)));
  const fx = Math.max(0, Math.min(1, (x - ci * lx) / lx));
  const fy = Math.max(0, Math.min(1, (y - cj * ly) / ly));
  const V = (i, j) => valorLosa(campoLosa, i, j);
  const v = (1 - fx) * (1 - fy) * V(ci, cj) + fx * (1 - fy) * V(ci + 1, cj)
          + fx * fy * V(ci + 1, cj + 1) + (1 - fx) * fy * V(ci, cj + 1);
  const et = { deflexion: 'w', momento_mx: 'Mx', momento_my: 'My' }[campoLosa];
  info.textContent = `${et} = ${v.toFixed(2)} ${losa.campos[campoLosa].unidad} @ (${x.toFixed(1)}, ${y.toFixed(1)}) m`;
}

function mostrarDiseno(el) {
  const kN = (n) => (n / 1000).toFixed(0);
  const dem = el.tipo === 'columna'
    ? `Pu=${kN(el.demanda.pu)} kN, Mu=${kN(el.demanda.mu)} kN·m`
    : `Mu=${kN(el.demanda.mu)} kN·m, Vu=${kN(el.demanda.vu)} kN`;
  info.textContent = `${el.designacion} · ${dem} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
}

// --- Carga ---
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
  frameBbox = data.bbox;
  encuadrar(data.bbox.min, data.bbox.max);
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);

  await cargarResultados();
  await cargarLosa();
  await cargarArmado();
  await cargarDiseno();
}

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

async function cargarLosa() {
  try {
    const r = await fetch('./losa');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    losa = await r.json();
  } catch (e) {
    return;
  }
  construirLosa();
  selEstado.add(new Option('losa: deflexión', 'losa-deflexion'));
  selEstado.add(new Option('losa: momento Mx', 'losa-momento_mx'));
  selEstado.add(new Option('losa: momento My', 'losa-momento_my'));
}

async function cargarArmado() {
  try {
    const r = await fetch('./armado');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    armado = await r.json();
  } catch (e) {
    return;
  }
  armadoGroup = construirJaula(armado, () => MAT_LONG);
  selEstado.add(new Option('refuerzo: armado', 'refuerzo'));
}

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

// --- WebXR ---
if (navigator.xr && navigator.xr.isSessionSupported) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}

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

  if (losaActiva) actualizarLosa();
  else actualizarBarras(fase);

  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();
```

- [ ] **Step 2: Validación estática**
1. `cp src/motor_fea/viz/static/app.js /tmp/app5.mjs && node --check /tmp/app5.mjs` → exit 0.
2. ids (`msg`, `estado`, `exag`, `play`, `info`) en `index.html`: `grep -o 'id="[^"]*"' src/motor_fea/viz/static/index.html` (los 5 presentes).

- [ ] **Step 3: Smoke manual** — `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`; en `http://127.0.0.1:8000/`: el selector tiene `diseño: armado`; al elegirlo la jaula se ve coloreada (gris=cumple, rojo=falla) sobre el hormigón fantasma; tocar un elemento muestra su designación + demanda en `#info`. Volver a `refuerzo` muestra el armado de ejemplo (rojo). Ctrl-C.

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): estado diseno (jaula real coloreada por cumple + tocar->etiqueta)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Documentación y verificación final

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Documentar el armado diseñado** — localizar el párrafo del armado 3D (Fase 4, que menciona `GET /armado`) y añadir justo después:

```markdown
Y `GET /diseno`: el estado **diseño: armado** muestra el armado **diseñado por las fuerzas
reales** del análisis (reusa `esfuerzos_elementos` + el diseño ACI por elemento), coloreado
por si cumple (gris) o no (rojo) la demanda. Tocar un elemento muestra su designación y su
demanda (columna: Pu/Mu; viga: Mu/Vu). A diferencia de `refuerzo` (armado de ejemplo), acá el
acero sale del cálculo por carga.
```

- [ ] **Step 2: Correr la suite completa** — `PYTHONPATH=src:tests python -m pytest -q`
Expected: PASS, ~176 tests (169 + 6 de diseño + 1 de servidor). Reportar el conteo exacto; si algo falla, STOP/BLOCKED.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(viz): documentar el armado disenado por fuerzas del visor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §8)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~176); `test_armado.py` sin regresión.
2. `GET /diseno` devuelve el armado diseñado + demanda + cumple por elemento.
3. En el visor: el estado `diseño` muestra la jaula coloreada por cumple y tocar un elemento muestra su designación + demanda.

## Notas de revisión (plan vs. spec)

- **Refactor seguro:** `_posiciones_columna`/`_posiciones_viga` preservan el comportamiento; `test_armado.py` lo verifica (Task 1 Step 2).
- **Reuso, no duplicación:** `diseno.py` reusa la geometría de `armado` y el diseño de `diseno_elemento`; solo agrega demanda + cumple. En el visor, `construirJaula(dto, matLongFn)` la comparten `refuerzo` y `diseño`.
- **Picking por elemento:** las cajas llevan `id`; el raycast en estado `diseño` mapea el hit al elemento del DTO. `resetOverlays` apaga losa/armado/diseño/fantasma en cada cambio de estado.
- **Demanda en kN/kN·m** en la etiqueta (el DTO viaja en N/N·m).
