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
