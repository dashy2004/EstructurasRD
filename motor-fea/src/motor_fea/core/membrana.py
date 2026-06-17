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


def esfuerzos_elemento(nodos_xy: list[tuple[float, float]],
                       E: float, nu: float, d_elem: list[float],
                       xi: float = 0.0, eta: float = 0.0
                       ) -> tuple[float, float, float]:
    """Esfuerzos (σxx, σyy, τxy) en Pa en el punto natural (ξ, η).

    ``d_elem`` = 8 GDL nodales [ux,uy ×4]. σ = D·B·d_elem. El centro (0,0) es el
    punto de superconvergencia del Q4 (mejor precisión).
    """
    if len(d_elem) != 8:
        raise ValueError(
            f"d_elem debe tener exactamente 8 componentes [ux,uy ×4 nodos]; "
            f"se recibieron {len(d_elem)}.")
    B, _ = _matriz_B(nodos_xy, xi, eta)
    D = constitutiva_plana(E, nu)
    eps = [sum(B[r][k] * d_elem[k] for k in range(8)) for r in range(3)]
    sig = [sum(D[r][k] * eps[k] for k in range(3)) for r in range(3)]
    return (sig[0], sig[1], sig[2])
