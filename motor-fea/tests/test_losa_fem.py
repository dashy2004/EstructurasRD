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


# ---- Losa RECTANGULAR (caso real, a/b ≠ 1) vs Timoshenko (w = α q b⁴/D, b=corto) ----
def _w(a: float, b: float, nx: int, ny: int) -> float:
    return resolver_losa_rectangular(a, b, nx, ny, E, NU, T, Q, "simple").w_central


def test_rectangular_ab_2_converge_a_timoshenko():
    b = 4.0
    a = 2.0 * b
    w_ref = 0.01013 * Q * b ** 4 / D          # α(a/b=2) = 0.01013
    err_grueso = abs(_w(a, b, 8, 4) - w_ref) / w_ref
    err_fino = abs(_w(a, b, 16, 8) - w_ref) / w_ref
    assert err_fino < err_grueso              # converge al refinar
    assert err_fino < 0.02                    # malla fina dentro del 2%


def test_rectangular_ab_1_5_converge_a_timoshenko():
    b = 4.0
    a = 1.5 * b
    w_ref = 0.00772 * Q * b ** 4 / D          # α(a/b=1.5) = 0.00772
    err_grueso = abs(_w(a, b, 6, 4) - w_ref) / w_ref
    err_fino = abs(_w(a, b, 12, 8) - w_ref) / w_ref
    assert err_fino < err_grueso
    assert err_fino < 0.02


def test_rectangular_momento_vano_corto_domina():
    # a/b=2: el momento que flexiona el vano corto (My, con b en Y) domina sobre Mx.
    r = resolver_losa_rectangular(8.0, 4.0, 16, 8, E, NU, T, Q, "simple")
    assert r.my_max > r.mx_max


# ---- Momento de apoyo (acero superior): empotrada vs simplemente apoyada ----
def test_simple_apoyo_casi_cero_y_decrece():
    r6 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, Q, "simple")
    r10 = resolver_losa_rectangular(A, A, 10, 10, E, NU, T, Q, "simple")
    # Apoyo simple → momento de borde ≈0 (M_n=0) y << momento de vano.
    assert r10.m_apoyo_max < 0.05 * r10.mx_max
    assert r10.m_apoyo_max < r6.m_apoyo_max          # decrece hacia 0 al refinar


def test_empotrada_tiene_momento_de_apoyo_que_domina():
    rs = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "simple")
    re = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "empotrado")
    # La empotrada desarrolla momento de apoyo grande (la SS no).
    assert re.m_apoyo_max > 10 * rs.m_apoyo_max
    # En placa empotrada cuadrada el momento de apoyo gobierna sobre el de vano.
    assert re.m_apoyo_max > re.mx_max


def test_empotrada_apoyo_converge():
    qa2 = Q * A * A
    c6 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, Q, "empotrado").m_apoyo_max / qa2
    c10 = resolver_losa_rectangular(A, A, 10, 10, E, NU, T, Q, "empotrado").m_apoyo_max / qa2
    assert c6 < c10                                   # converge hacia ~0.05 (Timoshenko)
    assert 0.04 < c10 < 0.055


def test_momentos_nodales_cubre_la_malla_y_es_simetrico_en_el_centro():
    r = resolver_losa_rectangular(A, A, 4, 4, E, NU, T, Q, "simple")
    # cubre los (nx+1)*(ny+1) = 25 nodos de la grilla
    assert len(r.momentos_nodales) == 25
    # nodo central (2,2): por simetría de la losa cuadrada, mx ≈ my
    mx_c, my_c = r.momentos_nodales[(2, 2)]
    assert abs(mx_c - my_c) < 1e-6 * (abs(mx_c) + abs(my_c))
    # momento interior no nulo
    assert abs(mx_c) > 0.0
