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


def _subbloque(K, dofs):
    """Submatriz de K en los índices globales de shell `dofs` (en orden)."""
    return [[K[i][j] for j in dofs] for i in dofs]


def test_shell_forma_y_simetria():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    assert len(K) == 24 and all(len(fila) == 24 for fila in K)
    for i in range(24):
        for j in range(24):
            assert math.isclose(K[i][j], K[j][i], rel_tol=1e-9, abs_tol=1e-3)


def test_shell_bloques_desacoplados_membrana_placa():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    dofs_mem = [6 * a + d for a in range(4) for d in (0, 1)]        # ux,uy
    dofs_placa = [6 * a + d for a in range(4) for d in (2, 3, 4)]   # uz,θx,θy
    for i in dofs_mem:
        for j in dofs_placa:
            assert K[i][j] == 0.0
            assert K[j][i] == 0.0


def test_shell_reduce_a_membrana():
    # Con drilling apagado (gamma=0), el sub-bloque (ux,uy)×4 = rigidez_membrana.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=0.0)
    dofs_mem = [6 * a + d for a in range(4) for d in (0, 1)]
    sub = _subbloque(K, dofs_mem)
    Km = membrana.rigidez_membrana(NODOS_RECT, E, NU, T)
    for i in range(8):
        for j in range(8):
            assert math.isclose(sub[i][j], Km[i][j], rel_tol=1e-9, abs_tol=1e-3)


def test_shell_reduce_a_placa():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    dofs_placa = [6 * a + d for a in range(4) for d in (2, 3, 4)]
    sub = _subbloque(K, dofs_placa)
    lx, ly = shell._dims_rectangulo(NODOS_RECT)
    Kp = placa.rigidez_placa(lx, ly, E, NU, T)
    for i in range(12):
        for j in range(12):
            assert math.isclose(sub[i][j], Kp[i][j], rel_tol=1e-9, abs_tol=1e-3)
