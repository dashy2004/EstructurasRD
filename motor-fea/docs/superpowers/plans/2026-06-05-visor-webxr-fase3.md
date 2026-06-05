# Visor estructural WebXR (Fase 3: heatmaps en losas + tocar→valor) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar una losa en el visor como superficie coloreada por deflexión o momento (Mx/My), abombada según la deformada, con tocar→valor.

**Architecture:** Un cambio aditivo en `core/losa_fem.py` (campo nodal de momentos), una unidad pura nueva `viz/resultados_losa.py` que empaqueta un `LosaDTO`, un endpoint `GET /losa`, y el visor (`app.js`) con estados de losa + malla coloreada + picking. `normativa/` y `diseno_losa.py` no se tocan.

**Tech Stack:** Python 3.11 + stdlib (`losa_fem`, `resultados_losa`), FastAPI (endpoint), three.js vendorizado (`BufferGeometry` + vertex colors + `Raycaster`).

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-visor-webxr-fase3-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/core/losa_fem.py` (mod, **aditivo**) | `ResultadoLosa` gana `momentos_nodales: {(i,j)→(mx,my)}`, promediando el momento de centro de los elementos adyacentes. |
| `src/motor_fea/viz/resultados_losa.py` (nuevo, **puro**) | `calcular_resultados_losa(...) → LosaDTO`: malla + campos por nodo (deflexión mm, Mx/My kN·m/m), unidades, min/max, `factor_sugerido`. |
| `tests/test_resultados_losa.py` (nuevo) | Tests puros del DTO. |
| `src/motor_fea/api/servidor.py` (mod) | Endpoint `GET /losa`. |
| `tests/test_losa_fem.py` (mod) | +1 test de `momentos_nodales`. |
| `tests/test_servidor.py` (mod) | +1 test de `/losa`. |
| `src/motor_fea/viz/static/app.js` (mod) | Estados de losa + superficie/heatmap + relieve + picking. |
| `README.md` (mod) | Mención del heatmap de losas. |

**Contrato `LosaDTO`** (lo que `calcular_resultados_losa` devuelve y `/losa` sirve):

```jsonc
{
  "a": 5.0, "b": 5.0, "nx": 8, "ny": 8,
  "factor_sugerido": 180.0,
  "campos": {
    "deflexion":  { "unidad": "mm",     "min": 0.0,  "max": 12.3, "valores": { "4,4": 12.3 } },
    "momento_mx": { "unidad": "kN·m/m", "min": -3.1, "max": 8.7,  "valores": { "4,4": 8.7 } },
    "momento_my": { "unidad": "kN·m/m", "min": -3.1, "max": 8.7,  "valores": { "4,4": 8.7 } }
  }
}
```

---

## Task 1: Campo nodal de momentos en el FEM de losas (core, aditivo)

**Files:**
- Modify: `src/motor_fea/core/losa_fem.py`
- Test: `tests/test_losa_fem.py`

- [ ] **Step 1: Escribir el test que falla**

Añadir al final de `tests/test_losa_fem.py` (el archivo usa asserts simples, **sin** `import pytest`; mantener ese estilo y las constantes de módulo `A, E, NU, T, Q`):

```python
def test_momentos_nodales_cubre_la_malla_y_es_simetrico_en_el_centro():
    r = resolver_losa_rectangular(A, A, 4, 4, E, NU, T, Q, "simple")
    # cubre los (nx+1)*(ny+1) = 25 nodos de la grilla
    assert len(r.momentos_nodales) == 25
    # nodo central (2,2): por simetría de la losa cuadrada, mx ≈ my
    mx_c, my_c = r.momentos_nodales[(2, 2)]
    assert abs(mx_c - my_c) < 1e-6 * (abs(mx_c) + abs(my_c))
    # momento interior no nulo
    assert abs(mx_c) > 0.0
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_losa_fem.py::test_momentos_nodales_cubre_la_malla_y_es_simetrico_en_el_centro -q`
Expected: FAIL con `AttributeError: 'ResultadoLosa' object has no attribute 'momentos_nodales'`.

- [ ] **Step 3: Añadir el campo `momentos_nodales` a `ResultadoLosa`**

En `src/motor_fea/core/losa_fem.py`, cambiar el import de dataclasses (línea 15) de:
```python
from dataclasses import dataclass
```
a:
```python
from dataclasses import dataclass, field
```

Y en la dataclass `ResultadoLosa`, después de la línea `m_apoyo_max: float = 0.0 # …`, añadir:
```python
    momentos_nodales: dict[tuple[int, int], tuple[float, float]] = field(default_factory=dict)
```

- [ ] **Step 4: Poblar `momentos_nodales` en el loop existente y devolverlo**

En `resolver_losa_rectangular`, reemplazar el bloque que calcula los momentos máximos (el comentario `# Recuperar momentos en el centro de cada elemento y quedarse con los máximos.` y su doble loop) por esta versión que, además del máximo, acumula el momento de centro en los 4 nodos de cada celda:

```python
    # Recuperar momentos en el centro de cada elemento: máximos (vano) y, de paso,
    # acumularlos en los nodos de cada celda para el campo nodal (heatmap, Fase 3).
    mx_max = my_max = mxy_max = 0.0
    suma_m: dict[tuple[int, int], list[float]] = {}   # (i,j) → [Σmx, Σmy, n_adyacentes]
    for cj in range(ny):
        for ci in range(nx):
            nodos = [_idx(ci, cj, nx), _idx(ci + 1, cj, nx),
                     _idx(ci + 1, cj + 1, nx), _idx(ci, cj + 1, nx)]
            d_elem = [u[nd * 3 + d] for nd in nodos for d in range(3)]
            mx, my, mxy = momentos_elemento(lx, ly, E, nu, t, d_elem, 0.5, 0.5)
            mx_max = max(mx_max, abs(mx))
            my_max = max(my_max, abs(my))
            mxy_max = max(mxy_max, abs(mxy))
            for ij in ((ci, cj), (ci + 1, cj), (ci + 1, cj + 1), (ci, cj + 1)):
                acc = suma_m.setdefault(ij, [0.0, 0.0, 0])
                acc[0] += mx
                acc[1] += my
                acc[2] += 1
    momentos_nodales = {ij: (s[0] / s[2], s[1] / s[2]) for ij, s in suma_m.items()}
```

Y reemplazar la línea `return` final por:
```python
    return ResultadoLosa(nx, ny, desplazamientos, w_central, mx_max, my_max, mxy_max,
                         m_apoyo_max, momentos_nodales)
```

- [ ] **Step 5: Correr la suite del FEM de losas para verificar que pasa (y nada se rompió)**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_losa_fem.py -q`
Expected: PASS — el test nuevo y todos los existentes (los máximos y `desplazamientos_w` no cambian).

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/core/losa_fem.py tests/test_losa_fem.py
git commit -m "feat(losa): campo nodal de momentos (momentos_nodales) en ResultadoLosa

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Empaquetado del LosaDTO (frontera pura)

**Files:**
- Create: `src/motor_fea/viz/resultados_losa.py`
- Test: `tests/test_resultados_losa.py`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/test_resultados_losa.py` con el contenido completo:

```python
"""Tests puros del empaquetado de resultados de losa para el visor."""
import math

import pytest

from motor_fea.viz import resultados_losa


def _dto():
    return resultados_losa.calcular_resultados_losa(a=4.0, b=4.0, nx=4, ny=4)


def test_campos_y_unidades():
    dto = _dto()
    assert set(dto["campos"]) == {"deflexion", "momento_mx", "momento_my"}
    assert dto["campos"]["deflexion"]["unidad"] == "mm"
    assert dto["campos"]["momento_mx"]["unidad"] == "kN·m/m"
    assert dto["campos"]["momento_my"]["unidad"] == "kN·m/m"


def test_un_valor_por_nodo():
    dto = _dto()
    n = (4 + 1) * (4 + 1)
    for c in dto["campos"].values():
        assert len(c["valores"]) == n


def test_deflexion_central_positiva():
    dto = _dto()
    assert dto["campos"]["deflexion"]["valores"]["2,2"] > 0.0


def test_min_max_finitos_y_factor_sugerido_positivo():
    dto = _dto()
    for c in dto["campos"].values():
        assert math.isfinite(c["min"]) and math.isfinite(c["max"])
        assert c["min"] <= c["max"]
    assert dto["factor_sugerido"] > 0.0 and math.isfinite(dto["factor_sugerido"])


def test_momento_interior_no_nulo():
    dto = _dto()
    assert dto["campos"]["momento_mx"]["valores"]["2,2"] != 0.0
    assert dto["campos"]["momento_my"]["valores"]["2,2"] != 0.0


def test_parametros_invalidos_lanzan_valueerror():
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(nx=0)
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(t=0.0)
    with pytest.raises(ValueError):
        resultados_losa.calcular_resultados_losa(borde="otro")
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_resultados_losa.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.viz.resultados_losa'`.

- [ ] **Step 3: Implementar `resultados_losa.py`**

Crear `src/motor_fea/viz/resultados_losa.py` con el contenido completo:

```python
"""Cálculo de resultados de losa para el visor (capa frontera).

Empaqueta el FEM de losas (``core.losa_fem``) en un LosaDTO render-agnóstico:
malla rectangular + campos escalares por nodo (deflexión, momentos Mx y My) con
unidades, min/max y un factor de exageración sugerido. Función pura: solo usa
``core``; no toca HTTP ni three.js, así que se prueba con asserts normales.

Unidades de presentación: deflexión en mm; momentos en kN·m/m.
"""
from __future__ import annotations

import math

from motor_fea.core.losa_fem import resolver_losa_rectangular


def calcular_resultados_losa(a: float = 5.0, b: float = 5.0, nx: int = 8, ny: int = 8,
                             E: float = 2.0e10, nu: float = 0.2, t: float = 0.2,
                             q: float = 10000.0, borde: str = "simple") -> dict:
    """LosaDTO: malla + campos por nodo (deflexión, Mx, My). ValueError si los parámetros son inválidos."""
    if a <= 0 or b <= 0 or t <= 0 or q <= 0:
        raise ValueError("a, b, t y q deben ser positivos.")
    if nx < 1 or ny < 1:
        raise ValueError("nx y ny deben ser ≥ 1.")
    if borde not in ("simple", "empotrado"):
        raise ValueError(f"borde desconocido: {borde!r} (use 'simple' o 'empotrado').")

    res = resolver_losa_rectangular(a, b, nx, ny, E, nu, t, q, borde)

    deflexion: dict[str, float] = {}
    momento_mx: dict[str, float] = {}
    momento_my: dict[str, float] = {}
    max_w = 0.0
    for i in range(nx + 1):
        for j in range(ny + 1):
            w = res.desplazamientos_w[(i, j)]
            mx, my = res.momentos_nodales[(i, j)]
            clave = f"{i},{j}"
            deflexion[clave] = w * 1000.0          # m → mm
            momento_mx[clave] = mx / 1000.0        # N·m/m → kN·m/m
            momento_my[clave] = my / 1000.0
            max_w = max(max_w, abs(w))

    diag = math.sqrt(a * a + b * b)
    factor_sugerido = 0.08 * diag / max_w if max_w > 0.0 else 1.0

    def campo(valores: dict[str, float], unidad: str) -> dict:
        vs = list(valores.values())
        return {"unidad": unidad, "min": min(vs), "max": max(vs), "valores": valores}

    return {
        "a": a, "b": b, "nx": nx, "ny": ny,
        "factor_sugerido": factor_sugerido,
        "campos": {
            "deflexion": campo(deflexion, "mm"),
            "momento_mx": campo(momento_mx, "kN·m/m"),
            "momento_my": campo(momento_my, "kN·m/m"),
        },
    }
```

- [ ] **Step 4: Correr los tests para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_resultados_losa.py -q`
Expected: PASS (6 passed).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/resultados_losa.py tests/test_resultados_losa.py
git commit -m "feat(viz): LosaDTO (malla + campos deflexion/Mx/My) reusando losa_fem

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Endpoint `GET /losa`

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir el test que falla**

Añadir al final de `tests/test_servidor.py`:

```python
def test_losa_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/losa")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"a", "b", "nx", "ny", "factor_sugerido", "campos"}
    assert set(data["campos"]) == {"deflexion", "momento_mx", "momento_my"}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_losa_ok -q`
Expected: FAIL con HTTP 404 (la ruta `/losa` aún no existe; la sirve `StaticFiles` y no la encuentra).

- [ ] **Step 3: Añadir el import de `calcular_resultados_losa`**

En `src/motor_fea/api/servidor.py`, después de la línea `from motor_fea.viz.resultados import calcular_resultados`, añadir:
```python
from motor_fea.viz.resultados_losa import calcular_resultados_losa
```

- [ ] **Step 4: Registrar el endpoint `/losa`**

En `crear_app`, después del endpoint `resultados()` y antes de `app.mount("/", ...)`, añadir:
```python
    @app.get("/losa")
    def losa():
        try:
            return calcular_resultados_losa()
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 5: Correr los tests del servidor para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`
Expected: PASS (todos, incluido `test_losa_ok`).

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): endpoint GET /losa (malla + campos de la losa de ejemplo)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Estados de losa, heatmap y picking en el visor

> Sin unit test (decisión del spec §9): smoke manual. Los pasos producen el `app.js`
> completo y una validación estática; la verificación visual es con `motor-fea --serve`.
> `index.html` **no cambia** (la leyenda y el valor tocado reusan `#info`).

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Reescribir `app.js` con soporte de losa (heatmap + relieve + picking)**

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
let frameBbox = null;        // bbox del pórtico (de /escena) para reencuadrar

let losa = null;             // DTO de /losa
let losaMesh = null;         // superficie de la losa (BufferGeometry coloreada)
let losaActiva = false;      // ¿hay un estado de losa seleccionado?
let campoLosa = 'deflexion'; // campo activo: deflexion | momento_mx | momento_my

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

// --- Losa: construcción de la malla, color por campo y relieve ---
function valorLosa(campoNombre, i, j) {
  return losa.campos[campoNombre].valores[`${i},${j}`];
}

function colorDeCampo(nombre, v, min, max) {
  if (nombre === 'deflexion') {                       // secuencial azul→rojo
    const t = max > min ? (v - min) / (max - min) : 0;
    return new THREE.Color().setHSL((1 - t) * 240 / 360, 1, 0.5);
  }
  const M = Math.max(Math.abs(min), Math.abs(max)) || 1;   // divergente centrado en 0
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
  // Heatmap sin iluminación (MeshBasic): el color leído es el dato, no la sombra.
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

function actualizarLosa() {                            // relieve z = −w·exag (cada frame)
  const pos = losaMesh.geometry.getAttribute('position');
  const { nx, ny } = losa;
  for (let j = 0; j <= ny; j++) {
    for (let i = 0; i <= nx; i++) {
      const n = j * (nx + 1) + i;
      const w_m = valorLosa('deflexion', i, j) / 1000;   // mm → m
      pos.setZ(n, -w_m * exag);
    }
  }
  pos.needsUpdate = true;
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

function entrarLosa(est) {
  campoLosa = est.slice(5);                            // 'deflexion' | 'momento_mx' | 'momento_my'
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

function setEstado(nuevo) {
  if (nuevo.startsWith('losa-')) { estado = nuevo; entrarLosa(nuevo); return; }
  const veniaDeLosa = losaActiva;
  estado = nuevo;
  losaActiva = false;
  if (losaMesh) losaMesh.visible = false;
  for (const bar of barras) bar.mesh.visible = true;
  if (veniaDeLosa && frameBbox) encuadrar(frameBbox.min, frameBbox.max);
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

// --- Tocar la losa → valor interpolado del campo activo ---
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
}

// --- Carga de resultados de pórtico (/resultados) ---
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

// --- Carga de la losa (/losa) ---
async function cargarLosa() {
  try {
    const r = await fetch('./losa');
    if (!r.ok) throw new Error('HTTP ' + r.status);
    losa = await r.json();
  } catch (e) {
    return;   // sin losa: simplemente no se agregan los estados de losa
  }
  construirLosa();
  selEstado.add(new Option('losa: deflexión', 'losa-deflexion'));
  selEstado.add(new Option('losa: momento Mx', 'losa-momento_mx'));
  selEstado.add(new Option('losa: momento My', 'losa-momento_my'));
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

1. Sintaxis JS: `cp src/motor_fea/viz/static/app.js /tmp/app3.mjs && node --check /tmp/app3.mjs` → exit 0 (valida solo sintaxis; los imports de three los resuelve el importmap del navegador).
2. Cross-check de ids usados con `getElementById` (`msg`, `estado`, `exag`, `play`, `info`): todos existen en `index.html` (no se modificó). Confirmar con `grep -o 'id="[^"]*"' src/motor_fea/viz/static/index.html`.

- [ ] **Step 3: Smoke manual del visor**

Run: `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`
Abrir `http://127.0.0.1:8000/` y verificar:
- El selector ahora incluye, además de los de Fase 2, `losa: deflexión`, `losa: momento Mx`, `losa: momento My`.
- Al elegir un estado de losa: desaparece el pórtico y aparece la superficie de la losa coloreada (deflexión azul→rojo; momento azul/blanco/rojo divergente), abombada según el slider `exag`.
- `#info` muestra la leyenda (`"momento Mx: −3.1 … 8.7 kN·m/m"`).
- Hacer click/touch sobre la losa muestra `"Mx = … kN·m/m @ (x, y) m"`.
- Volver a un estado de pórtico (p.ej. `deformada`) reaparece el pórtico y se reencuadra.

Detener el servidor (Ctrl-C).

- [ ] **Step 4: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): estados de losa con heatmap + relieve + tocar->valor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Documentación y verificación final

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Documentar el heatmap de losas en el README**

Localizar el párrafo de la vista de resultados del visor (el que añadió Fase 2, que menciona `GET /resultados` y los modos) y añadir, justo después, este texto:

```markdown
La vista también obtiene `GET /losa`: el selector gana estados **losa: deflexión /
momento Mx / momento My** que muestran una losa como superficie coloreada (mapa de
calor) y abombada según su deformada. Tocar un punto de la losa muestra el valor
interpolado del campo activo (deflexión en mm, momentos en kN·m/m). El FEM de la
losa corre en el servidor (reusa `losa_fem`); el visor solo colorea y anima.
```

- [ ] **Step 2: Correr la suite completa**

Run: `PYTHONPATH=src:tests python -m pytest -q`
Expected: PASS, ~133 tests (125 de Fase 2 + 1 core + 6 frontera + 1 servidor).

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs(viz): documentar el heatmap de losas (deflexion/momento) del visor

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §9)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~133 tests).
2. `GET /losa` devuelve `a/b/nx/ny`, `factor_sugerido` y `campos` con deflexión y
   momentos Mx/My, cada uno con `min/max` coherentes y un valor por nodo.
3. En el visor: los estados de losa muestran la superficie coloreada con relieve, y
   tocar la losa muestra el valor interpolado del campo activo en `#info`.

## Notas de revisión (plan vs. spec)

- **Cambio en core mínimo:** `momentos_nodales` reusa el `(mx, my)` de centro que el
  loop ya calcula (sin evaluaciones FEM nuevas); `desplazamientos_w` y los máximos no
  cambian → `diseno_losa.py` y todos sus tests siguen verdes.
- **Heatmap sin iluminación:** la losa usa `MeshBasicMaterial` (no `MeshStandard`) para
  que el color leído sea el dato y no quede mezclado con sombreado; por eso no se
  recomputan normales por frame.
- **Picking robusto:** el relieve solo mueve `z`, así que el `(x, y)` del rayo mapea
  directo a coordenadas de losa; `ci/cj/fx/fy` se acotan a la grilla antes de interpolar.
- **index.html no cambia:** la leyenda y el valor tocado reusan `#info` (los 5 ids del
  panel de Fase 2 siguen siendo los únicos que `app.js` busca).
