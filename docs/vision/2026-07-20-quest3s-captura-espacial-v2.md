# Quest 3S — Visor 3D + Ingesta multi-fuente + Export estructural (Brief v2)

> **Continuación de** `2026-07-20-quest3s-captura-espacial-evaluacion.md` (v1).
> **Fecha:** 2026-07-20. **Rama:** `claude/meta-quest-3-extension-bmiy4u`.
> **Rol de este documento (Opus 4.8):** cerrar las decisiones **que bloquean el resto**
> — visor 3D, **contrato de ingesta multi-fuente** y ruta "ya-en-3D" — verificadas contra
> el código real de VisionRD/IncidenciasRD. El código (prototipo PCA, `pose→track`,
> exportadores, visor front) queda asignado a Fable 5 / Antigravity según §7.

## 0. Qué decide este v2 (resumen)

1. **Visor 3D (Capa 1):** **web Three.js como superficie única para *ver*** (celular +
   navegador Quest + escritorio), extendiendo el `visor3d.html` que VisionRD **ya tiene**.
   **Nativo Quest = solo para *capturar*** (PCA), nunca para ver. Ver §2.
2. **Ingesta multi-fuente (Capa 2):** todo converge en `Reconstruccion` + **PLY canónico**.
   La bifurcación ocurre **solo en la entrada**: imágenes → reconstrucción (`generar()`);
   **ya-en-3D → import directo (`importar_nube()`), sin reconstrucción**. Ruta nueva
   `POST /reconstrucciones/importar`, **no** `/ingest`. Ver §3–§4.
3. **Export estructural (Capa 3):** EstructurasRD exporta el **mismo PLY canónico** (+
   `.las/.laz`) que Blender MCP ya consume. Ver §5.

---

## 1. Anclas verificadas en el código (base de las decisiones)

- **Visor web ya existe:** `visionrd/dashboard/visor3d.html` — Three.js `PLYLoader` +
  `OrbitControls`, selector de reconstrucción, carga `/reconstrucciones/{id}/nube.ply`.
  (Hoy importa `three` desde unpkg; para offline/Quest hay que **vendorizarlo**, igual que
  EstructurasRD ya hizo con `viz/static/vendor/three.module.js`.)
- **PLY canónico** (`visionrd/app/pipeline/ply.py`): binario LE, `x/y/z float32 +
  red/green/blue uint8`. Declarado interoperable con **Three.js, CloudCompare, Potree,
  PDAL, MeshLab** — y Blender lo importa nativo. Es el **formato pivote** de todo.
- **Subsistema de reconstrucción** (`app/api/reconstrucciones.py`, `models.py`): entidad
  `Reconstruccion{origen, modo, num_fuentes, num_puntos, lat, lon, estado, nube_url}`.
  Dos orígenes hoy: `"fotos"` (`POST /reconstrucciones`) y `"trabajo"`
  (`POST /trabajos/{id}/reconstruir`). Ambos → `procesar_reconstruccion` → `generar()` →
  PLY → `GET /reconstrucciones/{id}/nube.ply`. **Todo lo downstream ya es genérico.**
- **`generar()`** (`pipeline/reconstruir.py`) hoy: `RECON_MODE ∈ {mock, profundidad}`;
  `profundidad` = monocular (torch+transformers), "escala aproximada, una nube por escena".

---

## 2. Capa 1 — Visor 3D en IncidenciasRD (decisión: web, no nativo)

### 2.1 Decisión
**Una sola superficie de visualización: web Three.js**, extendiendo `visor3d.html`.
- **Celular / perito / ajustador:** navegador normal (OrbitControls, `PLYLoader`).
- **Quest 3S / XR:** el **mismo** visor con una sesión **WebXR `immersive-vr`** opcional
  (VRButton) para recorrer la nube a escala. Sin app nativa para ver.
- **Nativo Quest queda reservado a la *captura* (PCA, v1 §2).** Ver ≠ capturar.

### 2.2 Por qué (criterio del brief: código compartido + tres superficies)
- El visor web **ya existe** en VisionRD y **ya corre en las tres superficies** (un
  navegador es un navegador). Cero reescritura de base.
- **Comparte stack** con: `visor3d.html` (VisionRD), el visor WebXR de EstructurasRD
  (`viz/static/`, mismo `three.module.js` vendorizado + `VRButton`) y el concepto
  FOL-Visor-XR. Un solo `PLYLoader`, un solo patrón de degradación VR→órbita.
- WebXR **para ver** no tiene el bloqueo de v1: ese bloqueo era para **captar RGB** (no hay
  cámara en WebXR). Para **mostrar** una nube, WebXR sobra.

### 2.3 Alcance
- **Dentro:** modo 3D **por reporte** — cargar la nube de esa reconstrucción, anclada a su
  ubicación, navegable en celular y en VR. Integrar como tercer modo junto al mapa 2D.
- **Fuera (diferido, ya lo marcaba el brief):** "mapa arriba" que junta todos los reportes
  en una sola escena (eso es Fase M / 3D Tiles del `VISION_ROADMAP`).
- **Trabajo de habilitación:** vendorizar Three.js + `PLYLoader` + `VRButton` en VisionRD
  (offline/CSP), y exponer el visor embebible desde el front de IncidenciasRD apuntando a
  la `nube_url` del reporte. **[Antigravity]** hace la integración front con preview.

### 2.4 Contrato que consume el visor (ya existe, no cambia)
`GET /reconstrucciones` (lista) · `GET /reconstrucciones/{id}` (incluye `nube_url`,
`lat`, `lon`, `reporte_id`) · `GET /reconstrucciones/{id}/nube.ply`. El visor solo
necesita la `nube_url` y, para XR, la escala métrica (mejora con Quest, §v1 5.3).

### 2.5 Cómo cada reporte obtiene su nube — feature **video/foto → point cloud** (ya existe)
El visor por-reporte (§2.1) no crea nubes: **consume** las que produce la cadena de
reconstrucción de VisionRD, que ya está construida. Tres formas de que un reporte tenga nube:

- **Puente automático IncidenciasRD → nubes** (`visionrd/app/puente.py`, `BRIDGE_MODE=poll`,
  **verificado**): lee `GET /api/reports` (GeoJSON público, sin auth), toma las **fotos del
  reporte** (`properties.images` servidas en `/storage`), **dedup por `reporte_id`**, y crea
  **una `Reconstruccion{reporte_id=...}` por reporte** vía `procesar_reconstruccion`. Acotado
  por `BRIDGE_MAX_POR_CORRIDA` (la GPU en modo profundidad es el recurso caro). Manual:
  `POST /puente/correr`. → **Este es el feature "reporte → point cloud" para el mapa.**
- **Reconstrucción directa** (`POST /reconstrucciones`, fotos; o `POST /trabajos/{id}/reconstruir`,
  fotogramas de una pasada de `/ingest`): la ruta **video → point cloud** propiamente dicha —
  el video entra por `/ingest`, se extraen fotogramas, y de ahí sale la nube (§v1 5.2).
- **Import ya-en-3D** (`POST /reconstrucciones/importar`, §4): para `.ply/.las/.laz`.

**Enlace visor↔reporte (la clave para "este mapa"):** la `Reconstruccion` guarda
`reporte_id`, y el visor ya soporta `visor3d.html?reporte={id}` con **enlace de vuelta a
`/incidencia/{id}`**. Entonces el **tercer modo 3D por reporte** (§2.1) es exactamente:
*desde un pin del mapa 2D → abrir el visor con la nube cuyo `reporte_id` coincide*. No hay
que inventar el vínculo: `reporte_id` + `?reporte=` ya existen.

**Estado hoy vs. objetivo:**
- El puente reconstruye desde **fotos** del reporte. El objetivo v1/v2 es que la fuente sea
  **video** (teléfono o Quest) → nube más densa (§v1: la foto única no basta). Cuando el
  reporte lleve video, el puente/`/ingest` alimentan la misma `Reconstruccion{reporte_id}` →
  **el visor no cambia**; solo mejora la nube que carga.
- **[VERIFICAR en IncidenciasRD]** hoy `POST /api/reports` acepta `images: List[UploadFile]`
  (fotos). Para el flujo video-nativo por reporte hay que confirmar/añadir soporte de
  **adjunto de video** en el reporte (o mantener el video del lado VisionRD vía `/ingest`,
  asociándolo por `reporte_id`), decisión que toca IncidenciasRD → ver §8.5.

---

## 3. Capa 2 — Contrato de ingesta multi-fuente (decisión central)

### 3.1 Principio
**Un solo destino canónico** (`Reconstruccion` + PLY x/y/z+rgb) al que llegan **todas** las
fuentes. **La bifurcación ocurre solo en la entrada**, según si la geometría *ya está
resuelta* o *hay que derivarla*. Nada downstream se duplica.

```
                       ┌─ imágenes (derivar geometría) ──► generar()  [depth/recon]
 fuentes ──► entrada ──┤                                                  │
                       └─ ya-en-3D (geometría resuelta) ─► importar_nube()│
                                                                          ▼
                                    Reconstruccion + PLY canónico ──► nube.ply ──► visor3d
```

### 3.2 Clasificación de fuentes

| Fuente | ¿Geometría resuelta? | Ruta | Entra por |
|---|---|---|---|
| Video teléfono (RGB) | No → reconstruir | `generar()` | `/reconstrucciones` o `/ingest`(+track) |
| Quest RGB+pose (MVP, v1 C+pose) | No → reconstruir (pose ayuda escala) | `generar()` | `/reconstrucciones` (o `/ingest` para detección) |
| Dashcam con depth | Parcial (depth asistida) | `generar()` variante | `/reconstrucciones` |
| Quest Depth API (fase 2) | Casi (depth métrico) | `generar()` `quest_depth` | `/reconstrucciones` |
| **LiDAR `.las`/`.laz`** | **Sí** | **`importar_nube()`** | **`/reconstrucciones/importar`** |
| **`.ply` ya-nube** | **Sí** | **`importar_nube()`** | **`/reconstrucciones/importar`** |

### 3.3 Decisión: ya-en-3D entra por ruta nueva, **no** por `/ingest`
`/ingest` es semánticamente *"video + track → detección + georef + dedup"*. Un `.ply`/`.las`
**no tiene video, ni frames, ni detección, ni track**. Forzarlo por `/ingest` obligaría a
falsear un track y a bifurcar TODO el pipeline de detección. En cambio, **una nube es una
reconstrucción** → pertenece al subsistema `/reconstrucciones` como un tercer `origen`
junto a `"fotos"` y `"trabajo"`. Bifurcación mínima, coherente con el modelo existente.

> Cumple el criterio del brief: *no re-derivar geometría ya resuelta* (el import **salta**
> `generar()`) y *minimizar bifurcación* (un solo paso de entrada nuevo; downstream intacto).

---

## 4. Ruta "ya-en-3D" — especificación (para implementar en [Opus] paso 4)

### 4.1 Endpoint nuevo
```
POST /reconstrucciones/importar        (multipart, 202 Accepted)
  archivo: UploadFile   # .ply | .las | .laz     (tope RECON_IMPORT_MAX_MB, nuevo setting)
  lat: float | None     # opcional; misma validación RD que /reconstrucciones
  lon: float | None
→ {"id": <recon_id>, "estado": "recibido"}
```
- Reusa `_validar_extension` con `_EXT_NUBE = {".ply", ".las", ".laz"}`.
- Crea `Reconstruccion(origen="importada", modo="importado", num_fuentes=1, lat, lon)`.
- Guarda con `_guardar_upload` (chunked) en `.../reconstrucciones/{id}/fuente.<ext>`.
- Encola `procesar_importacion` (BackgroundTask, gemelo de `procesar_reconstruccion`).

### 4.2 Nuevo paso puro `importar_nube(ruta) -> NubePuntos`
Convierte la fuente al **PLY canónico** sin re-derivar geometría:
- **`.ply`** → **lector** en `pipeline/ply.py` (hoy solo tiene `escribir`; añadir `leer`,
  stdlib+numpy, sin deps nuevas). Normaliza a `x/y/z float32 + rgb uint8` (si no trae
  color, gris por defecto).
- **`.las`/`.laz`** → `laspy` (dep opcional nueva, `requirements-nubes.txt`; `.laz` requiere
  `lazrs`/`laszip`). Extrae XYZ (+ RGB si existe, escalando 16→8 bit). Reproyección de CRS
  **[DECISIÓN]**: LiDAR suele venir en UTM/geográficas; mapear a los ejes del visor
  (X derecha, Y arriba, −Z al frente) y fijar `lat/lon` desde el header o el form.
- Acota a `RECON_MAX_PUNTOS` (reusa `_submuestrear`) y escribe el PLY con `ply.escribir`.
- `procesar_importacion` setea `num_puntos`, `estado="completada"` → **el resto del sistema
  (nube.ply, listar, visor3d) funciona sin cambios**.

### 4.3 Reglas
- **No detección, no georef-OSM, no dedup** para importadas (no aplican a una nube ya hecha).
- Validación de límites RD sobre `lat/lon` si vienen (consistencia con el resto).
- Errores de parseo → `estado="error"`, `error=<detalle>` (patrón existente).

### 4.4 Convergencia con Quest
- **MVP Quest** sigue por `generar()` (RGB+pose, v1). No es "ya-en-3D".
- **Quest Depth API (fase 2):** si el device produce *depth* → `generar()` `quest_depth`
  (v1 §5.4). Si en el futuro produce una *nube* directa → entra por `/reconstrucciones/importar`
  como cualquier `.ply`. El contrato ya lo cubre.

---

## 5. Capa 3 — EstructurasRD como laboratorio de export (decisión)

- **Flujo:** levantamiento caminando con Quest 3S → reconstrucción **métrica** (Depth API /
  `quest_depth`, v1 §5.4; aquí la precisión sí importa) → **export a Blender MCP**.
- **Formato de export = PLY canónico** (Blender lo importa nativo) **+ `.las/.laz`** vía
  `laspy` para el flujo LiDAR. Es **el mismo `ply.py` + el mismo conversor** de §4.2, en
  sentido de escritura. → **cero formato nuevo**; el pivote PLY sirve import y export.
- **Simetría útil:** `importar_nube()` (§4.2) y el exportador comparten el lector/escritor
  PLY y el conversor `.las`. Implementarlos juntos evita duplicar la serialización.
- **Caso separado del flujo urbano:** distinto dominio (estructura, no bache), distinto
  requisito (métrica real, no aproximada), se prueba solo. **[Fable]** hace los exportadores.
- Requisitos finos de EstructurasRD **siguen sin definir**: no se asumen aquí.

---

## 6. Cambios concretos en VisionRD que implican estas decisiones

> Resumen accionable (para quien implemente). VisionRD sigue siendo **sidecar** de
> IncidenciasRD (no toca su código); estos cambios son **dentro de VisionRD** salvo la
> integración front, que es **dentro de IncidenciasRD**.

| # | Cambio | Archivo(s) | Modelo |
|---|---|---|---|
| a | Lector PLY canónico (`leer`) | `app/pipeline/ply.py` | Fable |
| b | `importar_nube()` (.ply; .las/.laz vía laspy) | nuevo `app/pipeline/importar.py` | Fable |
| c | Endpoint `POST /reconstrucciones/importar` + `procesar_importacion` | `app/api/reconstrucciones.py` | Opus (contrato) → Fable (impl) |
| d | `origen="importada"`, settings `RECON_IMPORT_MAX_MB`, `requirements-nubes.txt` (laspy) | `models.py`/`settings.py` | Fable |
| e | Vendorizar Three.js + `PLYLoader` + `VRButton`; WebXR en `visor3d.html` | `dashboard/` | Antigravity |
| f | Embeber el visor 3D por reporte en el front de IncidenciasRD | IncidenciasRD front | Antigravity |
| g | Backend `quest_depth` en `generar()` (fase 2) | `pipeline/reconstruir.py` | Fable |
| h | Exportadores PLY/LAS para EstructurasRD | EstructurasRD / util compartida | Fable |

---

## 7. División de modelos y orden (del brief, con dependencias afinadas)

1. **[Opus 4.8] ✅ (este doc)** — decisión visor (§2) + contrato ingesta multi-fuente (§3–§4).
   *Desbloquea el resto.*
2. **[Fable 5]** Prototipo PCA (10 s RGB + `frames.jsonl`) + `pose→track` puro (límites RD).
3. **[Fable 5]** E2E contra VisionRD en `*_MODE=mock` (video+track sintéticos; sin GPU/Quest).
4. **[Fable 5, contrato ya fijado en §4]** Ruta ya-en-3D: lector PLY + `importar_nube` +
   endpoint `importar` (cambios a–d). *Nota: el brief lo asignaba a Opus por ser
   toca-pipeline; con el contrato §4 cerrado, el riesgo baja y es implementable por Fable;
   **si surge ambigüedad de CRS/laspy (§4.2), escalar a Opus**.*
5. **[Antigravity]** Visor 3D en IncidenciasRD (cambios e–f), tras existir ≥1 nube de prueba.
6. **[Fable 5]** Exportadores Blender MCP para EstructurasRD (cambio h).
7. **[Fable 5, fase 2]** `quest_depth` (cambio g), tras validar la Depth API en hardware.

> Recordatorio del brief: Fable redirige <5% a Opus por safeguards → solo en tareas
> reintentables (2–4, 6–7), nunca en ruta crítica no-recuperable. Kimi 3 fuera hasta validar.

---

## 8. Pendiente de decisión humana (ningún modelo lo resuelve)

1. **Ancla lat/lon del track sintético del Quest** (v1 §4.4): input manual, selección en
   mapa, o desde la incidencia existente. Bloquea la ruta detección §v1 5.1 en interiores.
2. **CRS de importación LiDAR** (§4.2): a qué marco se normalizan `.las/.laz` (UTM 19N RD
   vs geográficas) y cómo se fija el `lat/lon` cuando el header no lo trae.
3. **Copia del documento en VisionRD:** v1 y v2 viven en EstructurasRD. La integración vive
   en VisionRD. **Recomendación:** copiar ambos a `visionrd/docs/` (o un enlace) para que
   quien implemente §6 los tenga junto al código; decisión tuya.
4. **"Mapa arriba" (todos los reportes en una escena):** confirmar que sigue fuera de alcance
   (es Fase M del `VISION_ROADMAP`, 3D Tiles/Cesium), no parte de este visor por-reporte.
5. **Video en el reporte de IncidenciasRD (§2.5):** hoy `POST /api/reports` acepta solo
   fotos (`images`). Decidir si el reporte gana un **adjunto de video** (toca IncidenciasRD)
   o si el video vive del lado VisionRD (entra por `/ingest`, se asocia por `reporte_id`) y
   IncidenciasRD solo guarda un fotograma/portada. Afecta cómo el puente pasa de fotos a
   video como fuente de la nube.

---

## 9. Estado para el reanude post-reset

- **Repos en sesión:** `estructurasrd`, `incidenciasrd`, `visionrd` (clonados).
- **Decisiones que desbloquean:** cerradas en §2–§5. Lo que sigue es código (Fable/Antigravity)
  + las 4 decisiones humanas de §8.
- **Formato pivote confirmado:** PLY canónico de `visionrd/app/pipeline/ply.py` — import,
  export, visor y Blender MCP giran todos alrededor de él.
