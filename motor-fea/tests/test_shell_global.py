"""Tests del shell en coordenadas globales (M2b): tríada local, proyección 2D,
transformación 24×24 e invariancia por rotación rígida del elemento."""
from __future__ import annotations

import math

from motor_fea.core import membrana, shell

# Rectángulo de referencia (mismo que M2a) tumbado en el plano global z=0.
NODOS_2D = [(0.0, 0.0), (2.0, 0.0), (2.0, 3.0), (0.0, 3.0)]
NODOS_Z0 = [(x, y, 0.0) for (x, y) in NODOS_2D]
E, NU, T = 2.1e11, 0.2, 0.2


def _energia(K, d):
    Kd = membrana.matvec(K, d)
    return sum(d[i] * Kd[i] for i in range(len(d)))


def _cruz(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _rot(theta, phi):
    """R = Rz(theta)·Rx(phi), rotación ortonormal de prueba."""
    cz, sz, cx, sx = math.cos(theta), math.sin(theta), math.cos(phi), math.sin(phi)
    Rz = [[cz, -sz, 0.0], [sz, cz, 0.0], [0.0, 0.0, 1.0]]
    Rx = [[1.0, 0.0, 0.0], [0.0, cx, -sx], [0.0, sx, cx]]
    return [[sum(Rz[i][k] * Rx[k][j] for k in range(3)) for j in range(3)]
            for i in range(3)]


def _aplicar(R, nodos3d):
    return [tuple(sum(R[i][k] * p[k] for k in range(3)) for i in range(3))
            for p in nodos3d]


def _modos_rigidos_globales(nodos3d):
    """6 modos de cuerpo rígido en coordenadas GLOBALES (24 GDL)."""
    modos = []
    for comp in (0, 1, 2):                       # traslaciones ux, uy, uz
        d = [0.0] * 24
        for a in range(4):
            d[6 * a + comp] = 1.0
        modos.append(d)
    for omega in ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)):
        d = [0.0] * 24
        for a, p in enumerate(nodos3d):
            t = _cruz(omega, p)                  # u = ω × p (rotación rígida)
            d[6 * a + 0], d[6 * a + 1], d[6 * a + 2] = t
            d[6 * a + 3], d[6 * a + 4], d[6 * a + 5] = omega
        modos.append(d)
    return modos


def _pivotes_positivos(M):
    n = len(M)
    A = [list(f) for f in M]
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


def test_global_reduce_a_local_en_z0():
    # En el plano z=0 la tríada es la identidad → K global = K local de M2a.
    Kg = shell.rigidez_shell_global(NODOS_Z0, E, NU, T)
    Kl = shell.rigidez_shell(NODOS_2D, E, NU, T)
    assert len(Kg) == 24 and all(len(f) == 24 for f in Kg)
    for i in range(24):
        for j in range(24):
            assert math.isclose(Kg[i][j], Kl[i][j], rel_tol=1e-9, abs_tol=1e-3)


def test_global_simetria():
    Kg = shell.rigidez_shell_global(_aplicar(_rot(0.7, 0.4), NODOS_Z0), E, NU, T)
    for i in range(24):
        for j in range(24):
            assert math.isclose(Kg[i][j], Kg[j][i], rel_tol=1e-9, abs_tol=1e-3)


def test_global_invariante_traza_frobenius():
    # T ortogonal ⇒ traza y norma de Frobenius se conservan bajo Tᵀ·K·T.
    Kl = shell.rigidez_shell(NODOS_2D, E, NU, T)
    Kg = shell.rigidez_shell_global(_aplicar(_rot(0.7, 0.4), NODOS_Z0), E, NU, T)
    trl = sum(Kl[i][i] for i in range(24))
    trg = sum(Kg[i][i] for i in range(24))
    frl = math.sqrt(sum(Kl[i][j] ** 2 for i in range(24) for j in range(24)))
    frg = math.sqrt(sum(Kg[i][j] ** 2 for i in range(24) for j in range(24)))
    assert math.isclose(trl, trg, rel_tol=1e-9)
    assert math.isclose(frl, frg, rel_tol=1e-9)


def test_global_seis_modos_rigidos_energia_cero():
    nodos3d = _aplicar(_rot(0.7, 0.4), NODOS_Z0)
    Kg = shell.rigidez_shell_global(nodos3d, E, NU, T)
    ref = max(abs(Kg[i][i]) for i in range(24))
    for d in _modos_rigidos_globales(nodos3d):
        dscale = max(abs(v) for v in d) or 1.0
        assert abs(_energia(Kg, d)) < 1e-6 * ref * dscale * dscale


def test_global_rango_18():
    # nodo 0 en el origen (invariante bajo rotación): fijar sus 6 GDL mata los
    # 6 modos rígidos → reducida 18×18 definida positiva.
    nodos3d = _aplicar(_rot(0.7, 0.4), NODOS_Z0)
    Kg = shell.rigidez_shell_global(nodos3d, E, NU, T, gamma=E * T)
    libres = list(range(6, 24))
    Kred = [[Kg[i][j] for j in libres] for i in libres]
    assert _pivotes_positivos(Kred)
