import math

import pytest

from motor_fea.core.membrana import (
    constitutiva_plana,
    matvec,
    rigidez_membrana,
)


# Cuadrado unitario de referencia para varios tests.
_CUADRADO = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
_E, _NU, _T = 2.0e10, 0.2, 0.25


def test_constitutiva_plana_valores_cerrados():
    E, nu = 2.0e10, 0.2
    D = constitutiva_plana(E, nu)
    factor = E / (1.0 - nu * nu)
    assert D[0][0] == pytest.approx(factor)
    assert D[1][1] == pytest.approx(factor)
    assert D[0][1] == pytest.approx(factor * nu)
    assert D[1][0] == pytest.approx(factor * nu)
    assert D[2][2] == pytest.approx(factor * (1.0 - nu) / 2.0)
    assert D[0][2] == 0.0 and D[1][2] == 0.0
    assert D[2][0] == 0.0 and D[2][1] == 0.0


def test_rigidez_simetrica():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    assert len(K) == 8 and all(len(fila) == 8 for fila in K)
    for i in range(8):
        for j in range(8):
            assert K[i][j] == pytest.approx(K[j][i], rel=1e-9, abs=1.0)


def test_modos_cuerpo_rigido_traslacion():
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    trans_x = [1.0, 0.0] * 4          # ux=1 en los 4 nodos
    trans_y = [0.0, 1.0] * 4          # uy=1 en los 4 nodos
    for u in (trans_x, trans_y):
        f = matvec(K, u)
        assert all(abs(fi) < 1e-3 for fi in f)


def test_modo_cuerpo_rigido_rotacion():
    # Rotación infinitesimal en torno al origen: u=-y, v=x → deformación nula.
    K = rigidez_membrana(_CUADRADO, _E, _NU, _T)
    u_rot = []
    for (x, y) in _CUADRADO:
        u_rot.extend([-y, x])
    f = matvec(K, u_rot)
    assert all(abs(fi) < 1e-3 for fi in f)


def test_rigidez_degenerada_lanza():
    horario = [(0.0, 0.0), (0.0, 1.0), (1.0, 1.0), (1.0, 0.0)]  # orden horario
    with pytest.raises(ValueError):
        rigidez_membrana(horario, _E, _NU, _T)
