"""Herramientas (function-calling) que la IA local invoca para usar el motor-fea.

Cada herramienta = un esquema JSON (estilo Ollama/OpenAI ``tools``) + el dispatcher
``ejecutar`` que llama al :class:`MotorCliente`. La IA construye el modelo/parámetros;
los errores del motor se devuelven como ``{"error": ...}`` para que la IA corrija.
"""
from __future__ import annotations

from motor_fea_ia.motor_cliente import MotorCliente

_ESQUEMA_ANALIZAR = {
    "type": "object",
    "properties": {
        "nodos": {"type": "array", "items": {"type": "object", "properties": {
            "id": {"type": "integer"}, "x": {"type": "number"}, "y": {"type": "number"},
            "z": {"type": "number"}}, "required": ["id", "x", "y", "z"]}},
        "materiales": {"type": "array", "items": {"type": "object", "properties": {
            "id": {"type": "integer"}, "E": {"type": "number", "description": "módulo elástico (Pa)"},
            "nu": {"type": "number"}}, "required": ["id", "E"]}},
        "secciones": {"type": "array", "items": {"type": "object", "properties": {
            "id": {"type": "integer"}, "area": {"type": "number"}, "inercia_y": {"type": "number"},
            "inercia_z": {"type": "number"}, "constante_torsion": {"type": "number"}},
            "required": ["id", "area", "inercia_y", "inercia_z", "constante_torsion"]}},
        "elementos": {"type": "array", "items": {"type": "object", "properties": {
            "id": {"type": "integer"}, "nodo_i": {"type": "integer"}, "nodo_j": {"type": "integer"},
            "material_id": {"type": "integer"}, "seccion_id": {"type": "integer"}},
            "required": ["id", "nodo_i", "nodo_j", "material_id", "seccion_id"]}},
        "apoyos": {"type": "array", "items": {"type": "object", "properties": {
            "nodo_id": {"type": "integer"}, "ux": {"type": "boolean"}, "uy": {"type": "boolean"},
            "uz": {"type": "boolean"}, "rx": {"type": "boolean"}, "ry": {"type": "boolean"},
            "rz": {"type": "boolean"}}, "required": ["nodo_id"]}},
        "cargas": {"type": "array", "items": {"type": "object", "properties": {
            "nodo_id": {"type": "integer"}, "fx": {"type": "number"}, "fy": {"type": "number"},
            "fz": {"type": "number"}, "mx": {"type": "number"}, "my": {"type": "number"},
            "mz": {"type": "number"}}, "required": ["nodo_id"]}},
    },
    "required": ["nodos", "materiales", "secciones", "elementos", "apoyos", "cargas"],
}

_ESQUEMA_LOSA = {
    "type": "object",
    "properties": {
        "a": {"type": "number", "description": "luz en x (m)"},
        "b": {"type": "number", "description": "luz en y (m)"},
        "nx": {"type": "integer"}, "ny": {"type": "integer"},
        "E": {"type": "number", "description": "módulo elástico (Pa)"},
        "nu": {"type": "number"},
        "t": {"type": "number", "description": "espesor (m)"},
        "q": {"type": "number", "description": "carga distribuida (Pa)"},
        "fc": {"type": "number", "description": "f'c (MPa)"},
        "fy": {"type": "number", "description": "fy (MPa)"},
        "recubrimiento": {"type": "number", "description": "recubrimiento (mm)"},
        "borde": {"type": "string", "enum": ["simple", "empotrado"]},
    },
    "required": ["a", "b", "nx", "ny", "E", "t", "q", "fc", "fy"],
}

ESQUEMAS = [
    {"type": "function", "function": {
        "name": "analizar_estructura",
        "description": "Resuelve un pórtico/estructura 3D por elementos finitos (rigidez directa) y "
                       "devuelve desplazamientos y reacciones nodales. Unidades SI (N, m, Pa).",
        "parameters": _ESQUEMA_ANALIZAR}},
    {"type": "function", "function": {
        "name": "disenar_losa",
        "description": "Diseña una losa de hormigón por FEM (ACI 318-19): deflexión central, momentos "
                       "máximos y armadura por franja. Espesor/luz en m, carga en Pa, f'c/fy en MPa.",
        "parameters": _ESQUEMA_LOSA}},
]


def ejecutar(nombre: str, argumentos: dict, cliente: "MotorCliente | None" = None) -> dict:
    """Ejecuta la herramienta ``nombre`` con ``argumentos`` vía el motor; errores → ``{"error": ...}``."""
    cliente = cliente or MotorCliente()
    try:
        if nombre == "analizar_estructura":
            return cliente.analizar(argumentos)
        if nombre == "disenar_losa":
            return cliente.disenar_losa(argumentos)
        return {"error": f"herramienta desconocida: {nombre}"}
    except Exception as ex:                      # el motor validó/falló → la IA recibe el error y corrige
        return {"error": str(ex)}
