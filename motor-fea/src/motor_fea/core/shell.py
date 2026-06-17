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


def _dims_rectangulo(nodos_xy: list[tuple[float, float]]) -> tuple[float, float]:
    """Lados (lx, ly) de un rectángulo eje-alineado: |n1−n0| y |n2−n1|.

    Precondición: ``nodos_xy`` debe ser un rectángulo eje-alineado con nodos en
    orden antihorario ``[(x0,y0),(x0+lx,y0),(x0+lx,y0+ly),(x0,y0+ly)]``.
    Lanza ``ValueError`` si no se cumple esta condición, si ``lx≤0`` o ``ly≤0``
    (elemento degenerado/colineal). M2b debe suministrar coordenadas locales
    rectangulares eje-alineadas; el bloque de placa ACM no soporta quads generales.

    Alimenta a la placa ACM (rectangular). Para muros mallados en rectángulos (M3)
    coincide con la geometría real del elemento.
    """
    (x0, y0), (x1, y1), (x2, y2), (x3, y3) = nodos_xy
    lx = math.hypot(x1 - x0, y1 - y0)
    ly = math.hypot(x2 - x1, y2 - y1)

    if lx <= 0.0 or ly <= 0.0:
        raise ValueError(
            f"Shell M2a: elemento degenerado — lx={lx}, ly={ly} deben ser > 0. "
            "Verifique que los nodos no sean colineales."
        )

    tol = 1e-9 * max(lx, ly)

    # Nodo 0 en (x0,y0), nodo 1 en (x0+lx, y0), nodo 2 en (x0+lx, y0+ly),
    # nodo 3 en (x0, y0+ly): rectángulo eje-alineado exacto.
    if (abs(y1 - y0) > tol          # n1 debe tener misma y que n0
            or abs(x2 - x1) > tol   # n2 debe tener misma x que n1
            or abs(x3 - x0) > tol   # n3 debe tener misma x que n0
            or abs(y3 - y2) > tol   # n3 debe tener misma y que n2
            or abs((x1 - x0) - lx) > tol
            or abs((y2 - y1) - ly) > tol):
        raise ValueError(
            "Shell M2a: el bloque de placa ACM requiere un rectángulo eje-alineado. "
            f"Nodos recibidos: {nodos_xy}. "
            "M2b debe suministrar coordenadas locales rectangulares eje-alineadas; "
            "quads generales (trapecios, rombos, etc.) no son soportados en M2a."
        )

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
