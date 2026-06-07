"""Adaptador de IA local para motor-fea — sin modificar el motor.

La IA local (Ollama + Qwen) **adopta** el motor como herramienta a través de su
contrato JSON público (ADR-0001 / ``motor_fea.api.contrato``). Este paquete es
hermano de ``motor_fea`` y no toca su código: solo lo consume.

* :class:`~motor_fea_ia.motor_cliente.MotorCliente` — bridge al motor (en proceso o subproceso).
* :mod:`motor_fea_ia.herramientas` — esquemas de function-calling + dispatcher.
* :class:`~motor_fea_ia.agente.AgenteEstructural` — loop Ollama/Qwen ↔ herramientas.
"""
from motor_fea_ia.agente import AgenteEstructural
from motor_fea_ia.motor_cliente import MotorCliente

__all__ = ["AgenteEstructural", "MotorCliente"]
