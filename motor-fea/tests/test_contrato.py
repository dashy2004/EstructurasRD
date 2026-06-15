"""Tests del contrato JSON y del CLI --analyze (frontera de integración)."""
import io
import json
import os
import tempfile
from contextlib import redirect_stdout

import pytest

from motor_fea.api import cli, contrato
from motor_fea.core.modelo import (
    Apoyo,
    CargaNodal,
    ElementoFrame,
    Material,
    ModeloEstructural,
    Nodo,
    Seccion,
)
from motor_fea.core.solver import resolver
from motor_fea.viz.escena import exportar_escena
from motor_fea.viz.resultados import calcular_resultados

E = 2.0e10
I = 0.30**4 / 12
L = 3.0
P = 1000.0


def _voladizo_dict() -> dict:
    return {
        "nodos": [{"id": 1, "x": 0.0, "y": 0.0, "z": 0.0},
                  {"id": 2, "x": L, "y": 0.0, "z": 0.0}],
        "materiales": [{"id": 1, "E": E, "nu": 0.2, "densidad": 2400.0}],
        "secciones": [{"id": 1, "area": 0.09, "inercia_y": I, "inercia_z": I,
                       "constante_torsion": 1.139e-3}],
        "elementos": [{"id": 1, "nodo_i": 1, "nodo_j": 2, "material_id": 1,
                       "seccion_id": 1, "vector_referencia": [0.0, 0.0, 1.0]}],
        "apoyos": [{"nodo_id": 1, "ux": True, "uy": True, "uz": True,
                    "rx": True, "ry": True, "rz": True}],
        "cargas": [{"nodo_id": 2, "fx": 0.0, "fy": 0.0, "fz": -P,
                    "mx": 0.0, "my": 0.0, "mz": 0.0}],
    }


def test_roundtrip_modelo_dict():
    d = _voladizo_dict()
    m = contrato.modelo_desde_dict(d)
    d2 = contrato.modelo_a_dict(m)
    assert d2 == d                       # round-trip exacto


def test_modelo_desde_dict_aplica_defaults():
    m = contrato.modelo_desde_dict({
        "nodos": [{"id": 1, "x": 0, "y": 0}],          # z por defecto 0
        "materiales": [{"id": 1, "E": E}],             # nu/densidad por defecto
    })
    assert m.nodos[0].z == 0.0
    assert m.materiales[0].nu == 0.2


def test_analizar_dict_voladizo():
    res = contrato.analizar_dict(_voladizo_dict())
    uz = res["desplazamientos"]["2"][2]
    esperado = -P * L**3 / (3 * E * I)              # hacia abajo
    assert abs(uz - esperado) / abs(esperado) < 1e-9
    assert res["n_gdl"] == 12


def test_analizar_json_devuelve_json_valido():
    salida = contrato.analizar_json(json.dumps(_voladizo_dict()))
    parsed = json.loads(salida)
    assert "desplazamientos" in parsed and "reacciones" in parsed


def test_cli_version():
    buf = io.StringIO()
    with redirect_stdout(buf):
        rc = cli.main(["--version"])
    assert rc == 0
    assert buf.getvalue().strip() != ""


def test_cli_analyze_archivo():
    fd, path = tempfile.mkstemp(suffix=".json")
    try:
        with os.fdopen(fd, "w") as f:
            json.dump(_voladizo_dict(), f)
        buf = io.StringIO()
        with redirect_stdout(buf):
            rc = cli.main(["--analyze", path])
        assert rc == 0
        out = json.loads(buf.getvalue())
        uz = out["desplazamientos"]["2"][2]
        assert abs(uz - (-P * L**3 / (3 * E * I))) / (P * L**3 / (3 * E * I)) < 1e-9
    finally:
        os.unlink(path)


_PARAMS_LOSA = {
    "a": 5.0, "b": 5.0, "nx": 6, "ny": 6, "E": 2.0e10, "nu": 0.2, "t": 0.2,
    "q": 10000.0, "fc": 21.0, "fy": 420.0, "recubrimiento": 25.0, "borde": "simple",
}


def test_disenar_losa_dict_devuelve_momentos_y_acero():
    out = contrato.disenar_losa_dict(_PARAMS_LOSA)
    assert out["mx_max"] > 0 and out["my_max"] > 0
    assert out["w_central"] > 0
    assert out["franja_x"]["as_diseno"] > 0
    assert out["franja_x"]["cumple"] is True
    assert "#" in out["franja_x"]["disponer"]


def test_cli_disenar_losa_archivo():
    fd, path = tempfile.mkstemp(suffix=".json")
    try:
        with os.fdopen(fd, "w") as f:
            json.dump(_PARAMS_LOSA, f)
        buf = io.StringIO()
        with redirect_stdout(buf):
            rc = cli.main(["--disenar-losa", path])
        assert rc == 0
        out = json.loads(buf.getvalue())
        assert out["franja_x"]["as_diseno"] > 0
        assert out["mx_max"] > 0
    finally:
        os.unlink(path)


# ---------------------------------------------------------------------------
# Task 1: esfuerzos_a_dict
# ---------------------------------------------------------------------------
def test_esfuerzos_a_dict_forma_y_convenciones():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    resultado = resolver(modelo)
    d = contrato.esfuerzos_a_dict(modelo, resultado, n=11)

    assert set(d) == {"orden_componentes", "elementos"}
    assert d["orden_componentes"] == ["N", "Vy", "Vz", "T", "My", "Mz"]
    assert len(d["elementos"]) == len(modelo.elementos)

    e0 = d["elementos"][0]
    assert set(e0) == {"id", "longitud", "extremo_i", "extremo_j", "diagrama"}
    assert e0["id"] == modelo.elementos[0].id
    assert len(e0["extremo_i"]) == 6 and len(e0["extremo_j"]) == 6

    assert len(e0["diagrama"]) == 11
    assert e0["diagrama"][0][0] == 0.0
    assert abs(e0["diagrama"][-1][0] - e0["longitud"]) < 1e-9
    assert len(e0["diagrama"][0]) == 7

    estacion0 = e0["diagrama"][0][1:]
    for comp, ext in zip(estacion0, e0["extremo_i"]):
        assert abs(comp - (-ext)) < 1e-9


def test_esfuerzos_a_dict_n_configurable():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    resultado = resolver(modelo)
    d = contrato.esfuerzos_a_dict(modelo, resultado, n=5)
    assert all(len(e["diagrama"]) == 5 for e in d["elementos"])


# ---------------------------------------------------------------------------
# Task 2: analizar_completo_dict
# ---------------------------------------------------------------------------
def test_analizar_completo_dict_estructura():
    md = _voladizo_dict()
    d = contrato.analizar_completo_dict(md, n=11)

    assert set(d) == {"resultados", "esfuerzos"}
    assert set(d["resultados"]) == {"n_gdl", "desplazamientos", "reacciones"}
    assert set(d["esfuerzos"]) == {"orden_componentes", "elementos"}
    modelo = contrato.modelo_desde_dict(md)
    directo = contrato.esfuerzos_a_dict(modelo, resolver(modelo), n=11)
    assert d["esfuerzos"] == directo


def test_esfuerzos_a_dict_rechaza_n_menor_2():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    resultado = resolver(modelo)
    with pytest.raises(ValueError):
        contrato.esfuerzos_a_dict(modelo, resultado, n=1)


def test_esfuerzos_modelo_dict_equivale_a_resolver_mas_serializar():
    modelo = contrato.modelo_desde_dict(_voladizo_dict())
    assert contrato.esfuerzos_modelo_dict(modelo, n=11) == \
        contrato.esfuerzos_a_dict(modelo, resolver(modelo), n=11)


# ---------------------------------------------------------------------------
# #2: visor_dict — DTOs que el visor necesita para un modelo propio
# ---------------------------------------------------------------------------
def test_visor_dict_estructura_y_coherencia():
    md = _voladizo_dict()
    d = contrato.visor_dict(md, n=11)

    assert set(d) == {"escena", "resultados", "esfuerzos"}
    assert set(d["escena"]) == {"unidades", "bbox", "nodos", "barras", "losas"}
    assert set(d["resultados"]) == {"deformada", "modos"}
    assert set(d["esfuerzos"]) == {"orden_componentes", "elementos"}

    modelo = contrato.modelo_desde_dict(md)
    # coherencia: esfuerzos del visor == esfuerzos_modelo_dict directo (mismo solve lógico)
    assert d["esfuerzos"] == contrato.esfuerzos_modelo_dict(modelo, 11)
    assert d["escena"] == exportar_escena(modelo)
    assert d["resultados"] == calcular_resultados(modelo)


def test_visor_dict_rechaza_n_menor_2():
    with pytest.raises(ValueError):
        contrato.visor_dict(_voladizo_dict(), n=1)


def test_modelo_desde_dict_parsea_losas():
    from motor_fea.api.contrato import modelo_desde_dict
    d = {
        "nodos": [{"id": 1, "x": 0, "y": 0, "z": 0}],
        "losas": [{"id": 7, "puntos": [[0, 0, 0], [3, 0, 0], [3, 3, 0], [0, 3, 0]]}],
    }
    m = modelo_desde_dict(d)
    assert len(m.losas) == 1
    assert m.losas[0].id == 7
    assert m.losas[0].puntos[2] == [3.0, 3.0, 0.0]
