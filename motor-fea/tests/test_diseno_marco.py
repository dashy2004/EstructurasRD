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


def _capas_columna(b, h, rec, rho, num=8):
    as_total = rho * b * h
    dbar = aci318._diametro_barra(num)
    return [(rec + dbar / 2, as_total / 2), (h - rec - dbar / 2, as_total / 2)]


def test_diagrama_interaccion_tiene_n_puntos():
    diag = aci318.diagrama_interaccion(400, 400, 28, 420, _capas_columna(400, 400, 50, 0.02), n=40)
    assert len(diag) == 40
    assert all(isinstance(p, aci318.PuntoInteraccion) for p in diag)


def test_momento_capacidad_en_un_nodo():
    diag = aci318.diagrama_interaccion(400, 400, 28, 420, _capas_columna(400, 400, 50, 0.02), n=40)
    p = diag[30]                                        # punto de compresión
    assert aci318.momento_capacidad(p.phi_pn, diag) == pytest.approx(abs(p.phi_mn), rel=1e-6)


def test_columna_cumple_demanda_dentro_del_diagrama():
    b, h, fc, fy, rec = 400.0, 400.0, 28.0, 420.0, 50.0
    diag = aci318.diagrama_interaccion(b, h, fc, fy, _capas_columna(b, h, rec, 0.02))
    p = diag[30]
    pu = max(p.phi_pn, 1.0)
    cap = aci318.momento_capacidad(pu, diag)
    d = aci318.disenar_columna_pm(pu, 0.5 * cap, b, h, fc, fy, rec)
    assert d.cumple
    assert 0.01 <= d.rho <= 0.08


def test_columna_insuficiente_si_excede_rho_max():
    b, h, fc, fy, rec = 400.0, 400.0, 28.0, 420.0, 50.0
    diag = aci318.diagrama_interaccion(b, h, fc, fy, _capas_columna(b, h, rec, 0.08))
    p = diag[30]
    pu = max(p.phi_pn, 1.0)
    cap_max = aci318.momento_capacidad(pu, diag)
    d = aci318.disenar_columna_pm(pu, 1.5 * cap_max, b, h, fc, fy, rec)
    assert not d.cumple


def test_momento_capacidad_interpola_entre_nodos():
    diag = aci318.diagrama_interaccion(400, 400, 28, 420, _capas_columna(400, 400, 50, 0.02), n=40)
    pares = sorted((p.phi_pn, abs(p.phi_mn)) for p in diag)
    (p0, m0), (p1, m1) = pares[28], pares[29]
    cap = aci318.momento_capacidad((p0 + p1) / 2, diag)
    lo, hi = sorted((m0, m1))
    assert lo - 1e-6 <= cap <= hi + 1e-6          # cae entre los momentos de los nodos


def test_columna_pura_axial_cumple():
    # axial puro modesto (mu=0) → cumple con armado mínimo.
    d = aci318.disenar_columna_pm(200000.0, 0.0, 400, 400, 28, 420, 50)
    assert d.cumple
    assert d.mu == 0.0
