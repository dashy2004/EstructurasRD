"""Tests de integración del shell en el modelo y el solver (M2b, Slices 2-3)."""
from __future__ import annotations

import math

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoShell, Material, ModeloEstructural, Nodo,
)
from motor_fea.core.solver import ensamblar_global, resolver

E, NU, T = 2.1e10, 0.2, 0.2

# Muro 3(ancho)×4(alto) en el plano vertical y=0; nodos antihorario en su plano.
NODOS_MURO = [
    Nodo(0, 0.0, 0.0, 0.0),
    Nodo(1, 3.0, 0.0, 0.0),
    Nodo(2, 3.0, 0.0, 4.0),
    Nodo(3, 0.0, 0.0, 4.0),
]


def _modelo_muro(cargas=None, apoyos=None):
    return ModeloEstructural(
        nodos=list(NODOS_MURO),
        materiales=[Material(0, E, NU)],
        elementos_shell=[ElementoShell(0, (0, 1, 2, 3), material_id=0, espesor=T)],
        apoyos=apoyos or [],
        cargas=cargas or [],
    )


def test_shell_valida_ok():
    assert _modelo_muro().validar() == []


def test_shell_valida_nodo_inexistente():
    m = ModeloEstructural(
        nodos=list(NODOS_MURO),
        materiales=[Material(0, E, NU)],
        elementos_shell=[ElementoShell(0, (0, 1, 2, 99), material_id=0, espesor=T)],
    )
    assert any("99" in e for e in m.validar())


def test_shell_valida_nodos_repetidos():
    m = ModeloEstructural(
        nodos=list(NODOS_MURO),
        materiales=[Material(0, E, NU)],
        elementos_shell=[ElementoShell(0, (0, 1, 2, 2), material_id=0, espesor=T)],
    )
    assert m.validar() != []


def test_ensamblaje_shell_simetrico_y_no_nulo():
    m = _modelo_muro()
    K = ensamblar_global(m)
    n = m.n_gdl
    assert n == 24 and len(K) == 24
    for i in range(n):
        for j in range(n):
            assert math.isclose(K[i][j], K[j][i], rel_tol=1e-9, abs_tol=1e-3)
    assert any(K[i][j] != 0.0 for i in range(n) for j in range(n))


def test_muro_shell_resuelve_bajo_carga_lateral():
    # Base (nodos 0,1) empotrada; carga lateral en X en la cabeza (nodos 2,3).
    apoyos = [Apoyo.empotrado(0), Apoyo.empotrado(1)]
    P = 1.0e4
    cargas = [CargaNodal(2, fx=P, caso="W"), CargaNodal(3, fx=P, caso="W")]
    m = _modelo_muro(cargas=cargas, apoyos=apoyos)
    assert m.validar() == []
    res = resolver(m)

    # La cabeza se desplaza en +X (dirección de la carga); la base no.
    ux_cabeza = res.desplazamientos[2][0] + res.desplazamientos[3][0]
    assert ux_cabeza > 0.0
    assert math.isclose(res.desplazamientos[0][0], 0.0, abs_tol=1e-12)

    # Equilibrio: ΣRx en la base = −ΣFx aplicada.
    rx = res.reacciones[0][0] + res.reacciones[1][0]
    assert math.isclose(rx, -2.0 * P, rel_tol=1e-6)
