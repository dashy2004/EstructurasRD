# Fase 4b.1: diseño de columnas y vigas por fuerzas — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar los huecos de diseño de `aci318` (estribos, diagrama P-M, selección de barras) y orquestar el diseño del refuerzo de cada columna/viga a partir de su demanda real (Pu/Mu/Vu de `esfuerzos_elementos`).

**Architecture:** Aditivo en `normativa/aci318.py` (rutinas de diseño ACI puras) + un módulo nuevo `diseno_elemento.py` (orquestador que extrae la demanda de `EsfuerzosElemento`, convierte unidades y llama a aci318). Sin visor/endpoint.

**Tech Stack:** Python 3.11 + stdlib (sin NumPy). Unidades: aci318 en N/mm/MPa; orquestador convierte desde N/m/N·m.

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-fase4b1-diseno-por-fuerzas-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/normativa/aci318.py` (mod, **aditivo**) | `seleccionar_barras`, `disenar_estribo_viga`, `diagrama_interaccion`, `momento_capacidad`, `disenar_columna_pm` + dataclasses. |
| `src/motor_fea/diseno_elemento.py` (nuevo) | `disenar_viga`/`disenar_columna(esf, b, h, fc, fy, rec)` + `DisenoViga`. |
| `tests/test_diseno_marco.py` (nuevo) | Tests: estribo, barras, columna P-M, orquestador. |

---

## Task 1: aci318 — diseñador de estribos + selección de barras

**Files:**
- Modify: `src/motor_fea/normativa/aci318.py`
- Test: `tests/test_diseno_marco.py`

- [ ] **Step 1: Escribir los tests que fallan** — crear `tests/test_diseno_marco.py`:

```python
"""Tests del motor de diseño de pórtico (Fase 4b.1): estribos, barras, columna P-M, orquestador."""
import math

import pytest

from motor_fea.normativa import aci318


def test_estribo_no_requerido_cuando_vu_bajo():
    bw, d, fc = 300.0, 260.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    e = aci318.disenar_estribo_viga(0.1 * aci318.PHI_CORTANTE * vc, bw, d, fc)
    assert e.vs_requerido == 0.0
    assert e.cumple
    assert e.espaciamiento == pytest.approx(min(d / 2, 600.0))


def test_estribo_disenado_cumple():
    bw, d, fc = 300.0, 500.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    vu = 2.0 * aci318.PHI_CORTANTE * vc                 # requiere Vs > 0
    e = aci318.disenar_estribo_viga(vu, bw, d, fc)
    assert e.vs_requerido > 0
    assert 50.0 <= e.espaciamiento <= d / 2
    assert aci318.verificar_viga_cortante(vu, bw, d, fc, e.av, 420.0, e.espaciamiento).cumple
    assert e.cumple


def test_estribo_insuficiente_cuando_vu_enorme():
    bw, d, fc = 300.0, 400.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    vs_max = aci318.cortante_acero_maximo(bw, d, fc)
    vu = aci318.PHI_CORTANTE * (vc + 2.0 * vs_max)      # Vs_req > Vs_max
    e = aci318.disenar_estribo_viga(vu, bw, d, fc)
    assert not e.cumple
    assert "INSUFICIENTE" in e.disponer


def test_seleccionar_barras_cubre_as():
    sel = aci318.seleccionar_barras(600.0, 300.0, num=5)
    assert sel.n_barras >= 2
    assert sel.as_provista >= 600.0
    assert sel.cumple


def test_seleccionar_barras_as_nan_no_cumple():
    sel = aci318.seleccionar_barras(float("nan"), 300.0, 5)
    assert not sel.cumple
```

- [ ] **Step 2: Correr — esperar FAIL** (`AttributeError: module 'motor_fea.normativa.aci318' has no attribute 'disenar_estribo_viga'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q` (use `.venv/bin/pytest` si falta python/pytest).

- [ ] **Step 3: Implementar** — añadir al **final** de `src/motor_fea/normativa/aci318.py` (el módulo ya tiene `import math`, `from dataclasses import dataclass`, `AREAS_BARRA_MM2`, `PHI_CORTANTE`, `cortante_concreto`, `cortante_acero_maximo`, `verificar_viga_cortante`):

```python
# ===================== Diseño de pórtico (Fase 4b.1) =====================
def _diametro_barra(num: int) -> float:
    """Diámetro nominal de una barra #num (octavos de pulgada) en mm."""
    return num * 25.4 / 8.0


@dataclass(frozen=True)
class SeleccionBarras:
    numero_barra: int
    n_barras: int
    as_provista: float     # mm²
    cumple: bool


def seleccionar_barras(as_req: float, ancho_disponible: float, num: int = 5) -> SeleccionBarras:
    """Elige n barras #num (≥2) que cubran As y verifica que entren en el ancho disponible."""
    area = AREAS_BARRA_MM2[num]
    if math.isnan(as_req):                              # sección insuficiente a flexión
        return SeleccionBarras(num, 0, 0.0, False)
    n = max(2, math.ceil(as_req / area))
    as_provista = n * area
    d = _diametro_barra(num)
    entra = n * d + (n - 1) * 25.0 <= ancho_disponible  # separación libre mínima 25 mm
    return SeleccionBarras(num, n, as_provista, as_provista >= as_req and entra)


@dataclass(frozen=True)
class DisenoEstribo:
    numero_barra: int
    n_ramas: int
    espaciamiento: float   # mm
    av: float              # mm²
    vs_requerido: float    # N
    cumple: bool
    disponer: str


def disenar_estribo_viga(vu: float, bw: float, d: float, fc: float, fyt: float = 420.0,
                         num_estribo: int = 3, n_ramas: int = 2, lam: float = 1.0) -> DisenoEstribo:
    """Diseña los estribos de una viga para Vu (ACI 318-19 §22.5 / §9.6.3)."""
    if bw <= 0 or d <= 0 or fc <= 0 or fyt <= 0:
        raise ValueError("bw, d, fc y fyt deben ser positivos.")
    vu = abs(vu)
    vc = cortante_concreto(bw, d, fc, lam)
    av = n_ramas * AREAS_BARRA_MM2[num_estribo]
    s_max = min(d / 2.0, 600.0)
    vs_max = cortante_acero_maximo(bw, d, fc)
    if vu <= 0.5 * PHI_CORTANTE * vc:
        vs_req, s = 0.0, s_max                          # no requeridos por resistencia
    else:
        vs_req = vu / PHI_CORTANTE - vc
        if vs_req <= 0:
            s = s_max                                   # mínimos
        elif vs_req > vs_max:
            return DisenoEstribo(num_estribo, n_ramas, s_max, av, vs_req, False,
                                 "SECCIÓN INSUFICIENTE A CORTANTE")
        else:
            s = min(av * fyt * d / vs_req, s_max)
    s = max(50.0, math.floor(s / 25.0) * 25.0)          # múltiplo de 25 mm, ≥ 50
    cumple = verificar_viga_cortante(vu, bw, d, fc, av, fyt, s, lam).cumple
    return DisenoEstribo(num_estribo, n_ramas, s, av, vs_req, cumple,
                         f"E#{num_estribo} {n_ramas}R @ {s:.0f} mm")
```

- [ ] **Step 4: Correr — esperar PASS** (5 passed):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/normativa/aci318.py tests/test_diseno_marco.py
git commit -m "feat(aci318): disenar_estribo_viga + seleccionar_barras (diseno por demanda)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: aci318 — diagrama de interacción + diseñador de columna P-M

**Files:**
- Modify: `src/motor_fea/normativa/aci318.py`
- Test: `tests/test_diseno_marco.py`

- [ ] **Step 1: Escribir los tests que fallan** — añadir al final de `tests/test_diseno_marco.py`:

```python
def _capas_columna(b, h, rec, rho, num=8):
    as_total = rho * b * h
    dbar = aci318._diametro_barra(num)
    return [(rec + dbar / 2, as_total / 2), (h - rec - dbar / 2, as_total / 2)]


def test_diagrama_interaccion_tiene_n_puntos():
    diag = aci318.diagrama_interaccion(400, 400, 28, 420, _capas_columna(400, 400, 50, 0.02), n=40)
    assert len(diag) == 40
    assert all(isinstance(p, aci318.PuntoInteraccion) for p in diag)


def test_momento_capacidad_en_un_nodo():
    diag = aci318.diagrama_interaccion(400, 400, 28, 420, _capas_columna(400, 400, 50, 0.02), n=40)
    p = diag[30]                                        # punto de compresión
    assert aci318.momento_capacidad(p.phi_pn, diag) == pytest.approx(abs(p.phi_mn), rel=1e-6)


def test_columna_cumple_demanda_dentro_del_diagrama():
    b, h, fc, fy, rec = 400.0, 400.0, 28.0, 420.0, 50.0
    diag = aci318.diagrama_interaccion(b, h, fc, fy, _capas_columna(b, h, rec, 0.02))
    p = diag[30]
    pu = max(p.phi_pn, 1.0)
    cap = aci318.momento_capacidad(pu, diag)
    d = aci318.disenar_columna_pm(pu, 0.5 * cap, b, h, fc, fy, rec)
    assert d.cumple
    assert 0.01 <= d.rho <= 0.08


def test_columna_insuficiente_si_excede_rho_max():
    b, h, fc, fy, rec = 400.0, 400.0, 28.0, 420.0, 50.0
    diag = aci318.diagrama_interaccion(b, h, fc, fy, _capas_columna(b, h, rec, 0.08))
    p = diag[30]
    pu = max(p.phi_pn, 1.0)
    cap_max = aci318.momento_capacidad(pu, diag)
    d = aci318.disenar_columna_pm(pu, 1.5 * cap_max, b, h, fc, fy, rec)
    assert not d.cumple
```

- [ ] **Step 2: Correr — esperar FAIL** (`AttributeError: ... 'diagrama_interaccion'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 3: Implementar** — añadir al **final** de `src/motor_fea/normativa/aci318.py` (después de lo de Task 1; reusa `punto_interaccion`, `PuntoInteraccion`, `axial_maxima_diseno`, `AREAS_BARRA_MM2`):

```python
@dataclass(frozen=True)
class DisenoColumna:
    pu: float              # N
    mu: float              # N·mm
    numero_barra: int
    n_barras: int
    rho: float
    cumple: bool
    disponer: str


def diagrama_interaccion(b: float, h: float, fc: float, fy: float,
                         capas: list[tuple[float, float]], n: int = 40) -> list[PuntoInteraccion]:
    """Envolvente P-M: barre el eje neutro c de 0.05·h a 2·h en n puntos."""
    return [punto_interaccion(0.05 * h + (2.0 - 0.05) * h * k / (n - 1), b, h, fc, fy, capas)
            for k in range(n)]


def momento_capacidad(phi_pn_demanda: float, diagrama: list[PuntoInteraccion]) -> float:
    """φMn (N·mm) interpolado al nivel axial φPn demandado; 0 si está fuera del rango."""
    pares = sorted((p.phi_pn, abs(p.phi_mn)) for p in diagrama)
    if phi_pn_demanda < pares[0][0] or phi_pn_demanda > pares[-1][0]:
        return 0.0
    for (p0, m0), (p1, m1) in zip(pares, pares[1:]):
        if p0 <= phi_pn_demanda <= p1:
            return m0 if p1 == p0 else m0 + (m1 - m0) * (phi_pn_demanda - p0) / (p1 - p0)
    return 0.0


def disenar_columna_pm(pu: float, mu: float, b: float, h: float, fc: float, fy: float,
                       recubrimiento: float, num: int = 8) -> DisenoColumna:
    """Diseña el refuerzo longitudinal de una columna para (Pu, Mu) por diagrama de interacción.

    Itera el nº de barras desde ρ_min=1% (ACI 10.6.1.1) hasta ρ_max=8%; arma 2 capas (sup e inf) y
    verifica que (Pu, Mu) caiga dentro de la envolvente φ con el tope φPn,max.
    """
    if b <= 0 or h <= 0 or fc <= 0 or fy <= 0 or recubrimiento <= 0:
        raise ValueError("b, h, fc, fy y recubrimiento deben ser positivos.")
    if h - 2 * recubrimiento <= 0:
        raise ValueError("Recubrimiento incompatible con la sección.")
    pu, mu = abs(pu), abs(mu)
    ag = b * h
    area = AREAS_BARRA_MM2[num]
    d_barra = _diametro_barra(num)
    n = max(4, math.ceil(0.01 * ag / area))
    ultimo_n = n
    while n * area / ag <= 0.08:
        as_total = n * area
        capas = [(recubrimiento + d_barra / 2.0, as_total / 2.0),
                 (h - recubrimiento - d_barra / 2.0, as_total / 2.0)]
        diagrama = diagrama_interaccion(b, h, fc, fy, capas)
        if pu <= axial_maxima_diseno(ag, as_total, fc, fy) and mu <= momento_capacidad(pu, diagrama):
            return DisenoColumna(pu, mu, num, n, as_total / ag, True, f"{n}#{num}")
        ultimo_n = n
        n += 1
    return DisenoColumna(pu, mu, num, ultimo_n, ultimo_n * area / ag, False, "SECCIÓN INSUFICIENTE")
```

- [ ] **Step 4: Correr — esperar PASS** (9 passed: 5 de Task 1 + 4):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/normativa/aci318.py tests/test_diseno_marco.py
git commit -m "feat(aci318): diagrama de interaccion P-M + disenar_columna_pm

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Orquestador por elemento + verificación final

**Files:**
- Create: `src/motor_fea/diseno_elemento.py`
- Test: `tests/test_diseno_marco.py`

- [ ] **Step 1: Escribir los tests que fallan** — añadir al final de `tests/test_diseno_marco.py`:

```python
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.solver import esfuerzos_elementos, resolver
from motor_fea import diseno_elemento

_E, _NU, _L, _P = 2.0e10, 0.2, 3.0, 1000.0


def _voladizo(carga, lado=0.30):
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, _L, 0, 0)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=lado * lado, inercia_y=lado ** 4 / 12,
                               inercia_z=lado ** 4 / 12, constante_torsion=0.1406 * lado ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(carga)
    return m


def test_disenar_viga_voladizo_cumple():
    m = _voladizo(CargaNodal(2, fz=_P))
    esf = esfuerzos_elementos(m, resolver(m))[1]
    d = diseno_elemento.disenar_viga(esf, b=0.30, h=0.30)
    assert d.mu == pytest.approx(_P * _L, rel=1e-3)     # Mu ≈ P·L
    assert d.vu == pytest.approx(_P, rel=1e-3)          # Vu ≈ P
    assert d.flexion is not None and d.flexion.cumple
    assert d.estribo.cumple
    assert d.cumple


def test_disenar_columna_extrae_axial():
    # columna 0.40×0.40 por Z, axial de compresión modesto + lateral pequeño.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, _L)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-150000.0), CargaNodal(2, fx=5000.0)]
    esf = esfuerzos_elementos(m, resolver(m))[1]
    d = diseno_elemento.disenar_columna(esf, b=0.40, h=0.40, fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d.pu == pytest.approx(150000.0, rel=1e-3)    # axial extraído
    assert d.cumple
```

- [ ] **Step 2: Correr — esperar FAIL** (`ModuleNotFoundError: No module named 'motor_fea.diseno_elemento'`):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py::test_disenar_viga_voladizo_cumple -q`

- [ ] **Step 3: Implementar** — crear `src/motor_fea/diseno_elemento.py`:

```python
"""Diseño de refuerzo por elemento a partir de los esfuerzos del análisis (capa de composición).

Extrae la demanda (Pu, Mu, Vu) de un ``EsfuerzosElemento`` y la pasa a las rutinas de diseño de
``normativa.aci318``, convirtiendo de las unidades del modelo (N, m, N·m) a las de aci318 (N, mm, MPa).
"""
from __future__ import annotations

from dataclasses import dataclass

from motor_fea.core.solver import EsfuerzosElemento
from motor_fea.normativa import aci318


@dataclass(frozen=True)
class DisenoViga:
    mu: float                                  # N·m (demanda)
    vu: float                                  # N
    flexion: aci318.SeleccionBarras | None
    estribo: aci318.DisenoEstribo
    cumple: bool
    disponer: str


def _demanda(esf: EsfuerzosElemento, n: int = 21) -> tuple[float, float]:
    """(Mu, Vu) del diagrama: Mu = max|My|,|Mz|; Vu = max|Vy|,|Vz| (N·m, N)."""
    mu = vu = 0.0
    for _s, _n, vy, vz, _t, my, mz in esf.diagrama(n):
        mu = max(mu, abs(my), abs(mz))
        vu = max(vu, abs(vy), abs(vz))
    return mu, vu


def disenar_viga(esf: EsfuerzosElemento, b: float, h: float, fc: float = 21.0, fy: float = 420.0,
                 recubrimiento: float = 0.04) -> DisenoViga:
    """Diseña una viga (flexión + estribos) por la demanda de sus esfuerzos. b, h, rec en metros."""
    mu, vu = _demanda(esf)
    b_mm, d_mm = b * 1000.0, (h - recubrimiento) * 1000.0
    as_req, insuf = aci318.as_requerido_flexion(mu * 1000.0, b_mm, d_mm, fc, fy)
    as_dis = float("nan") if insuf else max(as_req, aci318.as_minimo_flexion(b_mm, d_mm, fc, fy))
    flexion = None if insuf else aci318.seleccionar_barras(as_dis, (b - 2 * recubrimiento) * 1000.0)
    estribo = aci318.disenar_estribo_viga(vu, b_mm, d_mm, fc, fy)
    cumple = (not insuf) and flexion is not None and flexion.cumple and estribo.cumple
    disponer = ("SECCIÓN INSUFICIENTE A FLEXIÓN" if insuf
                else f"{flexion.n_barras}#{flexion.numero_barra} + {estribo.disponer}")
    return DisenoViga(mu, vu, flexion, estribo, cumple, disponer)


def disenar_columna(esf: EsfuerzosElemento, b: float, h: float, fc: float = 21.0, fy: float = 420.0,
                    recubrimiento: float = 0.04) -> aci318.DisenoColumna:
    """Diseña una columna (P-M) por la demanda de sus esfuerzos. b, h, rec en metros."""
    mu, _vu = _demanda(esf)
    return aci318.disenar_columna_pm(abs(esf.axial), mu * 1000.0, b * 1000.0, h * 1000.0,
                                     fc, fy, recubrimiento * 1000.0)
```

- [ ] **Step 4: Correr los tests de diseño — esperar PASS** (11 passed):
`PYTHONPATH=src:tests python -m pytest tests/test_diseno_marco.py -q`

- [ ] **Step 5: Correr la suite completa (sin regresión)**
`PYTHONPATH=src:tests python -m pytest -q`
Expected: ~161 passed (153 + 8 nuevos). `aci318.py` solo creció de forma aditiva (no cambia los primitivos), así que `test_aci318.py`, `test_diseno_losa.py` y todo lo demás siguen verdes.

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/diseno_elemento.py tests/test_diseno_marco.py
git commit -m "feat(diseno): orquestador disenar_viga/columna desde esfuerzos por elemento

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §7)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~161 tests).
2. `disenar_viga`/`disenar_columna` producen armado que cumple la demanda real del voladizo, y marcan
   insuficiencia cuando la demanda excede la capacidad (ρ≤8% / Vs_max), verificado por los tests
   auto-consistentes de columna y los del diseñador de estribos.

## Notas de revisión (plan vs. spec)

- **Aditivo en normativa:** todo se agrega al final de `aci318.py`; los primitivos (`punto_interaccion`,
  `cortante_*`, `as_*_flexion`) no cambian → la regresión la verifica el Step 5 de Task 3.
- **Columna P-M real:** el diagrama se barre con `punto_interaccion` (la verdad del motor) y los tests son
  **auto-consistentes** (demanda derivada del propio diagrama), evitando números mágicos.
- **Unidades en la frontera:** `diseno_elemento` convierte m→mm y N·m→N·mm antes de aci318; `b/h/rec` se
  pasan en metros (como en `escena`/visor).
- **Insuficiencia = bandera:** flexión (NaN), cortante (Vs>Vs_max) y columna (ρ>8%) devuelven `cumple=False`
  con `disponer`, no excepción — patrón de `diseno_losa`.
```
