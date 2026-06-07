"""Tests del motor de diseño de pórtico (Fase 4b.1): estribos, barras, columna P-M, orquestador."""
import math

import pytest

from motor_fea.normativa import aci318
from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.solver import esfuerzos_elementos, resolver
from motor_fea import diseno_elemento


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


_E, _NU, _L, _P = 2.0e10, 0.2, 3.0, 1000.0


def _voladizo(carga, lado=0.30):
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, _L, 0, 0)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=lado * lado, inercia_y=lado ** 4 / 12,
                               inercia_z=lado ** 4 / 12, constante_torsion=0.1406 * lado ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(carga)
    return m


def test_disenar_viga_voladizo_cumple():
    m = _voladizo(CargaNodal(2, fz=_P))
    esf = esfuerzos_elementos(m, resolver(m))[1]
    d = diseno_elemento.disenar_viga(esf, b=0.30, h=0.30)
    assert d.mu == pytest.approx(_P * _L, rel=1e-3)     # Mu ≈ P·L
    assert d.vu == pytest.approx(_P, rel=1e-3)          # Vu ≈ P
    assert d.flexion is not None and d.flexion.cumple
    assert d.estribo.cumple
    assert d.cumple


def test_disenar_columna_extrae_axial():
    # columna 0.40×0.40 por Z, axial de compresión modesto + lateral pequeño.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, _L)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-150000.0), CargaNodal(2, fx=5000.0)]
    esf = esfuerzos_elementos(m, resolver(m))[1]
    d = diseno_elemento.disenar_columna(esf, b=0.40, h=0.40, fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d.pu == pytest.approx(150000.0, rel=1e-3)    # axial extraído
    assert d.cumple


def test_disenar_viga_seccion_insuficiente():
    # sección chica (0.20×0.20) bajo un Mu enorme → insuficiente a flexión (flexion=None).
    m = _voladizo(CargaNodal(2, fz=50000.0), lado=0.20)
    esf = esfuerzos_elementos(m, resolver(m))[1]
    d = diseno_elemento.disenar_viga(esf, b=0.20, h=0.20)
    assert d.flexion is None
    assert not d.cumple
    assert "INSUFICIENTE" in d.disponer


def test_cortante_concreto_columna_axial():
    vc0 = aci318.cortante_concreto_columna(400, 360, 28, 0.0, 160000)
    vc_comp = aci318.cortante_concreto_columna(400, 360, 28, 500000.0, 160000)
    vc_trac = aci318.cortante_concreto_columna(400, 360, 28, -500000.0, 160000)
    assert vc_comp > vc0 > vc_trac >= 0


def test_confinamiento_ash_proporcional_a_s():
    a1 = aci318.confinamiento_ash(100, 300, 28, 420, 160000, 90000)
    a2 = aci318.confinamiento_ash(200, 300, 28, 420, 160000, 90000)
    assert a1 > 0 and a2 == pytest.approx(2 * a1)


def test_estribo_columna_confinamiento_cumple():
    e = aci318.disenar_estribo_columna(10000.0, 200000.0, 400, 400, 28,
                                       aci318._diametro_barra(8), 40)
    assert e.cumple
    assert e.espaciamiento >= 50
    assert e.gobierna == "confinamiento"


def test_estribo_columna_vs_requerido_crece_con_vu():
    e_lo = aci318.disenar_estribo_columna(10000.0, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    e_hi = aci318.disenar_estribo_columna(300000.0, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    assert e_hi.vs_requerido > e_lo.vs_requerido


def test_estribo_columna_insuficiente():
    e = aci318.disenar_estribo_columna(2.0e6, 200000.0, 400, 400, 28, aci318._diametro_barra(8), 40)
    assert not e.cumple
    assert "INSUFICIENTE" in e.disponer


def test_capas_biaxial_cuadrada_simetrica():
    capas_y, capas_z = aci318._capas_biaxial(400, 400, 50, 8, 8)
    assert sum(As for _, As in capas_y) == pytest.approx(sum(As for _, As in capas_z))
    assert sorted(di for di, _ in capas_y) == pytest.approx(sorted(di for di, _ in capas_z))


def test_factor_biaxial_uniaxial_y_biaxial():
    b = h = 400.0
    capas_y, capas_z = aci318._capas_biaxial(b, h, 50, 8, 8)
    diag_y = aci318.diagrama_interaccion(b, h, 28, 420, capas_z)
    p = diag_y[20]
    pu = max(p.phi_pn, 1.0)
    cmy = aci318.momento_capacidad(pu, diag_y)
    assert cmy > 0
    # uniaxial (muz=0, muy=cmy) → factor ≈ 1
    assert aci318.factor_biaxial(pu, cmy, 0.0, b, h, 28, 420, capas_y, capas_z) == pytest.approx(1.0, rel=1e-6)
    # biaxial (muy=muz=cmy) en sección cuadrada simétrica → ≈ 2
    assert aci318.factor_biaxial(pu, cmy, cmy, b, h, 28, 420, capas_y, capas_z) == pytest.approx(2.0, rel=1e-3)


def test_factor_biaxial_fuera_de_rango_inf():
    import math as _m
    capas_y, capas_z = aci318._capas_biaxial(400, 400, 50, 8, 8)
    assert aci318.factor_biaxial(1.0e9, 10.0, 10.0, 400, 400, 28, 420, capas_y, capas_z) == _m.inf
