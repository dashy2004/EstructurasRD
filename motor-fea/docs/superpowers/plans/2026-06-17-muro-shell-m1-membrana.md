# M1 — Elemento de membrana (Q4 tensión plana) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir un elemento finito Q4 isoparamétrico de tensión plana (membrana) autónomo, con matriz de rigidez 8×8 y recuperación del campo de esfuerzos en el plano.

**Architecture:** Un módulo nuevo `src/motor_fea/core/membrana.py` de funciones puras (stdlib, sin NumPy), espejo de `core/placa.py`. Funciones de forma bilineales, jacobiano isoparamétrico, integración Gauss 2×2. No toca el solver ni la síntesis del edificio; M2 lo consumirá después.

**Tech Stack:** Python 3.14, stdlib `math`, pytest. Sin NumPy.

## Global Constraints

- Unidades SI: longitudes en m, E en Pa, t en m → rigidez en N/m, esfuerzos en Pa (N/m²).
- Stdlib puro: solo `import math`. Sin NumPy, sin I/O.
- 2 GDL/nodo (ux, uy); orden de GDL local: `[ux1, uy1, ux2, uy2, ux3, uy3, ux4, uy4]`.
- Orden de nodos: antihorario, esquinas naturales (−1,−1),(1,−1),(1,1),(−1,1) — consistente con `placa.py`.
- La suite completa (hoy 276 verde) debe seguir verde tras cada commit.
- Docstrings y anotaciones de tipo al estilo de `placa.py`; el docstring del módulo documenta la limitación de shear locking.

## File Structure

- **Create:** `src/motor_fea/core/membrana.py` — el elemento completo (constitutiva, helpers de forma/jacobiano/B, rigidez, esfuerzos).
- **Create:** `tests/test_membrana.py` — los 8 tests del spec.
- **Modify:** ninguno. M1 no toca archivos existentes.

---

### Task 1: Constitutiva de tensión plana

**Files:**
- Create: `src/motor_fea/core/membrana.py`
- Test: `tests/test_membrana.py`

**Interfaces:**
- Consumes: nada.
- Produces: `constitutiva_plana(E: float, nu: float) -> list[list[float]]` — matriz D 3×3.

- [ ] **Step 1: Write the failing test**

```python
# tests/test_membrana.py
import math

import pytest

from motor_fea.core.membrana import constitutiva_plana


def test_constitutiva_plana_valores_cerrados():
    E, nu = 2.0e10, 0.2
    D = constitutiva_plana(E, nu)
    factor = E / (1.0 - nu * nu)
    assert D[0][0] == pytest.approx(factor)
    assert D[1][1] == pytest.approx(factor)
    assert D[0][1] == pytest.approx(factor * nu)
    assert D[1][0] == pytest.approx(factor * nu)
    assert D[2][2] == pytest.approx(factor * (1.0 - nu) / 2.0)
    assert D[0][2] == 0.0 and D[1][2] == 0.0
    assert D[2][0] == 0.0 and D[2][1] == 0.0
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/python -m pytest tests/test_membrana.py::test_constitutiva_plana_valores_cerrados -v`
Expected: FAIL — `ModuleNotFoundError: No module named 'motor_fea.core.membrana'`

- [ ] **Step 3: Write minimal implementation**

```python
# src/motor_fea/core/membrana.py
"""Elemento de membrana Q4 (tensión plana) — capa 1, análisis FEM.

Cuadrilátero isoparamétrico bilineal de 4 nodos, 2 GDL/nodo (ux, uy) = 8 GDL,
para el comportamiento **en el plano** de muros de cortante. Funciones de forma
bilineales, jacobiano general (admite cuadriláteros no rectangulares), rigidez
por cuadratura de Gauss 2×2.

Hipótesis de tensión plana (σzz≈0): correcta para muros delgados. Unidades SI:
longitudes en m, E en Pa, t en m → rigidez en N/m, esfuerzos en Pa.

Orden de GDL local: [ux1,uy1, ux2,uy2, ux3,uy3, ux4,uy4]. Nodos en orden
antihorario, esquinas naturales (-1,-1),(1,-1),(1,1),(-1,1).

LIMITACIÓN: el Q4 bilineal sufre *shear locking* parásito en flexión en el
plano para muros muy esbeltos con malla gruesa. Se mitiga mallando (rebanada
M3); un Q4 con modos incompatibles queda como mejora futura.
"""
from __future__ import annotations

import math


def constitutiva_plana(E: float, nu: float) -> list[list[float]]:
    """Matriz constitutiva de tensión plana D (3×3): σ = D·ε, ε=[εxx,εyy,γxy]."""
    f = E / (1.0 - nu * nu)
    return [[f, f * nu, 0.0],
            [f * nu, f, 0.0],
            [0.0, 0.0, f * (1.0 - nu) / 2.0]]
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/python -m pytest tests/test_membrana.py::test_constitutiva_plana_valores_cerrados -v`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/membrana.py tests/test_membrana.py
git commit -m "feat(M1): constitutiva de tensión plana del elemento de membrana"
```

---

### Task 2: Rigidez del elemento + modos de cuerpo rígido + guarda de degeneración

**Files:**
- Modify: `src/motor_fea/core/membrana.py`
- Test: `tests/test_membrana.py`

**Interfaces:**
- Consumes: `constitutiva_plana` (Task 1).
- Produces:
  - `rigidez_membrana(nodos_xy: list[tuple[float, float]], E: float, nu: float, t: float) -> list[list[float]]` — K 8×8.
  - `matvec(K: list[list[float]], x: list[float]) -> list[float]` — producto matriz·vector.
  - Helpers internos: `_derivadas_forma(xi, eta) -> list[tuple[float, float]]`, `_matriz_B(nodos_xy, xi, eta) -> tuple[list[list[float]], float]` (devuelve (B 3×8, detJ); lanza `ValueError` si detJ≤0).

- [ ] **Step 1: Write the failing tests**

```python
# tests/test_membrana.py  (añadir imports y tests)
from motor_fea.core.membrana import (
    constitutiva_plana,
    matvec,
    rigidez_membrana,
)

# Cuadrado unitario de referencia para varios tests.
_CUADRADO = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
_E, _NU, _T = 2.0e10, 0.2, 0.25


def test_rigidez_simetrica():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    assert len(K) == 8 and all(len(fila) == 8 for fila in K)
    for i in range(8):
        for j in range(8):
            assert K[i][j] == pytest.approx(K[j][i], rel=1e-9, abs=1.0)


def test_modos_cuerpo_rigido_traslacion():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    trans_x = [1.0, 0.0] * 4          # ux=1 en los 4 nodos
    trans_y = [0.0, 1.0] * 4          # uy=1 en los 4 nodos
    for u in (trans_x, trans_y):
        f = matvec(K, u)
        assert all(abs(fi) < 1e-3 for fi in f)


def test_modo_cuerpo_rigido_rotacion():
    # Rotación infinitesimal en torno al origen: u=-y, v=x → deformación nula.
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    u_rot = []
    for (x, y) in _CUADRADO:
        u_rot.extend([-y, x])
    f = matvec(K, u_rot)
    assert all(abs(fi) < 1e-3 for fi in f)


def test_rigidez_degenerada_lanza():
    horario = [(0.0, 0.0), (0.0, 1.0), (1.0, 1.0), (1.0, 0.0)]  # orden horario
    with pytest.raises(ValueError):
        rigidez_membrana(horario, _E, _NU, _T)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `.venv/bin/python -m pytest tests/test_membrana.py -v`
Expected: FAIL — `ImportError: cannot import name 'rigidez_membrana'` (o `matvec`)

- [ ] **Step 3: Write minimal implementation**

Añadir a `src/motor_fea/core/membrana.py`:

```python
# Esquinas en coordenadas naturales, orden antihorario.
_ESQUINAS = ((-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0))
# Puntos y pesos de Gauss 2×2.
_G = 1.0 / math.sqrt(3.0)
_GAUSS2 = ((-_G, 1.0), (_G, 1.0))


def matvec(K: list[list[float]], x: list[float]) -> list[float]:
    """Producto matriz·vector."""
    return [sum(K[i][j] * x[j] for j in range(len(x))) for i in range(len(K))]


def _derivadas_forma(xi: float, eta: float) -> list[tuple[float, float]]:
    """(∂N/∂ξ, ∂N/∂η) de las 4 funciones bilineales en (ξ, η)."""
    out = []
    for (xa, ea) in _ESQUINAS:
        dndxi = 0.25 * xa * (1.0 + ea * eta)
        dndeta = 0.25 * ea * (1.0 + xa * xi)
        out.append((dndxi, dndeta))
    return out


def _matriz_B(nodos_xy: list[tuple[float, float]], xi: float, eta: float
              ) -> tuple[list[list[float]], float]:
    """Matriz B (3×8) y detJ en (ξ, η). Lanza ValueError si detJ ≤ 0."""
    d = _derivadas_forma(xi, eta)
    j00 = sum(d[a][0] * nodos_xy[a][0] for a in range(4))
    j01 = sum(d[a][0] * nodos_xy[a][1] for a in range(4))
    j10 = sum(d[a][1] * nodos_xy[a][0] for a in range(4))
    j11 = sum(d[a][1] * nodos_xy[a][1] for a in range(4))
    detJ = j00 * j11 - j01 * j10
    if detJ <= 0.0:
        raise ValueError(
            f"Jacobiano no positivo (detJ={detJ:.3e}): nodos colineales o en "
            f"orden horario. Se esperan 4 nodos en orden antihorario.")
    # Inversa del jacobiano.
    i00, i01 = j11 / detJ, -j01 / detJ
    i10, i11 = -j10 / detJ, j00 / detJ
    B = [[0.0] * 8 for _ in range(3)]
    for a in range(4):
        dndx = i00 * d[a][0] + i01 * d[a][1]
        dndy = i10 * d[a][0] + i11 * d[a][1]
        B[0][2 * a] = dndx
        B[1][2 * a + 1] = dndy
        B[2][2 * a] = dndy
        B[2][2 * a + 1] = dndx
    return B, detJ


def rigidez_membrana(nodos_xy: list[tuple[float, float]],
                     E: float, nu: float, t: float) -> list[list[float]]:
    """Matriz de rigidez 8×8 del elemento Q4 de tensión plana de espesor t."""
    D = constitutiva_plana(E, nu)
    K = [[0.0] * 8 for _ in range(8)]
    for xi, wxi in _GAUSS2:
        for eta, weta in _GAUSS2:
            B, detJ = _matriz_B(nodos_xy, xi, eta)
            peso = wxi * weta * detJ * t
            DB = [[sum(D[r][k] * B[k][c] for k in range(3)) for c in range(8)]
                  for r in range(3)]
            for a in range(8):
                for b in range(8):
                    s = 0.0
                    for r in range(3):
                        s += B[r][a] * DB[r][b]
                    K[a][b] += peso * s
    return K
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `.venv/bin/python -m pytest tests/test_membrana.py -v`
Expected: PASS (5 tests: el de Task 1 + los 4 nuevos)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/membrana.py tests/test_membrana.py
git commit -m "feat(M1): rigidez Q4 de membrana + modos rígidos + guarda de jacobiano"
```

---

### Task 3: Recuperación de esfuerzos + patch test + tracción + cortante

**Files:**
- Modify: `src/motor_fea/core/membrana.py`
- Test: `tests/test_membrana.py`

**Interfaces:**
- Consumes: `constitutiva_plana`, `_matriz_B` (Tasks 1–2), `motor_fea.core.solver.resolver_lineal` (existente, solo en el test).
- Produces: `esfuerzos_elemento(nodos_xy, E, nu, d_elem, xi=0.0, eta=0.0) -> tuple[float, float, float]` — (σxx, σyy, τxy) en Pa.

- [ ] **Step 1: Write the failing tests**

```python
# tests/test_membrana.py  (añadir imports y tests)
from motor_fea.core.membrana import esfuerzos_elemento
from motor_fea.core.solver import resolver_lineal


def test_patch_esfuerzo_constante():
    # Campo de desplazamiento lineal u=a+b·x+c·y, v=d+e·x+f·y → deformación
    # constante → esfuerzo constante exacto en el centro.
    b, c, e, f = 1e-4, 2e-5, -3e-5, 4e-5
    d_elem = []
    for (x, y) in _CUADRADO:
        d_elem.extend([0.001 + b * x + c * y, -0.002 + e * x + f * y])
    eps = [b, f, c + e]                      # [εxx, εyy, γxy]
    D = constitutiva_plana(_E, _NU)
    esperado = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(esperado[0], rel=1e-6)
    assert syy == pytest.approx(esperado[1], rel=1e-6)
    assert txy == pytest.approx(esperado[2], rel=1e-6)


def test_cortante_puro():
    # u = gamma·y, v = 0 → γxy = gamma, εxx = εyy = 0 → τxy = G·gamma.
    gamma = 1e-4
    d_elem = []
    for (x, y) in _CUADRADO:
        d_elem.extend([gamma * y, 0.0])
    G = _E / (2.0 * (1.0 + _NU))
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(0.0, abs=1.0)
    assert syy == pytest.approx(0.0, abs=1.0)
    assert txy == pytest.approx(G * gamma, rel=1e-6)


def test_traccion_uniaxial_resuelta():
    # Cuadrado L×L×t. Apoyos: nodo0 (ux,uy)=0, nodo3 ux=0 (permite Poisson).
    # Carga P/2 en +x sobre los nodos 1 y 2 (borde derecho).
    # Q4 reproduce exacto el estado de esfuerzo constante: σxx = P/(L·t).
    L, P = 1.0, 1.0e6
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    # GDL globales = locales (un solo elemento). Índices: nodo n → 2n (ux), 2n+1 (uy).
    fijos = {0, 1, 7}                        # ux0, uy0, ux3
    libres = [d for d in range(8) if d not in fijos]
    F = [0.0] * 8
    F[2] += P / 2.0                          # ux nodo1
    F[4] += P / 2.0                          # ux nodo2
    Kff = [[K[i][j] for j in libres] for i in libres]
    Ff = [F[i] for i in libres]
    uf = resolver_lineal(Kff, Ff)
    u = [0.0] * 8
    for pos, dgl in enumerate(libres):
        u[dgl] = uf[pos]
    sxx, syy, txy = esfuerzos_elemento(_CUADRADO, _E, _NU, u, 0.0, 0.0)
    assert sxx == pytest.approx(P / (L * _T), rel=1e-6)
    assert syy == pytest.approx(0.0, abs=P / (L * _T) * 1e-6)
    assert txy == pytest.approx(0.0, abs=P / (L * _T) * 1e-6)
    # Alargamiento del borde derecho: εxx·L = (σxx/E)·L.
    assert u[2] == pytest.approx((P / (L * _T)) / _E * L, rel=1e-6)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `.venv/bin/python -m pytest tests/test_membrana.py -v`
Expected: FAIL — `ImportError: cannot import name 'esfuerzos_elemento'`

- [ ] **Step 3: Write minimal implementation**

Añadir a `src/motor_fea/core/membrana.py`:

```python
def esfuerzos_elemento(nodos_xy: list[tuple[float, float]],
                       E: float, nu: float, d_elem: list[float],
                       xi: float = 0.0, eta: float = 0.0
                       ) -> tuple[float, float, float]:
    """Esfuerzos (σxx, σyy, τxy) en Pa en el punto natural (ξ, η).

    ``d_elem`` = 8 GDL nodales [ux,uy ×4]. σ = D·B·d_elem. El centro (0,0) es el
    punto de superconvergencia del Q4 (mejor precisión).
    """
    B, _ = _matriz_B(nodos_xy, xi, eta)
    D = constitutiva_plana(E, nu)
    eps = [sum(B[r][k] * d_elem[k] for k in range(8)) for r in range(3)]
    sig = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    return (sig[0], sig[1], sig[2])
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `.venv/bin/python -m pytest tests/test_membrana.py -v`
Expected: PASS (8 tests acumulados)

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/membrana.py tests/test_membrana.py
git commit -m "feat(M1): recuperación de esfuerzos + patch test, tracción y cortante"
```

---

### Task 4: Cuadrilátero no rectangular (jacobiano general)

**Files:**
- Test: `tests/test_membrana.py`

**Interfaces:**
- Consumes: `rigidez_membrana`, `esfuerzos_elemento`, `constitutiva_plana` (Tasks 1–3).
- Produces: nada (test de cierre que valida el jacobiano sobre un cuadrilátero general).

- [ ] **Step 1: Write the failing test**

```python
# tests/test_membrana.py  (añadir test)
def test_cuadrilatero_no_rectangular_patch():
    # Trapecio (jacobiano variable). K simétrica + patch de esfuerzo constante.
    trapecio = [(0.0, 0.0), (2.0, 0.0), (1.5, 1.0), (0.2, 1.0)]
    K = rigidez_membrana(trapecio, _E, _NU, _T)
    for i in range(8):
        for j in range(8):
            assert K[i][j] == pytest.approx(K[j][i], rel=1e-9, abs=1.0)
    b, c, e, f = 1e-4, 2e-5, -3e-5, 4e-5
    d_elem = []
    for (x, y) in trapecio:
        d_elem.extend([0.001 + b * x + c * y, -0.002 + e * x + f * y])
    eps = [b, f, c + e]
    D = constitutiva_plana(_E, _NU)
    esperado = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    sxx, syy, txy = esfuerzos_elemento(trapecio, _E, _NU, d_elem, 0.0, 0.0)
    assert sxx == pytest.approx(esperado[0], rel=1e-6)
    assert syy == pytest.approx(esperado[1], rel=1e-6)
    assert txy == pytest.approx(esperado[2], rel=1e-6)
```

- [ ] **Step 2: Run test to verify it fails (or passes if implementation is general)**

Run: `.venv/bin/python -m pytest tests/test_membrana.py::test_cuadrilatero_no_rectangular_patch -v`
Expected: PASS — la implementación de Task 2/3 ya es isoparamétrica general; este test es la verificación explícita. Si FALLA, hay un error en el jacobiano de `_matriz_B`.

- [ ] **Step 3: (Solo si falló) corregir `_matriz_B`**

Revisar el cálculo del jacobiano y su inversa en `_matriz_B`. No se espera cambio si Task 2 está correcto.

- [ ] **Step 4: Run the full suite**

Run: `.venv/bin/python -m pytest -q`
Expected: PASS — 276 previos + 9 nuevos de membrana, sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add tests/test_membrana.py
git commit -m "test(M1): patch test sobre cuadrilátero no rectangular (jacobiano general)"
```

---

## Self-Review

**Spec coverage:**
- Constitutiva tensión plana → Task 1 ✓
- `rigidez_membrana` 8×8 Gauss 2×2 → Task 2 ✓
- `esfuerzos_elemento` → Task 3 ✓
- Cuadriláteros generales (jacobiano) → Task 2 (`_matriz_B`) + Task 4 (verificación) ✓
- Guarda `detJ ≤ 0` → ValueError → Task 2 ✓
- Tests del spec: (1) constitutiva→T1; (2) cuerpo rígido traslación→T2; (3) rango/rotación+simetría→T2; (4) patch→T3; (5) tracción uniaxial→T3; (6) cortante puro→T3; (7) no rectangular→T4; (8) degeneración→T2 ✓
- Docstring con limitación de shear locking → Task 2 Step 3 (docstring del módulo) ✓
- Sin NumPy / stdlib puro → Global Constraints ✓

**Placeholder scan:** sin TBD/TODO; todo el código está completo en cada paso.

**Type consistency:** `rigidez_membrana(nodos_xy, E, nu, t)`, `esfuerzos_elemento(nodos_xy, E, nu, d_elem, xi, eta)`, `matvec(K, x)`, `_matriz_B(nodos_xy, xi, eta)->(B, detJ)`, `_derivadas_forma(xi, eta)` — nombres y firmas idénticos entre tareas. Orden de GDL `[ux,uy]×4` consistente en rigidez, esfuerzos y tests. ✓
