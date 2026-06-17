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
