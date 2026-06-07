"""Tests de la integración IA local ↔ motor-fea (motor_fea_ia). El motor se ejercita
de verdad; la IA (Ollama) se mockea para verificar el cableado del function-calling."""
import pytest

from motor_fea_ia import herramientas
from motor_fea_ia.agente import AgenteEstructural
from motor_fea_ia.motor_cliente import MotorCliente


def _voladizo():
    """Voladizo: 1 elemento, fz=-1000 en la punta (caso de validación del solver)."""
    return {
        "nodos": [{"id": 1, "x": 0, "y": 0, "z": 0}, {"id": 2, "x": 3, "y": 0, "z": 0}],
        "materiales": [{"id": 1, "E": 2.0e10, "nu": 0.2}],
        "secciones": [{"id": 1, "area": 0.09, "inercia_y": 6.75e-4, "inercia_z": 6.75e-4,
                       "constante_torsion": 1.14e-3}],
        "elementos": [{"id": 1, "nodo_i": 1, "nodo_j": 2, "material_id": 1, "seccion_id": 1}],
        "apoyos": [{"nodo_id": 1, "ux": True, "uy": True, "uz": True, "rx": True, "ry": True, "rz": True}],
        "cargas": [{"nodo_id": 2, "fz": -1000.0}],
    }


def test_cliente_analiza_en_proceso():
    r = MotorCliente().analizar(_voladizo())
    assert set(r) >= {"n_gdl", "desplazamientos", "reacciones"}
    assert r["desplazamientos"]["2"][2] < 0          # el nodo libre baja por la carga -fz


def test_cliente_subproceso_igual_que_en_proceso():
    en_proc = MotorCliente().analizar(_voladizo())["desplazamientos"]["2"][2]
    sub = MotorCliente(usar_cli=True).analizar(_voladizo())["desplazamientos"]["2"][2]
    assert sub == pytest.approx(en_proc, rel=1e-9)


def test_cliente_disena_losa():
    p = {"a": 5, "b": 5, "nx": 6, "ny": 6, "E": 2.0e10, "nu": 0.2, "t": 0.2, "q": 10000,
         "fc": 21, "fy": 420, "recubrimiento": 25, "borde": "simple"}
    d = MotorCliente().disenar_losa(p)
    assert "w_central" in d and "franja_x" in d


def test_herramienta_modelo_invalido_devuelve_error():
    # Elemento que referencia nodos/material/sección inexistentes → el motor lo rechaza.
    invalido = {"nodos": [], "materiales": [], "secciones": [],
                "elementos": [{"id": 1, "nodo_i": 1, "nodo_j": 2, "material_id": 1, "seccion_id": 1}],
                "apoyos": [], "cargas": []}
    assert "error" in herramientas.ejecutar("analizar_estructura", invalido)


def test_herramienta_desconocida():
    assert "error" in herramientas.ejecutar("inexistente", {})


def test_agente_loop_con_ollama_mockeado():
    # 1ª respuesta de la IA: llama la herramienta; 2ª: respuesta final con el resultado.
    paso1 = {"message": {"content": "",
                         "tool_calls": [{"function": {"name": "analizar_estructura",
                                                      "arguments": _voladizo()}}]}}
    paso2 = {"message": {"content": "El nodo 2 baja ~3 mm; la estructura cumple.", "tool_calls": []}}
    secuencia = iter([paso1, paso2])

    class OllamaFake:
        def chat(self, **kw):
            assert kw["tools"] == herramientas.ESQUEMAS          # se le pasan las tools
            assert kw["model"]                                   # y un modelo
            return next(secuencia)

    ag = AgenteEstructural()
    ag._cliente_ollama = lambda: OllamaFake()
    salida = ag.consultar("Resolvé el voladizo de 3 m con 1 kN en la punta")
    assert "cumple" in salida
