# Fase 5A.2: combo gobernante en el visor — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El `/diseno` y el visor pasan a diseñar por combinaciones LRFD, mostrando el combo gobernante y su demanda factorada por elemento; el modelo de ejemplo se enriquece (D+W) para que los combos se vean.

**Architecture:** `viz/diseno.py` cambia su pipeline a `esfuerzos_por_caso` + `disenar_*_combos` (5A.1) y agrega `combo` al DTO; `modelo_ejemplo` gana gravedad D + lateral W; `app.js` muestra el combo en la etiqueta. Reusa todo 5A.1; core/normativa/aci318/armado intactos.

**Tech Stack:** Python 3.11 + stdlib; FastAPI; three.js vendorizado.

**Spec de referencia:** `docs/superpowers/specs/2026-06-06-fase5a2-combo-gobernante-visor-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/viz/diseno.py` (mod) | `calcular_diseno` por combos + `combo` en el DTO. |
| `tests/test_diseno_visual.py` (mod) | +test de combos; `combo` en el DTO. |
| `src/motor_fea/api/servidor.py` (mod) | `modelo_ejemplo`: D + W. |
| `tests/test_servidor.py` (mod) | `combo` en `/diseno`. |
| `src/motor_fea/viz/static/app.js` (mod) | combo en la etiqueta. |

---

## Task 1: `viz/diseno.py` — diseño por combos + `combo` en el DTO

**Files:**
- Modify: `src/motor_fea/viz/diseno.py`
- Test: `tests/test_diseno_visual.py`

- [ ] **Step 1: Escribir los tests que fallan** — en `tests/test_diseno_visual.py`:

(a) En el test existente `test_columnas_y_vigas_con_armado_y_demanda`, dentro del `for e in dto["elementos"]:`, añadir una aserción de que el campo `combo` existe y no es vacío:
```python
        assert e["combo"]                                 # combo gobernante (5A.2)
```

(b) Añadir al final del archivo este test nuevo:
```python
def test_diseno_combo_con_W_gobierna():
    # columna con SOLO carga lateral W → un combo con W gobierna (≠ "1" = 1.4D, que con D=0 da 0).
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(2, fx=20000.0, caso="W"))
    dto = diseno.calcular_diseno(m, fc=28.0, fy=420.0, recubrimiento=0.05)
    el = dto["elementos"][0]
    assert el["combo"] and el["combo"] != "1"             # gobierna un combo con W, no 1.4D
    assert set(el["demanda"]) == {"pu", "mu", "vu"}
    assert all(v >= 0 for v in el["demanda"].values())
```

- [ ] **Step 2: Correr — esperar FAIL** (`KeyError: 'combo'` en el test existente y el nuevo, porque el `calcular_diseno` actual no emite `combo`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q` (use `.venv/bin/pytest` si falta python/pytest).

- [ ] **Step 3: Implementar** — reemplazar el contenido COMPLETO de `src/motor_fea/viz/diseno.py` por:

```python
"""Cálculo del armado DISEÑADO por combinaciones LRFD para el visor (capa frontera).

Por cada elemento: corre un análisis por caso de carga (``esfuerzos_por_caso``), diseña el
refuerzo cubriendo todos los combos LRFD (``diseno_elemento.disenar_*_combos``) y empaqueta el
armado real + el combo gobernante + su demanda factorada (Pu/Mu/Vu) + cumple, reusando la
derivación de posiciones de ``viz.armado``. Función pura: usa core (casos), viz (escena/armado)
y diseno_elemento (que envuelve aci318); no toca HTTP ni three.js.

Unidades del DTO: metros (posiciones, estribo) y N/N·m (demanda), como la escena.
"""
from __future__ import annotations

from motor_fea import diseno_elemento
from motor_fea.core.casos import esfuerzos_por_caso
from motor_fea.core.modelo import ModeloEstructural
from motor_fea.viz import armado
from motor_fea.viz.escena import _clasificar, _dimensiones


def calcular_diseno(modelo: ModeloEstructural, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> dict:
    """DisenoDTO: armado por combinaciones LRFD + combo gobernante + demanda factorada por elemento."""
    if fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("fc, fy y recubrimiento deben ser positivos.")
    errores = modelo.validar()
    if errores:
        raise ValueError("Modelo inválido: " + "; ".join(errores))

    epc = esfuerzos_por_caso(modelo)
    nodos = {n.id: n for n in modelo.nodos}
    secs = {s.id: s for s in modelo.secciones}
    d_est = armado._diametro_m(3)
    elementos: list[dict] = []
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        b, h = _dimensiones(secs[e.seccion_id])
        if b - 2 * recubrimiento <= 0 or h - 2 * recubrimiento <= 0:
            raise ValueError(f"Recubrimiento {recubrimiento} incompatible con la sección {b}×{h}.")
        esf_por_caso = {caso: epc[caso][e.id] for caso in epc}
        if _clasificar(ni, nj) == "columna":
            d = diseno_elemento.disenar_columna_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            long = armado._posiciones_columna(b, h, recubrimiento, d.numero_barra, d.n_barras)
            # Estribo de columna por la regla ACI 25.7.2.1 (el diseño no dimensiona estribos de columna).
            s = max(0.05, min(16 * armado._diametro_m(d.numero_barra), 48 * d_est, min(b, h)))
            tipo, designacion, cumple, combo = "columna", d.disponer, d.cumple, d.combo_gobernante
        else:
            d = diseno_elemento.disenar_viga_combos(esf_por_caso, b, h, fc, fy, recubrimiento)
            # Si la sección es insuficiente a flexión (d.flexion=None) se dibujan barras nominales
            # (2#5) solo para que el elemento se vea; cumple=False / designacion lo marcan.
            num = d.flexion.numero_barra if d.flexion else 5
            n_inf = d.flexion.n_barras if d.flexion else 2
            long = armado._posiciones_viga(b, h, recubrimiento, num, n_inf)
            s = d.estribo.espaciamiento / 1000.0      # mm → m
            tipo, designacion, cumple, combo = "viga", d.disponer, d.cumple, d.combo_flexion
        # Demanda factorada del combo gobernante (siempre N/N·m → evita el mismatch de unidades
        # de los dataclasses: columna mu en N·mm vs viga mu en N·m).
        pu, mu, vu = diseno_elemento._demanda_por_combo(esf_por_caso)[combo]
        elementos.append({
            "id": e.id, "i": e.nodo_i, "j": e.nodo_j, "tipo": tipo,
            "long": long,
            "estribo": {"d": d_est, "s": s, "w": b - 2 * recubrimiento, "h": h - 2 * recubrimiento},
            "designacion": designacion,
            "demanda": {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)},
            "combo": combo, "cumple": cumple,
        })
    return {"recubrimiento": recubrimiento, "elementos": elementos}
```

- [ ] **Step 4: Correr — esperar PASS** (todos los de `test_diseno_visual.py`, incluido el nuevo):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_visual.py -q`
Expected: el `_portico()` todo-D ahora pasa por combos → `combo=="1"`; el nuevo test con W → `combo!="1"`. Reportar el conteo.

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/diseno.py tests/test_diseno_visual.py
git commit -m "feat(viz): /diseno por combinaciones LRFD + combo gobernante en el DTO

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `modelo_ejemplo` con casos (D + W)

**Files:**
- Modify: `src/motor_fea/api/servidor.py`
- Test: `tests/test_servidor.py`

- [ ] **Step 1: Escribir el test que falla** — añadir al final de `tests/test_servidor.py`:

```python
def test_diseno_tiene_combo_y_casos():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/diseno")
    assert r.status_code == 200
    data = r.json()
    assert len(data["elementos"]) == 8
    for e in data["elementos"]:
        assert e["combo"]                                 # combo gobernante presente
    # con D+W, no todos los elementos los gobierna 1.4D
    combos = {e["combo"] for e in data["elementos"]}
    assert combos - {"1"}
```

- [ ] **Step 2: Correr — esperar FAIL** (el `modelo_ejemplo` actual tiene un solo caso → todos `combo=="1"` → `combos - {"1"}` vacío):
`PYTHONPATH=src:tests python -m pytest tests/test_servidor.py::test_diseno_tiene_combo_y_casos -q`

- [ ] **Step 3: Implementar** — en `src/motor_fea/api/servidor.py`, dentro de `modelo_ejemplo`, el loop de cargas actual:
```python
    for n in (5, 6, 7, 8):
        m.cargas.append(CargaNodal(n, fx=10000.0))
```
pasa a (gravedad D + viento W):
```python
    for n in (5, 6, 7, 8):
        m.cargas.append(CargaNodal(n, fz=-40000.0, caso="D"))   # gravedad
        m.cargas.append(CargaNodal(n, fx=10000.0, caso="W"))    # viento
```

- [ ] **Step 4: Correr los tests del servidor — esperar PASS**:
`PYTHONPATH=src:tests python -m pytest tests/test_servidor.py -q`
Expected: todos verdes — `/escena`/`/resultados`/`/losa`/`/armado` no dependen del caso (cambian números, no forma); `/diseno` ahora muestra combos reales.

- [ ] **Step 5: Correr la suite completa (sin regresión)**
`PYTHONPATH=src:tests python -m pytest -q`
Expected: ~188 passed (186 + 1 diseño_visual + 1 servidor). Reportar el conteo exacto; si algo falla, STOP/BLOCKED.

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/api/servidor.py tests/test_servidor.py
git commit -m "feat(api): modelo de ejemplo con casos D+W (combos LRFD visibles)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Etiqueta del visor con el combo gobernante

> Sin unit test (smoke manual). Dos ediciones puntuales a `app.js`; `index.html` no cambia.

**Files:**
- Modify: `src/motor_fea/viz/static/app.js`

- [ ] **Step 1: Mostrar el combo en `mostrarDiseno`** — en `src/motor_fea/viz/static/app.js`, la última línea de `mostrarDiseno`:
```javascript
  info.textContent = `${el.designacion} · ${dem} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
```
pasa a:
```javascript
  info.textContent = `${el.designacion} · combo ${el.combo} · ${dem} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
```

- [ ] **Step 2: Resumen LRFD al entrar** — en `entrarDiseno`, la línea:
```javascript
  info.textContent = `diseño por fuerzas — ${ok}/${n} cumplen`;
```
pasa a:
```javascript
  info.textContent = `diseño LRFD — ${ok}/${n} cumplen`;
```

- [ ] **Step 3: Validación estática**
1. `cp src/motor_fea/viz/static/app.js /tmp/app5a2.mjs && node --check /tmp/app5a2.mjs` → exit 0.
2. Confirmar que `el.combo` se usa: `grep -n 'combo' src/motor_fea/viz/static/app.js` (debe aparecer en `mostrarDiseno`).

- [ ] **Step 4: Smoke manual** — `PYTHONPATH=src python -m motor_fea.api.cli --serve --port 8000`; en `http://127.0.0.1:8000/` estado `diseño: armado`: tocar un elemento muestra `"… · combo N · …"`; con el ejemplo D+W aparecen combos ≠ "1" (p.ej. "4" en columnas con viento). Ctrl-C.

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/viz/static/app.js
git commit -m "feat(viz): mostrar el combo gobernante en la etiqueta del elemento

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §9)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~188); sin regresión en las rutas/visor de Fases 2-4b.
2. `GET /diseno` devuelve, por elemento, el armado diseñado + `combo` gobernante + `demanda` factorada de ese combo.
3. En el visor: tocar un elemento muestra `"… · combo N · …"`; el ejemplo D+W muestra combos reales.

## Notas de revisión (plan vs. spec)

- **Reuso de 5A.1:** `calcular_diseno` solo cambia de pipeline (single-case → combos); reusa `esfuerzos_por_caso`,
  `disenar_*_combos`, `_demanda_por_combo`. `core/`, `normativa/`, `aci318`, `viz/armado.py` intactos.
- **Demanda uniforme:** se toma de `_demanda_por_combo` (N/N·m) y no de los dataclasses (columna mu N·mm vs
  viga mu N·m) — evita el footgun marcado en la revisión de integración de 5A.1.
- **Ejemplo que ejercita la feature:** D+W hace que los combos gobiernen ≠ "1"; sin eso la demo es trivial.
- **`_demanda_por_combo[combo]` es seguro:** `combo` (de `combo_gobernante`/`combo_flexion`) es una clave que la
  propia función produce con el mismo `esf_por_caso`.
- **Retrocompat:** los tests de 4b.2 (`_portico` todo-D) siguen verdes (combos sobre un caso = combo "1").
