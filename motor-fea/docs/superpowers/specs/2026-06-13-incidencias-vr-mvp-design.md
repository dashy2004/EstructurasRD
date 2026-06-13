# Diseño — Asistente de incidencias en VR (MVP-A)

**Fecha:** 2026-06-13
**Estado:** brainstorming aprobado (2026-06-13) + decisiones de diseño confirmadas en
esta sesión; spec escrito, pendiente de revisión y de `writing-plans`.
**Alcance de este documento:** el MVP-A de la visión `docs/vision/2026-06-13-vr-meta-fusion-vision.md`.
Recorrer una maqueta glTF del solar en VR (`immersive-vr`), colocar marcadores de
incidencia anclados en la escena, clasificarlos con IA asistida, e importar/exportar
por JSON con la plataforma **Incidencias RD** (acople flojo, por archivos).

> **Confidencialidad:** el source de Incidencias RD
> (`~/Documents/IncidenciasRD/...`) es **privado**. Aquí sólo se documenta la
> **frontera de interop** (forma del JSON que el MVP debe producir/consumir). No se
> copia código propietario; el acople es por archivos, no por código.

---

## 1. Objetivo

App WebXR (three.js, `immersive-vr`) servida por el `motor-fea` desde un router
FastAPI nuevo. El ingeniero recorre una maqueta 3D del solar, coloca marcadores de
incidencia anclados en la escena, la IA sugiere `categoría/severidad/acción` a partir
de una descripción, y las incidencias viajan por JSON hacia/desde Incidencias RD.

Es una **app hermana** del visor FEA existente: reusa el vendor three.js + el patrón
WebXR/VRButton + el servidor FastAPI + el patrón de IA local (`motor_fea_ia`), pero es
otro producto (otro router, otra app estática).

## 2. Decisiones tomadas

### 2.1 Brainstorming (2026-06-13)

| Tema | Decisión |
|---|---|
| Relación con Incidencias RD | Acople por archivos (import/export JSON), no API |
| Stack | WebXR reusando three.js de `motor-fea` |
| MR vs VR | VR pura (`immersive-vr`) primero — maqueta, sin passthrough |
| Rol de IA | Clasificación asistida: descripción → `{categoría, severidad, acción}` |
| Escena 3D | Importar glTF externo (Revit→glTF) como maqueta del solar |
| Dónde vive | Subproyecto en `motor-fea` |

### 2.2 Confirmaciones de esta sesión (tras leer el source de Incidencias RD)

| Fork | Decisión | Razón |
|---|---|---|
| **Coordenadas** | **Georreferenciar la maqueta** (ancla = origen lat/lng + rumbo + escala). El export lleva lat/lng reales válidos en RD. | Incidencias RD georreferencia por lat/lng con límites RD; un export drop-in los necesita. |
| **Motor IA** | **Pluggable**: por defecto **Ollama local** (offline, sin key, reusa el patrón `motor_fea_ia` + capa anti-inyección); **Claude opcional** por config. | Campo sin señal; consistencia con la casa; Claude cuando haya red/calidad. |
| **Contrato** | **Alinear al `Report`** de Incidencias RD (category/subcategory/severity/status/description/images/lat/lng…). `recursos[]` queda como extensión **VR-only** namespaced. | Export importable directo, sin adaptador de tirar. |
| Ubicación | Rama `engine/incidencias-vr-mvp` en EstructurasRD-engine. | Reusa infra; extraíble si crece. |
| 3ª plataforma | Fuera del MVP. | Acotar alcance. |

## 3. Hallazgos de Incidencias RD que condicionan el contrato

(Referencia de solo-lectura; no se copia código.)

- La entidad núcleo es **`Report`**, georreferenciada por **`latitude`/`longitude`**
  con **validación de límites RD** (lng ∈ `[-72.0, -68.2]`; lat dentro de RD).
- Taxonomía: **`category` + `subcategory`** (+ `fault_type` legacy), **`severity`**,
  `status`, `description`, `images[]`, `municipality`, `sector`.
- Ya existe **clasificación IA**: salida estructurada
  `{categoria, subcategoria, severidad, resumen, accion_sugerida, es_duplicado, es_sospechoso}`,
  validada estrictamente (lo que no cumpla el schema se descarta), contra **Ollama
  local** detrás de una **capa anti-inyección** (sanea el texto del usuario antes del LLM).
- **No hay** campo `recursos[]`/`equipos`; lo más cercano es `accion_sugerida` (string).
  → en el MVP, `recursos[]` es una **extensión VR-only**, no parte del round-trip.

## 4. Arquitectura

App estática nueva + router nuevo, reusando vendor + infra. Respeta la regla de capas
(el núcleo y la IA no tocan I/O; sólo `api/` + funciones puras de georref/contrato).

```
[glTF Revit→export]──carga──► three.js (immersive-vr + desktop)
                                   │  raycast → coloca marcador → ficha
                                   ▼
 static/incidencias/app.js ──fetch──► api/incidencias.py (router FastAPI)
        │  (pos escena x,y,z)            │
        │                               ├─ georref.py (PURA): escena⇄lat/lng
        │                               ├─ clasificador (pluggable): Ollama|Claude
        │                               └─ store JSON: load/save {incidencias,…}
        ▼                                        │
   marcadores VR                        [Incidencias RD] ◄─JSON import/export─►
```

### 4.1 `src/motor_fea/api/incidencias.py` — router (I/O delgado)

`APIRouter` montado en `crear_app()` junto al visor FEA. Endpoints:

- `GET  /incidencias/` → sirve la app estática (`static/incidencias/index.html`).
- `POST /api/incidencias/clasificar` → `{descripcion}` → clasificador → salida
  estructurada (§8). No expone API keys; saneo anti-inyección antes del LLM.
- `GET  /api/incidencias` → carga el JSON del store (import).
- `POST /api/incidencias` → guarda el JSON del store (export), validando el contrato
  y los límites RD de cada lat/lng derivada.

### 4.2 `src/motor_fea/viz/georref.py` — georreferencia (función PURA)

Convierte coordenadas de escena (metros, locales a la maqueta) ⇄ lat/lng, usando un
**ancla** (plano tangente ENU local alrededor del origen). Sin web, sin NumPy
obligatorio: se testea con asserts normales (round-trip e inversa).

```python
@dataclass
class Ancla:
    lat0: float; lon0: float      # origen del solar (grados)
    rumbo_deg: float = 0.0        # rotación del +Z de escena respecto al Norte
    escala: float = 1.0           # metros reales por unidad de escena (1.0 = maqueta 1:1)

def escena_a_geo(p_xyz, ancla: Ancla) -> tuple[float, float]: ...   # → (lat, lon)
def geo_a_escena(lat, lon, ancla: Ancla) -> tuple[float,float,float]: ...
```

Mapeo: el suelo de three.js es el plano `x–z` (`y` = arriba). Con θ = rumbo:
`este = (x·cosθ + z·sinθ)·escala`, `norte = (−x·sinθ + z·cosθ)·escala`;
`lat = lat0 + norte/111320`, `lon = lon0 + este/(111320·cos lat0)`. Inversa simétrica.
Valida resultado contra límites RD; fuera de rango → `ValueError`.

### 4.3 Clasificador pluggable (reusa el patrón `motor_fea_ia`)

Interfaz común + dos backends. Por defecto Ollama (como `motor_fea_ia.agente`, lib
`ollama`, modelo tool-calling/estructurado); Claude opcional (extra `anthropic`).

```python
class Clasificador(Protocol):
    def clasificar(self, descripcion: str) -> AnalisisIncidencia: ...

# OllamaClasificador(modelo=...)   ← default, offline
# ClaudeClasificador(modelo=...)   ← opcional, structured output; requiere ANTHROPIC_API_KEY
def crear_clasificador(config) -> Clasificador: ...   # elige por env/config
```

`AnalisisIncidencia` (pydantic, **validación estricta**) replica la forma de Incidencias
RD: `{categoria, subcategoria, severidad, resumen, accion_sugerida}`. El texto del
ingeniero se **sanea** (capa anti-inyección, espejo del enfoque de la plataforma) antes
de llegar al modelo; salida fuera de schema → se descarta.

### 4.4 `src/motor_fea/viz/static/incidencias/{index.html, app.js}` — visor VR

three.js vanilla por import-map (sin build), reusando `../vendor/three.module.js`,
`../vendor/addons/webxr/VRButton.js`, `../vendor/addons/controls/OrbitControls.js`, y
**`GLTFLoader` nuevo** en `../vendor/addons/loaders/`. Responsabilidades de `app.js`,
con **lógica pura separada y testeable** (raycast→pos, CRUD de marcador, serializar):

1. Cargar la maqueta glTF (URL configurable; por defecto la maqueta de ejemplo §4.6).
2. `immersive-vr` si hay WebXR (VRButton + teletransporte, escala 1:1); si no,
   `OrbitControls` (degradación elegante; mismo patrón que el visor FEA).
3. Raycast del control/ratón → posición en la escena → crear marcador (esfera/recuadro
   anclado) → ficha `{categoria, descripcion, severidad, recursos[]}`.
4. Botón **"Clasificar con IA"** → `POST /clasificar` → prerellena la ficha.
5. Editar/borrar marcador; **import/export** del store JSON.

### 4.5 Store / persistencia

Archivo JSON (§5) en una ruta del servidor (configurable por CLI/env). `GET`/`POST
/api/incidencias` cargan/guardan; el POST valida contrato + límites RD. El acople con
Incidencias RD es **manual por archivo** (exportar JSON aquí → importar allá, y vuelta).

### 4.6 Maqueta de ejemplo

glTF mínimo generado (solar plano + caja/estructura simple) en `static/incidencias/`
para no bloquear desarrollo ni el gate en el Quest. Sustituible por el export Revit real.

## 5. Contrato de datos (alineado a `Report`)

Raíz del archivo. Cada incidencia usa nombres de `Report`; `pos` (escena) y la
extensión VR viven en un bloque namespaced que Incidencias RD ignora.

```jsonc
{
  "version": 1,
  "georref": { "lat0": 18.4861, "lon0": -69.9312, "rumbo_deg": 0.0, "escala": 1.0 },
  "incidencias": [
    {
      "id": "uuid",
      "latitude": 18.48613,           // derivada de pos vía georref (válida en RD)
      "longitude": -69.93118,
      "category": "infraestructura_vial",
      "subcategory": null,
      "severity": "medium",           // low|medium|high|critical
      "description": "Grieta en muro de contención",
      "status": "pending",
      "images": [],
      "vr": {                          // extensión VR-only (Incidencias RD la ignora)
        "pos": { "x": 1.2, "y": 0.0, "z": -3.4 },   // escena (m)
        "recursos": ["cuadrilla albañilería", "epoxi inyectable"]
      }
    }
  ]
}
```

- **Export → Incidencias RD:** subset con los campos de `Report`/`ReportCreate`
  (lat/lng, category, subcategory, severity, description, status, images).
- **Import ← Incidencias RD:** se aceptan `Report`(Response/Create); `vr.pos` se
  recalcula con `geo_a_escena` si falta. Campos extra se conservan/ignoran sin romper.

## 6. Endpoints (resumen)

| Método | Ruta | Entrada | Salida |
|---|---|---|---|
| GET  | `/incidencias/` | — | app estática |
| POST | `/api/incidencias/clasificar` | `{descripcion}` | `AnalisisIncidencia` |
| GET  | `/api/incidencias` | — | `{version, georref, incidencias[]}` |
| POST | `/api/incidencias` | contrato §5 | `{ok, n}` o 400 con detalle |

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| lat/lng derivada fuera de límites RD | `georref`/POST lanza `ValueError` → HTTP 400 con detalle. |
| Descripción con intento de inyección | Capa de saneo la neutraliza; se registra; el LLM nunca recibe instrucciones del usuario. |
| Salida del LLM fuera de schema | Se descarta; `/clasificar` devuelve un análisis por defecto + flag, no rompe. |
| Ollama no disponible (campo) | `/clasificar` degrada: error claro y la ficha se llena a mano; el visor sigue. |
| `anthropic` no instalado y backend=Claude | Mensaje accionable (`pip install -e '.[ia]'`); cae a Ollama si está. |
| WebXR ausente | Visor degrada a `OrbitControls`; oculta el botón VR. |
| glTF no carga | Mensaje en pantalla, no pantalla en blanco. |

## 8. Clasificación IA (detalle)

- **Estructurada y estricta:** `AnalisisIncidencia` (pydantic). Ollama con
  formato/tool-calling como `motor_fea_ia`; Claude con structured output (tool/JSON
  schema) cuando backend=Claude.
- **Anti-inyección:** saneo del texto del ingeniero antes del prompt (espejo del
  enfoque de la plataforma; el texto del usuario nunca es instrucción del sistema).
- **Sin secretos en el front:** la key (si Claude) vive en env del servidor.

## 9. Testing (TDD; backend primero)

| Qué | Cómo | Notas |
|---|---|---|
| `viz/georref.py` | Tests puros: round-trip escena→geo→escena ≈ id; origen→(lat0,lon0); fuera de RD → `ValueError`; rumbo/escala. | stdlib pura, sin extras. |
| Clasificador | Ollama **mockeado** (como `test_ia.py`) y Claude mockeado: asserts sobre el parseo de la salida estructurada; saneo neutraliza payloads de inyección; salida mala → descarte. | `importorskip` para extras. |
| `api/incidencias.py` | `TestClient`: `/clasificar` (clasificador mockeado), round-trip `GET`/`POST /api/incidencias`, POST inválido (lat fuera de RD) → 400. | `importorskip("fastapi")`. |
| Frontend (lógica pura) | Funciones puras JS (raycast→pos, CRUD marcador, serializar contrato §5). | Sesión VR → gate humano. |
| Visor VR | Sin unit test. **Gate humano en el Quest** (§criterio). | three.js no se testea aquí. |

**Criterio de aceptación:**

1. `( cd motor-fea && .venv/bin/python -m pytest -q )` sigue verde (208 actuales + nuevos).
2. Con extra `api`: el server levanta; `/incidencias/` carga la maqueta; round-trip
   JSON conserva las incidencias; `/clasificar` (Ollama local) devuelve análisis válido.
3. **Gate Quest:** recorrer la maqueta en VR, colocar 1 incidencia, clasificarla con
   IA, exportar JSON e importarlo de vuelta (los marcadores reaparecen en su sitio).

## 10. Alcance / YAGNI

- **Dentro:** cargar glTF, CRUD de marcadores, georref escena⇄lat/lng, clasificación IA
  pluggable por texto, import/export JSON alineado a `Report`, VR + fallback desktop.
- **Fuera (diferido):** passthrough/MR, spatial anchors, foto/visión, sync en vivo con
  API de Incidencias RD, voz→texto, multiusuario, etapas 4D (MVP-B).

## 11. Archivos afectados

**Nuevos**
- `src/motor_fea/api/incidencias.py` (router)
- `src/motor_fea/viz/georref.py` (función pura)
- `src/motor_fea/viz/incidencias_clasificador.py` (interfaz + Ollama/Claude)
- `src/motor_fea/viz/static/incidencias/{index.html, app.js}`
- `src/motor_fea/viz/static/vendor/addons/loaders/GLTFLoader.js` (vendor)
- `src/motor_fea/viz/static/incidencias/maqueta_ejemplo.gltf`
- `tests/test_georref.py`, `tests/test_incidencias_clasificador.py`, `tests/test_incidencias_api.py`

**Modificados**
- `src/motor_fea/api/servidor.py` (montar el router) y/o `cli.py` (flag/ruta del store)
- `pyproject.toml` (extra `[ia]` con `anthropic` opcional; `ollama` ya documentado;
  incluir `static/incidencias/*` y el vendor nuevo como package-data)
- `README.md` (documentar la app de incidencias)

## 12. Siguiente paso

`writing-plans` → plan por tareas con TDD, en este orden: (1) `georref` puro →
(2) clasificador pluggable mockeado → (3) router `incidencias.py` + store →
(4) frontend three.js (glTF, marcadores, ficha, botón IA) → (5) `VRButton` + gate Quest.
Ejecución por `subagent-driven-development` en rama `engine/incidencias-vr-mvp`.
