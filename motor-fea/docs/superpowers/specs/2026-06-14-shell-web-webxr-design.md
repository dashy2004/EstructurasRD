# Diseño — Shell web/WebXR: cargar + analizar + visualizar un modelo propio (#2)

**Fecha:** 2026-06-14
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito.
**Motivación:** hoy el visor (`viz/static/app.js`) solo lee el **modelo de ejemplo** del server
vía GET (`/escena`, `/resultados`, `/losa`, `/armado`, `/diseno`). No hay forma de ver un modelo
propio. #1 entregó `POST /analizar`; #2 convierte el demo en **app real**: el usuario carga su
modelo, se analiza, y el visor lo pinta — reusando los DTOs que ya sabe renderizar.

Construye sobre #1 ([`2026-06-13-api-escritura-esfuerzos-design.md`](2026-06-13-api-escritura-esfuerzos-design.md))
y habilita #3 (diagramas) y #4 (vista en secciones).

---

## 1. Alcance (MVP confirmado)

**Dentro:** cargar un modelo propio (JSON del contrato) en el front, enviarlo al server, y
renderizar geometría + deformada + modos, con un readout mínimo de esfuerzos al hacer pick.
Guardar = descargar el JSON del modelo cargado.

**Fuera (confirmado):** editor de modelo en navegador; losa/armado/diseño LRFD sobre modelo custom;
persistencia más allá de descargar JSON; diagramas P/V/M completos (eso es #3).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Alcance | Cargar + analizar + visualizar (sin editor). |
| Entrada del modelo | **Ambos**: botón "cargar archivo" (.json) **y** textarea para pegar JSON. |
| Origen de la geometría/DTOs del modelo custom | **Server**: un `POST /visor` que reusa `exportar_escena` + `calcular_resultados` + esfuerzos y devuelve los mismos DTOs que pinta el visor (paridad visual, cambio de front mínimo). |
| Overlays del ejemplo (losa/armado/diseño) en modo-custom | **Ocultarlos**: el modo-custom muestra solo sin-deformar/deformada/modos/esfuerzos. El modo-ejemplo inicial conserva todo. |
| Esfuerzos en #2 | **Pick readout mínimo** (axial + \|M\|máx al tocar un elemento). Los diagramas completos son #3. |

## 3. Arquitectura

Un endpoint server nuevo alimenta al visor con los **mismos DTOs** que ya pinta, pero para un
modelo propio. El front gana una **shell de carga** (`shell.js`) y un **modo-custom**; el cálculo
(`core/`) no se toca; la lógica de render de `app.js` se reusa vía un refactor dirigido (no reescritura).

```
┌─ index.html ──────────────────────────────┐
│  controles shell (cargar / pegar / guardar)│
│  panel existente (estado, exag, diseño)    │
└───────────────┬────────────────────────────┘
   shell.js     │  app.js (render three.js/WebXR)
  (UI de carga) │  ├─ limpiarEscena()  ← nuevo (teardown)
   ── POST ──►  │  ├─ renderEscena(bundle) ← extraído de cargar()
  /visor        │  ├─ modo-ejemplo: GET escena/resultados/losa/armado/diseno + /esfuerzos
                │  └─ modo-custom:  POST /visor → renderEscena
                ▼
   servidor.py: POST /visor → contrato.visor_dict → viz (exportar_escena, calcular_resultados) + esfuerzos
```

## 4. Server (TDD, mismo patrón aditivo que #1)

### 4.1 `contrato.visor_dict(modelo_dict, n=11) -> dict`
Pipeline dict→dict que compone los DTOs del visor para un modelo propio:

```python
def visor_dict(modelo_dict: dict, n: int = 11) -> dict:
    """Pipeline dict→dict: los DTOs que el visor necesita para un modelo propio."""
    modelo = modelo_desde_dict(modelo_dict)
    return {
        "escena": exportar_escena(modelo),
        "resultados": calcular_resultados(modelo),
        "esfuerzos": esfuerzos_a_dict(modelo, resolver(modelo), n),
    }
```

`exportar_escena` y `calcular_resultados` se importan de `motor_fea.viz.escena` / `motor_fea.viz.resultados`.
Es una dependencia **hacia abajo** desde la frontera JSON (sin ciclo: `viz` no importa `contrato`).
`esfuerzos_a_dict` y `resolver` ya están en `contrato`.

**Formas de los DTOs reusados (no se modifican):**
- `exportar_escena(modelo)` → `{"unidades", "bbox":{min,max}, "nodos":[{id,p}], "barras":[{id,i,j,tipo,b,h}], "losas":[]}`
- `calcular_resultados(modelo, n_modos=3)` → `{"deformada":{desplazamientos, factor_sugerido}, "modos":[{indice, periodo, forma, factor_sugerido}]}`
- `esfuerzos_a_dict` → `{"orden_componentes", "elementos":[{id, longitud, extremo_i, extremo_j, diagrama}]}`

### 4.2 `POST /visor?n=11` en `servidor.py`
Wrapper delgado, registrado **antes** del `app.mount("/", StaticFiles…)`:

```python
@app.post("/visor")
def visor(modelo_dict: dict = Body(...), n: int = Query(11, ge=2)):
    try:
        return visor_dict(modelo_dict, n)
    except (ValueError, KeyError, TypeError) as ex:
        raise HTTPException(status_code=400, detail=f"Modelo inválido: {ex}")
```

### 4.3 Limitación conocida (documentada)
`calcular_resultados` y `esfuerzos_a_dict` resuelven el modelo por separado → **2 solves** por request.
Aceptable para el MVP (modelos pequeños). Compartir un único solve es optimización futura; no se hace aquí.

## 5. Front

### 5.1 `shell.js` (nuevo, una sola responsabilidad)
Construye y cablea la UI de carga: botón **"cargar archivo"** (`<input type=file accept=.json>` leído con
`FileReader`), **textarea para pegar**, botón **"analizar"**, y una zona de estado/errores. Flujo:
texto JSON (de archivo o textarea) → `JSON.parse` → `POST /visor` → en éxito invoca un callback
`onModelo(bundle, modeloJson)`; en error muestra el mensaje y no toca la escena. Expone también el
"guardar": descargar el `modeloJson` actual como `.json` (Blob + `<a download>`).

### 5.2 `app.js` (refactor dirigido — extraer, no reescribir)
De la función `cargar()` actual se extraen dos piezas reutilizables:

- **`limpiarEscena()`** — teardown para cambiar de modelo: quita de la escena las barras, `losaMesh`,
  `armadoGroup`, `disenoGroup` (con `dispose` de geometrías), vacía los arreglos/estado (`barras`,
  `basePos`, `resultados`, `losa`, `armado`, `diseno`, flags de overlay) y reconstruye el `<select>`
  dejando solo `sin-deformar`.
- **`renderEscena({escena, resultados, esfuerzos})`** — construye barras + habilita deformada/modos
  desde los DTOs (el código de render actual de `cargar`/`cargarResultados`, parametrizado por el
  bundle en vez de `fetch` hardcodeado). Guarda `esfuerzos` en el estado para el pick.

Dos caminos de entrada:
- **modo-ejemplo** (arranque, comportamiento actual): GET `escena/resultados/losa/armado/diseno`
  **+ GET `/esfuerzos`** (de #1) → `renderEscena` (+ overlays losa/armado/diseño como hoy).
- **modo-custom** (al cargar un modelo): `limpiarEscena()` → `POST /visor` → `renderEscena`. El
  `<select>` solo recibe sin-deformar/deformada/modos (sin losa/armado/diseño).

### 5.3 `index.html`
Añadir los controles de la shell (un botón "cargar modelo" que despliega archivo + textarea de pegado,
y un botón "descargar .json"). Mantener el panel existente. CSS mínimo en el mismo `<style>` inline.

## 6. Esfuerzos — pick readout mínimo

`resumenEsfuerzos(id)` con `esfuerzos.elementos` (indexado por `id`):
- **axial** = `-extremo_i[0]` (tracción +) → etiqueta "tracción"/"compresión".
- **\|M\|máx** = máximo sobre el `diagrama` de `max(|My|, |Mz|)` (componentes 5 y 6 de cada estación).

Extiende el handler `pointerdown` existente (mismo patrón que el pick de diseño): al intersectar una
barra, busca su `id` y muestra en `#info`: `"N = X kN (compresión) · |M|máx = Y kN·m"`. Disponible en
ambos modos (ejemplo y custom), porque ambos cargan `esfuerzos`.

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| Modelo inválido (estructura mala, refs colgantes) | `POST /visor` → 400 con `detail`; la shell muestra el mensaje y **conserva la escena actual** (no rompe el visor). |
| Archivo ilegible / JSON malformado | La shell lo captura (`try/catch` en `JSON.parse`/`FileReader`) y muestra error; no postea. |
| `n` del visor | Queda en su default (11); no se expone al usuario en #2. (`Query(ge=2)` → 422 solo es alcanzable por API directa.) |

## 8. Testing

### 8.1 Server (TDD estricto — primero rojo)
**`tests/test_contrato.py` (aditivo):**
- `visor_dict`: claves `{escena, resultados, esfuerzos}`; `escena` con `{unidades, bbox, nodos, barras, losas}`; `resultados` con `{deformada, modos}`; `esfuerzos` con `{orden_componentes, elementos}`. Coherencia: `esfuerzos` == `esfuerzos_a_dict(modelo, resolver(modelo))` directo.

**`tests/test_servidor.py` (aditivo):**
- `POST /visor` modelo de ejemplo serializado → 200; claves `{escena, resultados, esfuerzos}`; `escena.barras` y `esfuerzos.elementos` con 8 elementos; round-trip: `escena` coincide con `GET /escena`, `esfuerzos` coincide con `GET /esfuerzos`.
- `POST /visor` modelo inválido → 400.
- `POST /visor?n=1` → 422.
- (Regresión) `index.html` se sigue sirviendo en `/`.

**Criterio de aceptación:** suite Python verde; `POST /visor` del ejemplo serializado reproduce
`GET /escena` y `GET /esfuerzos` del mismo modelo.

### 8.2 Front (verificación manual — sin inventar infra de tests JS)
El proyecto no tiene runner JS (es Python). **No se añade uno (YAGNI).** Checklist manual:
1. Arranque: el modelo de ejemplo renderiza idéntico a hoy (barras, deformada, modos, losa, armado, diseño).
2. Cargar un `.json` de modelo válido → la escena se reconstruye con esa geometría + deformada/modos; sin overlays de ejemplo.
3. Pegar el mismo JSON en el textarea + "analizar" → mismo resultado.
4. JSON malformado o modelo inválido → mensaje de error en la shell; la escena previa se conserva.
5. Pick de un elemento → `#info` muestra `N = … (tracción/compresión) · |M|máx = …`.
6. "Descargar .json" → baja el modelo cargado.

## 9. Archivos afectados

**Crear:**
- `src/motor_fea/viz/static/shell.js`

**Modificar:**
- `src/motor_fea/api/contrato.py` (`visor_dict` + imports de `viz`)
- `src/motor_fea/api/servidor.py` (`POST /visor`, docstring)
- `src/motor_fea/viz/static/app.js` (extraer `limpiarEscena`/`renderEscena`, modo-custom, pick esfuerzos, GET `/esfuerzos` en modo-ejemplo)
- `src/motor_fea/viz/static/index.html` (controles de la shell + botón descargar)
- `tests/test_contrato.py`, `tests/test_servidor.py` (tests nuevos del server)

`core/`, `normativa/` y el resto de `viz/` (escena/resultados/etc.) **no se tocan**.

## 10. Roadmap habilitado

| Item | Reusa de #2 |
|---|---|
| #3 diagramas P/V/M | `esfuerzos` ya en el estado del front + modo-custom |
| #4 vista en secciones | geometría custom + `esfuerzos.diagrama` |
