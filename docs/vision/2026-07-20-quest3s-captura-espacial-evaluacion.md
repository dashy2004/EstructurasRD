# Captura espacial en Meta Quest 3S — Evaluación de SDK + diseño de módulo

> **Tipo:** evaluación técnica + diseño de arquitectura (no es aún un plan de
> implementación por tareas). **Fecha:** 2026-07-20. **Estado:** borrador para
> decisión. **Hardware objetivo:** Meta Quest 3S.
>
> **Alcance de acceso:** redactado con acceso a **tres** repos de `dashy2004`:
> `EstructurasRD`, `IncidenciasRD` y `VisionRD`. El pipeline (§5) y el contrato de
> integración (§4) están **verificados contra el código real** de VisionRD e
> IncidenciasRD. Lo que aún depende de hardware/SDK de Meta se marca **[VERIFICAR
> contra SDK actual]** (mi conocimiento de las APIs de Meta tiene corte y su SDK XR
> evoluciona rápido).

---

## 0. Resumen ejecutivo (para retomar rápido)

- **La foto única no sirve**; el flujo de captura debe ser **video**. Asumido en todo
  el documento.
- **Hallazgo que reordena las opciones de SDK:** en Quest, el RGB del mundo real **no**
  se obtiene por WebXR ni por el grabador de pantalla del sistema. **Cualquier** captura
  RGB exige una **app nativa (Unity/OpenXR + Meta XR SDK) usando Passthrough Camera
  Access (PCA)**. El visor WebXR previo (FOL-Visor-XR) sirve para *ver*, no para *capturar*.
- **Hallazgo que redefine la integración (verificado en código):** el video del Quest
  **no** se adjunta a IncidenciasRD directamente. El destino natural es **VisionRD**, que
  ya expone `POST /ingest` recibiendo **`video` + `track`** y corre TODO el pipeline
  (anonimizar → fotogramas → detectar → georef → dedup → publicar a IncidenciasRD). El
  Quest se vuelve **una fuente de video más** para un pipeline que ya existe.
- **Recomendación de SDK:** **Opción C en variante "C+pose"** — RGB por PCA + **pose 6DoF
  por frame** + intrínsecos. Es la de **menor cambio server-side** (el video entra por el
  `/ingest` que ya existe) y la pose resuelve el punto que a VisionRD hoy le falta para
  interiores/estructuras: **escala métrica y georreferencia sin GPS**.
- **Opción A (Depth API)** = fase de enriquecimiento; paga justo donde VisionRD es hoy
  "aproximado" (reconstrucción 3D monocular) y donde EstructurasRD exigirá precisión.
  **Opción B (Scene mesh)** = capa de contexto/anclaje, nunca primitiva de captura.
- **Primera tarea real de Claude Code = esta evaluación (§2).** No pre-decidir el SDK
  fuera de aquí.

---

## 1. Problema y encuadre

### 1.1 Por qué video, no foto
1. Una sola foto no da paralaje → sin reconstrucción de point cloud confiable.
2. La detección sobre frame único es poco fiable (oclusión, ángulo, blur).

→ La captura debe ser **video** (secuencia con movimiento): paralaje para profundidad
multi-vista y múltiples vistas por objeto para la detección.

### 1.2 Tres líneas de trabajo (relacionadas, **no** fusionadas)
1. **Captura** de video en Quest 3S (módulo independiente).
2. **Subida** de ese video a **VisionRD `/ingest`** (que ya lo procesa e integra a
   IncidenciasRD).
3. **Integración** con el pipeline VisionRD ya existente (rfdetr/yolox + depth + PostGIS).
4. **Caso paralelo EstructurasRD:** levantamiento de campo con el mismo visor (fase
   separada; requisitos aún no definidos).

### 1.3 Restricción rectora de la decisión
Criterio de desempate del brief: **"cuál requiere menos cambios al pipeline server-side
existente"**. Con VisionRD verificado, esto se vuelve concreto: **la opción que produzca
el input `video + track` que `/ingest` ya acepta es la de cero cambios.**

---

## 2. Evaluación de las tres opciones de captura (Tarea 1)

### 2.1 Fundamento común (aplica a las tres)

- **El RGB del mundo real requiere Passthrough Camera Access (PCA)** — API de Meta que
  expone los frames de la cámara RGB frontal a la app en foco, con intrínsecos/extrínsecos.
  **[VERIFICAR versión mínima de Horizon OS y límites en 3S]** (histórico: PCA en Quest
  3/3S desde Horizon OS ~v74; permiso `horizonos.permission.HEADSET_CAMERA` +
  `android.permission.CAMERA`; solo foreground; resolución/fps limitados).
- **WebXR no da píxeles de cámara** (privacidad): tiene depth-sensing y hit-test/anchors,
  pero **no** RGB → no puede alimentar la detección. El visor WebXR previo no es la capa
  de captura.
- **El grabador de pantalla no captura el passthrough** (queda en negro).

**Consecuencia:** la primitiva de captura de las tres opciones es una **app nativa con
PCA**. La diferencia real es **qué se captura junto al RGB** y **cuánto obliga a tocar
VisionRD**.

### 2.2 Matriz de evaluación

| Criterio (peso) | A · Passthrough+Depth API | B · Scene/Room mesh | C+pose · RGB+pose server-side |
|---|---|---|---|
| **Cambios en VisionRD (MÁXIMO peso)** | Altos: nuevo ingest depth+pose, fusión multi-vista | N/A: la mesh no entra a detección | **Mínimos**: `video + track` = el `/ingest` actual |
| Costo on-device | Alto (PCA + Depth + sync) | Bajo | **Bajo–medio** (PCA + pose + encode) |
| Calidad de geometría | Alta (depth métrico) | Muy baja (planos) | Media (monocular; ↑ con pose) |
| Sirve a detección (rfdetr/yolox) | Sí | **No** (no hay imagen) | Sí |
| Escala métrica / georref sin GPS | Sí (nativa) | Sí (nativa) | **Sí vía pose+ancla** (§5.3) |
| Reutiliza VisionRD | Parcial | Casi nada | **Casi todo** |
| Riesgo | Depth API baja-res, pensada para oclusión | Coarse, inútil para incidencias | Calidad del monocular ya existente en VisionRD |

### 2.3 Lectura de cada opción

**A — Passthrough + Depth API.** Da depth métrico y pose → el mejor point cloud potencial
y registro directo. Pero la Depth API es **baja-res y pensada para oclusión**, no para
reconstrucción fina **[VERIFICAR]**, y **obliga a un ingest nuevo** en VisionRD (aceptar
depth+pose en vez de video plano). Es la que **más se aleja** del pipeline actual.
→ **No como MVP. Sí como enriquecimiento** de la reconstrucción 3D (§5) y para EstructurasRD.

**B — Scene/Room mesh.** Métrica y baratísima pero **coarse**: no capta la incidencia ni
produce imagen para el detector. → **Descartada como captura**; útil solo como **anclaje/
contexto** (georreferenciar la nube al cuarto).

**C+pose — RGB por PCA + pose 6DoF.** Produce exactamente lo que `/ingest` ya consume
(**video + track**), y la pose cubre el punto ciego de VisionRD en interiores (georref sin
GPS + escala). **Menor cambio server-side** — el criterio rector. → **MVP.**

### 2.4 Decisión propuesta

> **MVP = C+pose.** App nativa (Unity + Meta XR SDK/OpenXR) que graba **RGB por PCA** y
> escribe un **sidecar de pose 6DoF + intrínsecos por frame**. On-device o en un paso
> ligero, la pose se convierte a un **`track` JSON `[{t,lat,lon}]`** (georref por ancla,
> §5.3) → se sube a **VisionRD `/ingest`** **sin cambiar el pipeline**.
>
> **Fase 2 (enriquecimiento) = Opción A (Depth API)** para la reconstrucción 3D métrica
> (hoy monocular "aproximada" en VisionRD) y para EstructurasRD.
>
> **Opción B** solo como capa de anclaje/contexto.

---

## 3. Diseño del módulo de captura (Tarea 2) — independiente

Módulo **autónomo**, sin acople a IncidenciasRD/VisionRD/EstructurasRD. Contrato de salida
estable; los consumidores se acoplan a ese contrato.

### 3.1 Stack
- **App nativa Unity + Meta XR SDK (OpenXR)** — mejor soporte PCA/Depth/Scene en Quest.
- Grabación RGB a **H.264/H.265 .mp4** (encoder HW del XR2 Gen 2).
- Sidecar de telemetría por frame en **JSONL**.

### 3.2 Responsabilidades
1. Permisos PCA (`HEADSET_CAMERA` + `CAMERA`) y arranque de la cámara.
2. **Grabar RGB** (resolución/fps configurables; empezar conservador **[VERIFICAR límites
   PCA en 3S]**).
3. **Registrar por frame:** timestamp, **pose 6DoF** (posición + cuaternión en espacio de
   captura), **intrínsecos** de PCA, estado de tracking.
4. **Anclaje:** una **Spatial Anchor** al inicio como origen del espacio de captura.
5. **Empaquetar** el artefacto §3.3 y entregarlo a la capa de subida (§4).
6. **Degradación:** si PCA no está disponible → mensaje claro, sin crash.

### 3.3 Contrato de salida (artefacto de captura) — **estable**

```
captura_<uuid>/
  video.mp4                 # RGB, H.264/265
  frames.jsonl              # una línea por frame (pose + K + t)
  captura.json              # metadatos de sesión
```

`frames.jsonl` (una línea por frame):
```jsonc
{ "t": 1234567.89,                           // timestamp (s, monotónico)
  "pose": { "p": [x,y,z], "q": [x,y,z,w] },  // cámara en espacio de captura (m)
  "K": { "fx":.., "fy":.., "cx":.., "cy":.., "w":.., "h":.. },
  "tracking": "ok" }
```

`captura.json` (sesión):
```jsonc
{ "version": 1, "device": "quest3s",
  "capture_space_anchor": "uuid",
  "fps": 30, "codec": "h264", "created_utc": "2026-07-20T...",
  "anchor_geo": { "lat0": null, "lon0": null, "rumbo_deg": 0.0, "escala": 1.0 },
  "domain": "incidencias" }   // etiqueta; NO acopla el módulo
```

> **Desacople:** el módulo no sabe de "incidencias" ni "estructuras". `anchor_geo` y
> `domain` los rellena quien invoca (§4). El campo `anchor_geo` es el puente a georref
> (§5.3): si trae `lat0/lon0`, el sidecar de pose se puede convertir al `track` de VisionRD.

### 3.4 Fuera de alcance del módulo (por diseño)
Detección, profundidad, reconstrucción, PostGIS, UI de negocio. Eso vive en VisionRD.

---

## 4. Subida e integración con IncidenciasRD **vía VisionRD** (Tarea 3)

> **Corrección importante respecto al brief (verificada en código):** el video **no** se
> adjunta a una incidencia de IncidenciasRD. La ruta real y de menor cambio es
> **Quest → VisionRD `/ingest`**; VisionRD ya publica a IncidenciasRD por su API pública.

### 4.1 Contrato real de VisionRD `/ingest` (verificado — `app/api/ingesta.py`)
```
POST /ingest        (multipart, 202 Accepted)
  video: UploadFile   # .mp4/.mov/.mkv/.avi   (tope INGESTA_MAX_VIDEO_MB)
  track: UploadFile   # .gpx o .json          (≤ 50 MB)
→ {"id": <trabajo_id>, "estado": "recibido"}
GET /trabajos/{id}  # estado, num_frames, num_detecciones, num_clusters
```
El `track` JSON aceptado (verificado — `app/gps.py`):
```jsonc
[ { "t": "2026-07-20T12:00:00Z", "lat": 18.48, "lon": -69.93 }, ... ]  // ≥ 2 puntos
```

### 4.2 Cómo llega IncidenciasRD (verificado — `app/publicar.py` + `routers/reports.py`)
VisionRD publica cada defecto con **`POST /api/reports`** (multipart, creación anónima),
mapeando su taxonomía a los **slugs exactos** de IncidenciasRD, mandando detecciones en
`ai_labels`/`bounding_boxes` y el fotograma como imagen. Respeta el **rate-limit 10/h por
IP**, el **rango RD** (lat 17.4–20.0, lon −72.0…−68.2) y el **dedup server-side**
(~500 m / 6 h / misma categoría → `deduplicated=true`, se vuelve upvote).
→ **El Quest no habla con IncidenciasRD.** Habla con VisionRD; IncidenciasRD queda intacto.

### 4.3 Flujo de subida desde el Quest
1. Capturar (§3) → artefacto §3.3.
2. Convertir `frames.jsonl` → `track.json` de §4.1 (georref por ancla, §5.3).
3. `POST /ingest` con `video.mp4` + `track.json`. **Resumable/chunked** (video grande +
   conectividad de campo); reintentos con backoff; cola offline-first si no hay red.
4. Poll `GET /trabajos/{id}` para estado.

### 4.4 Incógnita real de esta ruta
`/ingest` **exige** un `track` con lat/lon. El Quest **no tiene GPS**. La pose+ancla lo
resuelve (§5.3) **si** se conoce el `lat0/lon0` del sitio. De dónde sale ese ancla:
- Incidencia ya existente en IncidenciasRD → tomar su lat/lon como ancla del recorrido.
- O fijarlo manualmente en la app de captura antes de grabar.
Esto es **[DECISIÓN de producto]**, no un cambio de pipeline.

---

## 5. Mapeo al pipeline VisionRD (Tarea 4) — **verificado**

Pipeline real (README + `app/pipeline/*`, `app/api/*`):
```
/ingest → anonimizar (Ley 172-13: mock|egoblur)
        → fotogramas (mock|ffmpeg, 1–2 fps + interpolación GPS)
        → detectar   (mock|rfdetr|yolox, clases RDD2022 D00/D10/D20/D40)
        → georef     (snapping al segmento OSM más cercano; shapely/PostGIS)
        → dedup      (clustering por clase + radio ~8 m)
        → GET /defectos (GeoJSON) · publicar → IncidenciasRD
Reconstrucción 3D (RECON_MODE: mock|profundidad):
  POST /reconstrucciones (1..N fotos)  ·  POST /trabajos/{id}/reconstruir (desde frames)
        → profundidad monocular (torch+transformers) → retroproyección pinhole
        → nube 2.5D por escena → nube.ply → visor3d
```

**Hay DOS rutas de consumo; el Quest encaja distinto en cada una:**

### 5.1 Ruta "detección + georef" (defectos → IncidenciasRD)
- **Reutilizable sin cambios:** muestreo de fotogramas del `.mp4` (Quest da mp4 estándar),
  detección rfdetr/yolox, georef, dedup, publicación. **El Quest es una fuente de video más.**
- **Condición:** requiere `track` lat/lon (§4.4) y el georef **hace snapping a OSM** →
  pensado para **daño vial/urbano al aire libre**. Para interiores/estructuras el snapping
  a calles **no aplica** → esta ruta sirve al caso urbano, no al estructural.
- **Nota de dominio:** las clases son **daño vial RDD2022** (grietas/baches). Otras
  incidencias urbanas usan la taxonomía de IncidenciasRD vía `publicar.py`/`taxonomia.py`.

### 5.2 Ruta "reconstrucción 3D" (nube de puntos)
- **Mejor encaje del Quest.** `POST /reconstrucciones` / `/trabajos/{id}/reconstruir` toma
  fotos/fotogramas → **profundidad monocular** → nube 2.5D. **No requiere GPS/OSM.**
- VisionRD es **honesto sobre sus límites** (`docs/nubes-de-puntos.md`): "escala métrica
  aproximada, una nube POR ESCENA". → **Aquí es donde el Quest agrega valor único.**

### 5.3 Adaptaciones que habilita el Quest (mejoras, no rediseños)
- **Track sin GPS (georref por ancla):** convertir `pose 6DoF` → `[{t,lat,lon}]` con un
  **plano tangente local** (ancla `lat0/lon0` + rumbo + escala). Es una **función pura**
  (`escena/pose ⇄ lat/lon`, con validación de límites RD) — la misma idea que ya vive en
  la familia de utilidades georref y que el propio VisionRD valida a rango RD. Esto
  **destraba** la ruta §5.1 para el Quest sin tocar `/ingest`.
- **Intrínsecos reales (`K`):** usar los de PCA en la retroproyección pinhole de §5.2 en
  vez de valores asumidos → mejor precisión 2D→3D. Adaptación pequeña en `reconstruir.py`.
- **Escala/registro por pose:** usar la pose para fijar **escala métrica** y **fusionar
  vistas** → ataca directamente el "aproximado / una nube por escena". Adaptación en la
  reconstrucción (nuevo camino que consume el sidecar).

### 5.4 Nuevo (fase 2, opcional): `RECON_MODE=quest_depth`
Ingerir la **Depth API (Opción A)** del Quest como profundidad métrica real, sustituyendo
la estimación monocular en §5.2. Es un **nuevo backend de `reconstruir.generar()`** que
convive con `mock`/`profundidad`; no rompe las rutas existentes.

### 5.5 Regla de oro del acople
El artefacto §3.3 es el único contrato Quest↔mundo. **Day-1 VisionRD usa solo
`video.mp4` (+ `track.json` derivado)**; `frames.jsonl` es aditivo (se consume cuando la
reconstrucción esté lista para pose/intrínsecos). VisionRD sigue siendo **sidecar** de
IncidenciasRD (no toca su código); el Quest es **sidecar** de VisionRD (no toca el pipeline).

---

## 6. EstructurasRD como fase separada (Tarea 5)

- **No fusionar producto.** Mismo módulo de captura (§3), otro dominio: **levantamiento de
  campo** para estructuras.
- Reusa el artefacto §3.3 (`domain: "estructuras"`). Aún **no** se asumen requisitos.
- **Diferencia previsible:** el levantamiento estructural exige **precisión métrica y
  geometría**, no detección de baches ni snapping a OSM. → El encaje es la **ruta de
  reconstrucción 3D (§5.2)** llevada a métrica real, es decir **Opción A (Depth API) /
  `quest_depth` (§5.4)**, probablemente con fusión multi-vista. La ruta §5.1 (georef vial)
  **no aplica**.
- **Se prueba independientemente** del flujo IncidenciasRD/VisionRD.

---

## 7. Riesgos e incógnitas abiertas

| # | Riesgo / incógnita | Estado |
|---|---|---|
| 1 | Límites reales de PCA en 3S (resolución, fps, latencia, foreground) | **[VERIFICAR SDK]** — prototipo mínimo |
| 2 | Calidad de la Depth API para reconstrucción (baja-res) | **[VERIFICAR]** — medir antes de comprometer Opción A |
| 3 | Origen del ancla lat/lon sin GPS (§4.4) | **[DECISIÓN de producto]** — incidencia existente o fijado manual |
| 4 | Contrato `/ingest` (video+track) | **✅ verificado** en VisionRD |
| 5 | Integración a IncidenciasRD (`POST /api/reports`, rate-limit, RD, dedup) | **✅ verificado** en `publicar.py`/`reports.py` |
| 6 | Georref para interiores (OSM-snapping no aplica) | Usar ruta §5.2 (recon), no §5.1 |
| 7 | Deriva de tracking en recorridos largos | Anclas espaciales + tramos cortos |
| 8 | Privacidad al grabar RGB (Ley 172-13) | VisionRD ya anonimiza (egoblur); confirmar consentimiento en campo |

---

## 8. Próximos pasos sugeridos (cuando se reanude)

1. **Confirmar §2.4** (C+pose como MVP) o ajustar.
2. **Prototipo PCA mínimo** en Quest 3S: grabar 10 s de RGB + volcar `frames.jsonl`.
   Valida el riesgo #1 antes del módulo completo.
3. **Función pura pose→track** (`[{t,lat,lon}]` por ancla, límites RD): pequeña, testeable,
   destraba la ruta §5.1 sin tocar `/ingest`.
4. **Prueba end-to-end en mock:** subir un `video.mp4` + `track.json` sintéticos a un
   VisionRD en `*_MODE=mock` y ver el trabajo completarse (sin GPU, sin Quest).
5. **Decidir el ancla** (§4.4): incidencia existente vs fijado manual.
6. Recién entonces: plan por tareas (TDD donde aplique) del módulo §3, la subida §4 y las
   adaptaciones §5.3.

---

## 9. Relación con la visión existente

Aterriza `docs/vision/2026-06-13-vr-meta-fusion-vision.md` (MVP-A incidencias en MR) al
problema de **adquisición**: cómo entra el mundo real. Conecta con `VISION_ROADMAP.md`
(gemelo digital de RD): la reconstrucción 3D de VisionRD (§5.2), llevada a métrica con el
Quest (§5.4), es materia prima del sustrato geoespacial; y el puente VisionRD→nubes ya
lee IncidenciasRD para armar nubes por reporte. El Quest se inserta como **fuente de
captura** en una cadena **Quest → VisionRD → IncidenciasRD** que ya está construida y
verificada, sin fusionar los productos.
