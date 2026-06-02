"""Elemento de flexión de placa delgada (Kirchhoff) — capa 1, análisis FEM.

Elemento rectangular **ACM** (Adini–Clough–Melosh), 4 nodos × 3 GDL
(w, θx=∂w/∂y, θy=−∂w/∂x) = 12 GDL. Campo de desplazamiento polinómico de 12
términos (no-conforme, pero converge para placas delgadas y pasa el patch test
de curvatura constante). Base para que el motor calcule losas por FEM (shells).

Rigidez: ``K = C⁻ᵀ (∫∫ Bᵀ Db B dA) C⁻¹``, con ``C`` el mapeo coeficientes→GDL,
``B`` las curvaturas (segundas derivadas) y ``Db`` la constitutiva de flexión.
La integral se evalúa por cuadratura de Gauss 3×3 (exacta para el integrando).

Unidades SI: longitudes en m, E en Pa, t en m → rigidez en N·m.
MITC4 (libre de shear-locking, placas gruesas) queda para una iteración futura.
"""
from __future__ import annotations

import math

from motor_fea.core.solver import resolver_lineal

# GDL por nodo: w, θx, θy. Coordenadas locales en [0,lx]×[0,ly].
GDL_POR_NODO_PLACA = 3


def constitutiva_flexion(E: float, nu: float, t: float) -> list[list[float]]:
    """Matriz constitutiva de flexión Db (3×3): D·[[1,ν,0],[ν,1,0],[0,0,(1−ν)/2]], D=Et³/12(1−ν²)."""
    D = E * t ** 3 / (12.0 * (1.0 - nu * nu))
    return [[D, D * nu, 0.0],
            [D * nu, D, 0.0],
            [0.0, 0.0, D * (1.0 - nu) / 2.0]]


def _curvaturas_poly(x: float, y: float) -> list[list[float]]:
    """B(x,y) (3×12): curvaturas [w_xx, w_yy, 2·w_xy] en función de los 12 coeficientes."""
    return [
        [0, 0, 0, 2, 0, 0, 6 * x, 2 * y, 0, 0, 6 * x * y, 0],            # w_xx
        [0, 0, 0, 0, 0, 2, 0, 0, 2 * x, 6 * y, 0, 6 * x * y],            # w_yy
        [0, 0, 0, 0, 2, 0, 0, 4 * x, 4 * y, 0, 6 * x * x, 6 * y * y],    # 2·w_xy
    ]


def _fila_coeficientes(x: float, y: float, tipo: str) -> list[float]:
    """Fila que evalúa w, ∂w/∂y (θx) o −∂w/∂x (θy) del polinomio de 12 términos en (x,y)."""
    if tipo == "w":
        return [1, x, y, x * x, x * y, y * y, x ** 3, x * x * y, x * y * y, y ** 3, x ** 3 * y, x * y ** 3]
    if tipo == "tx":   # ∂w/∂y
        return [0, 0, 1, 0, x, 2 * y, 0, x * x, 2 * x * y, 3 * y * y, x ** 3, 3 * x * y * y]
    if tipo == "ty":   # −∂w/∂x
        return [0, -1, 0, -2 * x, -y, 0, -3 * x * x, -2 * x * y, -y * y, 0, -3 * x * x * y, -y ** 3]
    raise ValueError(tipo)


def _matriz_C(lx: float, ly: float) -> list[list[float]]:
    """C (12×12): mapeo coeficientes del polinomio → GDL nodales (w,θx,θy en 4 esquinas)."""
    nodos = [(0.0, 0.0), (lx, 0.0), (lx, ly), (0.0, ly)]
    C: list[list[float]] = []
    for x, y in nodos:
        C.append(_fila_coeficientes(x, y, "w"))
        C.append(_fila_coeficientes(x, y, "tx"))
        C.append(_fila_coeficientes(x, y, "ty"))
    return C


def _inversa(M: list[list[float]]) -> list[list[float]]:
    """Inversa de M (n×n) resolviendo M·x = e_j por columna."""
    n = len(M)
    cols = []
    for j in range(n):
        e = [1.0 if i == j else 0.0 for i in range(n)]
        cols.append(resolver_lineal(M, e))
    # cols[j] es la columna j de la inversa → transponer a filas.
    return [[cols[j][i] for j in range(n)] for i in range(n)]


_GAUSS3 = [(-math.sqrt(3 / 5), 5 / 9), (0.0, 8 / 9), (math.sqrt(3 / 5), 5 / 9)]


def rigidez_placa(lx: float, ly: float, E: float, nu: float, t: float) -> list[list[float]]:
    """Matriz de rigidez 12×12 del elemento de placa ACM de lados lx×ly."""
    Db = constitutiva_flexion(E, nu, t)
    C_inv = _inversa(_matriz_C(lx, ly))

    # Kpoly = ∫∫ Bᵀ Db B dA por Gauss 3×3 (mapeo [-1,1]→[0,l]).
    Kpoly = [[0.0] * 12 for _ in range(12)]
    jac = (lx / 2.0) * (ly / 2.0)
    for xi, wxi in _GAUSS3:
        x = (xi + 1.0) * lx / 2.0
        for eta, weta in _GAUSS3:
            y = (eta + 1.0) * ly / 2.0
            B = _curvaturas_poly(x, y)
            DB = [[sum(Db[r][k] * B[k][c] for k in range(3)) for c in range(12)] for r in range(3)]
            peso = wxi * weta * jac
            for a in range(12):
                for b in range(12):
                    s = 0.0
                    for r in range(3):
                        s += B[r][a] * DB[r][b]
                    Kpoly[a][b] += peso * s

    # K = C⁻ᵀ · Kpoly · C⁻¹
    Ct = [[C_inv[i][j] for i in range(12)] for j in range(12)]   # (C⁻¹)ᵀ
    tmp = [[sum(Ct[a][k] * Kpoly[k][b] for k in range(12)) for b in range(12)] for a in range(12)]
    K = [[sum(tmp[a][k] * C_inv[k][b] for k in range(12)) for b in range(12)] for a in range(12)]
    return K


def matvec(K: list[list[float]], x: list[float]) -> list[float]:
    """Producto matriz·vector (utilidad para tests de cuerpo rígido)."""
    return [sum(K[i][j] * x[j] for j in range(len(x))) for i in range(len(K))]
