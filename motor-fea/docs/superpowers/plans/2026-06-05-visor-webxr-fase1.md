# Visor estructural WebXR (Fase 1) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Servir el modelo estructural como una escena 3D navegable en VR y móvil vía WebXR, con `motor-fea --serve`.

**Architecture:** Tres unidades en la capa de frontera (no tocan `core/` ni `normativa/`): un exportador puro `viz/escena.py` (modelo → SceneDTO), un servidor FastAPI delgado `api/servidor.py` (`GET /escena` + estáticos), y un visor `viz/static/` en three.js vanilla (CDN, sin build) con degradación órbita↔VR.

**Tech Stack:** Python 3.11 + stdlib (exportador), FastAPI + uvicorn (extra `api`, ya declarado), three.js 0.160 vía import-map/CDN, WebXR.

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-visor-webxr-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/viz/__init__.py` | Marca el paquete `viz`. |
| `src/motor_fea/viz/escena.py` | **Puro.** `exportar_escena(modelo) -> dict` (SceneDTO): clasifica columna/viga, deriva b/h, bbox. |
| `src/motor_fea/api/servidor.py` | FastAPI: `crear_app(modelo)`, `cargar_modelo(ruta)`, `modelo_ejemplo()`, `servir(...)`. |
| `src/motor_fea/viz/static/index.html` | Página del visor + import-map de three.js. |
| `src/motor_fea/viz/static/app.js` | Escena three.js, fetch `/escena`, OrbitControls + VR/teletransporte. |
| `tests/test_escena.py` | Tests puros del exportador. |
| `tests/test_servidor.py` | Tests del endpoint (skip si falta FastAPI/httpx). |
| `src/motor_fea/api/cli.py` (mod) | Flag `--serve [modelo] --host --port`. |
| `pyproject.toml` (mod) | `package-data` para `viz/static/*`. |
| `README.md` (mod) | Documentar `--serve`. |

---

## Task 1: Exportador de escena (puro)

**Files:**
- Create: `src/motor_fea/viz/__init__.py`
- Create: `src/motor_fea/viz/escena.py`
- Test: `tests/test_escena.py`

- [ ] **Step 1: Crear el paquete `viz`**

Create `src/motor_fea/viz/__init__.py` con una sola línea:

```python
"""Capa de visualización: exportación de geometría y visor WebXR (frontera)."""
```

- [ ] **Step 2: Escribir los tests que fallan**

Create `tests/test_escena.py`:

```python
"""Tests del exportador de escena (puro, stdlib)."""
import math

import pytest

import modelos_ref
from motor_fea.core.modelo import (
    Apoyo, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz.escena import B_VIS, H_VIS, exportar_escena


def test_voladizo_una_barra_viga():
    dto = exportar_escena(modelos_ref.voladizo())
    assert dto["unidades"] == "m"
    assert len(dto["nodos"]) == 2
    assert len(dto["barras"]) == 1
    barra = dto["barras"][0]
    # El voladizo va a lo largo de X (horizontal) -> viga.
    assert barra["tipo"] == "viga"
    # Sección 0.30x0.30: A=0.09, Iz=0.30^4/12 -> b=h=0.30.
    assert barra["b"] == pytest.approx(0.30, abs=1e-6)
    assert barra["h"] == pytest.approx(0.30, abs=1e-6)


def test_bbox_del_voladizo():
    dto = exportar_escena(modelos_ref.voladizo())
    assert dto["bbox"]["min"] == [0.0, 0.0, 0.0]
    assert dto["bbox"]["max"] == [3.0, 0.0, 0.0]


def test_barra_vertical_es_columna():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.1e-3))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    barra = exportar_escena(m)["barras"][0]
    assert barra["tipo"] == "columna"


def test_seccion_degenerada_usa_grosor_por_defecto():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 1.0, 0, 0)]
    m.materiales.append(Material(1, E=2.0e10))
    # Iz=0 -> no físico -> grosor visual por defecto.
    m.secciones.append(Seccion(1, area=0.01, inercia_y=0.0,
                               inercia_z=0.0, constante_torsion=0.0))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    barra = exportar_escena(m)["barras"][0]
    assert barra["b"] == B_VIS
    assert barra["h"] == H_VIS


def test_modelo_invalido_lanza_valueerror():
    m = ModeloEstructural()
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))  # refs inexistentes
    with pytest.raises(ValueError):
        exportar_escena(m)
```

- [ ] **Step 3: Correr los tests para verlos fallar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_escena.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.viz.escena'`

- [ ] **Step 4: Implementar `escena.py`**

Create `src/motor_fea/viz/escena.py`:

```python
"""Exportador de geometría del modelo a un SceneDTO render-agnóstico (capa frontera).

Función pura (stdlib, sin NumPy ni web): un :class:`ModeloEstructural` se traduce
a un dict JSON-able que cualquier visor 3D consume. No conoce three.js ni HTTP.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import ModeloEstructural

B_VIS = 0.20          # grosor visual por defecto (m) para secciones no rectangulares
H_VIS = 0.20
RELACION_MAX = 50.0   # tope b:h aceptable antes de caer al grosor por defecto


def _clasificar(ni, nj) -> str:
    """columna si la componente vertical (Δz) domina; si no, viga."""
    dx, dy, dz = abs(nj.x - ni.x), abs(nj.y - ni.y), abs(nj.z - ni.z)
    return "columna" if dz > dx and dz > dy else "viga"


def _dimensiones(sec) -> tuple[float, float]:
    """Deriva (b, h) de una sección rectangular desde A e Iz; si no es físico, default."""
    a, iz = sec.area, sec.inercia_z
    if a <= 0.0 or iz <= 0.0:
        return B_VIS, H_VIS
    h = math.sqrt(12.0 * iz / a)
    b = a / h
    if b <= 0.0 or h <= 0.0 or max(b, h) / min(b, h) > RELACION_MAX:
        return B_VIS, H_VIS
    return b, h


def exportar_escena(modelo: ModeloEstructural) -> dict:
    """Traduce el modelo a un SceneDTO (dict). Lanza ValueError si el modelo es inválido."""
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}

    barras = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        barras.append({"id": e.id, "i": e.nodo_i, "j": e.nodo_j,
                       "tipo": _clasificar(ni, nj), "b": b, "h": h})

    if modelo.nodos:
        xs = [n.x for n in modelo.nodos]
        ys = [n.y for n in modelo.nodos]
        zs = [n.z for n in modelo.nodos]
        bbox = {"min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)]}
    else:
        bbox = {"min": [0.0, 0.0, 0.0], "max": [0.0, 0.0, 0.0]}

    return {
        "unidades": "m",
        "bbox": bbox,
        "nodos": [{"id": n.id, "p": [n.x, n.y, n.z]} for n in modelo.nodos],
        "barras": barras,
        "losas": [],
    }
```

- [ ] **Step 5: Correr los tests para verlos pasar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_escena.py -q`
Expected: PASS (5 passed)

- [ ] **Step 6: Correr la suite completa (regresión)**

Run: `PYTHONPATH=src .venv/bin/python -m pytest -q`
Expected: PASS (113 passed — 108 previos + 5 nuevos)

- [ ] **Step 7: Commit**

```bash
git add src/motor_fea/viz/__init__.py src/motor_fea/viz/escena.py tests/test_escena.py
git commit -m "feat(viz): exportador puro de geometria a SceneDTO"
```

---

## Task 2: Servidor FastAPI

**Files:**
- Create: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir los tests que fallan**

Create `tests/test_servidor.py`:

```python
"""Tests del servidor del visor. Se saltan si el extra `api` no está instalado."""
import pytest

pytest.importorskip("fastapi")
pytest.importorskip("httpx")  # requerido por fastapi.testclient

from fastapi.testclient import TestClient

from motor_fea.api.servidor import crear_app, modelo_ejemplo
from motor_fea.core.modelo import ElementoFrame, ModeloEstructural


def test_escena_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/escena")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"unidades", "bbox", "nodos", "barras", "losas"}
    assert len(data["barras"]) == 8           # 4 columnas + 4 vigas
    tipos = {b["tipo"] for b in data["barras"]}
    assert tipos == {"columna", "viga"}


def test_escena_modelo_invalido_da_400():
    m = ModeloEstructural()
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))  # refs inexistentes
    cli = TestClient(crear_app(m))
    r = cli.get("/escena")
    assert r.status_code == 400


def test_index_se_sirve():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/")
    assert r.status_code == 200
    assert "text/html" in r.headers["content-type"]
```

- [ ] **Step 2: Correr los tests para verlos fallar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_servidor.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.api.servidor'` (o SKIPPED si falta el extra `api`; instálalo con `.venv/bin/pip install -e '.[api]' httpx`)

- [ ] **Step 3: Implementar `servidor.py`**

Create `src/motor_fea/api/servidor.py`:

```python
"""Servidor FastAPI del visor WebXR (capa frontera). Requiere el extra `api`.

Expone GET /escena (SceneDTO) y sirve los estáticos del visor. El análisis y la
exportación viven en otras capas; este módulo es I/O delgado.
"""
from __future__ import annotations

import json
from pathlib import Path

from fastapi import FastAPI, HTTPException
from fastapi.staticfiles import StaticFiles

from motor_fea.api.contrato import modelo_desde_dict
from motor_fea.core.modelo import (
    Apoyo, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz.escena import exportar_escena

_STATIC = Path(__file__).resolve().parent.parent / "viz" / "static"


def modelo_ejemplo() -> ModeloEstructural:
    """Pórtico de un vano (4 columnas + 4 vigas de techo, 4×4 m en planta, 3 m de alto)."""
    m = ModeloEstructural()
    m.nodos += [
        Nodo(1, 0, 0, 0), Nodo(2, 4, 0, 0), Nodo(3, 4, 4, 0), Nodo(4, 0, 4, 0),
        Nodo(5, 0, 0, 3), Nodo(6, 4, 0, 3), Nodo(7, 4, 4, 3), Nodo(8, 0, 4, 3),
    ]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    columnas = [(1, 5), (2, 6), (3, 7), (4, 8)]
    vigas = [(5, 6), (6, 7), (7, 8), (8, 5)]
    eid = 1
    for i, j in columnas + vigas:
        m.elementos.append(ElementoFrame(eid, i, j, 1, 1))
        eid += 1
    for n in (1, 2, 3, 4):
        m.apoyos.append(Apoyo.empotrado(n))
    return m


def cargar_modelo(ruta: str | None) -> ModeloEstructural:
    """Carga el modelo desde un JSON (esquema de contrato.py) o devuelve el de ejemplo."""
    if not ruta:
        return modelo_ejemplo()
    with open(ruta, encoding="utf-8") as f:
        return modelo_desde_dict(json.load(f))


def crear_app(modelo: ModeloEstructural) -> FastAPI:
    """Construye la app FastAPI que sirve `modelo` como escena 3D."""
    app = FastAPI(title="motor-fea · visor estructural")

    @app.get("/escena")
    def escena():
        try:
            return exportar_escena(modelo)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))

    # Montar al final: las rutas de API registradas arriba tienen prioridad.
    app.mount("/", StaticFiles(directory=str(_STATIC), html=True), name="static")
    return app


def servir(ruta: str | None = None, host: str = "127.0.0.1", port: int = 8000) -> None:
    """Levanta uvicorn sirviendo el visor. Bloqueante."""
    import uvicorn

    uvicorn.run(crear_app(cargar_modelo(ruta)), host=host, port=port)
```

- [ ] **Step 4: Crear un placeholder de estáticos para que el montaje no falle**

`StaticFiles(directory=...)` exige que el directorio exista. Lo creamos vacío ahora; Task 4 lo llena. Run:

```bash
mkdir -p src/motor_fea/viz/static
printf '<!doctype html><title>placeholder</title>' > src/motor_fea/viz/static/index.html
```

- [ ] **Step 5: Correr los tests para verlos pasar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_servidor.py -q`
Expected: PASS (3 passed). Si sale SKIPPED, instala el extra: `.venv/bin/pip install -e '.[api]' httpx` y reintenta.

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py src/motor_fea/viz/static/index.html
git commit -m "feat(viz): servidor FastAPI con GET /escena y modelo de ejemplo"
```

---

## Task 3: Flag `--serve` en el CLI

**Files:**
- Modify: `src/motor_fea/api/cli.py`

- [ ] **Step 1: Escribir el test de parseo (flag nuevo)**

Append a `tests/test_servidor.py`:

```python
def test_cli_serve_invoca_servir(monkeypatch):
    import motor_fea.api.servidor as srv
    llamado = {}

    def fake_servir(ruta=None, host="127.0.0.1", port=8000):
        llamado["args"] = (ruta, host, port)

    monkeypatch.setattr(srv, "servir", fake_servir)
    from motor_fea.api.cli import main
    rc = main(["--serve", "--port", "9001"])
    assert rc == 0
    assert llamado["args"] == (None, "127.0.0.1", 9001)
```

- [ ] **Step 2: Correr el test para verlo fallar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_servidor.py::test_cli_serve_invoca_servir -q`
Expected: FAIL (`--serve` no reconocido / `SystemExit: 2`)

- [ ] **Step 3: Modificar `cli.py`**

En `src/motor_fea/api/cli.py`, dentro de `main`, añadir los argumentos tras la línea de `--disenar-losa`:

```python
    parser.add_argument("--serve", nargs="?", const="", metavar="MODELO.json",
                        help="Levanta el visor 3D WebXR (requiere el extra api). "
                             "Sin MODELO.json sirve un pórtico de ejemplo.")
    parser.add_argument("--host", default="127.0.0.1", help="Host del servidor (--serve).")
    parser.add_argument("--port", type=int, default=8000, help="Puerto del servidor (--serve).")
```

Y antes del `parser.print_help()` final, añadir el handler:

```python
    if args.serve is not None:
        try:
            from motor_fea.api.servidor import servir
        except ImportError:
            print("error: el visor requiere FastAPI. Instala: pip install -e '.[api]'",
                  file=sys.stderr)
            return 1
        servir(args.serve or None, args.host, args.port)
        return 0
```

- [ ] **Step 4: Correr el test para verlo pasar**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_servidor.py::test_cli_serve_invoca_servir -q`
Expected: PASS

- [ ] **Step 5: Verificar que `--version` y la suite siguen bien**

Run: `PYTHONPATH=src .venv/bin/python -m pytest -q`
Expected: PASS (117 passed)

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/cli.py tests/test_servidor.py
git commit -m "feat(cli): flag --serve para levantar el visor WebXR"
```

---

## Task 4: Visor WebXR (estáticos)

**Files:**
- Modify/replace: `src/motor_fea/viz/static/index.html`
- Create: `src/motor_fea/viz/static/app.js`

No hay test unitario (three.js en navegador); la verificación es smoke manual (Step 4).

- [ ] **Step 1: Escribir `index.html` (reemplaza el placeholder)**

Replace `src/motor_fea/viz/static/index.html` con:

```html
<!DOCTYPE html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no">
  <title>Visor estructural — motor-fea</title>
  <style>
    html, body { margin: 0; height: 100%; overflow: hidden; background: #101418;
                 font-family: system-ui, sans-serif; }
    #msg { position: fixed; top: 8px; left: 8px; padding: 6px 10px;
           background: rgba(0,0,0,.6); color: #fff; border-radius: 6px; font-size: 14px; }
  </style>
  <script type="importmap">
  {
    "imports": {
      "three": "https://unpkg.com/three@0.160.0/build/three.module.js",
      "three/addons/": "https://unpkg.com/three@0.160.0/examples/jsm/"
    }
  }
  </script>
</head>
<body>
  <div id="msg">Cargando…</div>
  <script type="module" src="./app.js"></script>
</body>
</html>
```

- [ ] **Step 2: Escribir `app.js` (completo)**

Create `src/motor_fea/viz/static/app.js`:

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

function addBarra(b, nodos) {
  const pi = nodos[b.i], pj = nodos[b.j];
  if (!pi || !pj) return;
  const vi = new THREE.Vector3(pi[0], pi[1], pi[2]);
  const vj = new THREE.Vector3(pj[0], pj[1], pj[2]);
  const L = vi.distanceTo(vj);
  if (L === 0) return;
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(b.b, b.h, L), MAT[b.tipo] || MAT.viga);
  mesh.position.copy(vi).lerp(vj, 0.5);
  mesh.lookAt(vj);   // orienta el lado +Z (largo L) hacia el nodo j
  scene.add(mesh);
}

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
  const nodos = {};
  for (const n of data.nodos) nodos[n.id] = n.p;
  for (const b of data.barras) addBarra(b, nodos);

  // Auto-encuadre con bbox.
  const mn = new THREE.Vector3(data.bbox.min[0], data.bbox.min[1], data.bbox.min[2]);
  const mx = new THREE.Vector3(data.bbox.max[0], data.bbox.max[1], data.bbox.max[2]);
  const centro = mn.clone().add(mx).multiplyScalar(0.5);
  const radio = Math.max(mn.distanceTo(mx) / 2, 1);
  controls.target.copy(centro);
  camera.position.copy(centro).add(new THREE.Vector3(radio * 1.6, radio * 1.2, radio * 1.6));
  controls.update();
  setMsg(`${data.barras.length} barras · ${data.nodos.length} nodos`);
}

// --- WebXR: botón solo si hay soporte ---
if (navigator.xr && navigator.xr.isSessionSupported) {
  navigator.xr.isSessionSupported('immersive-vr').then((ok) => {
    if (ok) document.body.appendChild(VRButton.createButton(renderer));
  });
}

// --- Teletransporte en VR ---
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

renderer.setAnimationLoop(() => {
  if (renderer.xr.isPresenting) actualizarTeletransporte();
  else controls.update();
  renderer.render(scene, camera);
});

cargar();
```

- [ ] **Step 3: Verificación automática mínima (los estáticos se sirven)**

Run: `PYTHONPATH=src .venv/bin/python -m pytest tests/test_servidor.py::test_index_se_sirve -q`
Expected: PASS (el index real ya no es el placeholder)

- [ ] **Step 4: Smoke manual (documentar resultado en el commit)**

```bash
PYTHONPATH=src .venv/bin/python -m motor_fea.api.cli --serve --host 0.0.0.0 --port 8000
```

Verificar:
1. En la PC: abrir `http://127.0.0.1:8000/` → se ve el pórtico de ejemplo (4 columnas azules + 4 vigas naranjas); orbitar con el ratón funciona; el overlay dice "8 barras · 8 nodos".
2. En el celular (misma red): abrir `http://<IP-de-la-PC>:8000/` → se ve y se orbita con el dedo.
3. En el Quest (misma red): abrir la URL en el navegador → aparece "ENTER VR" → entrar → apuntar con el control + gatillo muestra la marca verde → soltar teletransporta. `Ctrl-C` para parar el server.

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/static/index.html src/motor_fea/viz/static/app.js
git commit -m "feat(viz): visor WebXR three.js (orbita movil + VR teletransporte)"
```

---

## Task 5: Empaquetado y documentación

**Files:**
- Modify: `pyproject.toml`
- Modify: `README.md`

- [ ] **Step 1: Incluir los estáticos como package-data**

En `pyproject.toml`, tras el bloque `[tool.setuptools.packages.find]`, añadir:

```toml
[tool.setuptools.package-data]
"motor_fea.viz" = ["static/*.html", "static/*.js"]
```

- [ ] **Step 2: Documentar `--serve` en el README**

En `README.md`, dentro de la sección **Frontera (`api`)**, tras la línea de `--analyze`, añadir:

```markdown
- `motor-fea --serve [modelo.json]` → visor 3D WebXR (VR + móvil) en
  `http://<host>:8000/`. Requiere el extra `api` (`pip install -e ".[api]"`).
  Sin `modelo.json` sirve un pórtico de ejemplo. Usa `--host 0.0.0.0` para
  acceder desde el celular/Quest en la misma red.
```

- [ ] **Step 3: Verificar el empaquetado**

Run: `.venv/bin/pip install -e . && .venv/bin/python -c "import importlib.resources as r; print((r.files('motor_fea.viz') / 'static' / 'app.js').is_file())"`
Expected: `True`

- [ ] **Step 4: Suite final**

Run: `PYTHONPATH=src .venv/bin/python -m pytest -q`
Expected: PASS (117 passed, con el extra `api` instalado; o algunos SKIPPED sin él)

- [ ] **Step 5: Commit**

```bash
git add pyproject.toml README.md
git commit -m "build(viz): empaquetar estaticos del visor + documentar --serve"
```

---

## Notas de ejecución

- **Conteo de tests:** 108 base → +5 (escena) → +3 (servidor) → +1 (CLI) = **117**. Los 4 de servidor/CLI requieren el extra `api` + `httpx`; sin ellos quedan SKIPPED y la suite base sigue verde (regla "stdlib pura" del README).
- **Orden de rutas en FastAPI:** `/escena` se registra antes del `app.mount("/", StaticFiles…)`; por eso no lo tapa el montaje estático.
- **Red local:** `--host 0.0.0.0` expone el server a la LAN (necesario para Quest/celular). En redes no confiables, limitar con firewall.
```
