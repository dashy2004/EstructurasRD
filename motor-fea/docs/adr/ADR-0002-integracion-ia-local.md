# ADR-0002 — Integración de IA local sin modificar el motor

**Fecha:** 2026-06-06
**Estado:** aceptado

## Contexto

EstructurasRD usa una IA local (Ollama + Qwen) para análisis de planos/CAD. Se quiere que
esa IA **trabaje con el motor de cálculo** (`motor-fea`) para resolver/diseñar estructuras,
**sin modificar el código del motor**.

## Decisión

Adoptar el patrón **"tool use" sobre el contrato JSON público** del motor (ADR-0001):

- El motor expone `motor_fea.api.contrato` (`analizar_dict`, `disenar_losa_dict`) y el CLI
  `motor-fea --analyze/--disenar-losa` (JSON por stdin/stdout). Esa frontera ya estaba
  pensada para integrar el C# sin tocar el núcleo.
- Se agrega un paquete **hermano** `motor_fea_ia` (no toca `motor_fea/`) con:
  - `MotorCliente`: bridge en proceso (contrato) o subproceso (CLI, caja negra).
  - `herramientas`: esquemas de function-calling + dispatcher (errores → `{"error": …}`).
  - `AgenteEstructural`: loop Ollama/Qwen ↔ herramientas.

## Consecuencias

- El motor queda agnóstico de la IA; cualquier cambio en la IA es aditivo y aislado.
- La misma frontera sirve al C#, a la IA y a futuros consumidores (HTTP, MCP).
- El bridge es testeable contra el motor real sin Ollama; el agente se testea con la IA mockeada.
- Limitación: la calidad del modelo que arma la IA depende del modelo Qwen; el motor valida y
  devuelve errores para que la IA corrija (loop de hasta `max_iteraciones`).
