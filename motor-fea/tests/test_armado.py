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


def test_recubrimiento_incompatible_lanza_valueerror():
    # sección 0.05×0.05 con recubrimiento por defecto 0.04 → b−2·rec < 0.
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.0025, inercia_y=0.05 ** 4 / 12,
                               inercia_z=0.05 ** 4 / 12, constante_torsion=1e-6))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    with pytest.raises(ValueError):
        armado.calcular_armado(m)


def _viga_de(area: float, iz: float) -> dict:
    """Armado del único elemento de una viga horizontal con la sección dada."""
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 3), Nodo(2, 4, 0, 3)]   # horizontal → viga
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=area, inercia_y=iz, inercia_z=iz,
                               constante_torsion=1e-3))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    return armado.calcular_armado(m)["elementos"][0]


def test_viga_mayor_tiene_mas_o_igual_barras_inferiores():
    chica = _viga_de(0.09, 6.75e-4)                       # 0.30×0.30
    grande = _viga_de(0.28, 0.40 * 0.70 ** 3 / 12)        # 0.40×0.70
    inf_chica = [b for b in chica["long"] if b["y"] < 0]
    inf_grande = [b for b in grande["long"] if b["y"] < 0]
    assert len(inf_grande) >= len(inf_chica)
