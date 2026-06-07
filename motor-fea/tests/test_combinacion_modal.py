"""Tests de la combinación modal SRSS / CQC."""
import math

from motor_fea.core.combinacion_modal import cqc, coeficiente_correlacion, srss


def test_srss_clasico_3_4_5():
    assert abs(srss([3.0, 4.0]) - 5.0) < 1e-12


def test_rho_coincidentes_es_1():
    assert coeficiente_correlacion(10.0, 10.0) == 1.0


def test_rho_decrece_al_separar_frecuencias():
    cercano = coeficiente_correlacion(10.0, 10.5)
    lejano = coeficiente_correlacion(10.0, 50.0)
    assert 0.0 < lejano < cercano < 1.0


def test_cqc_iguala_srss_con_modos_bien_separados():
    v = [3.0, 4.0]
    f = [5.0, 50.0]            # frecuencias muy separadas → correlación ~0
    assert abs(cqc(v, f) - srss(v)) / srss(v) < 1e-3


def test_cqc_supera_srss_con_frecuencias_cercanas():
    v = [3.0, 4.0]
    f = [10.0, 10.5]           # frecuencias cercanas → correlación positiva
    assert cqc(v, f) > srss(v)


def test_cqc_un_solo_modo_es_su_valor_absoluto():
    assert abs(cqc([7.0], [12.0]) - 7.0) < 1e-12


def test_validaciones():
    import pytest
    with pytest.raises(ValueError):
        cqc([1.0, 2.0], [10.0])            # longitudes distintas
    with pytest.raises(ValueError):
        coeficiente_correlacion(0.0, 10.0)  # frecuencia no positiva
