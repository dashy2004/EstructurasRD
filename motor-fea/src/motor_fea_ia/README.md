# motor_fea_ia — la IA local trabaja con el motor SIN modificarlo

`motor_fea_ia` permite que una IA local (Ollama + Qwen) **adopte** el motor de cálculo
`motor-fea` como una herramienta, a través de su **contrato JSON público** (ADR-0001).
El motor (`src/motor_fea/`) no cambia ni una línea: la IA lo invoca como caja negra.

## Cómo

```python
from motor_fea_ia import AgenteEstructural

ag = AgenteEstructural(modelo_ia="qwen2.5:7b")   # requiere Ollama corriendo
print(ag.consultar("Diseñá una losa de 5×5 m, espesor 0.2 m, carga 10 kPa, f'c 21, fy 420"))
```

La IA decide cuándo llamar a las herramientas `analizar_estructura` (pórticos 3D por FEM)
y `disenar_losa` (losas por FEM, ACI 318), arma el JSON, recibe el resultado del motor y
lo interpreta en lenguaje claro.

## Por qué no toca el motor

- El bridge `MotorCliente` usa la frontera pública `motor_fea.api.contrato` (en proceso) o
  el CLI `python -m motor_fea.api.cli` por stdin (subproceso / caja negra total, `usar_cli=True`).
- Las herramientas (`herramientas.ESQUEMAS`) son esquemas de *function-calling* que el motor
  no conoce; el motor sigue siendo agnóstico de la IA.
- Si Ollama no está instalado, sólo falla el `AgenteEstructural`; el `MotorCliente` (el bridge
  al motor) funciona igual y se puede testear sin IA.

## Requisitos

- El motor (`pip install -e .`) en el entorno.
- Para el agente: `pip install ollama` + un Ollama local con un modelo tool-calling
  (`ollama pull qwen2.5:7b`). El bridge y los tests no requieren Ollama.
