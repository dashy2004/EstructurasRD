"""Validación de los esfuerzos por elemento contra soluciones cerradas (voladizo, columna)."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.core.solver import esfuerzos_elementos, resolver

E = 2.0e10
NU = 0.2
B = 0.30
A = B * B
I = B ** 4 / 12
J = 0.1406 * B ** 4
L = 3.0
P = 1000.0


def _voladizo_x(carga: CargaNodal) -> ModeloEstructural:
    """Voladizo a lo largo de X: nodo 1 empotrado en origen, nodo 2 libre en (L,0,0)."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, L, 0, 0)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(carga)
    return m


def _esf_voladizo(carga: CargaNodal):
    m = _voladizo_x(carga)
    return esfuerzos_elementos(m, resolver(m))[1]


def test_axial_traccion_positiva():
    e = _esf_voladizo(CargaNodal(2, fx=P))
    assert e.axial == pytest.approx(P, rel=1e-6)
    assert e.extremo_i[0] == pytest.approx(-e.extremo_j[0], rel=1e-6)


def test_cortante_constante():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    for t in (0.0, 0.25, 0.5, 0.75, 1.0):
        assert e.internos(t)[2] == pytest.approx(P, rel=1e-6)        # Vz constante


def test_momento_lineal_voladizo():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    assert e.internos(0.0)[4] == pytest.approx(-P * L, rel=1e-6)     # My en el empotramiento
    assert e.internos(0.5)[4] == pytest.approx(-P * L / 2, rel=1e-6)
    assert abs(e.internos(1.0)[4]) < 1e-6                            # ≈ 0 en el extremo libre


def test_columna_valida_transformacion():
    # Columna a lo largo de Z (T ≠ identidad); carga fx=P (global X = local ey) → Mz de base = P·L.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, L)]
    m.materiales.append(Material(1, E=E, nu=NU))
    m.secciones.append(Seccion(1, area=A, inercia_y=I, inercia_z=I, constante_torsion=J))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(2, fx=P))
    e = esfuerzos_elementos(m, resolver(m))[1]
    assert abs(e.internos(0.0)[5]) == pytest.approx(P * L, rel=1e-6)   # Mz de base


def test_diagrama_estaciones():
    e = _esf_voladizo(CargaNodal(2, fz=P))
    d = e.diagrama(11)
    assert len(d) == 11
    assert d[0][0] == 0.0
    assert d[-1][0] == pytest.approx(L)
    with pytest.raises(ValueError):
        e.diagrama(1)


def test_cortante_y_momento_en_plano_xy():
    # Carga fy=P en el voladizo X (local = global) → flexión en el plano x-y: Vy y Mz locales.
    e = _esf_voladizo(CargaNodal(2, fy=P))
    for t in (0.0, 0.5, 1.0):
        assert abs(e.internos(t)[1]) == pytest.approx(P, rel=1e-6)    # |Vy| constante
    assert abs(e.internos(0.0)[5]) == pytest.approx(P * L, rel=1e-6)  # |Mz| en el empotramiento
    assert abs(e.internos(0.5)[5]) == pytest.approx(P * L / 2, rel=1e-6)
    assert abs(e.internos(1.0)[5]) < 1e-6                             # ≈ 0 en el extremo libre


def test_torsion_interna_constante():
    # Par torsor mx=P en la punta → torsión interna constante a lo largo de la barra.
    e = _esf_voladizo(CargaNodal(2, mx=P))
    for t in (0.0, 0.5, 1.0):
        assert abs(e.internos(t)[3]) == pytest.approx(P, rel=1e-6)


def test_internos_en_j_igualan_la_fuerza_de_extremo_j():
    # Por equilibrio del elemento (sin cargas de tramo), internos(1.0) coincide con extremo_j.
    e = _esf_voladizo(CargaNodal(2, fz=P))
    for interno, nodal in zip(e.internos(1.0), e.extremo_j):
        assert interno == pytest.approx(nodal, abs=1e-6)
