# Spec — Slice VR "editar carga → recalcular en vivo" (visor WebXR como herramienta)

**Fecha:** 2026-06-14
**Repo:** `~/Downloads/EstructurasRD-engine/motor-fea` (motor Python + visor), rama `engine/shell-web-webxr`.
**Tipo:** slice vertical (MVP de "herramienta real", no demo pasivo). 100% front, sobre el visor
WebXR de #2-#4. Sin cambios de server.

---

## 1. Por qué (la bifurcación que resuelve)

El visor en su estado actual es un **espejo 1:1** de lo que Blender exportó: visualización pasiva.
La pregunta estratégica era **demo de visualización escalada vs herramienta de diseño real**. Decisión
(brainstorming 2026-06-14): construir un **slice vertical** que prueba la tesis "herramienta real"
con el mínimo riesgo — el usuario **cambia algo** de un modelo importado y ve la **consecuencia
estructural recalculada en vivo, a escala, dentro del Quest**.

Reencuadre clave que lo hace barato: **el solver ya existe y ya habla HTTP.** #1 agregó
`POST /analizar` y #2 `POST /visor` al motor. "Cálculo en vivo" NO es meter un solver en el headset
— es que la app WebXR (que ya corre en el browser del Quest y ya hace `POST /visor`) reenvíe el
modelo editado y reciba deformada+esfuerzos. El pedazo difícil (la FEA) está hecho.

## 2. Definición de "hecho" (v1)

Dentro del Quest, sobre un modelo ya cargado:
1. El usuario **apunta con el control a un nodo** (raycast) → se selecciona y resalta.
2. Aparece un **control flotante** para la **magnitud de la carga** de ese nodo.
3. Al cambiarla, el front **re-arma el modelo** con la carga nueva y hace **`POST /visor`** (con
   debounce) → recibe deformada+esfuerzos → **re-renderiza a escala**.
4. Hay indicador "calculando…"; si el motor rechaza (400/422), se muestra el error y se **mantiene
   el último estado válido** (no se rompe la escena).

El slice está hecho cuando ese loop "cambiar carga → ver consecuencia" funciona en el **Quest real**.

## 3. Decisiones (brainstorming 2026-06-14)

| # | Decisión | Elección |
|---|---|---|
| D1 | Objetivo del MVP | **Slice vertical** = herramienta real pero acotada (demostrable Y semilla de producto). |
| D2 | Plataforma | **Visor WebXR/three.js de #2-#4** en el browser del Quest (no el export horneado de Blender). |
| D3 | Loop interactivo | **Editar + recalcular** (un cambio → consecuencia), no CAD-desde-cero ni solo-disparar. |
| D4 | La edición de v1 | **Cambiar la magnitud de una carga nodal.** Trivial en el motor, sin riesgo de inestabilidad. |
| D5 | Server | **Sin cambios.** `POST /visor` ya acepta un modelo arbitrario y devuelve escena+resultados+esfuerzos. |
| D6 | Escala | "A escala" v1 = modelo escalado **dentro de la escena VR**, NO anclado al cuarto físico (eso es fase 2 AR). |

## 4. Arquitectura (front-only, sobre #2-#4)

Reusa todo el visor; agrega entrada VR + editor de carga + re-post.

```
modelo en memoria (con cargas)
        │  [seleccionar nodo por raycast del control Quest]
        ▼
   editor de carga flotante (magnitud)
        │  [cambiar magnitud]  → debounce
        ▼
   re-armar modelo' con la carga editada  ── POST /visor ──▶ motor
        ▲                                                      │
        └──────── re-render (deformada + esfuerzos) ◀──────────┘  {escena, resultados, esfuerzos}
```

### Componentes
- **Selección de nodo (VR):** raycast desde el control del Quest a los nodos (esferas) → resalta el
  seleccionado. Reusa/extiende el pick por raycaster que #2 ya usa para el readout de esfuerzos.
- **Editor de carga flotante:** control en el espacio (slider o grip vertical) ligado a la carga del
  nodo seleccionado; muestra el valor actual (p.ej. kN). Edita SOLO la magnitud (dirección fija, p.ej.
  gravedad −Z) en v1.
- **Estado editable del modelo:** el front mantiene el modelo JSON (con `cargas`) en memoria; editar
  produce `modelo'` con la `carga` del nodo cambiada.
- **Re-análisis (debounced):** al cambiar, `POST /visor?n=…` con `modelo'`; al volver, re-render de
  deformada+esfuerzos (reusa `renderEscena`/overlays de #2-#4). Debounce (~150-300 ms) para no
  spamear el motor mientras se arrastra.
- **Estado/errores:** indicador "calculando…"; en 400/422/timeout, mensaje legible en VR y se
  conserva el último resultado válido.

## 5. Reuso vs nuevo
- **Reusa (#2-#4):** carga del modelo, `renderEscena`/`limpiarEscena`, pick raycaster, overlay de
  deformada, overlay/readout de esfuerzos, cliente `POST /visor`.
- **Nuevo:** (a) entrada con **controles del Quest en WebXR** (raycast-select + grip/trigger);
  (b) **editor de carga flotante**; (c) **re-post-al-editar con debounce** y manejo de estado/errores.

## 6. Prerequisito (paralelo, NO parte del loop)
Arreglar la **orientación del import** (pórticos alineados en Y): casi seguro convención de ejes
**Blender Y-up vs Z-up** del motor/Revit (rotación en el import) o la limitación documentada
(#3 §6.3 / #4) de usar ejes dibujados en vez del triedro local. Sin esto el demo se ve torcido. Es un
**fix aparte**, no parte del slice; conviene resolverlo antes de mostrarlo.

Bug menor relacionado, también aparte: **colores no se capturan en PNG** (materiales/vertex-colors no
rasterizan; los diagramas sí por ser geometría tipo SVG).

## 7. Fuera de alcance (fase 2+)
Dibujar geometría desde cero en planta (CAD en VR), mover nodos, quitar/poner apoyos, multi-nivel,
edición de dirección de carga, y **anclaje AR a escala real del mundo**.

## 8. Riesgos
- **Interacción WebXR con controles del Quest es lo único genuinamente nuevo.** El visor #2-#4 se
  verificó en browser de escritorio (Playwright), no necesariamente con controles en el headset. Por
  eso v1 = una sola edición, input mínimo. **Probar temprano en el Quest real.**
- **Latencia del round-trip** al motor durante el arrastre → mitigado con debounce + "calculando…".
- **Modelos inestables** no son riesgo en v1 (cambiar magnitud de carga no quita apoyos).

## 9. Testing
- Motor ya testeado (225+); patrón front-only sin runner JS (= #2-#4) → **verificación manual en el
  Quest real** del loop completo.
- La lógica pura nueva (re-armar `modelo'` con la carga editada de un nodo) se aísla como función
  pura y, si se quiere, se cubre con un test mínimo (sigue siendo opcional bajo el patrón del repo).

## 10. Próximo paso
**Parar aquí** (decisión del usuario): esta sesión ya entregó 5a completo + este diseño. La
implementación del slice arranca en **sesión limpia** rooteada en el repo engine
(`~/Downloads/EstructurasRD-engine/motor-fea`, rama `engine/shell-web-webxr`), invocando
`superpowers:writing-plans` con este spec. Primer paso técnico sugerido: el **prerequisito de
orientación** (§6), luego el loop (§4).
