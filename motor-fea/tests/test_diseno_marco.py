"""Tests del motor de diseño de pórtico (Fase 4b.1): estribos, barras, columna P-M, orquestador."""
import math

import pytest

from motor_fea.normativa import aci318


def test_estribo_no_requerido_cuando_vu_bajo():
    bw, d, fc = 300.0, 260.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    e = aci318.disenar_estribo_viga(0.1 * aci318.PHI_CORTANTE * vc, bw, d, fc)
    assert e.vs_requerido == 0.0
    assert e.cumple
    assert e.espaciamiento == pytest.approx(min(d / 2, 600.0))


def test_estribo_disenado_cumple():
    bw, d, fc = 300.0, 500.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    vu = 2.0 * aci318.PHI_CORTANTE * vc                 # requiere Vs > 0
    e = aci318.disenar_estribo_viga(vu, bw, d, fc)
    assert e.vs_requerido > 0
    assert 50.0 <= e.espaciamiento <= d / 2
    assert aci318.verificar_viga_cortante(vu, bw, d, fc, e.av, 420.0, e.espaciamiento).cumple
    assert e.cumple


def test_estribo_insuficiente_cuando_vu_enorme():
    bw, d, fc = 300.0, 400.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    vs_max = aci318.cortante_acero_maximo(bw, d, fc)
    vu = aci318.PHI_CORTANTE * (vc + 2.0 * vs_max)      # Vs_req > Vs_max
    e = aci318.disenar_estribo_viga(vu, bw, d, fc)
    assert not e.cumple
    assert "INSUFICIENTE" in e.disponer


def test_estribo_cerca_del_maximo_cumple():
    # Vs_req ≈ 0.95·Vs_max (sección adecuada): la separación es chica pero el diseño cumple,
    # no debe marcarse insuficiente por el redondeo de s.
    bw, d, fc = 300.0, 500.0, 21.0
    vc = aci318.cortante_concreto(bw, d, fc)
    vs_max = aci318.cortante_acero_maximo(bw, d, fc)
    vu = aci318.PHI_CORTANTE * (vc + 0.95 * vs_max)
    e = aci318.disenar_estribo_viga(vu, bw, d, fc)
    assert e.cumple
    assert e.espaciamiento >= 50.0


def test_seleccionar_barras_no_entra_en_ancho_chico():
    # Muchas barras en un ancho muy chico → no entran → cumple=False.
    sel = aci318.seleccionar_barras(5000.0, ancho_disponible=100.0, num=8)
    assert not sel.cumple


def test_seleccionar_barras_cubre_as():
    sel = aci318.seleccionar_barras(600.0, 300.0, num=5)
    assert sel.n_barras >= 2
    assert sel.as_provista >= 600.0
    assert sel.cumple


def test_seleccionar_barras_as_nan_no_cumple():
    sel = aci318.seleccionar_barras(float("nan"), 300.0, 5)
    assert not sel.cumple
