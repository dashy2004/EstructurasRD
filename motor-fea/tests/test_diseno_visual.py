"""Tests puros del armado diseñado por fuerzas para el visor."""
import pytest
import re

from motor_fea.core.modelo import (
    Apoyo, CargaNodal, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz import diseno


def _portico():
    """Pórtico 4×4×3 con carga lateral (todas en el caso D por defecto)."""
    m = ModeloEstructural()
    m.nodos += [
        Nodo(1, 0, 0, 0), Nodo(2, 4, 0, 0), Nodo(3, 4, 4, 0), Nodo(4, 0, 4, 0),
        Nodo(5, 0, 0, 3), Nodo(6, 4, 0, 3), Nodo(7, 4, 4, 3), Nodo(8, 0, 4, 3),
    ]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    eid = 1
    for i, j in [(1, 5), (2, 6), (3, 7), (4, 8), (5, 6), (6, 7), (7, 8), (8, 5)]:
        m.elementos.append(ElementoFrame(eid, i, j, 1, 1))
        eid += 1
    for n in (1, 2, 3, 4):
        m.apoyos.append(Apoyo.empotrado(n))
    for n in (5, 6, 7, 8):
        m.cargas.append(CargaNodal(n, fx=10000.0))
    return m


def test_un_diseno_por_elemento():
    dto = diseno.calcular_diseno(_portico())
    assert set(dto) == {"recubrimiento", "elementos"}
    assert len(dto["elementos"]) == 8


def test_columnas_y_vigas_con_armado_y_demanda():
    dto = diseno.calcular_diseno(_portico())
    cols = [e for e in dto["elementos"] if e["tipo"] == "columna"]
    vigas = [e for e in dto["elementos"] if e["tipo"] == "viga"]
    assert len(cols) == 4 and len(vigas) == 4
    for e in dto["elementos"]:
        assert len(e["long"]) >= 2
        assert set(e["demanda"]) == {"pu", "mu", "vu"}
        assert all(v >= 0 for v in e["demanda"].values())
        assert isinstance(e["cumple"], bool)
        assert e["designacion"]
        assert e["combo"]                                 # combo gobernante (5A.2)
    for c in cols:
        assert len(c["long"]) >= 4


def test_posiciones_dentro_de_la_seccion():
    dto = diseno.calcular_diseno(_portico())
    for e in dto["elementos"]:                  # sección 0.30×0.30 → |x|,|y| ≤ 0.15
        for bar in e["long"]:
            assert abs(bar["x"]) <= 0.15 + 1e-9
            assert abs(bar["y"]) <= 0.15 + 1e-9


def test_estribo_y_recubrimiento():
    dto = diseno.calcular_diseno(_portico())
    assert dto["recubrimiento"] == 0.04
    for e in dto["elementos"]:
        est = e["estribo"]
        assert est["d"] > 0 and est["s"] > 0 and est["w"] > 0 and est["h"] > 0


def test_seccion_insuficiente_marca_no_cumple():
    # columna 0.20×0.20 bajo axial enorme → demanda > capacidad incluso con ρ=8%.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.04, inercia_y=0.2 ** 4 / 12,
                               inercia_z=0.2 ** 4 / 12, constante_torsion=1e-4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas += [CargaNodal(2, fz=-3.0e6), CargaNodal(2, fx=2.0e5)]
    dto = diseno.calcular_diseno(m)
    assert dto["elementos"][0]["cumple"] is False


def test_fc_invalido_lanza_valueerror():
    with pytest.raises(ValueError):
        diseno.calcular_diseno(_portico(), fc=0.0)


def test_designacion_refleja_diseno_y_viga_tiene_momento():
    dto = diseno.calcular_diseno(_portico())
    vigas = [e for e in dto["elementos"] if e["tipo"] == "viga"]
    # el pórtico bajo carga lateral flexiona las vigas → al menos una con Mu > 0.
    assert any(v["demanda"]["mu"] > 0 for v in vigas)
    # la designación de un elemento que cumple refleja barras diseñadas (n#num).
    for e in dto["elementos"]:
        if e["cumple"]:
            assert re.search(r"\d+#\d+", e["designacion"])


def test_diseno_combo_con_W_gobierna():
    # columna con SOLO carga lateral W → un combo con W gobierna (≠ "1" = 1.4D, que con D=0 da 0).
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    bc = 0.40
    m.secciones.append(Seccion(1, area=bc * bc, inercia_y=bc ** 4 / 12,
                               inercia_z=bc ** 4 / 12, constante_torsion=0.1406 * bc ** 4))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    m.cargas.append(CargaNodal(2, fx=20000.0, caso="W"))
    dto = diseno.calcular_diseno(m, fc=28.0, fy=420.0, recubrimiento=0.05)
    el = dto["elementos"][0]
    assert el["combo"] and el["combo"] != "1"             # gobierna un combo con W, no 1.4D
    assert set(el["demanda"]) == {"pu", "mu", "vu"}
    assert all(v >= 0 for v in el["demanda"].values())
