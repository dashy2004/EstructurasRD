# Visión — Fusión EstructurasRD + Incidencias RD + VR/MR (Meta Quest)

> Documento de **idea/visión** (no es un plan de implementación). Capturado y
> analizado el 2026-06-13. Estado: **pendiente** — requiere su propia sesión de
> brainstorming antes de cualquier código.

---

## 1. La idea (como la planteó el usuario)

Fusionar tres plataformas propias y llevarlas a **realidad mixta en Meta Quest**,
anticipando los lentes AR (incluidos los próximos con Linux) que traerán esta opción:

- **EstructurasRD** (este repo): motor de cálculo estructural. La parte de **losas**
  está completa (vigas, columnas, cálculo) pero "sigue dando problemas" / requiere
  arreglos. Es la fuente de geometría y resultados.
- **Plataforma de Incidencias RD**: gestión de incidencias en obra.
- Una tercera plataforma (⚠️ identidad por confirmar — ver Preguntas abiertas).

**El producto que más le interesa (el "ya casi hecho"):** con el Quest, en **campo y
a escala**, ver el modelo de la estructura sobre el solar real y **recorrer las
etapas** de construcción: solar en blanco → excavación (civil) → modelo 3D
estructural importado (ya tiene flujo en Revit). Es **4D BIM en MR**: planificar
parándose en el terreno y viendo "lo que va a pasar".

**Punto de entrada más temprano (incidencias en MR):** llegar a la zona con el Quest,
que **reconozca/anote la incidencia**, le ponga un recuadro ("esto es, esto requiere
esto"), y muestre equipos/recursos como **asistencia al ingeniero**.

Motivación: aprovechar el plan de Anthropic ($20/mes, tokens) y **Fable 5** (modelo
nuevo, temporal hasta ~2026-06-22) con su stack ya configurado en Linux (agentes,
plugins, Quest, cuenta de developer). Objetivo: "seguir revolucionando y adaptando en
la República Dominicana".

---

## 2. Análisis

### ¿Encaja con el uso de Fable 5?
Sí, directamente. Fable es un modelo de codificación/agéntico de tope de gama:
scaffolding de una app XR (WebXR o Unity/OpenXR), integración con plataformas
existentes, diseño de contratos de datos y arquitectura multi-app es exactamente su
terreno. **Ventaja concreta de este repo:** ya existe trabajo **WebXR** previo
(pipeline three.js: geometría → deformada → heatmaps). Hay base real, no se parte de cero.

### Realidad técnica (desglose honesto)
La visión mezcla varias piezas grandes; hay que **descomponer** y no atacarlas juntas:
1. App nativa Meta Quest (mixed reality / passthrough).
2. Modelo estructural **a escala sobre el solar** (anclaje espacial / spatial anchors).
3. **Secuenciación de etapas** (4D BIM: excavación → civil → estructura).
4. Incidencias en MR con **reconocimiento/anotación asistida por IA**.
5. Importación desde **Revit / IFC / geometría EstructurasRD**.
6. **Fusión** de los datos de las 3 plataformas (contrato común).

### Dos MVPs candidatos (elegir UNO para empezar)
- **MVP-A · Asistente de incidencias en MR** *(el propio usuario lo señaló como el
  inicio más temprano).* En passthrough del Quest: recorrer la obra, colocar/anotar una
  incidencia con un recuadro 3D anclado, mostrar una ficha (qué es, qué requiere,
  equipos). **Menor alcance, alto valor, IA-forward**, y se apoya en Incidencias RD que
  ya existe. La "reconocimiento automático" empieza **manual + clasificación asistida
  por IA**, no auto-detección por visión (eso es research-grade).
- **MVP-B · Visor de etapas a escala (4D).** Importar un modelo (pipeline WebXR / Revit
  / geometría EstructurasRD), anclarlo a escala en el solar vía MR, y **scrubear** las
  etapas. Es el "anticipar el futuro" más vistoso pero depende de anclaje espacial
  robusto y de un export estable del motor.

### Stack recomendado (ruta de menor riesgo)
1. **Prototipo WebXR sobre el navegador del Quest** reutilizando el three.js de este
   repo (sesión `immersive-ar` con passthrough). Valida la experiencia **rápido** con
   los assets que ya tiene.
2. Si se necesita passthrough/anclaje/perf de producción → **Unity + Meta XR SDK
   (OpenXR)** nativo. (Godot XR es alternativa, pero Unity tiene el mejor soporte Quest.)

### Prerrequisitos y riesgos
- ⚠️ **EstructurasRD "todavía da problemas".** La capa VR **consume** las salidas del
  motor (geometría, resultados). Antes de invertir fuerte en VR, conviene un **contrato
  de export estable** (IFC/glTF/JSON) y estabilizar el core. No dejar que la visión VR
  desvíe el arreglo del motor.
- **Reconocimiento de incidencias por IA** sobre frames de passthrough es lo más
  difícil; arrancar con anotación manual + clasificación asistida.
- **Fable temporal hasta ~2026-06-22**: usar esta ventana para lo de **alto
  apalancamiento** (arquitectura, scaffolding, contratos), no para pulir detalles.
- **Fusión de 3 plataformas**: definir primero el **contrato de datos común** (modelo
  de proyecto/obra/incidencia/geometría) — es el corazón de la fusión.

---

## 3. Cómo empezar (recomendación)

No intentar la visión completa de una. Ruta sugerida:

1. **Sesión de brainstorming dedicada** (skill `brainstorming`) acotada a **UN** MVP
   — recomendado **MVP-A (incidencias en MR)** por menor riesgo y por apoyarse en una
   plataforma ya existente. Salida: un spec.
2. Definir el **contrato de datos** mínimo entre EstructurasRD/Incidencias y la app XR
   (qué entidades, en qué formato — probablemente glTF para geometría + JSON para
   incidencias/etapas).
3. **Prototipo WebXR en el Quest** del slice elegido (1 incidencia anclada con su ficha,
   o 1 modelo a escala con 2 etapas). Medir comodidad/anclaje antes de comprometerse a
   Unity nativo.
4. Decidir nativo (Unity/OpenXR) vs seguir en WebXR según lo aprendido.

## 4. Preguntas abiertas (resolver en el brainstorming)
- ¿Cuál es la **tercera plataforma**? (el usuario mencionó "tres" pero nombró dos).
- Fuente del modelo 3D: ¿**Revit/IFC**, glTF exportado, o la geometría nativa de
  EstructurasRD? ¿Hay ya un export utilizable?
- ¿Online (servidor/cuenta) u **offline** en campo (sin señal en el solar)?
- Anclaje: ¿spatial anchors del Quest, marcadores/QR en el terreno, o GPS+brújula?
- ¿Repo nuevo dedicado a la app XR, o un subproyecto dentro de EstructurasRD?
- Prioridad real: ¿MVP-A (incidencias) o MVP-B (etapas a escala) primero?

## 5. Relación con el trabajo actual
- **No bloquea** la línea UI1.x de EstructurasRD (ver `NEXT-SESSION-UI1.md`). Es una
  iniciativa paralela mayor.
- El mejor "puente" entre lo actual y la visión es **estabilizar + exportar** el modelo
  del motor (glTF/IFC), que sirve tanto a la app de escritorio como a la futura XR.
