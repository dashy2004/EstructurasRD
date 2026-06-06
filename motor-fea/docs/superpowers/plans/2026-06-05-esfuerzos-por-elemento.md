# Esfuerzos por elemento en el solver — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exponer, por elemento, las fuerzas de extremo y el diagrama de esfuerzos internos (N,V,T,M) en coordenadas locales, como post-procesamiento del resultado del análisis.

**Architecture:** Un cambio aditivo en `core/solver.py`: una dataclass `EsfuerzosElemento` y una función `esfuerzos_elementos(modelo, resultado)` que reusa `triada_local`/`rigidez_local`/`_transformacion_12`/`_matvec` para calcular `f_local = kl·(T·u)`. `resolver()` y `ResultadoAnalisis` no cambian.

**Tech Stack:** Python 3.11 + stdlib (sin NumPy), igual que el resto del solver.

**Spec de referencia:** `docs/superpowers/specs/2026-06-05-esfuerzos-por-elemento-design.md`

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/motor_fea/core/solver.py` (mod, **aditivo**) | `EsfuerzosElemento` + `esfuerzos_elementos(...)` al final del módulo. |
| `tests/test_esfuerzos.py` (nuevo) | Validación contra soluciones cerradas (voladizo, columna). |

---

## Task 1: Esfuerzos por elemento (core, aditivo)

**Files:**
- Modify: `src/motor_fea/core/solver.py`
- Test: `tests/test_esfuerzos.py`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/test_esfuerzos.py` con el contenido completo:

```python
"""Validación de los esfuerzos por elemento contra soluciones cerradas (voladizo, columna)."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.solver import esfuerzos_elementos, resolver

E = 2.0e10
NU = 0.2
B = 0.30
A = B * B
I = B ** 4 / 12
J = 0.1406 * B ** 4
L = 3.0
P = 1000.0


def _voladizo_x(carga: CargaNodal) -> ModeloEstructural:
    """Voladizo a lo largo de X: nodo 1 empotrado en origen, nodo 2 libre en (L,0,0)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, L, 0, 0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(carga)
    return m


def _esf_voladizo(carga: CargaNodal):
    m = _voladizo_x(carga)
    return esfuerzos_elementos(m, resolver(m))[1]


def test_axial_traccion_positiva():
    e = _esf_voladizo(CargaNodal(2, fx=P))
    assert e.axial == pytest.approx(P, rel=1e-6)
    assert e.extremo_i[0] == pytest.approx(-e.extremo_j[0], rel=1e-6)


def test_cortante_constante():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    for t in (0.0, 0.25, 0.5, 0.75, 1.0):
        assert e.internos(t)[2] == pytest.approx(P, rel=1e-6)        # Vz constante


def test_momento_lineal_voladizo():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    assert e.internos(0.0)[4] == pytest.approx(-P * L, rel=1e-6)     # My en el empotramiento
    assert e.internos(0.5)[4] == pytest.approx(-P * L / 2, rel=1e-6)
    assert abs(e.internos(1.0)[4]) < 1e-6                            # ≈ 0 en el extremo libre


def test_columna_valida_transformacion():
    # Columna a lo largo de Z (T ≠ identidad); carga fx=P (global X = local ey) → Mz de base = P·L.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, L)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(2, fx=P))
    e = esfuerzos_elementos(m, resolver(m))[1]
    assert abs(e.internos(0.0)[5]) == pytest.approx(P * L, rel=1e-6)   # Mz de base


def test_diagrama_estaciones():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    d = e.diagrama(11)
    assert len(d) == 11
    assert d[0][0] == 0.0
    assert d[-1][0] == pytest.approx(L)
    with pytest.raises(ValueError):
        e.diagrama(1)
```

- [ ] **Step 2: Correr los tests para verificar que fallan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_esfuerzos.py -q`
Expected: FAIL con `ImportError: cannot import name 'esfuerzos_elementos' from 'motor_fea.core.solver'`. (Use `.venv/bin/pytest` si falta python/pytest del sistema.)

- [ ] **Step 3: Implementar `EsfuerzosElemento` + `esfuerzos_elementos` al final de `solver.py`**

Añadir al **final** de `src/motor_fea/core/solver.py` (después de `resolver(...)`). El módulo ya importa
`dataclass` y define `triada_local`, `rigidez_local`, `_transformacion_12`, `_matvec`,
`ModeloEstructural`, `ResultadoAnalisis` — todo lo que se usa abajo:

```python
@dataclass
class EsfuerzosElemento:
    """Fuerzas de extremo y esfuerzos internos de un elemento frame, en coordenadas locales."""
    elemento_id: int
    longitud: float
    extremo_i: tuple[float, float, float, float, float, float]   # (N, Vy, Vz, T, My, Mz) nodal en i
    extremo_j: tuple[float, float, float, float, float, float]   # idem en j

    @property
    def axial(self) -> float:
        """Fuerza axial de la barra (tracción +). N = −N_i = N_j."""
        return -self.extremo_i[0]

    def internos(self, t: float) -> tuple[float, float, float, float, float, float]:
        """Esfuerzos internos (N, Vy, Vz, T, My, Mz) en la estación local s = t·L (t ∈ [0,1]).

        Por equilibrio del segmento [0, s] (solo la fuerza nodal del extremo i actúa sobre él):
        N/Vy/Vz/T constantes; My, Mz lineales. N tracción +.
        """
        ni, vy, vz, ti, my, mz = self.extremo_i
        s = t * self.longitud
        return (-ni, -vy, -vz, -ti, -my - s * vz, -mz + s * vy)

    def diagrama(self, n: int = 11) -> list[tuple[float, ...]]:
        """n estaciones equiespaciadas: cada una (s, N, Vy, Vz, T, My, Mz), s de 0 a L."""
        if n < 2:
            raise ValueError("n debe ser ≥ 2.")
        return [(t * self.longitud, *self.internos(t)) for t in (k / (n - 1) for k in range(n))]


def esfuerzos_elementos(modelo: ModeloEstructural,
                        resultado: ResultadoAnalisis) -> dict[int, EsfuerzosElemento]:
    """Fuerzas de extremo y diagramas por elemento (coordenadas locales) del resultado del análisis.

    Post-procesamiento puro: por elemento, ``f_local = kl·(T·u)`` con los desplazamientos globales de
    sus dos nodos. Reusa la geometría/rigidez del ensamblaje; ``resolver`` ya validó el modelo.
    """
    nodos = {n.id: n for n in modelo.nodos}
    mats = {m.id: m for m in modelo.materiales}
    secs = {s.id: s for s in modelo.secciones}
    salida: dict[int, EsfuerzosElemento] = {}
    for e in modelo.elementos:
        ni, nj = nodos[e.nodo_i], nodos[e.nodo_j]
        mat, sec = mats[e.material_id], secs[e.seccion_id]
        ex, ey, ez, L = triada_local(ni, nj, e.vector_referencia)
        kl = rigidez_local(mat.E, mat.G, sec.area, sec.inercia_y, sec.inercia_z, sec.constante_torsion, L)
        T = _transformacion_12(ex, ey, ez)
        u_g = list(resultado.desplazamientos[e.nodo_i]) + list(resultado.desplazamientos[e.nodo_j])
        f_local = _matvec(kl, _matvec(T, u_g))
        salida[e.id] = EsfuerzosElemento(e.id, L, tuple(f_local[0:6]), tuple(f_local[6:12]))
    return salida
```

- [ ] **Step 4: Correr los tests de esfuerzos para verificar que pasan**

Run: `PYTHONPATH=src:tests python -m pytest tests/test_esfuerzos.py -q`
Expected: PASS (5 passed).

- [ ] **Step 5: Correr la suite completa (sin regresión)**

Run: `PYTHONPATH=src:tests python -m pytest -q`
Expected: PASS, ~150 tests (145 previos + 5 de esfuerzos). `solver.py` solo creció de forma aditiva, así que `test_solver.py` y todo lo demás siguen verdes.

- [ ] **Step 6: Commit**

```bash
git add src/motor_fea/core/solver.py tests/test_esfuerzos.py
git commit -m "feat(solver): esfuerzos por elemento (fuerzas de extremo + diagrama N/V/M)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Criterio de aceptación (spec §8)

1. `PYTHONPATH=src:tests python -m pytest -q` verde (~150 tests).
2. `esfuerzos_elementos` reproduce, dentro de 1e-6: axial de tracción, cortante constante, momento
   lineal `−P·L → 0` del voladizo, y el momento de base `P·L` de la columna por Z (valida `T`).

## Notas de revisión (plan vs. spec)

- **Aditivo en el core:** solo se agrega al final de `solver.py`; `resolver()`/`ResultadoAnalisis`
  no cambian → cero regresión esperada (la verifica el Step 5).
- **Convención de signos pin-eada por los tests:** axial tracción+, momento interno por cuerpo libre
  desde el extremo i; el voladizo (`−P·L` en el empotramiento) y la columna (`P·L` de base) fijan el signo.
- **Diagrama exacto:** con cargas solo nodales N/V/T son constantes y M lineal, así que `internos(t)`
  es la solución cerrada, no una aproximación.
- **Fuera de alcance:** serialización JSON, endpoint, y uso en armado real (Fase 4b).
