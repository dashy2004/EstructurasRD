"""Tests del contrato JSON y del CLI --analyze (frontera de integración)."""
import io
import json
import os
import tempfile
from contextlib import redirect_stdout

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
