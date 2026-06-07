# Visor estructural WebXR (Fase 4: refuerzo 3D en secciones) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dibujar el armado 3D (barras longitudinales + estribos) dentro de cada sección de columna/viga, con el hormigón semi-transparente.

**Architecture:** Una unidad pura nueva `viz/armado.py` que, por elemento, deriva un armado de ejemplo (ρ≈1% columna / As_mín viga) reusando `escena` (b,h,tipo) y `aci318` (tabla de barras + As_mín); un endpoint `GET /armado`; y el visor (`app.js`) con un estado `refuerzo` que fantasmea el hormigón y muestra la jaula. `core/` y `normativa/` no se tocan.

**Tech Stack:** Python 3.11 + stdlib (`armado.py`), FastAPI (endpoint), three.js vendorizado (`CylinderGeometry` + `LineLoop`).

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-visor-webxr-fase4-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/viz/armado.py` (nuevo, **puro**) | `calcular_armado(modelo, fc, fy, recubrimiento) → ArmadoDTO`: longitudinales + estribo por elemento. |
| `tests/test_armado.py` (nuevo) | Tests puros del DTO. |
| `src/motor_fea/api/servidor.py` (mod) | Endpoint `GET /armado`. |
| `tests/test_servidor.py` (mod) | +1 test de `/armado`. |
| `src/motor_fea/viz/static/app.js` (mod) | Estado `refuerzo` + jaula 3D + hormigón fantasma. |
| `README.md` (mod) | Mención del armado 3D. |

**Contrato `ArmadoDTO`:**
```jsonc
{
  "recubrimiento": 0.04,
  "elementos": [
    { "id": 1, "i": 1, "j": 5, "tipo": "columna",
      "long": [ {"x": 0.10, "y": 0.10, "d": 0.019} ],
      "estribo": { "d": 0.0095, "s": 0.30, "w": 0.22, "h": 0.22 },
      "designacion": "4#6 + E#3@0.30" }
  ]
}
```

---

## Task 1: Cálculo del armado (frontera pura)

**Files:**
- Create: `src/motor_fea/viz/armado.py`
- Test: `tests/test_armado.py`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/test_armado.py` con el contenido completo:

```python
"""Tests puros del armado de ejemplo para el visor."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz import armado


def _portico_min():
    """1 columna (vertical) + 1 viga (horizontal), sección 0.30×0.30."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3), Nodo(3, 4, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    m.elementos += [ElementoFrame(1, 1, 2, 1, 1),   # columna (Δz domina)
                    ElementoFrame(2, 2, 3, 1, 1)]   # viga (horizontal)
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def _columna_grande():
    """1 columna 0.50×0.50."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.25, inercia_y=0.5 ** 4 / 12,
                               inercia_z=0.5 ** 4 / 12, constante_torsion=1e-3))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def test_un_armado_por_elemento():
    dto = armado.calcular_armado(_portico_min())
    assert set(dto) == {"recubrimiento", "elementos"}
    assert len(dto["elementos"]) == 2


def test_columna_y_viga_clasificadas_con_barras():
    dto = armado.calcular_armado(_portico_min())
    col = next(e for e in dto["elementos"] if e["tipo"] == "columna")
    viga = next(e for e in dto["elementos"] if e["tipo"] == "viga")
    assert len(col["long"]) >= 4
    ys = [bar["y"] for bar in viga["long"]]
    assert any(y > 0 for y in ys) and any(y < 0 for y in ys)   # barras sup + inf


def test_posiciones_dentro_de_la_seccion():
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:                  # sección 0.30×0.30 → |x|,|y| ≤ 0.15
        for bar in e["long"]:
            assert abs(bar["x"]) <= 0.15 + 1e-9
            assert abs(bar["y"]) <= 0.15 + 1e-9


def test_estribo_positivo():
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:
        est = e["estribo"]
        assert est["d"] > 0 and est["s"] >= 0.05 and est["w"] > 0 and est["h"] > 0


def test_diametros_de_la_tabla():
    from motor_fea.normativa.aci318 import AREAS_BARRA_MM2
    validos = {round(num * 25.4 / 8 / 1000, 6) for num in AREAS_BARRA_MM2}
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:
        for bar in e["long"]:
            assert round(bar["d"], 6) in validos


def test_seccion_mayor_tiene_mas_o_igual_barras():
    chica = armado.calcular_armado(_portico_min())
    col_chica = next(e for e in chica["elementos"] if e["tipo"] == "columna")
    col_grande = armado.calcular_armado(_columna_grande())["elementos"][0]
    assert len(col_grande["long"]) >= len(col_chica["long"])


def test_fc_invalido_lanza_valueerror():
    with pytest.raises(ValueError):
        armado.calcular_armado(_portico_min(), fc=0.0)
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_armado.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.viz.armado'`. (Use `.venv/bin/pytest` si falta python/pytest del sistema.)

- [ ] **Step 3: Implementar `armado.py`**

Crear `src/motor_fea/viz/armado.py` con el contenido completo:

```python
"""Cálculo del armado de ejemplo para el visor (capa frontera).

Por cada elemento del modelo, deriva un armado representativo (barras
longitudinales + estribo) para dibujarlo en 3D dentro de la sección. El motor
Python no calcula esfuerzos por elemento, así que es un *armado de ejemplo*:
la cantidad de acero sale de reglas ACI mínimas (ρ≈1% en columnas, As mínimo a
flexión en vigas) y la barra de la tabla AREAS_BARRA_MM2; las posiciones se
derivan de la geometría de la sección. Función pura: usa solo `viz.escena`
(geometría b,h y clasificación) y `normativa.aci318` (tabla + As mínimo).

Unidades del DTO: metros (posiciones, diámetros, separaciones), como la escena.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import ModeloEstructural
from motor_fea.normativa.aci318 import AREAS_BARRA_MM2, as_minimo_flexion
from motor_fea.viz.escena import _clasificar, _dimensiones


def _diametro_m(num: int) -> float:
    """Diámetro de una barra #num (octavos de pulgada) en metros."""
    return num * 25.4 / 8.0 / 1000.0


def _perimetro(n: int, ox: float, oy: float) -> list[tuple[float, float]]:
    """n puntos equiespaciados por longitud de arco en el perímetro del rectángulo
    de semi-ejes (ox, oy), empezando en la esquina (-ox, -oy) (antihorario)."""
    esquinas = [(-ox, -oy), (ox, -oy), (ox, oy), (-ox, oy)]
    lados = [2 * ox, 2 * oy, 2 * ox, 2 * oy]
    per = 2 * (2 * ox + 2 * oy)
    if per <= 0:
        return [(0.0, 0.0)] * n
    puntos: list[tuple[float, float]] = []
    for k in range(n):
        s = k * per / n
        for lado in range(4):
            if s <= lados[lado] or lado == 3:
                x0, y0 = esquinas[lado]
                x1, y1 = esquinas[(lado + 1) % 4]
                f = s / lados[lado] if lados[lado] > 0 else 0.0
                puntos.append((x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
                break
            s -= lados[lado]
    return puntos


def _barra_columna(as_req: float) -> tuple[int, int]:
    """(num, n) para As requerido: n = max(4, ceil(As/area)) a múltiplo de 4; si n>12 sube a #8."""
    for num in (6, 8):
        n = max(4, math.ceil(as_req / AREAS_BARRA_MM2[num]))
        n = ((n + 3) // 4) * 4
        if n <= 12 or num == 8:
            return num, n
    return 8, 12


def _armado_columna(b: float, h: float, rec: float) -> tuple[list[dict], int, int, float]:
    as_req = 0.01 * (b * 1000.0) * (h * 1000.0)      # ρ_min = 1% (ACI 10.6.1.1), mm²
    num, n = _barra_columna(as_req)
    d = _diametro_m(num)
    ox, oy = b / 2 - rec - d / 2, h / 2 - rec - d / 2
    long = [{"x": x, "y": y, "d": d} for x, y in _perimetro(n, ox, oy)]
    return long, num, n, d


def _armado_viga(b: float, h: float, rec: float, fc: float, fy: float) -> tuple[list[dict], int, float]:
    d_util = h - rec
    as_min = as_minimo_flexion(b * 1000.0, d_util * 1000.0, fc, fy)    # mm²
    d = _diametro_m(5)
    n_inf = max(2, math.ceil(as_min / AREAS_BARRA_MM2[5]))
    ox = b / 2 - rec - d / 2
    y_inf, y_sup = -(h / 2 - rec - d / 2), (h / 2 - rec - d / 2)
    long: list[dict] = []
    for k in range(n_inf):                            # fila inferior
        f = k / (n_inf - 1) if n_inf > 1 else 0.5
        long.append({"x": -ox + 2 * ox * f, "y": y_inf, "d": d})
    long += [{"x": -ox, "y": y_sup, "d": d}, {"x": ox, "y": y_sup, "d": d}]   # 2 sup
    return long, n_inf, d


def calcular_armado(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """ArmadoDTO: armado de ejemplo (longitudinal + estribo) por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = _diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        if _clasificar(ni, nj) == "columna":
            long, num, n, d_long = _armado_columna(b, h, recubrimiento)
            s = max(0.05, min(16 * d_long, 48 * d_est, min(b, h)))
            tipo, desig = "columna", f"{n}#{num} + E#3@{s:.2f}"
        else:
            long, n_inf, d_long = _armado_viga(b, h, recubrimiento, fc, fy)
            s = max(0.05, (h - recubrimiento) / 2)
            tipo, desig = "viga", f"{n_inf}#5 inf + 2#5 sup + E#3@{s:.2f}"
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": desig,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_armado.py -q`
Expected: PASS (7 passed).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/armado.py tests/test_armado.py
git commit -m "feat(viz): armado de ejemplo 3D por elemento (long + estribo) reusando aci318

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Endpoint `GET /armado`

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir el test que falla**

Añadir al final de `tests/test_servidor.py`:

```python
def test_armado_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/armado")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"recubrimiento", "elementos"}
    assert len(data["elementos"]) == 8          # 4 columnas + 4 vigas
    e0 = data["elementos"][0]
    assert "long" in e0 and "estribo" in e0
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_armado_ok -q`
Expected: FAIL con HTTP 404 (la ruta `/armado` aún no existe).

- [ ] **Step 3: Añadir el import de `calcular_armado`**

En `src/motor_fea/api/servidor.py`, después de la línea `from motor_fea.viz.resultados_losa import calcular_resultados_losa`, añadir:
```python
from motor_fea.viz.armado import calcular_armado
```

- [ ] **Step 4: Registrar el endpoint `/armado`**

En `crear_app`, después del endpoint `losa()` y antes de `app.mount("/", ...)`, añadir:
```python
    @app.get("/armado")
    def armado():
        try:
            return calcular_armado(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 5: Correr los tests del servidor para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`
Expected: PASS (todos, incluido `test_armado_ok`).

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): endpoint GET /armado (armado de ejemplo por elemento)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Estado `refuerzo` y jaula 3D en el visor

> Sin unit test (decisión del spec §9): smoke manual. Los pasos producen el `app.js`
> completo y una validación estática. `index.html` **no cambia**.

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Reescribir `app.js` con el estado de refuerzo**

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
const MAT_LONG = new THREE.MeshStandardMaterial({ color: 0xc0392b });   // acero longitudinal
const MAT_EST = new THREE.LineBasicMaterial({ color: 0x2ecc71 });       // estribo

// --- Estado del modelo y de la animación ---
const basePos = {};          // id -> THREE.Vector3 (posición sin deformar)
const barras = [];           // { mesh, i, j } con caja unitaria en Z (escalable)
let resultados = null;       // DTO de /resultados (deformada + modos)
let frameBbox = null;        // bbox del pórtico (de /escena) para reencuadrar

let losa = null;             // DTO de /losa
let losaMesh = null;
let losaActiva = false;
let campoLosa = 'deflexion';

let armado = null;           // DTO de /armado
let armadoGroup = null;      // jaula de acero (Group de Groups por elemento)
let refuerzoActivo = false;

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

// --- Barras como cajas unitarias reposicionables cada frame ---
function addBarra(b) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, 1), MAT[b.tipo] || MAT.viga);
  scene.add(mesh);
  barras.push({ mesh, i: b.i, j: b.j });
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

// --- Encuadre de cámara reutilizable ---
function encuadrar(min, max) {
  const mn = new THREE.Vector3(min[0], min[1], min[2]);
  const mx = new THREE.Vector3(max[0], max[1], max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
}

// --- Losa: malla, color por campo y relieve ---
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

// --- Armado (refuerzo) ---
function construirArmado() {
  armadoGroup = new THREE.Group();
  armadoGroup.visible = false;
  for (const el of armado.elementos) {
    const vi = basePos[el.i], vj = basePos[el.j];
    if (!vi || !vj) continue;
    const L = vi.distanceTo(vj);
    if (L === 0) continue;
    const g = new THREE.Group();
    g.position.copy(vi).lerp(vj, 0.5);
    g.lookAt(vj);                                   // +Z hacia j (como la caja)
    for (const bar of el.long) {                    // barras longitudinales
      const geo = new THREE.CylinderGeometry(bar.d / 2, bar.d / 2, L, 8);
      geo.rotateX(Math.PI / 2);                     // eje Y → Z local
      const cil = new THREE.Mesh(geo, MAT_LONG);
      cil.position.set(bar.x, bar.y, 0);
      g.add(cil);
    }
    const { w, h, s } = el.estribo;                 // estribos como aros
    const pts = [
      new THREE.Vector3(-w / 2, -h / 2, 0), new THREE.Vector3(w / 2, -h / 2, 0),
      new THREE.Vector3(w / 2, h / 2, 0), new THREE.Vector3(-w / 2, h / 2, 0),
    ];
    const loopGeo = new THREE.BufferGeometry().setFromPoints(pts);
    const nHoops = Math.max(2, Math.floor(L / s));
    for (let k = 0; k <= nHoops; k++) {
      const loop = new THREE.LineLoop(loopGeo, MAT_EST);
      loop.position.z = -L / 2 + k * (L / nHoops);
      g.add(loop);
    }
    armadoGroup.add(g);
  }
  scene.add(armadoGroup);
}

function fantasma(on) {
  for (const m of [MAT.columna, MAT.viga]) {
    m.transparent = on;
    m.opacity = on ? 0.25 : 1.0;
    m.depthWrite = !on;
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

function resetOverlays() {
  losaActiva = false;
  refuerzoActivo = false;
  if (losaMesh) losaMesh.visible = false;
  if (armadoGroup) armadoGroup.visible = false;
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
  fantasma(true);                                   // hormigón translúcido
  if (armadoGroup) armadoGroup.visible = true;
  exagInput.min = 0; exagInput.max = 1; exagInput.step = 1;
  exagInput.value = 0; exag = 0;                    // armado estático (sin deformar)
  info.textContent = armado
    ? `armado de ejemplo (ρ≈1% col · As_mín viga) — ${armado.elementos.length} elementos`
    : '';
  if (frameBbox) encuadrar(frameBbox.min, frameBbox.max);
}

function setEstado(nuevo) {
  const veniaEspecial = losaActiva || refuerzoActivo;
  estado = nuevo;
  resetOverlays();
  if (nuevo.startsWith('losa-')) { entrarLosa(nuevo); return; }
  if (nuevo === 'refuerzo') { entrarRefuerzo(); return; }
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

// --- Tocar la losa → valor interpolado ---
const punteroRay = new THREE.Raycaster();
const ndc = new THREE.Vector2();
renderer.domElement.addEventListener('pointerdown', (ev) => {
  if (!losaActiva || !losaMesh || renderer.xr.isPresenting) return;
  ndc.x = (ev.clientX / innerWidth) * 2 - 1;
  ndc.y = -(ev.clientY / innerHeight) * 2 + 1;
  punteroRay.setFromCamera(ndc, camera);
  const hits = punteroRay.intersectObject(losaMesh);
  if (!hits.length) return;
  mostrarValorEnPunto(hits[0].point.x, hits[0].point.y);
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
  frameBbox = data.bbox;
  encuadrar(data.bbox.min, data.bbox.max);
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);

  await cargarResultados();
  await cargarLosa();
  await cargarArmado();
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
    return;   // sin armado: no se agrega el estado de refuerzo
  }
  construirArmado();
  selEstado.add(new Option('refuerzo: armado', 'refuerzo'));
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

  if (losaActiva) actualizarLosa();
  else actualizarBarras(fase);

  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();
```

- [ ] **Step 2: Validación estática**

1. Sintaxis JS: `cp src/motor_fea/viz/static/app.js /tmp/app4.mjs && node --check /tmp/app4.mjs` → exit 0.
2. Cross-check de ids (`msg`, `estado`, `exag`, `play`, `info`) en `index.html` (no se modificó): `grep -o 'id="[^"]*"' src/motor_fea/viz/static/index.html`. Confirmar los 5.

- [ ] **Step 3: Smoke manual del visor**

Run: `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`
En `http://127.0.0.1:8000/`:
- El selector incluye, además de los de Fases 2/3, **`refuerzo: armado`**.
- Al elegirlo: las secciones de hormigón se ven translúcidas y aparece la jaula (cilindros longitudinales rojizos + aros de estribo verdes) adentro.
- Volver a un estado de pórtico restaura el hormigón opaco; volver a `losa` muestra la losa sin la jaula.

Detener (Ctrl-C).

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): estado refuerzo con jaula 3D (long + estribos) y hormigon fantasma

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Documentación y verificación final

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Documentar el armado 3D en el README**

Localizar el párrafo del heatmap de losas (el que añadió Fase 3, que menciona `GET /losa`) y añadir, justo después, este texto:

```markdown
Y `GET /armado`: el estado **refuerzo: armado** muestra, dentro de cada sección,
el armado de ejemplo en 3D — barras longitudinales (cilindros) y estribos (aros)
con el hormigón semi-transparente. La cantidad de acero sale de reglas ACI mínimas
(ρ≈1% en columnas, As mínimo a flexión en vigas) reusando `aci318`; el motor Python
no calcula esfuerzos por elemento, así que es un armado representativo, no un diseño
por carga.
```

- [ ] **Step 2: Correr la suite completa**

Run: `PYTHONPATH=src:tests python -m pytest -q`
Expected: PASS, ~141 tests (134 de Fase 3 + 7 de armado + 1 de servidor).

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(viz): documentar el armado 3D (refuerzo) del visor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §9)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~141 tests).
2. `GET /armado` devuelve un armado por elemento con `long` (≥4 en columnas) + `estribo`.
3. En el visor: el estado `refuerzo` muestra el hormigón translúcido con la jaula adentro.

## Notas de revisión (plan vs. spec)

- **100% frontera:** `armado.py` reusa `escena._dimensiones`/`_clasificar` (mismo paquete) y
  `aci318` (tabla + As_mín). No se toca `core/` ni `normativa/`.
- **Sin estiramiento de cilindros:** la jaula es un `Group` por elemento orientado con
  `lookAt(vj)` pero **sin `scale.z`**; los cilindros llevan su largo `L` en la geometría.
- **Armado estático:** la vista de refuerzo es de detallado (geometría sin deformar); no
  oscila ni sigue la deformada — decisión del spec §7.
- **Convivencia:** `resetOverlays()` centraliza el apagado de losa/armado/fantasma al
  cambiar de estado, evitando que la jaula quede visible al pasar a un estado de losa.
