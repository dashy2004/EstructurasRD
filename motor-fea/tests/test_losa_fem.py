"""Validación del FEM de losas por convergencia vs la placa cuadrada SS.

Para una placa cuadrada simplemente apoyada bajo carga uniforme, la deflexión
central exacta (Timoshenko) es ``w = 0.00406 q a⁴/D`` con ``D = Et³/12(1−ν²)``.
El elemento ACM converge monótonamente: el error de la malla 10×10 es < 0.5%.
"""
from motor_fea.core.losa_fem import resolver_losa_rectangular, rigidez_flexional_placa

A = 5.0
E, NU, T = 2.0e10, 0.2, 0.2
Q = 10000.0
D = rigidez_flexional_placa(E, NU, T)
W_EXACTO = 0.00406 * Q * A ** 4 / D     # placa cuadrada SS (Timoshenko)


def _err(nmesh: int) -> float:
    r = resolver_losa_rectangular(A, A, nmesh, nmesh, E, NU, T, Q, "simple")
    return abs(r.w_central - W_EXACTO) / W_EXACTO


def test_convergencia_monotona():
    e4, e6, e8, e10 = _err(4), _err(6), _err(8), _err(10)
    assert e4 > e6 > e8 > e10          # el error decrece al refinar
    assert e10 < 0.01                  # malla 10×10 dentro del 1%


def test_signo_y_magnitud_razonables():
    r = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "simple")
    assert r.w_central > 0             # se deflecta hacia la carga
    assert abs(r.w_central - W_EXACTO) / W_EXACTO < 0.02


def test_deflexion_lineal_en_la_carga():
    r1 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, Q, "simple")
    r2 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, 3 * Q, "simple")
    assert abs(r2.w_central / r1.w_central - 3.0) < 1e-9


def test_empotrada_se_deflecta_menos_que_simple():
    rs = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "simple")
    re = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "empotrado")
    assert re.w_central < rs.w_central     # los bordes empotrados rigidizan
    # La empotrada cuadrada ronda 0.00126 q a⁴/D (≈ 0.31× la simple).
    assert re.w_central < 0.5 * rs.w_central


def test_w_cero_en_el_borde():
    r = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, Q, "simple")
    for (i, j), w in r.desplazamientos_w.items():
        if i in (0, 6) or j in (0, 6):
            assert abs(w) < 1e-18          # apoyo: w=0 en el contorno
