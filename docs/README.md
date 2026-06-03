# Documentación — EstructurasRD

Organizada **por tipo** según con lo que se trabaje. Cada carpeta agrupa un
género de documento; los activos pesados (capturas, plantillas) viven aparte.

| Carpeta | Tipo | Contenido |
|---|---|---|
| [`handoff/`](handoff/) | Coordinación entre agentes | División de trabajo Claude ⟷ Antigravity, hand-offs de tareas, memoria de contexto. |
| [`roadmap/`](roadmap/) | Visión y planificación | Hacia dónde va el producto (fases K–N). |
| [`releases/`](releases/) | Notas de versión | Qué cambió en cada release. |
| [`referencia/`](referencia/) | Referencia técnica | Plantillas (`.docx`/`.xlsx`), diseños de UI, capturas de diseño. |
| [`screenshots/`](screenshots/) | Capturas catalogadas | Evidencia visual por pantalla (00–10). |
| [`negocio/`](negocio/) | Cliente / correspondencia | Cartas, minutas de reunión con el Ing. Perdomo. |

> `RUNNER_BEHAVIOR.md` queda en la raíz de `docs/` a propósito: lo referencian
> por ruta `README.md`, `src/README.md` y un comentario en
> `src/ViewModels/MainViewModel.cs`. Moverlo a `referencia/` exige actualizar esas
> 3 referencias (lane UI → Antigravity); ver el hand-off de reorganización.

## Índice de hand-offs vigentes

- [`handoff/DIVISION_TRABAJO.md`](handoff/DIVISION_TRABAJO.md) — contrato de lanes
  (Claude = motor headless · Antigravity = UI/pixeles) y fronteras por carpeta.
- [`handoff/HANDOFF_UI_ANTIGRAVITY.md`](handoff/HANDOFF_UI_ANTIGRAVITY.md) — modos
  de estilo del visor 3D + rediseño de navegación.
- [`handoff/HANDOFF_ANTIGRAVITY_REORG.md`](handoff/HANDOFF_ANTIGRAVITY_REORG.md) —
  **reorganización de archivos + unificación de proyectos + interfaces + bugs de
  sincronización CAD/Planta2D/3D** (este es el grande).
