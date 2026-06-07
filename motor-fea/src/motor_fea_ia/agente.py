"""Agente estructural sobre la IA local (Ollama + Qwen) por function-calling.

La IA recibe una consigna en lenguaje natural, arma el modelo, llama al motor a
través de las herramientas e interpreta los resultados. El motor no se modifica.
Requiere ``ollama`` y un modelo con tool-calling (p.ej. ``qwen2.5``) corriendo en Ollama.
"""
from __future__ import annotations

import json

from motor_fea_ia import herramientas
from motor_fea_ia.motor_cliente import MotorCliente

SISTEMA = (
    "Eres un ingeniero estructural. Para resolver o diseñar usás las herramientas "
    "analizar_estructura (pórticos 3D, FEM de rigidez directa) y disenar_losa (losas por "
    "FEM, ACI 318). Construí los modelos en unidades SI (N, m, Pa; la losa usa MPa/mm donde "
    "se indica). Llamá a la herramienta con el JSON apropiado, leé el resultado y explicá al "
    "usuario los desplazamientos, reacciones o el armado en lenguaje claro."
)


def _mensaje_de(respuesta):
    """Normaliza la respuesta de Ollama (dict u objeto) → (contenido, [llamadas])."""
    m = respuesta["message"] if isinstance(respuesta, dict) else respuesta.message
    contenido = (m["content"] if isinstance(m, dict) else m.content) or ""
    crudas = (m.get("tool_calls") if isinstance(m, dict) else getattr(m, "tool_calls", None)) or []
    llamadas = []
    for tc in crudas:
        f = tc["function"] if isinstance(tc, dict) else tc.function
        nombre = f["name"] if isinstance(f, dict) else f.name
        args = f["arguments"] if isinstance(f, dict) else f.arguments
        if isinstance(args, str):
            args = json.loads(args)
        llamadas.append({"name": nombre, "arguments": dict(args)})
    return contenido, llamadas


class AgenteEstructural:
    """Orquesta Qwen (vía Ollama) + las herramientas del motor. El motor no se modifica."""

    def __init__(self, modelo_ia: str = "qwen2.5:7b", cliente: "MotorCliente | None" = None,
                 host: "str | None" = None) -> None:
        self.modelo_ia = modelo_ia
        self.cliente = cliente or MotorCliente()
        self._host = host

    def _cliente_ollama(self):
        import ollama
        return ollama.Client(host=self._host) if self._host else ollama

    def consultar(self, consigna: str, max_iteraciones: int = 6) -> str:
        """Resuelve una consigna en lenguaje natural usando la IA + el motor como herramienta."""
        oll = self._cliente_ollama()
        mensajes = [{"role": "system", "content": SISTEMA},
                    {"role": "user", "content": consigna}]
        for _ in range(max_iteraciones):
            respuesta = oll.chat(model=self.modelo_ia, messages=mensajes, tools=herramientas.ESQUEMAS)
            contenido, llamadas = _mensaje_de(respuesta)
            mensajes.append({"role": "assistant", "content": contenido,
                             "tool_calls": [{"function": {"name": ll["name"], "arguments": ll["arguments"]}}
                                            for ll in llamadas]})
            if not llamadas:
                return contenido
            for ll in llamadas:
                resultado = herramientas.ejecutar(ll["name"], ll["arguments"], self.cliente)
                mensajes.append({"role": "tool", "name": ll["name"], "content": json.dumps(resultado)})
        return "(se alcanzó el máximo de iteraciones sin respuesta final)"
