"""Clasificador de incidencias por IA (pluggable). Descripción en lenguaje natural →
análisis estructurado y validado. Por defecto Ollama local (patrón motor_fea_ia);
Claude opcional (extra `ia`).

El texto del usuario se sanea antes del modelo (anti-inyección) y la salida se valida
estrictamente: lo que no cumpla el schema se normaliza/descarta. El modelo nunca
recibe el texto del usuario como instrucción del sistema.
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass

_SEVERIDADES = {"baja", "media", "alta", "critica"}

_INJECTION = re.compile(
    r"(ignora|ignore|olvida|forget)\s+(todo|todas|all|previous|las\s+instrucciones)"
    r"|(system|sistema)\s*:\s*"
    r"|(actua|actuá|act|behave|comportate)\s+(como|as|like)\s+"
    r"|<\s*script|javascript\s*:",
    re.IGNORECASE,
)

_SISTEMA = (
    "Eres un asistente de un ingeniero en obra. Clasificá la incidencia descrita y "
    "respondé SOLO un objeto JSON con las claves exactas: categoria, subcategoria, "
    "severidad (uno de: baja, media, alta, critica), resumen, accion_sugerida. "
    "No incluyas texto fuera del JSON."
)


@dataclass
class AnalisisIncidencia:
    categoria: str = ""
    subcategoria: str = ""
    severidad: str = "media"
    resumen: str = ""
    accion_sugerida: str = ""
    sospechoso: bool = False

    @classmethod
    def desde_dict(cls, d: dict) -> "AnalisisIncidencia":
        sev = str(d.get("severidad", "media")).strip().lower()
        if sev not in _SEVERIDADES:
            sev = "media"
        return cls(
            categoria=str(d.get("categoria", "")),
            subcategoria=str(d.get("subcategoria", "")),
            severidad=sev,
            resumen=str(d.get("resumen", "")),
            accion_sugerida=str(d.get("accion_sugerida", "")),
        )

    def to_dict(self) -> dict:
        return {
            "categoria": self.categoria, "subcategoria": self.subcategoria,
            "severidad": self.severidad, "resumen": self.resumen,
            "accion_sugerida": self.accion_sugerida, "sospechoso": self.sospechoso,
        }


def sanear(texto: str) -> tuple[str, bool]:
    """(texto_limpio, sospechoso). Quita control chars (deja \\n) y marca inyección."""
    limpio = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]", "", texto).strip()
    return limpio, bool(_INJECTION.search(limpio))


class _ClasificadorBase:
    """Plantilla: sanea → invoca el modelo → valida. Subclases implementan _invocar."""

    def clasificar(self, descripcion: str) -> AnalisisIncidencia:
        limpio, sospechoso = sanear(descripcion)
        if sospechoso:
            return AnalisisIncidencia(resumen=limpio[:200], sospechoso=True)
        try:
            crudo = self._invocar(limpio)
            return AnalisisIncidencia.desde_dict(crudo)
        except Exception:                       # modelo caído / salida no parseable
            return AnalisisIncidencia(resumen=limpio[:200], sospechoso=True)

    def _invocar(self, texto: str) -> dict:     # pragma: no cover
        raise NotImplementedError


@dataclass
class OllamaClasificador(_ClasificadorBase):
    modelo: str = "qwen2.5"
    host: str | None = None

    def _cliente(self):
        import ollama
        return ollama.Client(host=self.host) if self.host else ollama

    def _invocar(self, texto: str) -> dict:
        resp = self._cliente().chat(
            model=self.modelo,
            messages=[{"role": "system", "content": _SISTEMA},
                      {"role": "user", "content": texto}],
            format="json",
        )
        msg = resp["message"] if isinstance(resp, dict) else resp.message
        contenido = msg["content"] if isinstance(msg, dict) else msg.content
        return json.loads(contenido)


@dataclass
class ClaudeClasificador(_ClasificadorBase):
    modelo: str = "claude-fable-5"

    def _invocar(self, texto: str) -> dict:
        import anthropic
        client = anthropic.Anthropic(api_key=os.environ["ANTHROPIC_API_KEY"])
        tool = {
            "name": "registrar_analisis",
            "description": "Registra el análisis estructurado de la incidencia.",
            "input_schema": {
                "type": "object",
                "properties": {
                    "categoria": {"type": "string"},
                    "subcategoria": {"type": "string"},
                    "severidad": {"type": "string",
                                  "enum": ["baja", "media", "alta", "critica"]},
                    "resumen": {"type": "string"},
                    "accion_sugerida": {"type": "string"},
                },
                "required": ["categoria", "severidad", "resumen", "accion_sugerida"],
            },
        }
        resp = client.messages.create(
            model=self.modelo, max_tokens=512, system=_SISTEMA,
            tools=[tool], tool_choice={"type": "tool", "name": "registrar_analisis"},
            messages=[{"role": "user", "content": texto}],
        )
        for bloque in resp.content:
            if getattr(bloque, "type", None) == "tool_use":
                return dict(bloque.input)
        raise ValueError("respuesta sin tool_use")


def crear_clasificador(backend: str | None = None, modelo: str | None = None) -> _ClasificadorBase:
    """Elige backend por arg o env INCIDENCIAS_IA_BACKEND (default: ollama)."""
    backend = (backend or os.environ.get("INCIDENCIAS_IA_BACKEND", "ollama")).lower()
    if backend == "claude":
        return ClaudeClasificador(modelo=modelo or "claude-fable-5")
    return OllamaClasificador(modelo=modelo or "qwen2.5")
