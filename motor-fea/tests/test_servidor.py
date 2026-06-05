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
