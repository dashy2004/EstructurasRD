"""Tests del servidor del visor. Se saltan si el extra `api` no está instalado."""
import pytest

pytest.importorskip("fastapi")
pytest.importorskip("httpx")  # requerido por fastapi.testclient

from fastapi.testclient import TestClient

from motor_fea.api.servidor import crear_app, modelo_ejemplo
from motor_fea.core.modelo import ElementoFrame, ModeloEstructural


def test_escena_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/escena")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"unidades", "bbox", "nodos", "barras", "losas"}
    assert len(data["barras"]) == 8           # 4 columnas + 4 vigas
    tipos = {b["tipo"] for b in data["barras"]}
    assert tipos == {"columna", "viga"}


def test_escena_modelo_invalido_da_400():
    m = ModeloEstructural()
    m.elementos.append(ElementoFrame(1, 1, 2, 1, 1))  # refs inexistentes
    cli = TestClient(crear_app(m))
    r = cli.get("/escena")
    assert r.status_code == 400


def test_index_se_sirve():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/")
    assert r.status_code == 200
    assert "text/html" in r.headers["content-type"]


def test_cli_serve_invoca_servir(monkeypatch):
    import motor_fea.api.servidor as srv
    llamado = {}

    def fake_servir(ruta=None, host="127.0.0.1", port=8000):
        llamado["args"] = (ruta, host, port)

    monkeypatch.setattr(srv, "servir", fake_servir)
    from motor_fea.api.cli import main
    rc = main(["--serve", "--port", "9001"])
    assert rc == 0
    assert llamado["args"] == (None, "127.0.0.1", 9001)


def test_resultados_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/resultados")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"deformada", "modos"}
    assert "desplazamientos" in data["deformada"]
    assert len(data["modos"]) == 3          # los 4 nodos superiores tienen masa de peso propio
    assert all(m["periodo"] > 0 for m in data["modos"])


def test_losa_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/losa")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"a", "b", "nx", "ny", "factor_sugerido", "campos"}
    assert set(data["campos"]) == {"deflexion", "momento_mx", "momento_my"}


def test_armado_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/armado")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"recubrimiento", "elementos"}
    assert len(data["elementos"]) == 8          # 4 columnas + 4 vigas
    e0 = data["elementos"][0]
    assert "long" in e0 and "estribo" in e0


def test_diseno_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/diseno")
    assert r.status_code == 200
    data = r.json()
    assert set(data) >= {"recubrimiento", "elementos"}
    assert len(data["elementos"]) == 8
    e0 = data["elementos"][0]
    assert "demanda" in e0 and "cumple" in e0 and "long" in e0


def test_diseno_tiene_combo_y_casos():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.get("/diseno")
    assert r.status_code == 200
    data = r.json()
    assert len(data["elementos"]) == 8
    for e in data["elementos"]:
        assert e["combo"]                                 # combo gobernante presente
    # con D+W, no todos los elementos los gobierna 1.4D
    combos = {e["combo"] for e in data["elementos"]}
    assert combos - {"1"}
