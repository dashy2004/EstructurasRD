"""Momentos del FEM de losa + diseño de acero (cierre análisis→diseño).

Para la placa cuadrada SS bajo carga uniforme: Mx≈My en el centro (simetría) y
el coeficiente Mx/(q·a²) converge monótonamente al refinar la malla. Luego el
momento alimenta el diseño de acero ACI 318-19.
"""
from motor_fea.core.losa_fem import resolver_losa_rectangular
from motor_fea.diseno_losa import disenar_losa

A = 5.0
E, NU, T = 2.0e10, 0.2, 0.2
Q = 10000.0
QA2 = Q * A * A


def _coef(nmesh: int) -> float:
    r = resolver_losa_rectangular(A, A, nmesh, nmesh, E, NU, T, Q, "simple")
    return r.mx_max / QA2


def test_momentos_simetricos_en_placa_cuadrada():
    r = resolver_losa_rectangular(A, A, 8, 8, E, NU, T, Q, "simple")
    assert abs(r.mx_max - r.my_max) / r.mx_max < 0.01     # Mx ≈ My por simetría


def test_coeficiente_de_momento_converge_y_es_plausible():
    c4, c6, c8, c10 = _coef(4), _coef(6), _coef(8), _coef(10)
    assert c4 < c6 < c8 < c10                              # converge (crece al refinar)
    assert 0.035 < c10 < 0.052                             # banda plausible (SS cuadrada)


def test_momento_lineal_en_la_carga():
    r1 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, Q, "simple")
    r3 = resolver_losa_rectangular(A, A, 6, 6, E, NU, T, 3 * Q, "simple")
    assert abs(r3.mx_max / r1.mx_max - 3.0) < 1e-9


def test_diseno_losa_fem_end_to_end():
    # FEM → momentos → acero (f'c=21, fy=420 MPa).
    d = disenar_losa(A, A, 8, 8, E, NU, T, Q, fc_mpa=21.0, fy_mpa=420.0, recubrimiento_mm=25.0)
    assert d.analisis.mx_max > 0
    assert d.mu_x > 0 and d.mu_y > 0
    assert d.franja_x.armadura.cumple                     # la armadura seleccionada cumple
    assert d.franja_x.as_diseno > 0
    # Placa cuadrada → diseño X y Y prácticamente iguales.
    assert abs(d.franja_x.as_diseno - d.franja_y.as_diseno) / d.franja_x.as_diseno < 0.01


def test_diseno_losa_fem_mas_carga_pide_mas_acero():
    d1 = disenar_losa(A, A, 6, 6, E, NU, T, Q, 21.0, 420.0)
    d2 = disenar_losa(A, A, 6, 6, E, NU, T, 4 * Q, 21.0, 420.0)
    assert d2.franja_x.as_requerido >= d1.franja_x.as_requerido
