"""Tests de bajada de cargas losa→bordes (Rebanada C).

Port del tipo de dominio .NET ``RepartoCargaLosa.cs`` (áreas tributarias 45°),
extendido a carga muerta/viva separadas y a un fallback poligonal por perímetro.
Los casos 1–4 reusan los valores numéricos del test .NET como ancla de regresión.
"""
import math

import pytest

from motor_fea.edificio.cargas import FormaCarga, repartir_losa
from motor_fea.edificio.modelo import CargasLosa, Losa


def _losa_rect(lx, ly, muerta=0.0, viva=0.0, lid=1):
    puntos = ((0.0, 0.0), (lx, 0.0), (lx, ly), (0.0, ly))
    return Losa(id=lid, tipo="maciza", espesor=0.20, puntos=puntos,
                cargas=CargasLosa(muerta=muerta, viva=viva))


def test_port_net_cuadrado_reparte_por_igual():
    # Paño 5×5, q=10 (.NET): F=62.5, w=12.5, pico=25, total=250 en los 4 bordes.
    r = repartir_losa(_losa_rect(5, 5, muerta=10)).muerta
    assert r.carga_total == pytest.approx(250.0)
    assert len(r.bordes) == 4
    for b in r.bordes:
        assert b.fuerza_total == pytest.approx(62.5)
        assert b.linea_uniforme_equivalente == pytest.approx(12.5)
        assert b.intensidad_pico == pytest.approx(25.0)


def test_port_net_rectangular_triangulo_y_trapecio():
    # Paño 4×8, q=10 (.NET): corto F=40/w=10; largo F=120/w=15/pico=20; total=320.
    r = repartir_losa(_losa_rect(4, 8, muerta=10)).muerta
    assert r.carga_total == pytest.approx(320.0)
    cortos = [b for b in r.bordes if b.forma is FormaCarga.TRIANGULAR]
    largos = [b for b in r.bordes if b.forma is FormaCarga.TRAPEZOIDAL]
    assert len(cortos) == 2 and len(largos) == 2
    for b in cortos:
        assert b.longitud == pytest.approx(4.0)
        assert b.fuerza_total == pytest.approx(40.0)
        assert b.linea_uniforme_equivalente == pytest.approx(10.0)
    for b in largos:
        assert b.longitud == pytest.approx(8.0)
        assert b.fuerza_total == pytest.approx(120.0)
        assert b.linea_uniforme_equivalente == pytest.approx(15.0)
        assert b.intensidad_pico == pytest.approx(20.0)


def test_conserva_la_carga_total_del_pano():
    r = repartir_losa(_losa_rect(4, 8, muerta=10)).muerta
    suma = sum(b.fuerza_total for b in r.bordes)
    assert suma == pytest.approx(r.carga_total) == pytest.approx(320.0)


def test_es_simetrico_en_el_orden_de_lados():
    a = repartir_losa(_losa_rect(4, 8, muerta=10)).muerta
    b = repartir_losa(_losa_rect(8, 4, muerta=10)).muerta
    fa = sorted(x.fuerza_total for x in a.bordes)
    fb = sorted(x.fuerza_total for x in b.bordes)
    assert fa == pytest.approx(fb)


def test_muerta_y_viva_separadas():
    rep = repartir_losa(_losa_rect(4, 8, muerta=4, viva=2))
    assert rep.muerta.carga_total == pytest.approx(4 * 4 * 8)   # 128
    assert rep.viva.carga_total == pytest.approx(2 * 4 * 8)     # 64
    # Independientes: la viva es exactamente la mitad de la muerta (2 vs 4).
    assert rep.viva.carga_total == pytest.approx(rep.muerta.carga_total / 2)


def test_caso_de_carga_nula_da_bordes_vacios():
    rep = repartir_losa(_losa_rect(4, 8, muerta=10, viva=0))
    assert len(rep.muerta.bordes) == 4
    assert rep.viva.bordes == ()
    assert rep.viva.carga_total == pytest.approx(0.0)


def test_fallback_poligonal_por_perimetro():
    # Triángulo rectángulo 6×6 (no rectangular) → área 18, Q = q·A = 180.
    tri = Losa(id=7, tipo="maciza", espesor=0.2,
               puntos=((0.0, 0.0), (6.0, 0.0), (0.0, 6.0)),
               cargas=CargasLosa(muerta=10))
    rep = repartir_losa(tri)
    assert rep.rectangular is False
    r = rep.muerta
    assert all(b.forma is FormaCarga.UNIFORME for b in r.bordes)
    assert len(r.bordes) == 3
    assert r.carga_total == pytest.approx(180.0)
    assert sum(b.fuerza_total for b in r.bordes) == pytest.approx(180.0)
    # Línea uniforme común = Q / perímetro.
    perim = 6 + 6 + math.hypot(6, 6)
    for b in r.bordes:
        assert b.linea_uniforme_equivalente == pytest.approx(180.0 / perim)


def test_determinismo():
    losa = _losa_rect(4, 8, muerta=4, viva=2)
    assert repartir_losa(losa) == repartir_losa(losa)
