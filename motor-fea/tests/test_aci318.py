"""Tests de la verificación de vigas ACI 318-19 (N, mm, MPa)."""
import math

from motor_fea.normativa import aci318

# Ejemplo de referencia: b=300, d=540 mm; f'c=28, fy=420 MPa.
B, D, FC, FY = 300.0, 540.0, 28.0, 420.0


def test_as_requerido_consistencia_phiMn_igual_Mu():
    # Verificación interna: el As requerido para un Mu debe reproducir φMn ≈ Mu.
    mu = 200e6  # N·mm = 200 kN·m
    as_req, insuf = aci318.as_requerido_flexion(mu, B, D, FC, FY)
    assert not insuf
    phi_mn = aci318.momento_resistente(as_req, B, D, FC, FY)
    assert abs(phi_mn - mu) / mu < 1e-6


def test_as_requerido_valor_calculado_a_mano():
    as_req, _ = aci318.as_requerido_flexion(200e6, B, D, FC, FY)
    assert abs(as_req - 1038.56) < 0.5      # mm², calculado a mano


def test_as_minimo_flexion():
    # max(0.25√28/420, 1.4/420)·300·540
    rho = max(0.25 * math.sqrt(28) / 420, 1.4 / 420)
    assert abs(aci318.as_minimo_flexion(B, D, FC, FY) - rho * B * D) < 1e-6


def test_seccion_insuficiente_devuelve_nan():
    as_req, insuf = aci318.as_requerido_flexion(5000e6, B, D, FC, FY)
    assert insuf and math.isnan(as_req)


def test_verificar_flexion_cumple_y_no_cumple():
    # As provisto generoso → cumple; As escaso → no cumple.
    ok = aci318.verificar_viga_flexion(200e6, B, D, FC, FY, as_provisto=1500.0)
    assert ok.cumple and ok.ratio < 1.0
    no = aci318.verificar_viga_flexion(200e6, B, D, FC, FY, as_provisto=600.0)
    assert not no.cumple


def test_cortante_concreto_valor():
    vc = aci318.cortante_concreto(B, D, FC)
    # 0.17·√28·300·540
    assert abs(vc - 0.17 * math.sqrt(28) * B * D) < 1e-6
    assert abs(vc - 145729) < 50            # ~145.7 kN


def test_cortante_acero_y_resistencia():
    vs = aci318.cortante_acero(av=142.0, fyt=420.0, d=D, s=150.0)   # 2 ramas #10
    assert abs(vs - 142.0 * 420.0 * D / 150.0) < 1e-6
    r = aci318.verificar_viga_cortante(150e3, B, D, FC, av=142.0, fyt=420.0, s=150.0)
    assert r.phi_vn == 0.75 * (r.vc + r.vs)
    assert r.cumple == (r.ratio <= 1.0 + 1e-9 and not r.vs_excede_maximo)


def test_cortante_detecta_Vs_excesivo():
    # Estribos muy juntos → Vs por encima del máximo (§22.5.1.2).
    r = aci318.verificar_viga_cortante(150e3, B, D, FC, av=400.0, fyt=420.0, s=40.0)
    assert r.vs_excede_maximo
    assert not r.cumple
