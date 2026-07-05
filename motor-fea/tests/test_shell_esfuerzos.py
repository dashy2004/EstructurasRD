"""Tests de recuperación de esfuerzos/momentos del shell desde la solución global (M2c)."""
from __future__ import annotations

import math

import pytest

from motor_fea.core import placa, shell
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoShell, Material, ModeloEstructural, Nodo,
)
from motor_fea.core.solver import esfuerzos_shells, resolver

E, NU, T = 2.5e10, 0.2, 0.2

# Panel 2(x)×3(y) plano en XY: marco local = global (identidad).
PANEL_XY = [(0.0, 0.0, 0.0), (2.0, 0.0, 0.0), (2.0, 3.0, 0.0), (0.0, 3.0, 0.0)]
# El mismo panel parado como muro en el plano XZ: local x→X, local y→Z, normal→−Y.
PANEL_XZ = [(0.0, 0.0, 0.0), (2.0, 0.0, 0.0), (2.0, 0.0, 3.0), (0.0, 0.0, 3.0)]

# GDL de placa arbitrarios [w,θx,θy ×4] para validar el mapeo de bloques.
D_PLACA = [1e-4, 2e-5, -3e-5, -8e-5, 1e-5, 4e-5,
           5e-5, -2e-5, 6e-5, -7e-5, 3e-5, -1e-5]

EPS_X = 1e-4   # deformación unitaria del campo de extensión pura


def _d24_extension_mas_placa() -> list[float]:
    """Campo LOCAL combinado: extensión pura ux=εx·x + GDL de placa D_PLACA."""
    d = [0.0] * 24
    for a, (x, _y, _z) in enumerate(PANEL_XY):
        d[6 * a + 0] = EPS_X * x
        d[6 * a + 2] = D_PLACA[3 * a + 0]
        d[6 * a + 3] = D_PLACA[3 * a + 1]
        d[6 * a + 4] = D_PLACA[3 * a + 2]
    return d


def test_d24_largo_incorrecto():
    with pytest.raises(ValueError):
        shell.esfuerzos_shell_global(PANEL_XY, E, NU, T, [0.0] * 23)


def test_membrana_extension_pura_panel_xy():
    (sxx, syy, txy), _ = shell.esfuerzos_shell_global(
        PANEL_XY, E, NU, T, _d24_extension_mas_placa())
    sxx_teo = E / (1.0 - NU ** 2) * EPS_X   # tensión plana, εy = 0
    assert math.isclose(sxx, sxx_teo, rel_tol=1e-9)
    assert math.isclose(syy, NU * sxx_teo, rel_tol=1e-9)
    assert math.isclose(txy, 0.0, abs_tol=1e-3)


def test_momentos_coinciden_con_placa_directa():
    # El shell plano en XY debe extraer los GDL (w,θx,θy) tal cual (identidad).
    _, (mx, my, mxy) = shell.esfuerzos_shell_global(
        PANEL_XY, E, NU, T, _d24_extension_mas_placa())
    mx_d, my_d, mxy_d = placa.momentos_elemento(2.0, 3.0, E, NU, T, D_PLACA, 0.5, 0.5)
    assert math.isclose(mx, mx_d, rel_tol=1e-9)
    assert math.isclose(my, my_d, rel_tol=1e-9)
    assert math.isclose(mxy, mxy_d, rel_tol=1e-9)


def test_invariancia_rotacion_muro_xz():
    # El mismo campo local, expresado en global sobre el muro XZ, debe dar
    # exactamente los mismos esfuerzos/momentos LOCALES que el panel XY.
    base_m, base_p = shell.esfuerzos_shell_global(
        PANEL_XY, E, NU, T, _d24_extension_mas_placa())

    ex, ey, ez = shell._marco_shell(PANEL_XZ)
    R = [ex, ey, ez]                       # filas = ejes locales
    d_loc = _d24_extension_mas_placa()
    d_rot = [0.0] * 24                     # u_global = Rᵀ·u_local por terna
    for blk in range(8):
        o = 3 * blk
        for i in range(3):
            d_rot[o + i] = sum(R[j][i] * d_loc[o + j] for j in range(3))

    rot_m, rot_p = shell.esfuerzos_shell_global(PANEL_XZ, E, NU, T, d_rot)
    for a, b in zip(base_m + base_p, rot_m + rot_p):
        assert math.isclose(a, b, rel_tol=1e-9, abs_tol=1e-6)


def test_solver_muro_compresion_axial():
    # Muro 2(ancho)×3(alto) en XZ, base empotrada, ν=0; compresión axial P en
    # el tope → estado de deformación constante (exacto para el Q4):
    # σyy_local = −P/(ancho·t); lo demás ≈ 0 y sin flexión fuera del plano.
    P = 1.0e6
    nodos = [Nodo(0, 0.0, 0.0, 0.0), Nodo(1, 2.0, 0.0, 0.0),
             Nodo(2, 2.0, 0.0, 3.0), Nodo(3, 0.0, 0.0, 3.0)]
    m = ModeloEstructural(
        nodos=nodos,
        materiales=[Material(0, E, 0.0)],
        elementos_shell=[ElementoShell(0, (0, 1, 2, 3), material_id=0, espesor=T)],
        apoyos=[Apoyo.empotrado(0), Apoyo.empotrado(1)],
        cargas=[CargaNodal(2, fz=-P / 2.0, caso="D"),
                CargaNodal(3, fz=-P / 2.0, caso="D")],
    )
    assert m.validar() == []
    res = resolver(m)
    ef = esfuerzos_shells(m, res)

    assert set(ef) == {0} and ef[0].elemento_id == 0
    sxx, syy, txy = ef[0].membrana
    syy_teo = -P / (2.0 * T)
    assert math.isclose(syy, syy_teo, rel_tol=1e-6)
    assert abs(sxx) < abs(syy_teo) * 1e-6
    assert abs(txy) < abs(syy_teo) * 1e-6
    for mv in ef[0].momentos:
        assert abs(mv) < 1.0    # N·m/m — carga en el plano: sin flexión
