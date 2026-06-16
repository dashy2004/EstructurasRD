# Síntesis FEA · Columnas → malla — Plan de implementación (Rebanada B0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar `sintetizar(edificio) -> ModeloEstructural`: traducir columnas continuas autoradas a una malla FEA de barras con nodos compartidos, zapatas como apoyos, material/sección derivados, y losas como geometría inerte para el visor.

**Architecture:** Módulo nuevo `src/motor_fea/edificio/sintesis.py`, función pura sin I/O. Consume `motor_fea.edificio.modelo` (autoría) y produce `motor_fea.core.modelo.ModeloEstructural` (malla). Deduplicación de nodos por coordenada cuantizada a mm, de materiales por string, de secciones por dimensión. Asume edificio válido; garantiza salida que pasa `ModeloEstructural.validar()`.

**Tech Stack:** Python 3.11+, dataclasses stdlib, `math` (sin NumPy). Runner: `.venv/bin/pytest`.

**Spec:** `docs/superpowers/specs/2026-06-16-sintesis-fea-columnas-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/edificio/sintesis.py` | Crear: función `sintetizar` + helpers `material_a_E_pa`, sección/torsión, quiebres |
| `src/motor_fea/edificio/__init__.py` | Modificar: re-export de `sintetizar` y `material_a_E_pa` |
| `tests/test_sintesis_fea.py` | Crear: material, nodos/barras/sección, apoyos+dedup, losas, garantías |

---

## Task 1: Mapeo de material `H{n}` → E (Pa)

**Files:**
- Create: `src/motor_fea/edificio/sintesis.py`
- Test: `tests/test_sintesis_fea.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_sintesis_fea.py
"""Tests de la síntesis FEA (Rebanada B0: columnas → malla)."""
import math

import pytest


def test_material_H210_a_modulo_elastico():
    from motor_fea.edificio.sintesis import material_a_E_pa

    # H210: f'c = 210 kg/cm²; E = 15100·√210 kg/cm² → Pa
    esperado = 15100.0 * math.sqrt(210.0) * 98066.5
    assert material_a_E_pa("H210") == pytest.approx(esperado, rel=1e-9)
    assert material_a_E_pa("h210") == pytest.approx(esperado, rel=1e-9)   # case-insensitive
    assert material_a_E_pa("H210") == pytest.approx(2.146e10, rel=1e-3)


def test_material_invalido_lanza_valueerror():
    from motor_fea.edificio.sintesis import material_a_E_pa

    with pytest.raises(ValueError, match="no reconocido"):
        material_a_E_pa("madera")
    with pytest.raises(ValueError, match="no reconocido"):
        material_a_E_pa("HXY")
    with pytest.raises(ValueError, match="positivo"):
        material_a_E_pa("H0")
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -v`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.edificio.sintesis'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/sintesis.py
"""Síntesis FEA: modelo de autoría (Edificio) → malla estructural (ModeloEstructural).

Rebanada B0 — solo columnas: nodos compartidos por coordenada, barras entre
quiebres, zapata→apoyo, material/sección desde autoría, losas como geometría
inerte para el visor. Sin I/O, sin NumPy. Asume un edificio válido; garantiza
una salida que pasa ``ModeloEstructural.validar()``.
"""
from __future__ import annotations

import math

from motor_fea.core.modelo import (
    Apoyo,
    ElementoFrame,
    LosaViz,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.edificio.modelo import Columna, Edificio

_TOL = 6  # decimales de cuantización de coordenadas (≈ mm)

FACTOR_E_ACI = 15100.0     # E[kg/cm²] = 15100·√(f'c[kg/cm²])  (ACI 318, concreto)
KGF_CM2_A_PA = 98066.5     # 1 kgf/cm² en pascales


def material_a_E_pa(material: str) -> float:
    """Convierte un material de obra ``'H{n}'`` (f'c en kg/cm²) a E en pascales (ACI)."""
    s = material.strip().upper()
    fc = None
    if s.startswith("H"):
        try:
            fc = float(s[1:])
        except ValueError:
            fc = None
    if fc is None:
        raise ValueError(f"Material no reconocido: {material!r} (se espera 'H<f'c en kg/cm²>').")
    if fc <= 0:
        raise ValueError(f"Material {material!r}: f'c debe ser positivo.")
    return FACTOR_E_ACI * math.sqrt(fc) * KGF_CM2_A_PA
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/sintesis.py tests/test_sintesis_fea.py
git commit -m "feat(B0): mapeo de material H{n} a módulo elástico (ACI)"
```

---

## Task 2: `sintetizar` — nodos compartidos + barras + sección

**Files:**
- Modify: `src/motor_fea/edificio/sintesis.py` (añadir helpers de sección/quiebres + `sintetizar`)
- Test: `tests/test_sintesis_fea.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_sintesis_fea.py  (añadir)
def _columna_3niveles(con_zapata=False):
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel, Zapata
    col = Columna(id=1, posicion=(0.0, 0.0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=6.0, material="H210",
                  zapata=Zapata(1.2, 1.2, 0.4) if con_zapata else None)
    edi = Edificio(id=1, nombre="Bloque A",
                   niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0), Nivel(3, "N3", 6.0)],
                   elementos_verticales=[col])
    return edi


def test_columna_continua_genera_nodos_compartidos_y_barras():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles())

    # 3 nodos en (0,0,z) para z = 0, 3, 6
    assert sorted(n.z for n in m.nodos) == [0.0, 3.0, 6.0]
    assert all((n.x, n.y) == (0.0, 0.0) for n in m.nodos)
    # 2 barras consecutivas que comparten el nodo intermedio (z=3)
    assert len(m.elementos) == 2
    z = {n.id: n.z for n in m.nodos}
    e1, e2 = m.elementos
    assert z[e1.nodo_j] == 3.0 and z[e2.nodo_i] == 3.0      # nodo z=3 compartido
    assert e1.nodo_j == e2.nodo_i
    # sin zapata → sin apoyos
    assert m.apoyos == []


def test_seccion_cuadrada_propiedades():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles())
    assert len(m.secciones) == 1
    s = m.secciones[0]
    assert s.area == pytest.approx(0.30 * 0.30)
    assert s.inercia_y == pytest.approx(0.30**4 / 12)
    assert s.inercia_z == pytest.approx(0.30**4 / 12)
    assert s.constante_torsion == pytest.approx(0.1406 * 0.30**4, rel=1e-2)
    # un solo material, con el E de H210
    assert len(m.materiales) == 1
    assert m.materiales[0].E == pytest.approx(15100.0 * math.sqrt(210.0) * 98066.5)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -k "nodos_compartidos or seccion_cuadrada" -v`
Expected: FAIL con `ImportError: cannot import name 'sintetizar'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/sintesis.py  (añadir tras material_a_E_pa)
def _torsion_rectangular(base: float, peralte: float) -> float:
    """Constante de torsión J de una sección rectangular (β≈0.1406 para cuadrada)."""
    a = max(base, peralte)
    t = min(base, peralte)
    return a * t**3 * (1.0 / 3.0 - 0.21 * (t / a) * (1.0 - t**4 / (12.0 * a**4)))


def _propiedades_seccion(col: "Columna") -> tuple:
    """(area, inercia_y, inercia_z, J) de una columna rectangular base×peralte."""
    b, h = col.base, col.peralte
    return (b * h, h * b**3 / 12.0, b * h**3 / 12.0, _torsion_rectangular(b, h))


def _quiebres(col: "Columna", cotas_nivel: list) -> list:
    """Cotas Z donde la columna necesita un nodo: extremos + niveles intermedios."""
    qs = {round(col.cota_base, _TOL), round(col.cota_tope, _TOL)}
    for c in cotas_nivel:
        if col.cota_base < c < col.cota_tope:
            qs.add(round(c, _TOL))
    return sorted(qs)


def sintetizar(edificio: Edificio) -> ModeloEstructural:
    """Traduce un edificio autorado a una malla FEA (Rebanada B0: solo columnas)."""
    modelo = ModeloEstructural()
    cotas_nivel = [n.cota for n in edificio.niveles_ordenados()]

    nodos_por_coord: dict[tuple, int] = {}
    material_por_str: dict[str, int] = {}
    seccion_por_dim: dict[tuple, int] = {}

    def _nodo(x: float, y: float, z: float) -> int:
        key = (round(x, _TOL), round(y, _TOL), round(z, _TOL))
        if key not in nodos_por_coord:
            nid = len(nodos_por_coord) + 1
            nodos_por_coord[key] = nid
            modelo.nodos.append(Nodo(nid, key[0], key[1], key[2]))
        return nodos_por_coord[key]

    def _material(s: str) -> int:
        if s not in material_por_str:
            mid = len(material_por_str) + 1
            material_por_str[s] = mid
            modelo.materiales.append(Material(mid, E=material_a_E_pa(s)))
        return material_por_str[s]

    def _seccion(col: Columna) -> int:
        key = (round(col.base, _TOL), round(col.peralte, _TOL))
        if key not in seccion_por_dim:
            sid = len(seccion_por_dim) + 1
            seccion_por_dim[key] = sid
            area, iy, iz, j = _propiedades_seccion(col)
            modelo.secciones.append(
                Seccion(sid, area=area, inercia_y=iy, inercia_z=iz, constante_torsion=j))
        return seccion_por_dim[key]

    for col in edificio.elementos_verticales:
        if not isinstance(col, Columna):
            continue  # muros fuera de alcance (B0)
        x, y = col.posicion
        mat_id = _material(col.material)
        sec_id = _seccion(col)
        nodos_col = [_nodo(x, y, z) for z in _quiebres(col, cotas_nivel)]
        for ni, nj in zip(nodos_col, nodos_col[1:]):
            eid = len(modelo.elementos) + 1
            modelo.elementos.append(ElementoFrame(eid, ni, nj, mat_id, sec_id))

    return modelo
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -k "nodos_compartidos or seccion_cuadrada" -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/sintesis.py tests/test_sintesis_fea.py
git commit -m "feat(B0): sintetizar columnas → nodos compartidos + barras + sección"
```

---

## Task 3: Zapata → apoyo empotrado + deduplicación de nodo base

**Files:**
- Modify: `src/motor_fea/edificio/sintesis.py` (añadir apoyos a `sintetizar`)
- Test: `tests/test_sintesis_fea.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_sintesis_fea.py  (añadir)
def test_zapata_genera_empotramiento_en_base():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_columna_3niveles(con_zapata=True))
    assert len(m.apoyos) == 1
    z = {n.id: n.z for n in m.nodos}
    apoyo = m.apoyos[0]
    assert z[apoyo.nodo_id] == 0.0                       # apoyo en la base (cota_base)
    assert apoyo.restricciones() == (True,) * 6          # empotrado


def test_columnas_que_comparten_base_deduplican_nodo_y_apoyo():
    from motor_fea.edificio.modelo import Columna, Edificio, Nivel, Zapata
    from motor_fea.edificio.sintesis import sintetizar

    # dos columnas distintas con MISMA posición de base (apiladas en niveles distintos)
    inf = Columna(id=1, posicion=(2.0, 2.0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=3.0, material="H210",
                  zapata=Zapata(1.0, 1.0, 0.4))
    sup = Columna(id=2, posicion=(2.0, 2.0), base=0.30, peralte=0.30,
                  cota_base=3.0, cota_tope=6.0, material="H210")
    edi = Edificio(id=1, nombre="A",
                   niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0), Nivel(3, "N3", 6.0)],
                   elementos_verticales=[inf, sup])
    m = sintetizar(edi)

    # nodos en z=0,3,6 todos en (2,2): el nodo z=3 lo comparten ambas columnas
    assert sorted(n.z for n in m.nodos) == [0.0, 3.0, 6.0]
    assert len(m.elementos) == 2
    assert len(m.apoyos) == 1                            # solo la columna inferior tiene zapata
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -k "empotramiento or comparten_base" -v`
Expected: FAIL — `test_zapata_genera_empotramiento_en_base` falla con `assert len(m.apoyos) == 1` (apoyos vacíos)

- [ ] **Step 3: Write minimal implementation**

Añadir un set de control antes del bucle de columnas y emitir el apoyo dentro del bucle.

En `sintetizar`, tras `seccion_por_dim: dict[tuple, int] = {}` añadir:

```python
    apoyos_nodos: set[int] = set()
```

Y dentro del bucle `for col in ...`, después de crear `nodos_col` y las barras, añadir:

```python
        if col.zapata is not None:
            base_nid = nodos_col[0]   # quiebre más bajo = cota_base
            if base_nid not in apoyos_nodos:
                apoyos_nodos.add(base_nid)
                modelo.apoyos.append(Apoyo.empotrado(base_nid))
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -k "empotramiento or comparten_base" -v`
Expected: PASS (2 passed)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/sintesis.py tests/test_sintesis_fea.py
git commit -m "feat(B0): zapata → apoyo empotrado en base, deduplicado por nodo"
```

---

## Task 4: Losas → `LosaViz` (geometría inerte para el visor)

**Files:**
- Modify: `src/motor_fea/edificio/sintesis.py` (añadir losas a `sintetizar`)
- Test: `tests/test_sintesis_fea.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_sintesis_fea.py  (añadir)
def test_losas_se_transportan_como_geometria_a_su_cota():
    from motor_fea.edificio.modelo import Columna, Edificio, Losa, Nivel
    from motor_fea.edificio.sintesis import sintetizar

    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)))
    edi = Edificio(id=1, nombre="A",
                   niveles=[Nivel(1, "N1", 0.0),
                            Nivel(2, "N2", 3.0, (losa,))],   # losa en el nivel z=3
                   elementos_verticales=[
                       Columna(id=1, posicion=(0, 0), base=0.3, peralte=0.3,
                               cota_base=0.0, cota_tope=3.0, material="H210")])
    m = sintetizar(edi)

    assert len(m.losas) == 1
    pts = m.losas[0].puntos
    assert all(z == 3.0 for (_x, _y, z) in pts)          # elevada a la cota del nivel
    assert pts[0] == [0, 0, 3.0]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py::test_losas_se_transportan_como_geometria_a_su_cota -v`
Expected: FAIL con `assert len(m.losas) == 1` (lista vacía)

- [ ] **Step 3: Write minimal implementation**

En `sintetizar`, antes de `return modelo`, añadir:

```python
    for nivel in edificio.niveles_ordenados():
        for losa in nivel.losas:
            vid = len(modelo.losas) + 1
            modelo.losas.append(LosaViz(vid, nivel.puntos_losa_3d(losa)))
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py::test_losas_se_transportan_como_geometria_a_su_cota -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/sintesis.py tests/test_sintesis_fea.py
git commit -m "feat(B0): losas → LosaViz inerte a la cota del nivel"
```

---

## Task 5: Garantías (salida válida + determinismo) + re-export público

**Files:**
- Modify: `src/motor_fea/edificio/__init__.py` (re-exports)
- Test: `tests/test_sintesis_fea.py`

- [ ] **Step 1: Write the failing test**

```python
# tests/test_sintesis_fea.py  (añadir)
def _edificio_demo():
    from motor_fea.edificio.modelo import (
        CargasLosa, Columna, Edificio, Losa, Nivel, Zapata,
    )
    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)),
                cargas=CargasLosa(1.5, 2.0))
    return Edificio(
        id=1, nombre="Bloque A",
        niveles=[Nivel(1, "N1", 0.0, (losa,)), Nivel(2, "N2", 3.0), Nivel(3, "N3", 6.0)],
        elementos_verticales=[
            Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                    cota_base=0.0, cota_tope=6.0, material="H210",
                    zapata=Zapata(1.2, 1.2, 0.4)),
            Columna(id=2, posicion=(5, 0), base=0.30, peralte=0.30,
                    cota_base=0.0, cota_tope=6.0, material="H280",
                    zapata=Zapata(1.2, 1.2, 0.4)),
        ])


def test_salida_pasa_validacion_de_integridad():
    from motor_fea.edificio.sintesis import sintetizar

    m = sintetizar(_edificio_demo())
    assert m.validar() == []
    assert m.es_valido() is True


def test_sintesis_es_determinista():
    from motor_fea.edificio.sintesis import sintetizar

    edi = _edificio_demo()
    a, b = sintetizar(edi), sintetizar(edi)
    assert [(n.id, n.x, n.y, n.z) for n in a.nodos] == [(n.id, n.x, n.y, n.z) for n in b.nodos]
    assert [(e.id, e.nodo_i, e.nodo_j) for e in a.elementos] == \
           [(e.id, e.nodo_i, e.nodo_j) for e in b.elementos]


def test_reexport_publico_desde_paquete_edificio():
    from motor_fea.edificio import material_a_E_pa, sintetizar  # noqa: F401
    assert callable(sintetizar) and callable(material_a_E_pa)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/pytest tests/test_sintesis_fea.py -k "validacion_de_integridad or determinista or reexport" -v`
Expected: FAIL — `test_reexport_publico...` falla con `ImportError` (aún no re-exportado). Los otros dos deberían pasar ya (la funcionalidad existe); si alguno falla, corregir antes de continuar.

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/edificio/__init__.py  (añadir al final)
from motor_fea.edificio.sintesis import (  # noqa: E402
    material_a_E_pa,
    sintetizar,
)
```

- [ ] **Step 4: Run la suite completa**

Run: `.venv/bin/pytest -q`
Expected: PASS — 241 previos + los nuevos de `test_sintesis_fea.py`, todos verdes.

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/edificio/__init__.py tests/test_sintesis_fea.py
git commit -m "feat(B0): re-export público + garantías de validez y determinismo"
```

---

## Self-review (cobertura de la spec)

| Requisito de la spec | Task |
|---|---|
| Módulo `sintesis.py` con `sintetizar()` puro | Task 2 |
| Traducción 1: nodos por quiebre + nodos compartidos | Task 2 (`_quiebres`, `_nodo` dedup) |
| Traducción 2: barras entre quiebres consecutivos | Task 2 |
| Traducción 3: zapata → apoyo, dedup por nodo | Task 3 |
| Traducción 4: material `H{n}` → E (ACI), dedup + error | Task 1 |
| Traducción 5: sección base×peralte → A/Iy/Iz/J | Task 2 (`_propiedades_seccion`, `_torsion_rectangular`) |
| Traducción 6: losa → `LosaViz` a la cota | Task 4 |
| Garantía: salida pasa `validar()` | Task 5 |
| Garantía: determinismo | Task 5 |
| Re-export desde `__init__` | Task 5 |
| Muros fuera de alcance | Task 2 (`isinstance(col, Columna)` filtra muros) |

**Follow-ups (fuera de B0):** muros (barra equivalente o placa), bajada de cargas (Rebanada B), fusión multi-edificio, síntesis a nivel `Proyecto`.
