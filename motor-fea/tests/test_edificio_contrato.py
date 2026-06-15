"""Tests del contrato JSON del modelo canónico (Rebanada A)."""
import pytest

from motor_fea.edificio.contrato import (
    VERSION_CONTRATO, proyecto_a_dict, proyecto_desde_dict,
)
from motor_fea.edificio.modelo import (
    CargasGlobales, CargasLosa, Columna, Edificio, Losa, Metadata, Muro,
    Nivel, Proyecto, Zapata,
)


def _proyecto_demo() -> Proyecto:
    losa = Losa(id=1, tipo="maciza", espesor=0.20,
                puntos=((0, 0), (5, 0), (5, 5), (0, 5)),
                cargas=CargasLosa(muerta=1.5, viva=2.0))
    edi = Edificio(
        id=1, nombre="Bloque A",
        niveles=[Nivel(1, "Primer nivel", 0.0, (losa,)),
                 Nivel(2, "Segundo nivel", 3.0)],
        elementos_verticales=[
            Columna(id=1, posicion=(0, 0), base=0.30, peralte=0.30,
                    cota_base=0.0, cota_tope=3.0, material="H210",
                    zapata=Zapata(1.2, 1.2, 0.4)),
            Muro(id=2, linea=((0, 0), (0, 5)), espesor=0.20,
                 cota_base=0.0, cota_tope=3.0, material="H210"),
        ])
    return Proyecto(metadata=Metadata(nombre="Edificio demo", fecha="2026-06-15"),
                    cargas_globales=CargasGlobales(1.5, 2.0),
                    combinaciones=["1.2D+1.6L"], edificios=[edi])


def test_round_trip_parse_serialize_parse():
    proy = _proyecto_demo()
    d = proyecto_a_dict(proy)
    assert d["version"] == VERSION_CONTRATO
    reconstruido = proyecto_desde_dict(d)
    assert reconstruido == proy                      # round-trip exacto
    assert proyecto_a_dict(reconstruido) == d        # estable


def test_version_no_soportada_falla():
    d = proyecto_a_dict(_proyecto_demo())
    d["version"] = 99
    with pytest.raises(ValueError, match="ersión"):
        proyecto_desde_dict(d)
