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


def _modos_rigidos(nodos_xy):
    """Los 6 modos de cuerpo rígido del shell plano, como vectores de 24 GDL.

    GDL/nodo: [ux,uy,uz,θx,θy,θz]. Placa: θx=∂w/∂y, θy=−∂w/∂x.
    """
    n = len(nodos_xy)
    modos = []
    # 3 traslaciones.
    for comp in (0, 1, 2):                       # ux, uy, uz
        d = [0.0] * (6 * n)
        for a in range(n):
            d[6 * a + comp] = 1.0
        modos.append(d)
    # Rotación fuera del plano θx (w = y → θx=1, θy=0).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 2] = y         # uz = w = y
        d[6 * a + 3] = 1.0       # θx
    modos.append(d)
    # Rotación fuera del plano θy (w = x → θx=0, θy=−1).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 2] = x         # uz = w = x
        d[6 * a + 4] = -1.0      # θy
    modos.append(d)
    # Rotación en el plano θz (ux=-y, uy=x, θz=1).
    d = [0.0] * (6 * n)
    for a, (x, y) in enumerate(nodos_xy):
        d[6 * a + 0] = -y
        d[6 * a + 1] = x
        d[6 * a + 5] = 1.0
    modos.append(d)
    return modos


def test_shell_seis_modos_rigidos_energia_cero():
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T)
    ref = max(abs(K[i][i]) for i in range(24))
    modos = _modos_rigidos(NODOS_RECT)
    assert len(modos) == 6
    for d in modos:
        dscale = max(abs(v) for v in d) or 1.0
        assert abs(_energia(K, d)) < 1e-6 * ref * dscale * dscale


def test_shell_drilling_no_nulo():
    # Con gamma>0, θz diferencial (membrana/placa fijas) da energía positiva.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=E * T)
    d = [0.0] * 24
    d[5] = 1.0                  # θz del nodo 0
    assert _energia(K, d) > 0.0


def _pivotes_positivos(M):
    """True si la eliminación gaussiana simétrica de M (n×n) tiene todos los
    pivotes > 0 (≈ definida positiva). M se copia; no se modifica el original."""
    n = len(M)
    A = [list(fila) for fila in M]
    umbral = 1e-6 * max(abs(A[i][i]) for i in range(n))
    for k in range(n):
        piv = A[k][k]
        if piv <= umbral:
            return False
        for i in range(k + 1, n):
            f = A[i][k] / piv
            for j in range(k, n):
                A[i][j] -= f * A[k][j]
    return True


def test_shell_rango_18():
    # Fijar los 6 GDL del nodo 0 (en el origen) elimina los 6 modos rígidos.
    K = shell.rigidez_shell(NODOS_RECT, E, NU, T, gamma=E * T)
    libres = list(range(6, 24))                  # GDL del nodo 0 = 0..5 fijos
    Kred = [[K[i][j] for j in libres] for i in libres]
    assert len(Kred) == 18
    assert _pivotes_positivos(Kred)
