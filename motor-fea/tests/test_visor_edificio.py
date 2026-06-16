"""Tests V1: puente autoría → visor (Proyecto autorado → DTOs del visor)."""
import pytest

pytest.importorskip("fastapi")
pytest.importorskip("httpx")

from fastapi.testclient import TestClient

from motor_fea.api.contrato import visor_edificio_dict
from motor_fea.api.servidor import crear_app, modelo_ejemplo
from motor_fea.edificio import proyecto_a_dict
from motor_fea.edificio.modelo import (
    CargasLosa, Columna, Edificio, Losa, Metadata, Muro, Nivel, Proyecto, Zapata,
)

_ESQUINAS = ((0, 0), (4, 0), (4, 4), (0, 4))


def _proyecto(con_muro=False, vacio=False):
    if vacio:
        return Proyecto(metadata=Metadata(nombre="Vacío"))
    losa = Losa(id=1, tipo="maciza", espesor=0.20, puntos=_ESQUINAS,
                cargas=CargasLosa(muerta=5.0, viva=2.0))
    verticales = [
        Columna(id=i + 1, posicion=(x, y), base=0.30, peralte=0.30,
                cota_base=0.0, cota_tope=3.0, material="H210",
                zapata=Zapata(1.0, 1.0, 0.4))
        for i, (x, y) in enumerate(_ESQUINAS)
    ]
    if con_muro:
        verticales.append(
            Muro(id=99, linea=((0, 0), (4, 0)), espesor=0.20,
                 cota_base=0.0, cota_tope=3.0, material="H210",
                 zapata=Zapata(2.0, 1.0, 0.4)))
    edi = Edificio(
        id=1, nombre="Bloque A",
        niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0, (losa,))],
        elementos_verticales=verticales,
    )
    return Proyecto(metadata=Metadata(nombre="Demo"), edificios=[edi])


def test_puente_autoria_a_dtos_del_visor():
    out = visor_edificio_dict(proyecto_a_dict(_proyecto()))
    assert set(out) == {"escena", "resultados", "esfuerzos"}
    assert out["escena"]["barras"]                 # geometría sintetizada
    assert len(out["escena"]["barras"]) == 4       # 4 columnas 0→3


def test_muro_aparece_como_barra_en_la_escena():
    sin = visor_edificio_dict(proyecto_a_dict(_proyecto(con_muro=False)))
    con = visor_edificio_dict(proyecto_a_dict(_proyecto(con_muro=True)))
    assert len(con["escena"]["barras"]) == len(sin["escena"]["barras"]) + 1


def test_proyecto_vacio_falla():
    with pytest.raises(ValueError):
        visor_edificio_dict(proyecto_a_dict(_proyecto(vacio=True)))


def test_endpoint_visor_edificio_ok():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.post("/visor-edificio", json=proyecto_a_dict(_proyecto()))
    assert r.status_code == 200
    assert set(r.json()) == {"escena", "resultados", "esfuerzos"}


def test_endpoint_proyecto_invalido_da_400():
    cli = TestClient(crear_app(modelo_ejemplo()))
    r = cli.post("/visor-edificio", json={"basura": 1})
    assert r.status_code == 400
