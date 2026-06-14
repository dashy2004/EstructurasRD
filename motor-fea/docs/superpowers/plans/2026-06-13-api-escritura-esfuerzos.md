# API de escritura + esfuerzos por elemento (#1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publicar los esfuerzos internos por elemento (DTO extremos+diagrama) y añadir un POST stateless para analizar un modelo propio, sin tocar el cálculo del solver.

**Architecture:** Todo aditivo en la capa frontera. `api/contrato.py` (capa 3) gana dos funciones de serialización; `api/servidor.py` gana dos endpoints delgados que las orquestan y mapean errores a HTTP. El cálculo (`core/solver.esfuerzos_elementos`, `EsfuerzosElemento.internos/diagrama`) ya existe y no se modifica.

**Tech Stack:** Python (stdlib), FastAPI, pytest, `fastapi.testclient.TestClient`.

**Spec:** [`docs/superpowers/specs/2026-06-13-api-escritura-esfuerzos-design.md`](../specs/2026-06-13-api-escritura-esfuerzos-design.md)

---

## File Structure

| Archivo | Acción | Responsabilidad |
|---|---|---|
| `src/motor_fea/api/contrato.py` | Modificar (aditivo) | `esfuerzos_a_dict`, `analizar_completo_dict` + import de `esfuerzos_elementos` |
| `src/motor_fea/api/servidor.py` | Modificar (aditivo) | endpoints `GET /esfuerzos`, `POST /analizar`; docstring del módulo |
| `tests/test_contrato.py` | Modificar (aditivo) | tests de serialización + pipeline |
| `tests/test_servidor.py` | Modificar (aditivo) | tests de endpoints (200/400/422) |

**Convención de pruebas:** `pyproject.toml` fija `pythonpath=["src"]` y `testpaths=["tests"]` → se corre con `pytest` directo (sin `PYTHONPATH`). `test_contrato.py` importa el módulo como `from motor_fea.api import cli, contrato` y tiene el helper `_voladizo_dict()`. `test_servidor.py` usa `TestClient(crear_app(modelo_ejemplo()))` tras `pytest.importorskip("fastapi"/"httpx")`.

---

## Task 1: `esfuerzos_a_dict` — serializar esfuerzos por elemento

**Files:**
- Modify: `src/motor_fea/api/contrato.py`
- Test: `tests/test_contrato.py`

- [ ] **Step 1: Write the failing test**

Añade al final de `tests/test_contrato.py` (importa `resolver` arriba si no está):

```python
from motor_fea.core.solver import resolver  # añadir junto a los imports de cabecera


def test_esfuerzos_a_dict_forma_y_convenciones():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    resultado = resolver(modelo)
    d = contrato.esfuerzos_a_dict(modelo, resultado, n=11)

    assert set(d) == {"orden_componentes", "elementos"}
    assert d["orden_componentes"] == ["N", "Vy", "Vz", "T", "My", "Mz"]
    assert len(d["elementos"]) == len(modelo.elementos)

    e0 = d["elementos"][0]
    assert set(e0) == {"id", "longitud", "extremo_i", "extremo_j", "diagrama"}
    assert e0["id"] == modelo.elementos[0].id
    assert len(e0["extremo_i"]) == 6 and len(e0["extremo_j"]) == 6

    # diagrama: n estaciones, s de 0 a L
    assert len(e0["diagrama"]) == 11
    assert e0["diagrama"][0][0] == 0.0
    assert abs(e0["diagrama"][-1][0] - e0["longitud"]) < 1e-9
    assert len(e0["diagrama"][0]) == 7  # s + 6 componentes

    # convención: internos(0) == -extremo_i (componente a componente)
    estacion0 = e0["diagrama"][0][1:]
    for comp, ext in zip(estacion0, e0["extremo_i"]):
        assert abs(comp - (-ext)) < 1e-9


def test_esfuerzos_a_dict_n_configurable():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    resultado = resolver(modelo)
    d = contrato.esfuerzos_a_dict(modelo, resultado, n=5)
    assert all(len(e["diagrama"]) == 5 for e in d["elementos"])
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_contrato.py::test_esfuerzos_a_dict_forma_y_convenciones -v`
Expected: FAIL con `AttributeError: module 'motor_fea.api.contrato' has no attribute 'esfuerzos_a_dict'`

- [ ] **Step 3: Write minimal implementation**

En `src/motor_fea/api/contrato.py`, añade `esfuerzos_elementos` al import existente desde `core.solver`:

```python
from motor_fea.core.solver import ResultadoAnalisis, esfuerzos_elementos, resolver
```

y añade la función (tras `resultado_a_dict`):

```python
def esfuerzos_a_dict(modelo: ModeloEstructural, resultado: ResultadoAnalisis, n: int = 11) -> dict:
    """Serializa los esfuerzos por elemento a un dict JSON-able.

    Por elemento: ``extremo_i``/``extremo_j`` son las fuerzas NODALES de extremo crudas
    (``f_local``); ``diagrama`` son ``n`` estaciones del esfuerzo INTERNO de sección
    (``internos(t)``, tracción +), cada una ``[s, N, Vy, Vz, T, My, Mz]``. Nota:
    ``internos(0) == -extremo_i`` (convenciones distintas, ambas expuestas a propósito).
    """
    esf = esfuerzos_elementos(modelo, resultado)
    return {
        "orden_componentes": ["N", "Vy", "Vz", "T", "My", "Mz"],
        "elementos": [
            {
                "id": e.id,
                "longitud": esf[e.id].longitud,
                "extremo_i": list(esf[e.id].extremo_i),
                "extremo_j": list(esf[e.id].extremo_j),
                "diagrama": [list(fila) for fila in esf[e.id].diagrama(n)],
            }
            for e in modelo.elementos
        ],
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_contrato.py::test_esfuerzos_a_dict_forma_y_convenciones tests/test_contrato.py::test_esfuerzos_a_dict_n_configurable -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/api/contrato.py tests/test_contrato.py
git commit -m "feat(api): esfuerzos_a_dict — DTO de esfuerzos por elemento (extremos + diagrama)"
```

---

## Task 2: `analizar_completo_dict` — pipeline resultados + esfuerzos

**Files:**
- Modify: `src/motor_fea/api/contrato.py`
- Test: `tests/test_contrato.py`

- [ ] **Step 1: Write the failing test**

Añade al final de `tests/test_contrato.py`:

```python
def test_analizar_completo_dict_estructura():
    md = _voladizo_dict()
    d = contrato.analizar_completo_dict(md, n=11)

    assert set(d) == {"resultados", "esfuerzos"}
    assert set(d["resultados"]) == {"n_gdl", "desplazamientos", "reacciones"}
    assert set(d["esfuerzos"]) == {"orden_componentes", "elementos"}
    # round-trip coherente con esfuerzos_a_dict directo
    modelo = contrato.modelo_desde_dict(md)
    directo = contrato.esfuerzos_a_dict(modelo, resolver(modelo), n=11)
    assert d["esfuerzos"] == directo
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_contrato.py::test_analizar_completo_dict_estructura -v`
Expected: FAIL con `AttributeError: module 'motor_fea.api.contrato' has no attribute 'analizar_completo_dict'`

- [ ] **Step 3: Write minimal implementation**

En `src/motor_fea/api/contrato.py`, tras `esfuerzos_a_dict`:

```python
def analizar_completo_dict(modelo_dict: dict, n: int = 11) -> dict:
    """Pipeline dict→dict: deserializa, resuelve y serializa resultados + esfuerzos."""
    modelo = modelo_desde_dict(modelo_dict)
    resultado = resolver(modelo)
    return {
        "resultados": resultado_a_dict(resultado),
        "esfuerzos": esfuerzos_a_dict(modelo, resultado, n),
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pytest tests/test_contrato.py::test_analizar_completo_dict_estructura -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/api/contrato.py tests/test_contrato.py
git commit -m "feat(api): analizar_completo_dict — pipeline dict→{resultados, esfuerzos}"
```

---

## Task 3: `GET /esfuerzos` — endpoint de esfuerzos del modelo de ejemplo

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Write the failing test**

Añade al final de `tests/test_servidor.py`:

```python
def test_esfuerzos_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/esfuerzos")
    assert r.status_code == 200
    data = r.json()
    assert set(data) == {"orden_componentes", "elementos"}
    assert len(data["elementos"]) == 8          # 4 columnas + 4 vigas
    e0 = data["elementos"][0]
    assert set(e0) == {"id", "longitud", "extremo_i", "extremo_j", "diagrama"}
    assert len(e0["diagrama"]) == 11


def test_esfuerzos_n_configurable():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/esfuerzos?n=5")
    assert r.status_code == 200
    assert all(len(e["diagrama"]) == 5 for e in r.json()["elementos"])


def test_esfuerzos_n_invalido_da_422():
    cli = TestClient(crear_app(modelo_ejemplo()))
    assert cli.get("/esfuerzos?n=1").status_code == 422


def test_esfuerzos_modelo_invalido_da_400():
    m = ModeloEstructural()
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))  # refs inexistentes
    cli = TestClient(crear_app(m))
    assert cli.get("/esfuerzos").status_code == 400
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_servidor.py::test_esfuerzos_ok -v`
Expected: FAIL con status 404 (la ruta `/esfuerzos` aún no existe; el mount de StaticFiles devuelve 404)

- [ ] **Step 3: Write minimal implementation**

En `src/motor_fea/api/servidor.py`:

1. Cambia el import de fastapi para incluir `Query` y `Body`:

```python
from fastapi import Body, FastAPI, HTTPException, Query
```

2. Añade el import del contrato y del solver junto a los existentes:

```python
from motor_fea.api.contrato import analizar_completo_dict, esfuerzos_a_dict
from motor_fea.core.solver import resolver
```

3. Dentro de `crear_app`, **antes** del `app.mount("/", ...)` (el mount debe quedar al final), añade:

```python
    @app.get("/esfuerzos")
    def esfuerzos(n: int = Query(11, ge=2)):
        try:
            return esfuerzos_a_dict(modelo, resolver(modelo), n)
        except ValueError as ex:
            raise HTTPException(status_code=400, detail=str(ex))
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_servidor.py::test_esfuerzos_ok tests/test_servidor.py::test_esfuerzos_n_configurable tests/test_servidor.py::test_esfuerzos_n_invalido_da_422 tests/test_servidor.py::test_esfuerzos_modelo_invalido_da_400 -v`
Expected: PASS (4 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): GET /esfuerzos — esfuerzos por elemento del modelo de ejemplo"
```

---

## Task 4: `POST /analizar` — analizar un modelo propio (stateless)

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Write the failing test**

Añade al final de `tests/test_servidor.py` (importa `modelo_a_dict` arriba):

```python
from motor_fea.api.contrato import modelo_a_dict  # añadir junto a los imports de cabecera


def test_analizar_post_ok_coincide_con_gets():
    cli = TestClient(crear_app(modelo_ejemplo()))
    md = modelo_a_dict(modelo_ejemplo())
    r = cli.post("/analizar", json=md)
    assert r.status_code == 200
    data = r.json()
    assert set(data) == {"resultados", "esfuerzos"}
    # esfuerzos del POST coinciden con el GET /esfuerzos del mismo modelo
    assert data["esfuerzos"] == cli.get("/esfuerzos").json()


def test_analizar_post_modelo_invalido_da_400():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.post("/analizar", json={"nodos": [], "elementos": [{"id": 1, "nodo_i": 1,
                 "nodo_j": 2, "material_id": 1, "seccion_id": 1}]})
    assert r.status_code == 400


def test_analizar_post_n_invalido_da_422():
    cli = TestClient(crear_app(modelo_ejemplo()))
    md = modelo_a_dict(modelo_ejemplo())
    assert cli.post("/analizar?n=1", json=md).status_code == 422
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pytest tests/test_servidor.py::test_analizar_post_ok_coincide_con_gets -v`
Expected: FAIL con status 405 o 404 (no hay handler POST para `/analizar`)

- [ ] **Step 3: Write minimal implementation**

En `src/motor_fea/api/servidor.py`, dentro de `crear_app`, **antes** del `app.mount("/", ...)`, añade:

```python
    @app.post("/analizar")
    def analizar(modelo_dict: dict = Body(...), n: int = Query(11, ge=2)):
        try:
            return analizar_completo_dict(modelo_dict, n)
        except (ValueError, KeyError, TypeError) as ex:
            raise HTTPException(status_code=400, detail=f"Modelo inválido: {ex}")
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `pytest tests/test_servidor.py::test_analizar_post_ok_coincide_con_gets tests/test_servidor.py::test_analizar_post_modelo_invalido_da_400 tests/test_servidor.py::test_analizar_post_n_invalido_da_422 -v`
Expected: PASS (3 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): POST /analizar — analizar un modelo propio (stateless) → resultados + esfuerzos"
```

---

## Task 5: Actualizar docstring del módulo + suite completa verde

**Files:**
- Modify: `src/motor_fea/api/servidor.py`

- [ ] **Step 1: Actualizar el docstring del módulo `servidor.py`**

Reemplaza el párrafo de cabecera que enumera los endpoints (líneas 3-5) para incluir los nuevos. El docstring actual dice "Expone GET /escena … /diseno … sirve los estáticos". Cámbialo a:

```python
"""Servidor FastAPI del visor WebXR (capa frontera). Requiere el extra `api`.

Expone GET /escena (SceneDTO), GET /resultados (deformada + modos), GET /losa
(heatmap de losa), GET /armado (refuerzo 3D de ejemplo), GET /diseno (armado
diseñado por fuerzas), GET /esfuerzos (esfuerzos por elemento: extremos + diagrama),
POST /analizar (analiza un modelo propio, stateless → resultados + esfuerzos), y
sirve los estáticos del visor. El análisis y la exportación viven en otras capas;
este módulo es I/O delgado.
"""
```

- [ ] **Step 2: Correr la suite completa**

Run: `pytest -q`
Expected: PASS — toda la suite verde (los tests previos + los 9 nuevos: 2 contrato·forma/n + 1 contrato·pipeline + 4 servidor·GET + 3 servidor·POST). Ningún test previo roto.

- [ ] **Step 3: Commit**

```bash
git add src/motor_fea/api/servidor.py
git commit -m "docs(api): documentar endpoints /esfuerzos y /analizar en el docstring del servidor"
```

---

## Self-Review (hecho al escribir el plan)

**Spec coverage:**
- Spec §4 `GET /esfuerzos` → Task 3 ✅; `POST /analizar` → Task 4 ✅; validación `n` (422) → Tasks 3·step1, 4·step1 ✅.
- Spec §5 DTO (orden_componentes, elementos, extremo_i/j, diagrama, estaciones extremas) → Task 1 ✅.
- Spec §5 convenciones de signo (`internos(0) == -extremo_i`) → Task 1·step1 assert ✅.
- Spec §6 `esfuerzos_a_dict` → Task 1 ✅; `analizar_completo_dict` → Task 2 ✅.
- Spec §7 endpoints (imports Query/Body, mount al final) → Tasks 3·step3, 4·step3 ✅.
- Spec §8 errores (400 modelo inválido GET y POST; 422 n) → Tasks 3, 4 ✅.
- Spec §10 round-trip POST↔GET → Task 4·step1 ✅; suite verde → Task 5 ✅.
- Spec §9 (limitación de casos de carga) es documentación, sin tarea: correcto.

**Placeholder scan:** sin TBD/TODO; todo paso de código muestra el código. ✅

**Type consistency:** `esfuerzos_a_dict(modelo, resultado, n)`, `analizar_completo_dict(modelo_dict, n)`, claves del DTO (`orden_componentes`, `elementos`, `id`, `longitud`, `extremo_i`, `extremo_j`, `diagrama`) idénticas en Tasks 1/2/3/4. Endpoints `Query(11, ge=2)` y `Body(...)` consistentes. ✅
