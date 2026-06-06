# Diseño — Esfuerzos por elemento en el solver

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito.
**Motivación:** habilitar Fase 4b (armado por fuerzas reales) y, en general, exponer P/V/M por
elemento. Hoy `solver.resolver` devuelve solo desplazamientos y reacciones; no hay esfuerzos
internos por elemento.

---

## 1. Objetivo

Exponer, por cada elemento del modelo, sus **fuerzas de extremo** y el **diagrama de esfuerzos
internos** (N, V, T, M) a lo largo de la barra, en coordenadas locales. Es post-procesamiento puro
del resultado del análisis: el solver ya tiene todas las piezas (`triada_local`, `rigidez_local`,
`_transformacion_12`, `_matvec`).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| API | **Función aparte** `esfuerzos_elementos(modelo, resultado)`. Aditiva: no toca `resolver()` ni `ResultadoAnalisis`; se calcula solo cuando se necesita. |
| Salida | **Dataclass `EsfuerzosElemento`** con componentes nombradas por extremo `(N, Vy, Vz, T, My, Mz)` + propiedad `axial` (tracción +). |
| Alcance | **Fuerzas de extremo + muestreo del diagrama** (`internos(t)` y `diagrama(n)`). Con cargas nodales N/V/T son constantes y M lineal, así que el muestreo es exacto. |

## 3. Arquitectura

Cambio **aditivo en `core/solver.py`**. `resolver()` y `ResultadoAnalisis` quedan idénticos.

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Esfuerzos por elemento | `src/motor_fea/core/solver.py` (mod, **aditivo**) | `EsfuerzosElemento` (dataclass) + `esfuerzos_elementos(modelo, resultado) → dict[int, EsfuerzosElemento]`. |
| Tests | `tests/test_esfuerzos.py` (nuevo) | Validación contra soluciones cerradas (voladizo, columna). |

`normativa/` y la capa de frontera **no se tocan**. La serialización JSON / endpoint / uso en diseño
quedan **fuera de alcance** (Fase 4b).

## 4. Cálculo

Por cada elemento, con `u_elem_global` = los 12 desplazamientos globales de sus dos nodos
(de `resultado.desplazamientos`):

```
f_local = kl · (T · u_elem_global)
```

donde `kl = rigidez_local(E, G, A, Iy, Iz, J, L)` y `T = _transformacion_12(ex, ey, ez)` (global→local,
de `triada_local`). El vector `f_local` (12 componentes) son las **fuerzas nodales de extremo en
coordenadas locales**, en el orden de GDL del elemento: por extremo `(N, Vy, Vz, T, My, Mz)`.

Como el motor solo admite cargas **nodales** (no hay cargas de tramo), no hay fuerzas de
empotramiento que corregir: `f = kl·T·u` es exacto.

## 5. `EsfuerzosElemento`

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
```

`esfuerzos_elementos(modelo, resultado)` recorre `modelo.elementos`, arma `u_elem_global` desde
`resultado.desplazamientos[nodo_i] + [nodo_j]`, calcula `f_local`, y devuelve
`{e.id: EsfuerzosElemento(e.id, L, f_local[0:6], f_local[6:12])}`.

## 6. Convención de signos (documentada y testeada)

- `extremo_i` / `extremo_j`: las 12 fuerzas nodales locales estándar de rigidez directa (`f = kl·T·u`);
  son las fuerzas que los nodos ejercen sobre la barra.
- `axial = −extremo_i[0]`: **tracción positiva** (igual a `extremo_j[0]` por equilibrio sin carga axial de tramo).
- `internos(t)`: esfuerzos internos en la sección por cuerpo libre del segmento `[0, s]`; **N tracción +**.
  Derivación del término de momento: la fuerza nodal del extremo i, transportada al corte en `s`,
  aporta `s·Vz_i` a `My` y `−s·Vy_i` a `Mz`, con signo opuesto para el esfuerzo interno.

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| `diagrama(n)` con `n < 2` | `ValueError`. |
| Nodo/sección/material de un elemento ausente | `KeyError` natural (el modelo debió validarse en `resolver`; `esfuerzos_elementos` asume un `resultado` ya producido por `resolver`). |

(No se re-valida el modelo: `esfuerzos_elementos` consume un `ResultadoAnalisis` que `resolver` ya
produjo a partir de un modelo válido.)

## 8. Testing (`tests/test_esfuerzos.py`, soluciones cerradas)

Reusa el voladizo de `test_solver.py` (L=3, sección 0.30×0.30, E=2e10, ν=0.2).

| Qué | Caso | Esperado |
|---|---|---|
| Axial | voladizo con `fx=P` | `axial ≈ P` (tracción, 1e-6); `extremo_i[0] ≈ −extremo_j[0]`. |
| Cortante constante | voladizo con `fz=P` | `internos(t)[2] ≈ P` para varios `t` (constante). |
| Momento lineal | voladizo con `fz=P` | `internos(0)[4] ≈ −P·L`; `internos(1)[4] ≈ 0`; `internos(0.5)[4] ≈ −P·L/2`. |
| Transformación local↔global | **columna** vertical (eje Z, `T ≠ identidad`) con carga transversal `fx=P` en la punta (global X = local `ey` → flexión sobre local z) | momento de base `\|internos(0)[5]\| ≈ P·L` (Mz; valida `T`). |
| `diagrama` | cualquier voladizo | `len(diagrama(n)) == n`; `diagrama(n)[0][0] == 0`; `diagrama(n)[-1][0] ≈ L`; `n=1` → `ValueError`. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (145 de Fase 4 + ~5 de esfuerzos ≈ **150**).
2. `esfuerzos_elementos` reproduce las fuerzas de extremo, el cortante constante y el diagrama de
   momentos lineal del voladizo dentro de 1e-6, en barras alineadas y no alineadas con los ejes.

## 9. Roadmap (fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 4b | `esfuerzos_a_dict` (contrato JSON), endpoint `/esfuerzos`, y armado por P/M reales (columna por interacción P-M, viga por flexión). | `esfuerzos_elementos` + `aci318` |

## 10. Archivos afectados

**Nuevos**
- `tests/test_esfuerzos.py`

**Modificados**
- `src/motor_fea/core/solver.py` (dataclass `EsfuerzosElemento` + función `esfuerzos_elementos` — aditivo)

`normativa/`, la capa de frontera (`viz/`, `api/`) y el resto de `core/` **no se tocan**.
