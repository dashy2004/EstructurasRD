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

from dataclasses import dataclass

from motor_fea.core.placa import GDL_POR_NODO_PLACA, rigidez_placa
from motor_fea.core.solver import resolver_lineal


@dataclass
class ResultadoLosa:
    nx: int
    ny: int
    desplazamientos_w: dict[tuple[int, int], float]   # (i,j) → w
    w_central: float


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
    return ResultadoLosa(nx, ny, desplazamientos, w_central)


def rigidez_flexional_placa(E: float, nu: float, t: float) -> float:
    """Rigidez flexional de placa D = E·t³ / (12·(1−ν²))."""
    return E * t ** 3 / (12.0 * (1.0 - nu * nu))
