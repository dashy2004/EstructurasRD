"""Tests de georref: plano tangente local escena⇄geo, límites RD. Puros (stdlib)."""
import math
import pytest

from motor_fea.viz.georref import Ancla, escena_a_geo, geo_a_escena, validar_rd

# Origen de ejemplo: Santo Domingo (válido en RD).
SD = Ancla(lat0=18.4861, lon0=-69.9312)


def test_origen_mapea_al_ancla():
    lat, lon = escena_a_geo(0.0, 0.0, SD)
    assert lat == pytest.approx(18.4861)
    assert lon == pytest.approx(-69.9312)


def test_norte_aumenta_latitud():
    # +z (rumbo 0) = Norte → ~100 m ≈ 100/111320 grados de latitud.
    lat, lon = escena_a_geo(0.0, 100.0, SD)
    assert lat == pytest.approx(18.4861 + 100 / 111320.0, rel=1e-6)
    assert lon == pytest.approx(-69.9312, abs=1e-9)


def test_este_aumenta_longitud():
    # +x (rumbo 0) = Este → corrección por cos(lat0) en la longitud.
    cos_lat = math.cos(math.radians(18.4861))
    lat, lon = escena_a_geo(100.0, 0.0, SD)
    assert lon == pytest.approx(-69.9312 + 100 / (111320.0 * cos_lat), rel=1e-6)
    assert lat == pytest.approx(18.4861, abs=1e-9)


def test_round_trip_identidad():
    x, z = 12.5, -47.0
    lat, lon = escena_a_geo(x, z, SD)
    x2, z2 = geo_a_escena(lat, lon, SD)
    assert x2 == pytest.approx(x, abs=1e-6)
    assert z2 == pytest.approx(z, abs=1e-6)


def test_rumbo_90_manda_z_al_este():
    a = Ancla(lat0=18.4861, lon0=-69.9312, rumbo_deg=90.0)
    lat, lon = escena_a_geo(0.0, 100.0, a)
    # con rumbo 90°, +z apunta al Este → cambia lon, no lat.
    assert lat == pytest.approx(18.4861, abs=1e-9)
    assert lon > -69.9312


def test_escala_dobla_la_distancia():
    a = Ancla(lat0=18.4861, lon0=-69.9312, escala=2.0)
    lat, _ = escena_a_geo(0.0, 100.0, a)
    assert lat == pytest.approx(18.4861 + 200 / 111320.0, rel=1e-6)


def test_fuera_de_rd_lanza_valueerror():
    with pytest.raises(ValueError):
        escena_a_geo(0.0, 5_000_000.0, SD)   # 5000 km al norte → fuera de RD


def test_validar_rd_acepta_dentro_y_rechaza_fuera():
    validar_rd(18.5, -69.9)            # no lanza
    with pytest.raises(ValueError):
        validar_rd(40.0, -70.0)        # Nueva York: fuera de RD
