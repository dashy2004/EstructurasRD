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
