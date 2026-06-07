"""Análisis de losas por FEM — mallado + ensamblaje + solver (capa 1).

Construye una malla rectangular de elementos de placa ACM
(:mod:`motor_fea.core.placa`), ensambla la rigidez global (3 GDL/nodo:
w, θx, θy), aplica apoyos y resuelve la flexión bajo carga uniforme. Es el
camino para que el motor calcule losas como FEM (reemplazo de ``Losas.exe``).

Convención de nodos: rejilla (nx+1)×(ny+1); índice de nodo = j·(nx+1)+i con
i∈[0,nx], j∈[0,ny]. GDL global del nodo n = 3n + {0:w, 1:θx, 2:θy}.

Unidades SI: a,b,t en m; E en Pa; q en N/m² → w en m.
"""
from __future__ import annotations

from dataclasses import dataclass, field

from motor_fea.core.placa import GDL_POR_NODO_PLACA, momentos_elemento, rigidez_placa
from motor_fea.core.solver import resolver_lineal


@dataclass
class ResultadoLosa:
    nx: int
    ny: int
    desplazamientos_w: dict[tuple[int, int], float]   # (i,j) → w
    w_central: float
    mx_max: float = 0.0      # |Mx| máximo (N·m/m) — momento de vano (centros de elemento)
    my_max: float = 0.0      # |My| máximo
    mxy_max: float = 0.0     # |Mxy| máximo
    m_apoyo_max: float = 0.0 # |M| máximo en el contorno (momento de apoyo, acero superior)
    momentos_nodales: dict[tuple[int, int], tuple[float, float]] = field(default_factory=dict)


def _idx(i: int, j: int, nx: int) -> int:
    return j * (nx + 1) + i


def resolver_losa_rectangular(a: float, b: float, nx: int, ny: int,
                              E: float, nu: float, t: float, q: float,
                              borde: str = "simple") -> ResultadoLosa:
    """Resuelve una losa rectangular a×b mallada en nx×ny elementos bajo presión q.

    ``borde`` = "simple" (simplemente apoyada: w=0 en el contorno, rotaciones
    libres) o "empotrado" (w=θx=θy=0 en el contorno).
    """
    nnod = (nx + 1) * (ny + 1)
    n = nnod * GDL_POR_NODO_PLACA
    lx, ly = a / nx, b / ny

    # Rigidez del elemento (idéntica para toda la malla uniforme).
    ke = rigidez_placa(lx, ly, E, nu, t)

    K = [[0.0] * n for _ in range(n)]
    # Orden de nodos del elemento ACM: (i,j),(i+1,j),(i+1,j+1),(i,j+1).
    for cj in range(ny):
        for ci in range(nx):
            nodos = [_idx(ci, cj, nx), _idx(ci + 1, cj, nx),
                     _idx(ci + 1, cj + 1, nx), _idx(ci, cj + 1, nx)]
            mapa = [nd * 3 + d for nd in nodos for d in range(3)]
            for aa in range(12):
                Ka = K[mapa[aa]]
                kea = ke[aa]
                for bb in range(12):
                    Ka[mapa[bb]] += kea[bb]

    # Carga uniforme → cargas nodales por área tributaria (lumped) en el GDL w.
    F = [0.0] * n
    carga_cell = q * lx * ly / 4.0
    for cj in range(ny):
        for ci in range(nx):
            for nd in (_idx(ci, cj, nx), _idx(ci + 1, cj, nx),
                       _idx(ci + 1, cj + 1, nx), _idx(ci, cj + 1, nx)):
                F[nd * 3] += carga_cell

    # Condiciones de borde.
    fijos: set[int] = set()
    for i in range(nx + 1):
        for j in range(ny + 1):
            if i in (0, nx) or j in (0, ny):
                base = _idx(i, j, nx) * 3
                fijos.add(base)                      # w = 0
                if borde == "empotrado":
                    fijos.add(base + 1)
                    fijos.add(base + 2)
    libres = [d for d in range(n) if d not in fijos]

    Kff = [[K[i][j] for j in libres] for i in libres]
    Ff = [F[i] for i in libres]
    uf = resolver_lineal(Kff, Ff) if libres else []
    u = [0.0] * n
    for pos, d in enumerate(libres):
        u[d] = uf[pos]

    desplazamientos = {(i, j): u[_idx(i, j, nx) * 3]
                       for i in range(nx + 1) for j in range(ny + 1)}
    w_central = desplazamientos.get((nx // 2, ny // 2), 0.0)

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

    # Momento de apoyo (acero superior): muestrear el punto medio de la arista que
    # cae sobre el contorno, en los elementos de borde. En apoyo simple ~0; en
    # bordes empotrados es donde el momento negativo es máximo.
    m_apoyo_max = 0.0
    for cj in range(ny):
        for ci in range(nx):
            puntos = []
            if cj == 0:        puntos.append((0.5, 0.0))   # arista inferior (y=0)
            if cj == ny - 1:   puntos.append((0.5, 1.0))   # arista superior
            if ci == 0:        puntos.append((0.0, 0.5))   # arista izquierda (x=0)
            if ci == nx - 1:   puntos.append((1.0, 0.5))   # arista derecha
            if not puntos:
                continue
            nodos = [_idx(ci, cj, nx), _idx(ci + 1, cj, nx),
                     _idx(ci + 1, cj + 1, nx), _idx(ci, cj + 1, nx)]
            d_elem = [u[nd * 3 + d] for nd in nodos for d in range(3)]
            for fx, fy in puntos:
                mx, my, _ = momentos_elemento(lx, ly, E, nu, t, d_elem, fx, fy)
                m_apoyo_max = max(m_apoyo_max, abs(mx), abs(my))

    return ResultadoLosa(nx, ny, desplazamientos, w_central, mx_max, my_max, mxy_max,
                         m_apoyo_max, momentos_nodales)


def rigidez_flexional_placa(E: float, nu: float, t: float) -> float:
    """Rigidez flexional de placa D = E·t³ / (12·(1−ν²))."""
    return E * t ** 3 / (12.0 * (1.0 - nu * nu))
