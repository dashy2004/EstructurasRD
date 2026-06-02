# PLAN v1.2 — Ergonomía Interactiva, Representación Visual y PDF Underlay

## Contexto

El Epic v1.1 («Dibujo Avanzado») está cerrado y en producción (tag `v1.1-RC1`).
El Epic v1.2 enriquece el lienzo CAD (`CadCanvasHost`) en cuatro frentes, sin
romper el SSOT (`Sistema.Losas`) ni la arquitectura de renderizado retained-mode.

## Alcance — 4 iteraciones

### Iteración 1 — Representación visual de losas
Las losas dejan de ser rectángulos vacíos: se dibuja un patrón interior según su
condición estructural (`Losa.DireccionTrabajo`) — franjas horizontales (1D-H),
franjas verticales (1D-V) o cuadrícula reticular (2D) — y un rótulo interno de
tres líneas (`Id`, `Lx × Ly`, `Tipo`). Además, las adyacencias
(`Sistema.BordesX` / `BordesY`) se marcan con un ícono de acero adicional.

### Iteración 2 — Edición in-canvas
Doble clic sobre una losa abre un editor flotante (Lx, Ly y Tipo) superpuesto al
lienzo; al confirmar se dispara `ActualizarLosaCommand` con su snapshot de Undo.

### Iteración 3 — Toolbar: Mano, Snap y Mover Conectadas
Una herramienta Mano (pan dedicado) y dos toggles: **Snap** (enciende/apaga el
`SnappingEngine`) y **Mover Conectadas** (al mover una losa, un recorrido BFS
sobre las adyacencias arrastra toda la componente conexa con el mismo vector).

### Iteración 4 — PDF Underlay
Importación de planos en PDF: la primera página se rasteriza (librería
**Docnet.Core**) y se dibuja en la Capa 1 como underlay, con los mismos factores
de Escala / OffsetX / OffsetY que el bloque DXF.

## Principios de producción

- El interior de las losas y todos los marcadores se dibujan **sólo** con
  `DrawingContext` (`DrawLine` / `DrawText` / `DrawImage`) — prohibido
  `Shape` / `UIElement`, para sostener los 60 FPS.
- `src.Core` permanece WPF-agnóstico; lo que produce un `BitmapSource` vive en el
  proyecto WPF `src/`.
- Build con **0 warnings**; suite de tests en verde. Un **commit aislado** por
  iteración, con checkpoint de confirmación entre iteraciones.

## Roadmap → v1.3

- **Autoconectar losas por proximidad** — detección automática de adyacencias
  entre losas según su cercanía geométrica en el lienzo, evitando que el usuario
  cree cada `BordeAdic` a mano.
