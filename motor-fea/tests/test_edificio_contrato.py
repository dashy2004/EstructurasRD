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


def test_bug_end_to_end_por_el_contrato():
    """Los 3 escenarios del bug, parseados desde el JSON del contrato."""
    from motor_fea.edificio.contrato import proyecto_desde_dict

    d = {
        "version": 1,
        "proyecto": {"nombre": "Edificio demo", "fecha": "2026-06-15"},
        "cargas_globales": {"muerta_adicional": 1.5, "viva": 2.0},
        "combinaciones": ["1.2D+1.6L"],
        "edificios": [{
            "id": 1, "nombre": "Bloque A",
            "niveles": [
                {"id": 1, "nombre": "Primer nivel", "cota": 0.0,
                 "losas": [{"id": 1, "tipo": "maciza", "espesor": 0.20,
                            "puntos": [[0, 0], [5, 0], [5, 5], [0, 5]],
                            "cargas": {"muerta": 1.5, "viva": 2.0}}]},
                {"id": 2, "nombre": "Segundo nivel", "cota": 3.0, "losas": []},
                {"id": 3, "nombre": "Tercer nivel", "cota": 6.0, "losas": []},
            ],
            "elementos_verticales": [
                {"id": 1, "tipo": "columna", "posicion": [0, 0],
                 "seccion": {"base": 0.30, "peralte": 0.30},
                 "cota_base": 0.0, "cota_tope": 6.0, "material": "H210",
                 "zapata": {"ancho": 1.2, "largo": 1.2, "peralte": 0.4}},
            ],
        }],
    }
    proy = proyecto_desde_dict(d)
    assert proy.validar() == []                       # modelo válido

    edi = proy.edificios[0]
    nivel1 = edi.niveles[0]
    losa1 = nivel1.losas[0]

    # #1 — la cota del nivel se propaga a la losa
    assert all(z == 0.0 for (_x, _y, z) in nivel1.puntos_losa_3d(losa1))
    # #1 — el nombre del nivel es independiente del de la losa
    assert nivel1.nombre == "Primer nivel"
    # #2 — la columna continua 0→6 queda conectada a los 3 niveles
    col = edi.elementos_verticales[0]
    assert [n.cota for n in edi.niveles_atravesados(col)] == [0.0, 3.0, 6.0]


def test_round_trip_zapata_inversa():
    """Round-trip del combo inverso de zapata: columna sin, muro con."""
    from motor_fea.edificio.contrato import proyecto_a_dict, proyecto_desde_dict
    from motor_fea.edificio.modelo import (
        Columna, Edificio, Muro, Nivel, Proyecto, Zapata,
    )

    edi = Edificio(id=1, nombre="A",
                   niveles=[Nivel(1, "N1", 0.0), Nivel(2, "N2", 3.0)],
                   elementos_verticales=[
                       Columna(id=1, posicion=(0, 0), base=0.3, peralte=0.3,
                               cota_base=0.0, cota_tope=3.0, material="H210"),
                       Muro(id=2, linea=((0, 0), (0, 5)), espesor=0.2,
                            cota_base=0.0, cota_tope=3.0, material="H210",
                            zapata=Zapata(1.0, 2.0, 0.5)),
                   ])
    proy = Proyecto(edificios=[edi])
    reconstruido = proyecto_desde_dict(proyecto_a_dict(proy))
    assert reconstruido == proy
    # columna sin zapata, muro con zapata — preservados
    assert reconstruido.edificios[0].elementos_verticales[0].zapata is None
    assert reconstruido.edificios[0].elementos_verticales[1].zapata == Zapata(1.0, 2.0, 0.5)
