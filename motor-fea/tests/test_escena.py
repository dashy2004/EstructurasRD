"""Tests del exportador de escena (puro, stdlib)."""
import math

import pytest

import modelos_ref
from motor_fea.core.modelo import (
    Apoyo, ElementoFrame, Material, ModeloEstructural, Nodo, Seccion,
)
from motor_fea.viz.escena import B_VIS, H_VIS, exportar_escena


def test_voladizo_una_barra_viga():
    dto = exportar_escena(modelos_ref.voladizo())
    assert dto["unidades"] == "m"
    assert len(dto["nodos"]) == 2
    assert len(dto["barras"]) == 1
    barra = dto["barras"][0]
    # El voladizo va a lo largo de X (horizontal) -> viga.
    assert barra["tipo"] == "viga"
    # Sección 0.30x0.30: A=0.09, Iz=0.30^4/12 -> b=h=0.30.
    assert barra["b"] == pytest.approx(0.30, abs=1e-6)
    assert barra["h"] == pytest.approx(0.30, abs=1e-6)


def test_bbox_del_voladizo():
    dto = exportar_escena(modelos_ref.voladizo())
    assert dto["bbox"]["min"] == [0.0, 0.0, 0.0]
    assert dto["bbox"]["max"] == [3.0, 0.0, 0.0]


def test_barra_vertical_es_columna():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 0, 0, 3.0)]
    m.materiales.append(Material(1, E=2.0e10))
    m.secciones.append(Seccion(1, area=0.09, inercia_y=6.75e-4,
                               inercia_z=6.75e-4, constante_torsion=1.1e-3))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    barra = exportar_escena(m)["barras"][0]
    assert barra["tipo"] == "columna"


def test_seccion_degenerada_usa_grosor_por_defecto():
    m = ModeloEstructural()
    m.nodos += [Nodo(1, 0, 0, 0), Nodo(2, 1.0, 0, 0)]
    m.materiales.append(Material(1, E=2.0e10))
    # Iz=0 -> no físico -> grosor visual por defecto.
    m.secciones.append(Seccion(1, area=0.01, inercia_y=0.0,
                               inercia_z=0.0, constante_torsion=0.0))
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))
    m.apoyos.append(Apoyo.empotrado(1))
    barra = exportar_escena(m)["barras"][0]
    assert barra["b"] == B_VIS
    assert barra["h"] == H_VIS


def test_modelo_invalido_lanza_valueerror():
    m = ModeloEstructural()
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))  # refs inexistentes
    with pytest.raises(ValueError):
        exportar_escena(m)
