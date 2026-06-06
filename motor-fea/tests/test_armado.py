"""Tests puros del armado de ejemplo para el visor."""
import pytest

from motor_fea.core.modelo import (
    Apoyo, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz import armado


def _portico_min():
    """1 columna (vertical) + 1 viga (horizontal), sección 0.30×0.30."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3), Nodo(3, 4, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.14e-3))
    m.elementos += [ElementoFrame(1, 1, 2, 1, 1),   # columna (Δz domina)
                    ElementoFrame(2, 2, 3, 1, 1)]   # viga (horizontal)
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def _columna_grande():
    """1 columna 0.50×0.50."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.25, inercia_y=0.5 ** 4 / 12,
                               inercia_z=0.5 ** 4 / 12, constante_torsion=1e-3))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    return m


def test_un_armado_por_elemento():
    dto = armado.calcular_armado(_portico_min())
    assert set(dto) == {"recubrimiento", "elementos"}
    assert len(dto["elementos"]) == 2


def test_columna_y_viga_clasificadas_con_barras():
    dto = armado.calcular_armado(_portico_min())
    col = next(e for e in dto["elementos"] if e["tipo"] == "columna")
    viga = next(e for e in dto["elementos"] if e["tipo"] == "viga")
    assert len(col["long"]) >= 4
    ys = [bar["y"] for bar in viga["long"]]
    assert any(y > 0 for y in ys) and any(y < 0 for y in ys)   # barras sup + inf


def test_posiciones_dentro_de_la_seccion():
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:                  # sección 0.30×0.30 → |x|,|y| ≤ 0.15
        for bar in e["long"]:
            assert abs(bar["x"]) <= 0.15 + 1e-9
            assert abs(bar["y"]) <= 0.15 + 1e-9


def test_estribo_positivo():
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:
        est = e["estribo"]
        assert est["d"] > 0 and est["s"] >= 0.05 and est["w"] > 0 and est["h"] > 0


def test_diametros_de_la_tabla():
    from motor_fea.normativa.aci318 import AREAS_BARRA_MM2
    validos = {round(num * 25.4 / 8 / 1000, 6) for num in AREAS_BARRA_MM2}
    dto = armado.calcular_armado(_portico_min())
    for e in dto["elementos"]:
        for bar in e["long"]:
            assert round(bar["d"], 6) in validos


def test_seccion_mayor_tiene_mas_o_igual_barras():
    chica = armado.calcular_armado(_portico_min())
    col_chica = next(e for e in chica["elementos"] if e["tipo"] == "columna")
    col_grande = armado.calcular_armado(_columna_grande())["elementos"][0]
    assert len(col_grande["long"]) >= len(col_chica["long"])


def test_fc_invalido_lanza_valueerror():
    with pytest.raises(ValueError):
        armado.calcular_armado(_portico_min(), fc=0.0)


def test_viga_barras_inferiores_llegan_a_los_extremos():
    dto = armado.calcular_armado(_portico_min())
    viga = next(e for e in dto["elementos"] if e["tipo"] == "viga")
    inf = sorted(bar["x"] for bar in viga["long"] if bar["y"] < 0)
    assert len(inf) >= 2
    # las barras inferiores extremas son simétricas respecto al centro de la sección
    assert inf[0] == pytest.approx(-inf[-1])
    assert inf[0] < 0 < inf[-1]
