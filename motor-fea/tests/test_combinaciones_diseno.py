"""Tests de combinaciones de carga en el diseño (Fase 5A.1)."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.casos import esfuerzos_por_caso

_E, _NU, _L = 2.0e10, 0.2, 3.0


def _voladizo(cargas, lado=0.30):
    """Voladizo en X (empotrado en 1), con las CargaNodal dadas en la punta (nodo 2)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, _L, 0, 0)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=lado * lado, inercia_y=lado ** 4 / 12,
                               inercia_z=lado ** 4 / 12, constante_torsion=0.1406 * lado ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += cargas
    return m


def _mmax(esf):
    return max(abs(my) for _s, _n, _vy, _vz, _t, my, _mz in esf.diagrama())


def test_esfuerzos_por_caso_separa_y_analiza():
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D"), CargaNodal(2, fz=500.0, caso="L")])
    epc = esfuerzos_por_caso(m)
    assert set(epc) == {"D", "L"}
    assert _mmax(epc["D"][1]) == pytest.approx(1000.0 * _L, rel=1e-3)   # M_D ≈ fzD·L
    assert _mmax(epc["L"][1]) == pytest.approx(500.0 * _L, rel=1e-3)    # M_L ≈ fzL·L


def test_esfuerzos_por_caso_sin_cargas_vacio():
    m = _voladizo([])
    assert esfuerzos_por_caso(m) == {}


from motor_fea import diseno_elemento


def _columna(cargas, bc=0.40):
    """Columna en Z (empotrada en 1), con las CargaNodal dadas en la punta (nodo 2)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, _L)]
    m.materiales.append(Material(1, E=_E, nu=_NU))
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += cargas
    return m


def _por_caso(epc, elem_id):
    return {caso: epc[caso][elem_id] for caso in epc}


def test_viga_combo_gobernante_2():
    # M_D = 1000·3 = 3000, M_L = 500·3 = 1500 N·m → combo 2 (1.2D+1.6L) = 6000 gobierna.
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D"), CargaNodal(2, fz=500.0, caso="L")])
    d = diseno_elemento.disenar_viga_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.30, h=0.30)
    assert d.combo_flexion == "2"
    assert d.mu == pytest.approx(6000.0, rel=1e-2)
    assert d.flexion is not None and d.cumple
    assert d.combo_cortante == "2"
    assert d.vu == pytest.approx(1.2 * 1000.0 + 1.6 * 500.0, rel=1e-2)   # 2000 N


def test_viga_retrocompat_un_caso_D_es_combo_1():
    # solo caso D → combo 1 (1.4D) gobierna.
    m = _voladizo([CargaNodal(2, fz=1000.0, caso="D")])
    d = diseno_elemento.disenar_viga_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.30, h=0.30)
    assert d.combo_flexion == "1"
    assert d.mu == pytest.approx(1.4 * 3000.0, rel=1e-2)


def test_columna_combos_no_menos_barras_que_caso_D():
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="L")])
    epc = esfuerzos_por_caso(m)
    d_combos = diseno_elemento.disenar_columna_combos(_por_caso(epc, 1), b=0.40, h=0.40,
                                                      fc=28.0, fy=420.0, recubrimiento=0.05)
    d_D = diseno_elemento.disenar_columna(epc["D"][1], b=0.40, h=0.40,
                                          fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d_combos.n_barras >= d_D.n_barras
    assert d_combos.combo_gobernante                      # no vacío


def test_columna_caso_reversible_no_rompe():
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D"), CargaNodal(2, fx=20000.0, caso="E")])
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.40, h=0.40,
                                               fc=28.0, fy=420.0, recubrimiento=0.05)
    assert isinstance(d, diseno_elemento.DisenoColumnaCombos)
    assert d.combo_gobernante


def test_columna_combos_insuficiente():
    # 0.20×0.20 con axial enorme en D → ningún ρ≤8% cubre el combo → no cumple.
    m = _columna([CargaNodal(2, fz=-3.0e6, caso="D"), CargaNodal(2, fx=2.0e5, caso="L")], bc=0.20)
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.20, h=0.20,
                                               fc=28.0, fy=420.0, recubrimiento=0.04)
    assert d.cumple is False
    assert d.combo_gobernante


def test_columna_solo_D_gobierna_combo_1():
    # solo axial D → el combo 1 (1.4D) maximiza la demanda axial → gobierna.
    m = _columna([CargaNodal(2, fz=-200000.0, caso="D")])
    d = diseno_elemento.disenar_columna_combos(_por_caso(esfuerzos_por_caso(m), 1), b=0.40, h=0.40,
                                               fc=28.0, fy=420.0, recubrimiento=0.05)
    assert d.combo_gobernante == "1"
    assert d.cumple
