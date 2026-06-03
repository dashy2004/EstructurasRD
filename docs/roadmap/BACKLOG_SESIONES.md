# Backlog de sesiones — post-consolidación

> Rama de trabajo: `engine/columnas-diseno` (carpeta única tras consolidar).
> Estado: 869/869 verde, build 0/0, sin push. Hecho hasta hoy: Columnas (P-M +
> UI + plot), Aceros export, editor 2D (snapping), fix 3D en vivo, cutover
> `Losas.exe` aditivo (losas + bordes/aceros adicionales), navegación por
> categorías (B) + dibujo de muros 2D (C). Orden pedido por el usuario: **D → E → F**.
>
> **Recordar la visión** (`docs/roadmap/VISION_ROADMAP.md`): suite estructural
> completa, no solo losas — vigas, columnas, zapatas, BIM, escala urbana.

## ⛔ Restricción permanente (hasta nuevo aviso)
**NO eliminar `Losas.exe`.** El cutover por motor FEM queda **aditivo** (menú Engine
→ "Calcular losas con el motor"); `Losas.exe` se mantiene como **respaldo**. No hacerlo
default ni borrar el flujo `.DL`/`.TXT` sin que el usuario lo pida explícitamente.

## D — Carga última directa (próxima sesión, motor + algo de UI)
Calcular la **carga última factorizada** de forma directa, sin depender del ingreso
manual. Incluye:
- **Carga muerta:** peso propio de losa + **muros** (de `Sistema.Muros`: longitud×espesor×altura×γ) + sobrecarga muerta (acabados).
- **Carga viva:** **elegir el tipo** (uso/ocupación) → valor por R-001 / ASCE 7-22.
- **Combinación:** LRFD (1.2D+1.6L, etc.) — ya existe `normativa/combinaciones` en el motor y `Transmision/BajadaCargas`.
- Salida: `q_u` por losa/elemento, lista para alimentar el diseño (losas, vigas, columnas).
- Reusar lo que ya hay: `BajadaCargas`, `DescensoColumnas`, las combos del motor.

## E — Vigas: más énfasis (diagramas + secciones)
El usuario reporta que las vigas **no muestran sus diagramas ni secciones**.
- Investigar el editor de Vigas (`VigaEditorView` / `VigaEditorViewModel`): PLAN_MAESTRO
  dice que ya hay OxyPlot M/V/δ (`ModeloEsfuerzos`/`ModeloDeflexion`) — ¿por qué no se ven?
  (¿regresión por el cambio de navegación B? ¿bug de binding? ¿pestaña vacía?).
- Mostrar prominentes los **diagramas** (momento, cortante, deflexión).
- Agregar el dibujo de la **sección** transversal de la viga (b×h + armado).
- Lane mixto: el cálculo (motor) es de Claude; el plot/sección visual con Antigravity.

## F — Columnas: aceros + carga transmitida + características de diseño
Surface en la pestaña Columnas (que ya tiene el plot P-M):
- **Aceros** (ya calculado: P-M, estribos) — asegurarse que se muestren bien.
- **Carga transmitida de las losas** → usar `DescensoColumnas` para el axial `Pu` que baja
  de las losas/niveles a cada columna, y **alimentar el `Pu` del chequeo P-M** (hoy el Pu
  es manual). Cerrar el lazo: losa → descenso → columna.
- Otras **características de diseño** de la columna (esbeltez, longitud efectiva, etc. — evaluar alcance).

## Cómo seguir (loop)
1. Sesión **D** (carga última) — motor headless + UI mínima.
2. Aplicar **E** (vigas) y **F** (columnas).
3. Mantener TDD, build+test verde por paso, commits sin push, no tocar `avalonia-linux`/`main`.
4. UI/pixeles → hand-offs a Antigravity; motor/cálculo → Claude.
