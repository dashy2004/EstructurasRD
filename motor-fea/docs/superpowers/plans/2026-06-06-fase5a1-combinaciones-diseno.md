# Fase 5A.1: combinaciones de carga en el diseño (motor) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Etiquetar cada carga con su caso (D/L/W/E…), analizar un caso por vez, y diseñar cada elemento para todos los combos LRFD reportando el combo gobernante.

**Architecture:** `CargaNodal.caso` + validación en `core/modelo.py`; `core/casos.py` corre un análisis por caso (reusa `resolver`+`esfuerzos_elementos`); `diseno_elemento.py` gana funciones `*_combos` aditivas que combinan los esfuerzos por componente con `combinaciones_resistencia` y diseñan por combo gobernante.

**Tech Stack:** Python 3.11 + stdlib. Unidades del motor: N, m, N·m; aci318: N, mm, MPa.

**Spec de referencia:** `docs/superpowers/specs/2026-06-06-fase5a1-combinaciones-diseno-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/core/modelo.py` (mod) | `CargaNodal.caso` + `validar()` chequea `caso ∈ {D,L,Lr,S,R,W,E}`. |
| `src/motor_fea/core/casos.py` (nuevo) | `esfuerzos_por_caso(modelo)`. |
| `src/motor_fea/diseno_elemento.py` (mod, aditivo) | `_demanda_por_combo`, `disenar_viga_combos`, `disenar_columna_combos` + dataclasses Combos. |
| `tests/test_modelo.py` (mod) | +test del campo `caso`. |
| `tests/test_combinaciones_diseno.py` (nuevo) | Tests de casos + diseño por combo. |

---

## Task 1: `CargaNodal.caso` + validación

**Files:**
- Modify: `src/motor_fea/core/modelo.py`
- Test: `tests/test_modelo.py`

- [ ] **Step 1: Escribir el test que falla** — añadir al final de `tests/test_modelo.py`:

```python
def test_carga_nodal_caso_default_y_validacion():
    from motor_fea.core.modelo import CargaNodal, ModeloEstructural, Nodo
    assert CargaNodal(1).caso == "D"                      # default retrocompatible
    m = ModeloEstructural()
    m.nodos.append(Nodo(1, 0.0, 0.0, 0.0))
    m.cargas.append(CargaNodal(1, fz=-1000.0, caso="ZZ"))
    assert any("ZZ" in e for e in m.validar())            # caso inválido reportado
    m.cargas[-1] = CargaNodal(1, fz=-1000.0, caso="L")
    assert not any("caso" in e.lower() for e in m.validar())
```

- [ ] **Step 2: Correr — esperar FAIL** (`AttributeError: 'CargaNodal' object has no attribute 'caso'`):
`PYTHONPATH=src:tests python -m pytest tests/test_modelo.py::test_carga_nodal_caso_default_y_validacion -q` (use `.venv/bin/pytest` si falta python/pytest).

- [ ] **Step 3: Implementar** — en `src/motor_fea/core/modelo.py`:

(a) Añadir, cerca del tope del módulo (después de los imports / constantes como `GDL_POR_NODO`):
```python
CASOS_CARGA = frozenset({"D", "L", "Lr", "S", "R", "W", "E"})   # tipos LRFD (ACI 318-19 §5.3.1)
```

(b) En `CargaNodal`, añadir el campo `caso` al final (después de `mz`) y mencionarlo en el docstring:
```python
@dataclass(frozen=True)
class CargaNodal:
    """Carga puntual en un nodo. Fuerzas en N, momentos en N·m, ejes globales.

    ``caso`` es el tipo de carga LRFD (ACI 318-19 §5.3.1): D, L, Lr, S, R, W, E.
    """
    nodo_id: int
    fx: float = 0.0
    fy: float = 0.0
    fz: float = 0.0
    mx: float = 0.0
    my: float = 0.0
    mz: float = 0.0
    caso: str = "D"

    def componentes(self) -> tuple[float, ...]:
        return (self.fx, self.fy, self.fz, self.mx, self.my, self.mz)
```

(c) En `validar()`, dentro del loop de cargas, añadir el chequeo de `caso`:
```python
        for c in self.cargas:
            if c.nodo_id not in ids_nodo:
                errores.append(f"Carga en nodo {c.nodo_id} inexistente.")
            if c.caso not in CASOS_CARGA:
                errores.append(f"Carga en nodo {c.nodo_id}: caso '{c.caso}' inválido.")
```

- [ ] **Step 4: Correr — esperar PASS**:
`PYTHONPATH=src:tests python -m pytest tests/test_modelo.py -q`
(Toda la suite de `test_modelo.py` verde: el default `"D"` no rompe nada.)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/modelo.py tests/test_modelo.py
git commit -m "feat(core): caso de carga en CargaNodal (LRFD) + validacion

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `core/casos.py` — análisis por caso

**Files:**
- Create: `src/motor_fea/core/casos.py`
- Test: `tests/test_combinaciones_diseno.py`

- [ ] **Step 1: Escribir los tests que fallan** — crear `tests/test_combinaciones_diseno.py`:

```python
"""Tests de combinaciones de carga en el diseño (Fase 5A.1)."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.casos import esfuerzos_por_caso

_E, _NU, _L = 2.0e10, 0.2, 3.0


def _voladizo(cargas, lado=0.30):
    """Voladizo en X (empotrado en 1), con las CargaNodal dadas en la punta (nodo 2)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, _L, 0, 0)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=lado * lado, inercia_y=lado ** 4 / 12,
                               inercia_z=lado ** 4 / 12, constante_torsion=0.1406 * lado ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += cargas
    return m


def _mmax(esf):
    return max(abs(my) for _s, _n, _vy, _vz, _t, my, _mz in esf.diagrama())


def test_esfuerzos_por_caso_separa_y_analiza():
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D"), CargaNodal(2, fz=500.0, caso="L")])
    epc = esfuerzos_por_caso(m)
    assert set(epc) == {"D", "L"}
    assert _mmax(epc["D"][1]) == pytest.approx(1000.0 * _L, rel=1e-3)   # M_D ≈ fzD·L
    assert _mmax(epc["L"][1]) == pytest.approx(500.0 * _L, rel=1e-3)    # M_L ≈ fzL·L


def test_esfuerzos_por_caso_sin_cargas_vacio():
    m = _voladizo([])
    assert esfuerzos_por_caso(m) == {}
```

- [ ] **Step 2: Correr — esperar FAIL** (`ModuleNotFoundError: ... 'motor_fea.core.casos'`):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`

- [ ] **Step 3: Implementar** — crear `src/motor_fea/core/casos.py`:

```python
"""Análisis por caso de carga (capa core).

Separa las cargas del modelo por su ``caso`` (D/L/W/E…), corre un análisis lineal
independiente por caso y devuelve los esfuerzos por elemento de cada uno. La
combinación LRFD es posterior (a nivel de esfuerzos, en la capa de diseño).
"""
from __future__ import annotations

from dataclasses import replace

from motor_fea.core.modelo import ModeloEstructural
from motor_fea.core.solver import EsfuerzosElemento, esfuerzos_elementos, resolver


def esfuerzos_por_caso(modelo: ModeloEstructural) -> dict[str, dict[int, EsfuerzosElemento]]:
    """{caso: {elem_id: EsfuerzosElemento}} — un análisis lineal por caso de carga distinto."""
    casos = sorted({c.caso for c in modelo.cargas})
    salida: dict[str, dict[int, EsfuerzosElemento]] = {}
    for caso in casos:
        sub = replace(modelo, cargas=[c for c in modelo.cargas if c.caso == caso])
        salida[caso] = esfuerzos_elementos(sub, resolver(sub))
    return salida
```

- [ ] **Step 4: Correr — esperar PASS** (2 passed):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/casos.py tests/test_combinaciones_diseno.py
git commit -m "feat(core): esfuerzos_por_caso (un analisis lineal por caso de carga)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `diseno_elemento.py` — diseño por combo gobernante

**Files:**
- Modify: `src/motor_fea/diseno_elemento.py`
- Test: `tests/test_combinaciones_diseno.py`

- [ ] **Step 1: Escribir los tests que fallan** — añadir al final de `tests/test_combinaciones_diseno.py`:

```python
from motor_fea import diseno_elemento


def _columna(cargas, bc=0.40):
    """Columna en Z (empotrada en 1), con las CargaNodal dadas en la punta (nodo 2)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, _L)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += cargas
    return m


def _por_caso(epc, elem_id):
    return {caso: epc[caso][elem_id] for caso in epc}


def test_viga_combo_gobernante_2():
    # M_D = 1000·3 = 3000, M_L = 500·3 = 1500 N·m → combo 2 (1.2D+1.6L) = 6000 gobierna.
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D"), CargaNodal(2, fz=500.0, caso="L")])
    d = diseno_elemento.disenar_viga_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.30, h=0.30)
    assert d.combo_flexion == "2"
    assert d.mu == pytest.approx(6000.0, rel=1e-2)
    assert d.flexion is not None and d.cumple


def test_viga_retrocompat_un_caso_D_es_combo_1():
    # solo caso D → combo 1 (1.4D) gobierna.
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D")])
    d = diseno_elemento.disenar_viga_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.30, h=0.30)
    assert d.combo_flexion == "1"
    assert d.mu == pytest.approx(1.4 * 3000.0, rel=1e-2)


def test_columna_combos_no_menos_barras_que_caso_D():
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="L")])
    epc = esfuerzos_por_caso(m)
    d_combos = diseno_elemento.disenar_columna_combos(_por_caso(epc, 1), b=0.40, h=0.40,
                                                      fc=28.0, fy=420.0, recubrimiento=0.05)
    d_D = diseno_elemento.disenar_columna(epc["D"][1], b=0.40, h=0.40,
                                          fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d_combos.n_barras >= d_D.n_barras
    assert d_combos.combo_gobernante                      # no vacío


def test_columna_caso_reversible_no_rompe():
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="E")])
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.40, h=0.40,
                                               fc=28.0, fy=420.0, recubrimiento=0.05)
    assert isinstance(d, diseno_elemento.DisenoColumnaCombos)
    assert d.combo_gobernante


def test_columna_combos_insuficiente():
    # 0.20×0.20 con axial enorme en D → ningún ρ≤8% cubre el combo → no cumple.
    m = _columna([CargaNodal(2, fz=-3.0e6, caso="D"), CargaNodal(2, fx=2.0e5, caso="L")], bc=0.20)
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.20, h=0.20,
                                               fc=28.0, fy=420.0, recubrimiento=0.04)
    assert d.cumple is False
    assert d.combo_gobernante
```

- [ ] **Step 2: Correr — esperar FAIL** (`AttributeError: ... 'disenar_viga_combos'`):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`

- [ ] **Step 3: Implementar** — en `src/motor_fea/diseno_elemento.py`:

(a) Añadir a los imports del tope (el módulo ya tiene `from dataclasses import dataclass`, `from motor_fea.core.solver import EsfuerzosElemento`, `from motor_fea.normativa import aci318`):
```python
import math

from motor_fea.normativa.combinaciones import combinaciones_resistencia
```

(b) Añadir al **final** del módulo:
```python
# ===================== Diseño por combinaciones (Fase 5A.1) =====================
@dataclass(frozen=True)
class DisenoColumnaCombos:
    pu: float              # N (combo gobernante)
    mu: float              # N·mm (combo gobernante)
    numero_barra: int
    n_barras: int
    rho: float
    cumple: bool
    disponer: str
    combo_gobernante: str


@dataclass(frozen=True)
class DisenoVigaCombos:
    mu: float              # N·m (combo de flexión gobernante)
    vu: float              # N (combo de cortante gobernante)
    flexion: aci318.SeleccionBarras | None
    estribo: aci318.DisenoEstribo
    cumple: bool
    disponer: str
    combo_flexion: str
    combo_cortante: str


def _escalares_por_caso(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float]]:
    """{caso: (P, M, V)} — P axial con signo (N); M=max|My|,|Mz| (N·m); V=max|Vy|,|Vz| (N)."""
    out: dict[str, tuple[float, float, float]] = {}
    for caso, esf in esf_por_caso.items():
        mu = vu = 0.0
        for _s, _n, vy, vz, _t, my, mz in esf.diagrama(21):
            mu = max(mu, abs(my), abs(mz))
            vu = max(vu, abs(vy), abs(vz))
        out[caso] = (esf.axial, mu, vu)
    return out


def _demanda_por_combo(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float]]:
    """{combo: (Pu, Mu, Vu)} (N, N·m, N) — LRFD ACI §5.3.1, axial con signo, M/V por magnitud."""
    esc = _escalares_por_caso(esf_por_caso)
    combos_p = combinaciones_resistencia(**{caso: v[0] for caso, v in esc.items()})
    combos_m = combinaciones_resistencia(**{caso: v[1] for caso, v in esc.items()})
    combos_v = combinaciones_resistencia(**{caso: v[2] for caso, v in esc.items()})
    return {k: (combos_p[k], combos_m[k], combos_v[k]) for k in combos_p}


def _gobernante_columna(dem_mm: dict[str, tuple[float, float]], diagrama, pmax: float) -> str:
    """Combo con mayor relación demanda/capacidad (pu/pmax, mu/φMn)."""
    def ratio(pu: float, mu: float) -> float:
        cap = aci318.momento_capacidad(pu, diagrama)
        r_p = pu / pmax if pmax > 0 else math.inf
        r_m = mu / cap if cap > 0 else math.inf
        return max(r_p, r_m)
    return max(dem_mm, key=lambda k: ratio(*dem_mm[k]))


def disenar_columna_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                           fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04,
                           num: int = 8) -> DisenoColumnaCombos:
    """Diseña una columna (P-M) cubriendo todos los combos LRFD; reporta el gobernante. b,h,rec en m."""
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    b_mm, h_mm, rec_mm = b * 1000.0, h * 1000.0, recubrimiento * 1000.0
    if h_mm - 2 * rec_mm <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    dem_mm = {k: (abs(P), abs(M) * 1000.0) for k, (P, M, _V) in _demanda_por_combo(esf_por_caso).items()}
    ag = b_mm * h_mm
    area = aci318.AREAS_BARRA_MM2[num]
    d_barra = aci318._diametro_barra(num)
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        as_total = n * area
        capas = [(rec_mm + d_barra / 2.0, as_total / 2.0), (h_mm - rec_mm - d_barra / 2.0, as_total / 2.0)]
        diagrama = aci318.diagrama_interaccion(b_mm, h_mm, fc, fy, capas)
        pmax = aci318.axial_maxima_diseno(ag, as_total, fc, fy)
        if all(pu <= pmax and mu <= aci318.momento_capacidad(pu, diagrama) for pu, mu in dem_mm.values()):
            gob = _gobernante_columna(dem_mm, diagrama, pmax)
            return DisenoColumnaCombos(dem_mm[gob][0], dem_mm[gob][1], num, n, as_total / ag,
                                       True, f"{n}#{num}", gob)
        ultimo_n = n
        n += 1
    as_total = ultimo_n * area
    capas = [(rec_mm + d_barra / 2.0, as_total / 2.0), (h_mm - rec_mm - d_barra / 2.0, as_total / 2.0)]
    diagrama = aci318.diagrama_interaccion(b_mm, h_mm, fc, fy, capas)
    pmax = aci318.axial_maxima_diseno(ag, as_total, fc, fy)
    gob = _gobernante_columna(dem_mm, diagrama, pmax)
    return DisenoColumnaCombos(dem_mm[gob][0], dem_mm[gob][1], num, ultimo_n, ultimo_n * area / ag,
                               False, "SECCIÓN INSUFICIENTE", gob)


def disenar_viga_combos(esf_por_caso: dict[str, EsfuerzosElemento], b: float, h: float,
                        fc: float = 21.0, fy: float = 420.0, recubrimiento: float = 0.04) -> DisenoVigaCombos:
    """Diseña una viga (flexión + estribos) cubriendo todos los combos; reporta los gobernantes. b,h,rec en m."""
    demandas = _demanda_por_combo(esf_por_caso)                 # {combo: (Pu, Mu N·m, Vu N)}
    b_mm, d_mm = b * 1000.0, (h - recubrimiento) * 1000.0
    as_min = aci318.as_minimo_flexion(b_mm, d_mm, fc, fy)

    # Cortante: gobierna el combo de mayor |Vu| (más Vu → menor s) → diseñar para ese.
    combo_v = max(demandas, key=lambda k: abs(demandas[k][2]))
    vu_g = abs(demandas[combo_v][2])
    estribo = aci318.disenar_estribo_viga(vu_g, b_mm, d_mm, fc, fy)

    # Flexión: As requerido por combo; gobierna el de mayor As. Insuficiente en algún combo → None.
    as_por_combo: dict[str, float] = {}
    for k, (_P, M, _V) in demandas.items():
        as_req, insuf = aci318.as_requerido_flexion(abs(M) * 1000.0, b_mm, d_mm, fc, fy)
        if insuf:
            return DisenoVigaCombos(abs(M), vu_g, None, estribo, False,
                                    "SECCIÓN INSUFICIENTE A FLEXIÓN", k, combo_v)
        as_por_combo[k] = max(as_req, as_min)
    combo_flex = max(as_por_combo, key=lambda k: as_por_combo[k])
    flexion = aci318.seleccionar_barras(as_por_combo[combo_flex], (b - 2 * recubrimiento) * 1000.0)
    cumple = flexion.cumple and estribo.cumple
    disponer = f"{flexion.n_barras}#{flexion.numero_barra} + {estribo.disponer}"
    return DisenoVigaCombos(abs(demandas[combo_flex][1]), vu_g, flexion, estribo, cumple,
                            disponer, combo_flex, combo_v)
```

- [ ] **Step 4: Correr los tests de combinaciones — esperar PASS** (8 passed: 2 de Task 2 + 6):
`PYTHONPATH=src:tests python -m pytest tests/test_combinaciones_diseno.py -q`

- [ ] **Step 5: Correr la suite completa (sin regresión)**
`PYTHONPATH=src:tests python -m pytest -q`
Expected: ~186 passed (177 + 1 modelo + 2 casos + 6 diseño combos; reportar el conteo exacto). `aci318.py`/`api/`/`viz/` no se tocaron y `diseno_elemento` creció de forma aditiva → 4b.1/4b.2 sin regresión. Si algo falla, STOP/BLOCKED.

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/diseno_elemento.py tests/test_combinaciones_diseno.py
git commit -m "feat(diseno): diseno por combo gobernante (disenar_viga/columna_combos)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §9)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~186); 4b.1/4b.2 (un caso) sin regresión.
2. `disenar_viga_combos`/`disenar_columna_combos` diseñan para todos los combos LRFD y reportan el gobernante;
   un modelo de un solo caso "D" reproduce el diseño con el combo 1.4D (viga: `combo_flexion == "1"`).

## Notas de revisión (plan vs. spec)

- **Aditivo:** `aci318.py`, `api/`, `viz/` no se tocan; `diseno_elemento` agrega `*_combos` al lado de las
  funciones de un solo caso (4b.1), que el visor (4b.2) sigue usando hasta 5A.2.
- **Etiquetas = kwargs:** las etiquetas de caso (D/L/Lr/S/R/W/E) son los kwargs de `combinaciones_resistencia`,
  así que `combinaciones_resistencia(**{caso: valor})` mapea directo y los ausentes caen a 0.
- **Gobernante columna** = mayor demanda/capacidad (pu/pmax, mu/φMn) a la sección elegida; el diseño cubre
  *todos* los combos (itera barras hasta que todos caen dentro del diagrama).
- **Cortante de viga gobernante** = combo de mayor |Vu| (diseñar para ese ⇒ menor s = el más exigente).
- **Retrocompat:** `caso="D"` default + funciones de un solo caso intactas ⇒ sin regresión en la suite.
