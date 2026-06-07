"""Cliente del motor-fea para la IA local — sobre el contrato JSON público (ADR-0001).

No modifica el motor: lo usa como herramienta. Dos modos equivalentes:

* **en proceso** (por defecto, rápido): importa ``motor_fea.api.contrato`` — la API
  pública de (de)serialización JSON del motor. No toca el núcleo, sólo la frontera.
* **subproceso** (``usar_cli=True``, caja negra total): invoca
  ``python -m motor_fea.api.cli`` pasando el JSON por stdin, exactamente como lo
  consume el C# (ADR-0001). El motor es un binario externo; cero imports del núcleo.

Unidades del contrato: SI (N, m, Pa) para el análisis; la losa toma MPa/mm donde se indica.
"""
from __future__ import annotations

import json
import subprocess
import sys


class MotorCliente:
    """Bridge a motor-fea por su contrato JSON. ``usar_cli`` elige proceso vs subproceso."""

    def __init__(self, usar_cli: bool = False) -> None:
        self.usar_cli = usar_cli

    def analizar(self, modelo: dict) -> dict:
        """Resuelve el modelo (rigidez directa) → ``{n_gdl, desplazamientos, reacciones}``."""
        if self.usar_cli:
            return self._cli("--analyze", modelo)
        from motor_fea.api.contrato import analizar_dict
        return analizar_dict(modelo)

    def disenar_losa(self, params: dict) -> dict:
        """Diseña una losa por FEM (ACI 318) → ``{w_central, mx_max, …, franja_x, …}``."""
        if self.usar_cli:
            return self._cli("--disenar-losa", params)
        from motor_fea.api.contrato import disenar_losa_dict
        return disenar_losa_dict(params)

    @staticmethod
    def _cli(flag: str, payload: dict) -> dict:
        """Invoca el CLI del motor pasando ``payload`` por stdin y parsea su stdout JSON."""
        proc = subprocess.run(
            [sys.executable, "-m", "motor_fea.api.cli", flag, "-"],
            input=json.dumps(payload), capture_output=True, text=True)
        if proc.returncode != 0:
            raise RuntimeError(proc.stderr.strip() or f"motor-fea {flag} falló")
        return json.loads(proc.stdout)
