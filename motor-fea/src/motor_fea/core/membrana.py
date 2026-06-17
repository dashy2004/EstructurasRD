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
