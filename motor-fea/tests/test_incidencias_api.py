"""Router de incidencias: clasificar (clasificador fake), round-trip del store y
validación de límites RD. Se salta si falta el extra `api`."""
import pytest

pytest.importorskip("fastapi")
from fastapi import FastAPI
from fastapi.testclient import TestClient

from motor_fea.api.incidencias import crear_router
from motor_fea.viz.incidencias_clasificador import AnalisisIncidencia


class _FakeClasif:
    def clasificar(self, descripcion):
        return AnalisisIncidencia(categoria="estructural", severidad="alta",
                                  resumen="grieta", accion_sugerida="inspección")


def _cliente(tmp_path):
    app = FastAPI()
    app.include_router(crear_router(tmp_path / "store.json", clasificador=_FakeClasif()))
    return TestClient(app)


def test_clasificar_devuelve_analisis(tmp_path):
    r = _cliente(tmp_path).post("/api/incidencias/clasificar", json={"descripcion": "grieta"})
    assert r.status_code == 200
    assert r.json()["categoria"] == "estructural"
    assert r.json()["severidad"] == "alta"


def test_get_store_vacio(tmp_path):
    r = _cliente(tmp_path).get("/api/incidencias")
    assert r.status_code == 200
    assert r.json()["incidencias"] == []


def test_round_trip_guarda_y_carga(tmp_path):
    c = _cliente(tmp_path)
    doc = {
        "version": 1, "georref": None,
        "incidencias": [{
            "id": "a1", "latitude": 18.5, "longitude": -69.9,
            "category": "infraestructura_vial", "subcategory": None,
            "severity": "medium", "description": "bache", "status": "pending",
            "images": [], "vr": {"pos": {"x": 1.0, "y": 0.0, "z": 2.0}, "recursos": []},
        }],
    }
    assert c.post("/api/incidencias", json=doc).json() == {"ok": True, "n": 1}
    got = c.get("/api/incidencias").json()
    assert got["incidencias"][0]["description"] == "bache"


def test_post_lat_fuera_de_rd_da_400(tmp_path):
    doc = {"version": 1, "georref": None, "incidencias": [
        {"id": "x", "latitude": 40.0, "longitude": -70.0, "category": "c",
         "severity": "low", "description": "", "status": "pending"}]}
    r = _cliente(tmp_path).post("/api/incidencias", json=doc)
    assert r.status_code == 400


def test_post_deriva_latlng_de_vr_pos(tmp_path):
    doc = {"version": 1,
           "georref": {"lat0": 18.4861, "lon0": -69.9312, "rumbo_deg": 0.0, "escala": 1.0},
           "incidencias": [{"id": "y", "category": "c", "severity": "low",
                            "description": "", "status": "pending",
                            "vr": {"pos": {"x": 0.0, "y": 0.0, "z": 0.0}}}]}
    c = _cliente(tmp_path)
    assert c.post("/api/incidencias", json=doc).status_code == 200
    inc = c.get("/api/incidencias").json()["incidencias"][0]
    assert inc["latitude"] == pytest.approx(18.4861)
    assert inc["longitude"] == pytest.approx(-69.9312)


def test_post_cuerpo_no_objeto_da_4xx(tmp_path):
    # Un body que no es objeto JSON (p.ej. una lista) no debe dar 500.
    r = _cliente(tmp_path).post("/api/incidencias", json=[1, 2, 3])
    assert r.status_code in (400, 422)
