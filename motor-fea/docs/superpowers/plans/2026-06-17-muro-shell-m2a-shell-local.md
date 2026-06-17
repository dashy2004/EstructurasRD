# M2a — Elemento shell plano local (24×24) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Añadir `src/motor_fea/core/shell.py` con `rigidez_shell(nodos_xy, E, nu, t, gamma=None) -> 24×24`, el elemento shell plano local (membrana M1 + placa + drilling Hughes–Brezzi), autónomo y sin tocar el solver.

**Architecture:** Shell plano cuadrilátero de 4 nodos, 6 GDL/nodo en el orden del frame `[ux,uy,uz,θx,θy,θz]`. La rigidez 24×24 se ensambla de dos bloques 12×12 desacoplados: (a) membrana+drilling en GDL `(ux,uy,θz)` y (b) placa en GDL `(uz,θx,θy)`. La membrana viene de `core/membrana.py` (M1, isoparamétrica general), la placa de `core/placa.py` (ACM rectangular), y el drilling es una penalización rotacional de Hughes–Brezzi integrada por Gauss 2×2 que reutiliza `membrana._matriz_B`.

**Tech Stack:** Python 3 stdlib puro (solo `math`), sin NumPy, sin I/O. Tests con `pytest`. Intérprete del proyecto: `.venv/bin/python`.

## Global Constraints

- **Stdlib puro:** solo `import math` y los módulos `motor_fea.core.membrana` / `motor_fea.core.placa`. Sin NumPy, sin I/O.
- **Unidades SI:** longitudes en m, `E` en Pa, `t` en m.
- **Orden de GDL local (contrato con M2b):** por nodo `a` (0..3), GDL `[ux_a, uy_a, uz_a, θx_a, θy_a, θz_a]`; índice global `g(a,d) = 6·a + d` con `d ∈ {0:ux,1:uy,2:uz,3:θx,4:θy,5:θz}`.
- **Mapeo a bloques fuente:** membrana M1 (orden `[ux,uy]×4`): local `2a+{0,1}` → shell `6a+{0,1}`. Placa (orden `[w,θx,θy]×4`): local `3a+{0,1,2}` → shell `6a+{2,3,4}`. Drilling: θz en shell `6a+5`.
- **`gamma` por defecto = `E·t`** (factor de penalización de drilling, único y documentado).
- **No modificar archivos existentes:** solo se añaden `src/motor_fea/core/shell.py` y `tests/test_shell.py`.
- **Estilo del paquete:** `from __future__ import annotations`, docstrings en español, anotaciones de tipo como en `membrana.py`/`placa.py`.
- **Intérprete y suite:** ejecutar tests con `.venv/bin/python -m pytest`. La suite previa tiene 288 tests verde; no debe haber regresiones.
- **Rama:** `engine/muro-shell-m1` (worktree primario). No abrir PR en esta rebanada.

---

## File Structure

- **Crear `src/motor_fea/core/shell.py`** — único módulo nuevo. Contiene:
  - `_dims_rectangulo(nodos_xy)` — deriva `(lx, ly)` rectangulares equivalentes para alimentar a la placa.
  - `_rigidez_drilling(nodos_xy, E, t, gamma)` — bloque 12×12 de penalización Hughes–Brezzi en GDL `(ux,uy,θz)×4`.
  - `rigidez_shell(nodos_xy, E, nu, t, gamma=None)` — ensambla la 24×24.
- **Crear `tests/test_shell.py`** — los 7 tests del spec más el test de unidad del helper de drilling.

Helpers reutilizados de `membrana` (ya existen, NO reimplementar): `membrana.rigidez_membrana`, `membrana._matriz_B`, `membrana._ESQUINAS`, `membrana._GAUSS2`, `membrana.matvec`. De `placa`: `placa.rigidez_placa`.

---

## Task 1: Helper de drilling (Hughes–Brezzi)

De-risk de la pieza más delicada: la penalización rotacional debe anularse exactamente bajo rotación rígida en el plano (`θz = ω`). Se testea el helper de forma aislada antes de ensamblar el shell.

**Files:**
- Create: `src/motor_fea/core/shell.py`
- Test: `tests/test_shell.py`

**Interfaces:**
- Consumes: `membrana._matriz_B(nodos_xy, xi, eta) -> (B (3×8), detJ)`, `membrana._ESQUINAS` (4 pares naturales), `membrana._GAUSS2` (`((-g,1.0),(g,1.0))`), `membrana.matvec(K, x)`.
- Produces: `_rigidez_drilling(nodos_xy: list[tuple[float,float]], E: float, t: float, gamma: float) -> list[list[float]]` — 12×12 simétrica en GDL `(ux,uy,θz)×4` (índice de nodo `a`: ux→`3a`, uy→`3a+1`, θz→`3a+2`).

- [ ] **Step 1: Write the failing test**

En `tests/test_shell.py`:

```python
"""Tests del elemento shell plano local (M2a): membrana+placa+drilling, 24×24."""
from __future__ import annotations

import math

import pytest

from motor_fea.core import membrana, placa, shell

# Geometría rectangular de referencia (nodo 0 en el origen, antihorario).
NODOS_RECT = [(0.0, 0.0), (2.0, 0.0), (2.0, 3.0), (0.0, 3.0)]
E, NU, T = 2.1e11, 0.2, 0.2


def _energia(K, d):
    """dᵀ·K·d."""
    Kd = membrana.matvec(K, d)
    return sum(d[i] * Kd[i] for i in range(len(d)))


def test_drilling_forma_y_simetria():
    Kd = shell._rigidez_drilling(NODOS_RECT, E, T, gamma=E * T)
    assert len(Kd) == 12 and all(len(fila) == 12 for fila in Kd)
    for i in range(12):
        for j in range(12):
            assert math.isclose(Kd[i][j], Kd[j][i], rel_tol=1e-9, abs_tol=1e-6)


def test_drilling_rotacion_rigida_energia_cero():
    # Rotación rígida en el plano: ux=-y, uy=x, θz=1 (θ=1). θz = ω → energía 0.
    d = []
    for (x, y) in NODOS_RECT:
        d.extend([-y, x, 1.0])          # (ux, uy, θz) por nodo
    Kd = shell._rigidez_drilling(NODOS_RECT, E, T, gamma=E * T)
    ref = max(abs(Kd[i][i]) for i in range(12))
    assert abs(_energia(Kd, d)) < 1e-6 * ref


def test_drilling_diferencial_energia_positiva():
    # θz no uniforme con membrana fija → penalización estrictamente positiva.
    d = [0.0] * 12
    d[2] = 1.0                          # θz del nodo 0
    Kd = shell._rigidez_drilling(NODOS_RECT, E, T, gamma=E * T)
    assert _energia(Kd, d) > 0.0
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: FAIL con `ModuleNotFoundError: No module named 'motor_fea.core.shell'` (aún no existe `shell.py`).

- [ ] **Step 3: Write minimal implementation**

Crear `src/motor_fea/core/shell.py`:

```python
"""Elemento shell plano local (24×24) — capa 1, análisis FEM.

Cuadrilátero plano de 4 nodos, 6 GDL/nodo en el orden del frame del modelo
`[ux, uy, uz, θx, θy, θz]`. En un shell **plano** la membrana (en el plano) y la
flexión de placa (fuera del plano) **no se acoplan**: la rigidez 24×24 se ensambla
de dos bloques 12×12 disjuntos.

- `ux, uy` → membrana Q4 de tensión plana (M1, `core/membrana.py`, isoparamétrica
  general).
- `uz, θx, θy` → placa de flexión ACM (`core/placa.py`). LIMITACIÓN: la placa ACM
  es **rectangular**; M2a la alimenta con la geometría rectangular equivalente del
  elemento. Los muros llenos (M3) se mallan en rectángulos, donde esto aplica. La
  membrana, en cambio, sí es isoparamétrica general.
- `θz` → drilling (rotación en torno a la normal local). Estabilización de
  **Hughes–Brezzi** (penalización rotacional, NO física): `E_drill = ½γ∫(θz−ω)²dA`
  con `ω = ½(∂uy/∂x − ∂ux/∂y)`. Se anula bajo rotación rígida en el plano (`θz=ω`),
  preservando los 6 modos de cuerpo rígido, a cambio de acoplar `(ux,uy)` con `θz`.

Unidades SI: longitudes en m, E en Pa, t en m.
"""
from __future__ import annotations

import math

from motor_fea.core import membrana, placa


def _rigidez_drilling(nodos_xy: list[tuple[float, float]],
                      E: float, t: float, gamma: float) -> list[list[float]]:
    """Bloque 12×12 de drilling Hughes–Brezzi en GDL (ux,uy,θz)×4.

    Penalización ``K = γ ∫∫ gᵀg dA`` por Gauss 2×2, con
    ``g·d = θz − ω`` y ``ω = ½(∂uy/∂x − ∂ux/∂y)``. Por nodo ``a``:
    ``g[3a]=½∂N_a/∂y`` (ux), ``g[3a+1]=−½∂N_a/∂x`` (uy), ``g[3a+2]=N_a`` (θz).
    """
    K = [[0.0] * 12 for _ in range(12)]
    for xi, wxi in membrana._GAUSS2:
        for eta, weta in membrana._GAUSS2:
            B, detJ = membrana._matriz_B(nodos_xy, xi, eta)
            g = [0.0] * 12
            for a in range(4):
                xa, ea = membrana._ESQUINAS[a]
                Na = 0.25 * (1.0 + xa * xi) * (1.0 + ea * eta)
                dndx = B[0][2 * a]          # ∂N_a/∂x (de la fila εxx de B)
                dndy = B[1][2 * a + 1]      # ∂N_a/∂y (de la fila εyy de B)
                g[3 * a + 0] = 0.5 * dndy
                g[3 * a + 1] = -0.5 * dndx
                g[3 * a + 2] = Na
            peso = wxi * weta * detJ * gamma
            for i in range(12):
                gi = peso * g[i]
                fila = K[i]
                for j in range(12):
                    fila[j] += gi * g[j]
    return K
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/shell.py tests/test_shell.py
git commit -m "feat(M2a): helper de drilling Hughes–Brezzi (12×12, Gauss 2×2)"
```

---

## Task 2: `rigidez_shell` — ensamblaje en bloques 24×24

Ensambla membrana+drilling y placa en la 24×24 y valida forma/simetría, desacoplamiento, y reducción exacta a M1 y a la placa.

**Files:**
- Modify: `src/motor_fea/core/shell.py`
- Test: `tests/test_shell.py`

**Interfaces:**
- Consumes: `membrana.rigidez_membrana(nodos_xy, E, nu, t) -> 8×8` (orden `[ux,uy]×4`), `placa.rigidez_placa(lx, ly, E, nu, t) -> 12×12` (orden `[w,θx,θy]×4`, nodos `[(0,0),(lx,0),(lx,ly),(0,ly)]`), `_rigidez_drilling` (Task 1).
- Produces:
  - `_dims_rectangulo(nodos_xy) -> tuple[float, float]` — `(lx, ly)` = `(|n1−n0|, |n2−n1|)`.
  - `rigidez_shell(nodos_xy: list[tuple[float,float]], E: float, nu: float, t: float, gamma: float | None = None) -> list[list[float]]` — 24×24 simétrica; `gamma=None` usa `E·t`.

- [ ] **Step 1: Write the failing test**

Añadir a `tests/test_shell.py`:

```python
def _subbloque(K, dofs):
    """Submatriz de K en los índices globales de shell `dofs` (en orden)."""
    return [[K[i][j] for j in dofs] for i in dofs]


def test_shell_forma_y_simetria():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    assert len(K) == 24 and all(len(fila) == 24 for fila in K)
    for i in range(24):
        for j in range(24):
            assert math.isclose(K[i][j], K[j][i], rel_tol=1e-9, abs_tol=1e-3)


def test_shell_bloques_desacoplados_membrana_placa():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    dofs_mem = [6 * a + d for a in range(4) for d in (0, 1)]        # ux,uy
    dofs_placa = [6 * a + d for a in range(4) for d in (2, 3, 4)]   # uz,θx,θy
    for i in dofs_mem:
        for j in dofs_placa:
            assert K[i][j] == 0.0
            assert K[j][i] == 0.0


def test_shell_reduce_a_membrana():
    # Con drilling apagado (gamma=0), el sub-bloque (ux,uy)×4 = rigidez_membrana.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=0.0)
    dofs_mem = [6 * a + d for a in range(4) for d in (0, 1)]
    sub = _subbloque(K, dofs_mem)
    Km = membrana.rigidez_membrana(NODOS_RECT, E, NU, T)
    for i in range(8):
        for j in range(8):
            assert math.isclose(sub[i][j], Km[i][j], rel_tol=1e-9, abs_tol=1e-3)


def test_shell_reduce_a_placa():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    dofs_placa = [6 * a + d for a in range(4) for d in (2, 3, 4)]
    sub = _subbloque(K, dofs_placa)
    lx, ly = shell._dims_rectangulo(NODOS_RECT)
    Kp = placa.rigidez_placa(lx, ly, E, NU, T)
    for i in range(12):
        for j in range(12):
            assert math.isclose(sub[i][j], Kp[i][j], rel_tol=1e-9, abs_tol=1e-3)
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: FAIL con `AttributeError: module 'motor_fea.core.shell' has no attribute 'rigidez_shell'`.

- [ ] **Step 3: Write minimal implementation**

Añadir a `src/motor_fea/core/shell.py` (después de `_rigidez_drilling`):

```python
def _dims_rectangulo(nodos_xy: list[tuple[float, float]]) -> tuple[float, float]:
    """Lados (lx, ly) rectangulares equivalentes: |n1−n0| y |n2−n1|.

    Alimenta a la placa ACM (rectangular). Para muros llenos mallados en
    rectángulos (M3) coincide con la geometría real del elemento.
    """
    (x0, y0), (x1, y1), (x2, y2), _ = nodos_xy
    lx = math.hypot(x1 - x0, y1 - y0)
    ly = math.hypot(x2 - x1, y2 - y1)
    return lx, ly


def rigidez_shell(nodos_xy: list[tuple[float, float]],
                  E: float, nu: float, t: float,
                  gamma: float | None = None) -> list[list[float]]:
    """Rigidez local 24×24 del shell plano cuadrilátero de 4 nodos.

    ``nodos_xy`` = 4 pares (x, y) en el plano local, orden antihorario. 6 GDL/nodo
    en orden ``[ux, uy, uz, θx, θy, θz]``; índice global ``6·a + d``. ``gamma`` =
    factor de drilling; si ``None`` usa ``E·t``. Devuelve K simétrica (24×24).
    """
    if gamma is None:
        gamma = E * t

    Km = membrana.rigidez_membrana(nodos_xy, E, nu, t)         # 8×8  [ux,uy]×4
    lx, ly = _dims_rectangulo(nodos_xy)
    Kp = placa.rigidez_placa(lx, ly, E, nu, t)                 # 12×12 [w,θx,θy]×4
    Kd = _rigidez_drilling(nodos_xy, E, t, gamma)              # 12×12 [ux,uy,θz]×4

    K = [[0.0] * 24 for _ in range(24)]

    # Bloque membrana+drilling → GDL shell (ux,uy,θz) = 6a+{0,1,5}.
    idx_md = [6 * a + d for a in range(4) for d in (0, 1, 5)]
    # Embeber membrana (orden [ux,uy]×4, índice 2a+c) en el bloque md (3a+c).
    for a in range(4):
        for c in range(2):
            for b in range(4):
                for e in range(2):
                    K[idx_md[3 * a + c]][idx_md[3 * b + e]] += Km[2 * a + c][2 * b + e]
    # Sumar el drilling (ya en GDL (ux,uy,θz)×4 = orden del bloque md).
    for i in range(12):
        for j in range(12):
            K[idx_md[i]][idx_md[j]] += Kd[i][j]

    # Bloque placa → GDL shell (uz,θx,θy) = 6a+{2,3,4}.
    idx_p = [6 * a + d for a in range(4) for d in (2, 3, 4)]
    for i in range(12):
        for j in range(12):
            K[idx_p[i]][idx_p[j]] += Kp[i][j]

    return K
```

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: PASS (7 tests: 3 de Task 1 + 4 nuevos).

- [ ] **Step 5: Commit**

```bash
git add src/motor_fea/core/shell.py tests/test_shell.py
git commit -m "feat(M2a): rigidez_shell 24×24 (membrana+drilling+placa en bloques)"
```

---

## Task 3: Validación física — modos rígidos y drilling no nulo

Verifica los 6 modos de cuerpo rígido (energía ≈ 0) y que `θz` deja de ser mecanismo con `gamma>0`.

**Files:**
- Test: `tests/test_shell.py`

**Interfaces:**
- Consumes: `shell.rigidez_shell` (Task 2), `membrana.matvec`, `_energia` (helper de test, Task 1).
- Produces: nada nuevo de código (solo tests).

- [ ] **Step 1: Write the failing test**

Añadir a `tests/test_shell.py`:

```python
def _modos_rigidos(nodos_xy):
    """Los 6 modos de cuerpo rígido del shell plano, como vectores de 24 GDL.

    GDL/nodo: [ux,uy,uz,θx,θy,θz]. Placa: θx=∂w/∂y, θy=−∂w/∂x.
    """
    n = len(nodos_xy)
    modos = []
    # 3 traslaciones.
    for comp in (0, 1, 2):                       # ux, uy, uz
        d = [0.0] * (6 * n)
        for a in range(n):
            d[6 * a + comp] = 1.0
        modos.append(d)
    # Rotación fuera del plano θx (w = y → θx=1, θy=0).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 2] = y         # uz = w = y
        d[6 * a + 3] = 1.0       # θx
    modos.append(d)
    # Rotación fuera del plano θy (w = x → θx=0, θy=−1).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 2] = x         # uz = w = x
        d[6 * a + 4] = -1.0      # θy
    modos.append(d)
    # Rotación en el plano θz (ux=-y, uy=x, θz=1).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 0] = -y
        d[6 * a + 1] = x
        d[6 * a + 5] = 1.0
    modos.append(d)
    return modos


def test_shell_seis_modos_rigidos_energia_cero():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    ref = max(abs(K[i][i]) for i in range(24))
    modos = _modos_rigidos(NODOS_RECT)
    assert len(modos) == 6
    for d in modos:
        dscale = max(abs(v) for v in d) or 1.0
        assert abs(_energia(K, d)) < 1e-6 * ref * dscale * dscale


def test_shell_drilling_no_nulo():
    # Con gamma>0, θz diferencial (membrana/placa fijas) da energía positiva.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=E * T)
    d = [0.0] * 24
    d[5] = 1.0                  # θz del nodo 0
    assert _energia(K, d) > 0.0
```

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/python -m pytest tests/test_shell.py::test_shell_seis_modos_rigidos_energia_cero tests/test_shell.py::test_shell_drilling_no_nulo -q`
Expected: con la implementación de Task 2 estos tests **deberían pasar** (ejercen código ya implementado; no añaden producción). Si FALLAN, NO ajustar la tolerancia a ciegas: es un bug real de Task 2 (signo de `g` en el drilling, o mapeo de bloques). Diagnosticar antes de tocar código.

- [ ] **Step 3: (solo si fallan) corregir Task 2**

Si `test_shell_seis_modos_rigidos_energia_cero` falla en el modo de rotación en el plano (`θz`), el sospechoso es el signo de `g` en `_rigidez_drilling`: debe cumplirse `θz − ω = 0` con `ω = ½(∂uy/∂x − ∂ux/∂y)`, es decir `g[3a]=+½∂N/∂y`, `g[3a+1]=−½∂N/∂x`, `g[3a+2]=N`. Si falla un modo fuera del plano, revisar `_modos_rigidos` (convención `θx=∂w/∂y`, `θy=−∂w/∂x` de `placa.py`). No relajar tolerancias.

- [ ] **Step 4: Run test to verify it passes**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/test_shell.py
git commit -m "test(M2a): 6 modos de cuerpo rígido (energía≈0) + drilling no nulo"
```

---

## Task 4: Rango = 18 (24 − 6 modos nulos) + cierre de suite

Verifica que K tiene exactamente 6 modos de energía nula: tras fijar los 6 GDL del nodo 0 (en el origen, mata los 6 modos rígidos), la 18×18 reducida es definida positiva (pivotes de eliminación gaussiana simétrica > 0).

**Files:**
- Test: `tests/test_shell.py`

**Interfaces:**
- Consumes: `shell.rigidez_shell` (Task 2).
- Produces: nada de producción.

- [ ] **Step 1: Write the failing test**

Añadir a `tests/test_shell.py`:

```python
def _pivotes_positivos(M):
    """True si la eliminación gaussiana simétrica de M (n×n) tiene todos los
    pivotes > 0 (≈ definida positiva). M se copia; no se modifica el original."""
    n = len(M)
    A = [list(fila) for fila in M]
    umbral = 1e-6 * max(abs(A[i][i]) for i in range(n))
    for k in range(n):
        piv = A[k][k]
        if piv <= umbral:
            return False
        for i in range(k + 1, n):
            f = A[i][k] / piv
            for j in range(k, n):
                A[i][j] -= f * A[k][j]
    return True


def test_shell_rango_18():
    # Fijar los 6 GDL del nodo 0 (en el origen) elimina los 6 modos rígidos.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=E * T)
    libres = list(range(6, 24))                  # GDL del nodo 0 = 0..5 fijos
    Kred = [[K[i][j] for j in libres] for i in libres]
    assert len(Kred) == 18
    assert _pivotes_positivos(Kred)
```

> El nodo 0 de `NODOS_RECT` está en `(0,0)`: por eso sus 6 GDL fijos matan las 3 traslaciones, la rotación θz (θz_0=1) y las rotaciones fuera del plano (θx_0=1 / θy_0=−1, aunque `w_0=0` las pendientes no se anulan). La reducida → 18×18 definida positiva.

- [ ] **Step 2: Run test to verify it fails**

Run: `.venv/bin/python -m pytest tests/test_shell.py::test_shell_rango_18 -q`
Expected: PASS si la implementación es correcta (no añade producción). Si FAIL → hay un modo nulo espurio (rango < 18): revisar drilling/ensamblaje, no la tolerancia.

- [ ] **Step 3: Run full module + suite completa**

Run: `.venv/bin/python -m pytest tests/test_shell.py -q`
Expected: PASS (10 tests).

Run: `.venv/bin/python -m pytest -q`
Expected: PASS de toda la suite (288 previos + 10 nuevos = 298), sin regresiones.

- [ ] **Step 4: Verificar criterios de aceptación**

Revisar contra el spec:
- `shell.py` añadido; `test_shell.py` con los 7 tests del spec en verde (forma/simetría, desacoplamiento, reduce-a-membrana, reduce-a-placa, 6 modos rígidos, drilling no nulo, rango 18) — más el test de unidad del helper.
- Suite completa verde, sin regresiones.
- Stdlib puro (solo `math` + `membrana`/`placa`), sin NumPy, sin I/O.
- Docstring documenta: shell plano (membrana/placa desacopladas), drilling Hughes–Brezzi como estabilización (no física), limitación de placa rectangular vs. membrana isoparamétrica general.

- [ ] **Step 5: Commit**

```bash
git add tests/test_shell.py
git commit -m "test(M2a): rango 24−6=18 (18×18 reducida definida positiva)"
```

---

## Self-Review (autor del plan)

**1. Spec coverage:**
- Módulo `shell.py` stdlib puro → Tasks 1–2. ✓
- `rigidez_shell(nodos_xy,E,nu,t,gamma=None)` 24×24, 6 GDL/nodo orden frame → Task 2. ✓
- Membrana (ux,uy) de M1, placa (uz,θx,θy), drilling (θz) → Tasks 1–2. ✓
- Reutiliza `rigidez_membrana` y `rigidez_placa` sin reimplementar → Task 2. ✓
- Drilling Hughes–Brezzi `½γ∫(θz−ω)²`, Gauss 2×2, `B_ω` desde `_matriz_B`, `γ=E·t` por defecto → Task 1. ✓
- Ensamblaje en 2 bloques 12×12, cruzados membrana↔placa = 0 → Task 2 (test desacoplamiento). ✓
- `_dims_rectangulo` para alimentar la placa rectangular → Task 2. ✓
- 7 tests del spec → Tasks 2 (1–4), 3 (5–6), 4 (7). ✓
- Helper `_rigidez_drilling(nodos_xy,E,t,gamma)` → Task 1. ✓
- Criterios de aceptación (docstrings, sin NumPy, sin regresiones) → Task 4 Step 4. ✓

**2. Placeholder scan:** Sin TBD/TODO/"handle edge cases"; todo el código está completo. ✓

**3. Type consistency:** `_rigidez_drilling(nodos_xy,E,t,gamma)`, `_dims_rectangulo(nodos_xy)`, `rigidez_shell(nodos_xy,E,nu,t,gamma=None)` consistentes entre tasks. Mapeos `idx_md=6a+{0,1,5}`, `idx_p=6a+{2,3,4}` y embebido membrana `2a+c→3a+c` coherentes con el contrato de GDL. ✓
