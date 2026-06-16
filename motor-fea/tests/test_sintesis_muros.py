"""Tests B1: síntesis de muros → columna ancha equivalente.

Muro (línea en planta, extruido) → barra vertical en el centroide de la línea,
sección rectangular espesor×longitud con el eje fuerte (inercia_z) en el plano
del muro vía `vector_referencia = (-dy, dx, 0)`.
"""
import pytest

from motor_fea.edificio.modelo import (
    Columna, Edificio, Muro, Nivel, Zapata,
)
from motor_fea.edificio.sintesis import sintetizar


def _edificio_muro(linea, espesor=0.20, cota_base=0.0, cota_tope=3.0,
                   cotas_nivel=(0.0, 3.0), zapata=None, extra=()):
    muro = Muro(id=1, linea=linea, espesor=espesor, cota_base=cota_base,
                cota_tope=cota_tope, material="H210", zapata=zapata)
    niveles = [Nivel(i + 1, f"N{i + 1}", c) for i, c in enumerate(cotas_nivel)]
    return Edificio(id=1, nombre="M", niveles=niveles,
                    elementos_verticales=[muro, *extra])


def test_muro_a_barra_en_el_centroide():
    m = sintetizar(_edificio_muro(((0, 0), (4, 0))))
    coords = sorted((n.x, n.y, n.z) for n in m.nodos)
    assert coords == [(2.0, 0.0, 0.0), (2.0, 0.0, 3.0)]   # centroide de la línea
    assert len(m.elementos) == 1


def test_seccion_eje_fuerte_en_el_plano():
    m = sintetizar(_edificio_muro(((0, 0), (4, 0)), espesor=0.20))
    s = m.secciones[0]
    assert s.area == pytest.approx(0.8)                    # t·L = 0.2·4
    assert s.inercia_z == pytest.approx(0.2 * 4**3 / 12)   # FUERTE, en el plano ≈1.0667
    assert s.inercia_y == pytest.approx(4 * 0.2**3 / 12)   # débil ≈0.002667
    assert s.inercia_z > s.inercia_y


def test_orientacion_perpendicular_a_la_linea():
    m = sintetizar(_edificio_muro(((0, 0), (3, 4))))       # dx=3, dy=4
    assert m.elementos[0].vector_referencia == (-4.0, 3.0, 0.0)


def test_zapata_da_apoyo_en_la_base_del_muro():
    m = sintetizar(_edificio_muro(((0, 0), (4, 0)), zapata=Zapata(1.2, 1.2, 0.4)))
    assert len(m.apoyos) == 1
    nodo_base = next(n for n in m.nodos if n.id == m.apoyos[0].nodo_id)
    assert nodo_base.z == 0.0
    assert m.apoyos[0].restricciones() == (True,) * 6      # empotrado


def test_muros_y_columnas_conviven_valido_y_determinista():
    col = Columna(id=2, posicion=(10, 0), base=0.30, peralte=0.30,
                  cota_base=0.0, cota_tope=3.0, material="H210",
                  zapata=Zapata(1, 1, 0.4))
    edi = _edificio_muro(((0, 0), (4, 0)), zapata=Zapata(1.2, 1.2, 0.4), extra=(col,))
    a, b = sintetizar(edi), sintetizar(edi)
    assert a.validar() == []
    assert [(e.id, e.nodo_i, e.nodo_j) for e in a.elementos] == \
           [(e.id, e.nodo_i, e.nodo_j) for e in b.elementos]


def test_muro_atraviesa_nivel_intermedio():
    m = sintetizar(_edificio_muro(((0, 0), (4, 0)), cota_tope=6.0,
                                  cotas_nivel=(0.0, 3.0, 6.0)))
    assert len(m.nodos) == 3                               # 0, 3, 6 en el centroide
    assert len(m.elementos) == 2                           # comparten el nodo z=3
