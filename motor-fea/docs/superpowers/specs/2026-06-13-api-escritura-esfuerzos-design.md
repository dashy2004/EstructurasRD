# Diseño — API de escritura + esfuerzos por elemento (#1)

**Fecha:** 2026-06-13
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito.
**Motivación:** la API FastAPI (`api/servidor.py`) es hoy **read-only** sobre un modelo de
ejemplo hardcoded; y los esfuerzos internos por elemento —que el solver **ya calcula**
(`esfuerzos_elementos`, `EsfuerzosElemento.internos(t)`, `.diagrama(n)`)— **no se publican**.
Este item es la "llave maestra" del roadmap: convierte el visor-demo en app real (enviar tu
propio modelo) y desbloquea #2 (shell), #3 (diagramas) y #4 (vista en secciones).

El spec previo [`2026-06-05-esfuerzos-por-elemento-design.md`](2026-06-05-esfuerzos-por-elemento-design.md)
dejó explícitamente **fuera de alcance** "la serialización JSON / endpoint" (su §3 y §9, "Fase 4b").
Este spec cierra ese hueco.

---

## 1. Objetivo

1. **Endpoint POST** para analizar un modelo propio (contrato JSON ya existente en `api/contrato.py`).
2. **Exponer los esfuerzos internos por elemento** (fuerzas de extremo + diagrama) vía DTO/endpoint.

Sin tocar el cálculo (`core/solver.py`) ni la física: es serialización + capa frontera.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección | Razón |
|---|---|---|
| Semántica del POST | **Stateless** (cómputo puro: body→resultado en la misma respuesta) | Sin estado mutable de servidor, idempotente, trivial de testear. Los GET siguen sobre el ejemplo. |
| Contenido del DTO de esfuerzos | **Fuerzas de extremo + diagrama de `n` estaciones (configurable)** | Cubre el 100% del cálculo existente; habilita #3 (diagramas) directo. |
| Superficie de endpoints | **`GET /esfuerzos` + `POST /analizar`** | Lectura-demo (paralelo a `/resultados`) separada del cómputo-propio. |
| Convenciones de signo | **Exponer ambas** (extremo nodal crudo + esfuerzo interno de sección) | Sirven a usos distintos; documentadas en el DTO. |

## 3. Arquitectura (respeta el layering existente)

Toda la serialización nueva vive en la **capa 3** (`api/contrato.py`, "solo este módulo y el CLI
tocan I/O"). `api/servidor.py` queda como I/O delgado que solo orquesta y mapea errores a HTTP.
El **cálculo no se toca**: `core/solver.esfuerzos_elementos` ya existe y está verde.

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Serialización de esfuerzos | `api/contrato.py` (aditivo) | `esfuerzos_a_dict(modelo, resultado, n) → dict` |
| Pipeline de análisis completo | `api/contrato.py` (aditivo) | `analizar_completo_dict(modelo_dict, n) → {"resultados", "esfuerzos"}` |
| Endpoints | `api/servidor.py` (aditivo) | `GET /esfuerzos`, `POST /analizar` |
| Tests contrato | `tests/test_contrato.py` (aditivo) | serialización + pipeline |
| Tests servidor | `tests/test_servidor.py` (aditivo) | endpoints (200 / 400 / 422) |

`core/`, `viz/` y `normativa/` **no se tocan**. No se modifica `analizar_dict` existente
(puede tener consumidores): se añade `analizar_completo_dict` aparte.

## 4. Endpoints (stateless)

```
GET  /esfuerzos?n=11
POST /analizar?n=11        body = modelo JSON (esquema de contrato.py)
```

- **`GET /esfuerzos?n=11`** — esfuerzos del modelo de ejemplo fijado en `crear_app`. Devuelve el
  DTO de esfuerzos (§5). Paralelo a `/resultados`.
- **`POST /analizar?n=11`** — body = modelo JSON; devuelve `{"resultados": {...}, "esfuerzos": {...}}`,
  donde `resultados` = `resultado_a_dict` (desplazamientos + reacciones, ya existente) y `esfuerzos`
  = el DTO de §5. **No guarda estado**; los GET siguen sobre el ejemplo.

`n` se valida con `Query(default=11, ge=2)` → FastAPI responde **422** ante `n<2` antes de calcular
(coherente con el `n≥2` que ya exige `diagrama`).

## 5. DTO de esfuerzos (forma del JSON)

```jsonc
{
  "orden_componentes": ["N", "Vy", "Vz", "T", "My", "Mz"],   // documenta el orden de las 6 componentes
  "elementos": [
    {
      "id": 1,
      "longitud": 3.0,
      "extremo_i": [N, Vy, Vz, T, My, Mz],          // fuerza NODAL de extremo en i (f_local[0:6]), cruda
      "extremo_j": [N, Vy, Vz, T, My, Mz],          // fuerza NODAL de extremo en j (f_local[6:12]), cruda
      "diagrama": [[s, N, Vy, Vz, T, My, Mz], ...]  // n estaciones, s de 0 a L; esfuerzo INTERNO de sección
    }
  ]
}
```

- `elementos` recorre `modelo.elementos` en orden; uno por elemento.
- `diagrama` tiene exactamente `n` estaciones; `diagrama[0][0] == 0.0`, `diagrama[-1][0] == longitud`.

### Convenciones de signo (ambas, documentadas en el docstring del DTO)

- **`extremo_i` / `extremo_j`**: las fuerzas nodales locales estándar de rigidez directa
  (`f = kl·T·u`), tal cual los campos del dataclass `EsfuerzosElemento`. Son las fuerzas que los
  nodos ejercen sobre la barra. Útiles para equilibrio y diseño de conexiones.
- **`diagrama`**: convención de **esfuerzo interno de sección** de `internos(t)` (tracción +). Es la
  negación de la fuerza nodal en `s=0` (`internos(0) == −extremo_i`, componente a componente). Útil
  para diagramas P/V/M (#3).

Exponer ambas es intencional: `extremo_*` y `diagrama` responden a preguntas distintas. La relación
`internos(0) = −extremo_i` queda documentada y **testeada** (§8).

## 6. Funciones nuevas en `api/contrato.py`

```python
def esfuerzos_a_dict(modelo: ModeloEstructural, resultado: ResultadoAnalisis, n: int = 11) -> dict:
    """Serializa los esfuerzos por elemento a un dict JSON-able (DTO de §5)."""
    esf = esfuerzos_elementos(modelo, resultado)           # {id: EsfuerzosElemento}
    return {
        "orden_componentes": ["N", "Vy", "Vz", "T", "My", "Mz"],
        "elementos": [
            {
                "id": e.id,
                "longitud": esf[e.id].longitud,
                "extremo_i": list(esf[e.id].extremo_i),
                "extremo_j": list(esf[e.id].extremo_j),
                "diagrama": [list(fila) for fila in esf[e.id].diagrama(n)],
            }
            for e in modelo.elementos
        ],
    }


def analizar_completo_dict(modelo_dict: dict, n: int = 11) -> dict:
    """Pipeline dict→dict: deserializa, resuelve y serializa resultados + esfuerzos."""
    modelo = modelo_desde_dict(modelo_dict)
    resultado = resolver(modelo)
    return {
        "resultados": resultado_a_dict(resultado),
        "esfuerzos": esfuerzos_a_dict(modelo, resultado, n),
    }
```

(`esfuerzos_elementos` se importa desde `core.solver`, donde ya viven `resolver`/`ResultadoAnalisis`.)

## 7. Endpoints en `api/servidor.py`

```python
from fastapi import FastAPI, HTTPException, Query, Body
from motor_fea.api.contrato import analizar_completo_dict, esfuerzos_a_dict
from motor_fea.core.solver import resolver

@app.get("/esfuerzos")
def esfuerzos(n: int = Query(11, ge=2)):
    try:
        return esfuerzos_a_dict(modelo, resolver(modelo), n)
    except ValueError as ex:
        raise HTTPException(status_code=400, detail=str(ex))

@app.post("/analizar")
def analizar(modelo_dict: dict = Body(...), n: int = Query(11, ge=2)):
    try:
        return analizar_completo_dict(modelo_dict, n)
    except (ValueError, KeyError, TypeError) as ex:
        raise HTTPException(status_code=400, detail=f"Modelo inválido: {ex}")
```

## 8. Manejo de errores (mismo patrón que los endpoints actuales)

| Situación | Respuesta |
|---|---|
| Modelo inválido (`resolver` lanza `ValueError("Modelo inválido: …")`) | **400** con `detail` |
| JSON con claves faltantes / tipos malos (`KeyError`/`TypeError`/`ValueError` en `modelo_desde_dict`) | **400** con `detail` envuelto (`"Modelo inválido: …"`) |
| `n < 2` en query | **422** (validación de `Query(ge=2)`) |

## 9. Limitación conocida (documentada, fuera de alcance de #1)

`resolver` **suma todas las `cargas` sin distinguir `caso`** ("D"/"W"): construye un único vector de
cargas (`_vector_cargas`). Por tanto los esfuerzos devueltos son de **esa combinación sin factorar**.
Combinaciones de carga (D+W, factores LRFD) por endpoint quedan para un item futuro; se anota aquí y
**no se cambia** en este spec. (El endpoint `/diseno` ya hace combinaciones LRFD por su cuenta; este
camino crudo es deliberadamente el del análisis directo.)

## 10. Testing (TDD estricto — primero rojo)

Reusa convenciones existentes: `pytest.importorskip("fastapi"/"httpx")`, `TestClient`,
`modelo_ejemplo`, y para el contrato el round-trip `modelo_a_dict(modelo_ejemplo())`.

**`tests/test_contrato.py` (aditivo):**

| Qué | Esperado |
|---|---|
| `esfuerzos_a_dict` forma | claves `{orden_componentes, elementos}`; `len(elementos) == len(modelo.elementos)`; cada elemento con `{id, longitud, extremo_i, extremo_j, diagrama}` |
| longitud del diagrama | `len(diagrama) == n` (probar `n=11` default y un `n` distinto, p.ej. 5) |
| estaciones extremas | `diagrama[0][0] == 0.0`; `diagrama[-1][0] ≈ longitud` |
| coherencia de convenciones | `diagrama[0][1:] ≈ [−x for x in extremo_i]` (i.e. `internos(0) == −extremo_i`), componente a componente, 1e-9 |
| `analizar_completo_dict` | claves `{resultados, esfuerzos}`; `resultados` con `{n_gdl, desplazamientos, reacciones}`; round-trip: `analizar_completo_dict(modelo_a_dict(modelo_ejemplo()))` ≈ `GET /esfuerzos` del ejemplo |

**`tests/test_servidor.py` (aditivo):**

| Qué | Esperado |
|---|---|
| `GET /esfuerzos` | 200; estructura del DTO; `len(elementos) == 8` (4 col + 4 vigas) |
| `GET /esfuerzos?n=5` | 200; cada `diagrama` con 5 estaciones |
| `GET /esfuerzos?n=1` | 422 |
| `GET /esfuerzos` modelo inválido | 400 (modelo con refs inexistentes, como `test_escena_modelo_invalido_da_400`) |
| `POST /analizar` modelo de ejemplo serializado | 200; `{resultados, esfuerzos}`; coincide con los GET correspondientes |
| `POST /analizar` modelo inválido | 400 |
| `POST /analizar?n=1` | 422 |

**Criterio de aceptación:**

1. Suite Python verde (la actual + los nuevos tests de contrato/servidor).
2. `POST /analizar` con el modelo de ejemplo serializado reproduce exactamente los desplazamientos,
   reacciones y esfuerzos que dan `GET /resultados`/`GET /esfuerzos` del mismo modelo.

## 11. Archivos afectados

**Modificados (todo aditivo):**
- `src/motor_fea/api/contrato.py` (`esfuerzos_a_dict`, `analizar_completo_dict`; import de `esfuerzos_elementos`)
- `src/motor_fea/api/servidor.py` (endpoints `GET /esfuerzos`, `POST /analizar`; docstring del módulo)
- `tests/test_contrato.py`, `tests/test_servidor.py` (nuevos tests)

`core/`, `viz/`, `normativa/` y el resto **no se tocan**.

## 12. Roadmap habilitado (fuera de este spec)

| Item | Entrega | Reusa |
|---|---|---|
| #2 | shell de app (cargar/guardar modelo en el front) | `POST /analizar` |
| #3 | diagramas P/V/M en el visor | `diagrama` del DTO |
| #4 | vista en secciones | `internos(t)` por estación |
