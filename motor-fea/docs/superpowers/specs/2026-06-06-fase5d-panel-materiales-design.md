# Diseño — Fase 5D: panel de materiales editables (fc/fy/recubrimiento)

**Fecha:** 2026-06-06
**Estado:** aprobado (modo autónomo — opción más completa); end-to-end.
**Depende de:** 4b.2/5A.2/5C/5B.2 (`/diseno`, estado `diseño` del visor, `calcular_diseno(modelo, fc, fy, rec)`).
**Alcance:** editar `f'c`, `fy` y recubrimiento desde el visor y re-diseñar en vivo (endpoint `/diseno` con query params).

---

## 1. Objetivo

Hoy `/diseno` usa `fc=21, fy=420, rec=0.04` fijos. 5D permite al usuario cambiarlos desde un panel del visor y
**re-diseñar** (la jaula, el armado, los estribos y la utilización se recalculan con los nuevos materiales).

## 2. Decisiones (modo autónomo)

| Decisión | Elección |
|---|---|
| Entrada | **3 inputs numéricos** (f'c MPa, fy MPa, recubrimiento m) + botón **"rediseñar"** en el panel. |
| Endpoint | `/diseno` acepta **query params** `fc`, `fy`, `rec` (defaults 21/420/0.04). |
| Re-diseño | Al "rediseñar": re-fetch `/diseno?fc=…&fy=…&rec=…`, **reconstruir** la jaula `diseño` (dispose + rebuild) y refrescar si el estado activo es `diseño`. |

## 3. Arquitectura

| Unidad | Archivo | Cambio |
|---|---|---|
| Endpoint | `src/motor_fea/api/servidor.py` (mod) | `/diseno` gana query params `fc/fy/rec`. |
| Panel | `src/motor_fea/viz/static/index.html` (mod) | fila con inputs `fc/fy/rec` + botón `redisenar`. |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | leer inputs, fetch con params, reconstruir `disenoGroup`. |

`core/`, `normativa/`, `diseno_elemento.py`, `viz/diseno.py`, `viz/armado.py` **no se tocan** (la lógica de
diseño ya parametriza fc/fy/rec).

## 4. Endpoint (`servidor.py`)

```python
@app.get("/diseno")
def diseno(fc: float = 21.0, fy: float = 420.0, rec: float = 0.04):
    try:
        return calcular_diseno(modelo, fc, fy, rec)
    except ValueError as ex:
        raise HTTPException(status_code=400, detail=str(ex))
```
(FastAPI parsea los query params; valores inválidos → `calcular_diseno` lanza `ValueError` → 400, como hoy.)

## 5. Panel (`index.html`)

Una fila nueva en `#panel` (antes del `#info`):
```html
    <div class="fila">
      <label for="fc">f'c</label><input type="number" id="fc" value="21" min="1" step="1" style="width:3.2em">
      <label for="fy">fy</label><input type="number" id="fy" value="420" min="1" step="10" style="width:3.8em">
      <label for="rec">rec</label><input type="number" id="rec" value="0.04" min="0.01" step="0.005" style="width:3.8em">
      <button id="redisenar" type="button">rediseñar</button>
    </div>
```

## 6. Visor (`app.js`)

- `fetchDiseno()`: lee `fc/fy/rec` de los inputs (con defaults), arma `./diseno?fc=…&fy=…&rec=…`, devuelve el JSON.
- `cargarDiseno()`: usa `fetchDiseno()` (en vez del `fetch('./diseno')` fijo); agrega el estado `diseño` la primera vez.
- `disposeDiseno()`: `scene.remove(disenoGroup)` + dispone las geometrías (evita leaks al reconstruir).
- `redisenar()`: `nuevo = await fetchDiseno()`; en error → `#info` muestra el detalle y NO rompe. Si OK:
  `diseno = nuevo`, `disposeDiseno()`, `disenoGroup = construirJaula(diseno, cumple→MAT_OK/MAT_FALLA)`; si
  `disenoActivo`, `entrarDiseno()` (refresca jaula + resumen). El botón `redisenar` llama a `redisenar()`.

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| `fc/fy/rec` inválidos (≤0, recubrimiento incompatible) | `/diseno` → 400; `redisenar()` muestra el error en `#info` y mantiene la jaula previa. |
| Input vacío / NaN | default (21/420/0.04) vía `parseFloat(...) || default`. |

## 8. Testing

| Qué | Casos |
|---|---|
| `servidor` (`test_servidor.py`) | `GET /diseno?fc=35&fy=500&rec=0.05` → 200, 8 elementos; `GET /diseno?fc=-1` → 400; `GET /diseno` (sin params) sigue dando 200 con defaults. |
| Visor JS | smoke — cambiar f'c a 35, "rediseñar"; la jaula/armado se recalcula; valores inválidos muestran error sin romper. |
| Suite | sin regresión (la lógica de diseño no cambia). |

**Criterio de aceptación:**
1. Suite verde (~201 + ~2 ≈ 203); sin regresión.
2. `GET /diseno?fc=&fy=&rec=` re-diseña con esos materiales; sin params usa los defaults.
3. El panel del visor tiene inputs f'c/fy/rec y un botón "rediseñar" que reconstruye la jaula con los nuevos valores.

## 9. Cierre de Fase 5

Con 5D, el motor de diseño está **completo**: análisis FEA 3D → esfuerzos por elemento → combinaciones LRFD →
diseño de vigas (flexión + cortante) y columnas (P-M-M biaxial + estribos con cortante-axial + confinamiento
sísmico) → losas → todo visible y parametrizable en el visor WebXR.

## 10. Archivos afectados

**Modificados**
- `src/motor_fea/api/servidor.py` (`/diseno` con query params)
- `src/motor_fea/viz/static/index.html` (panel de materiales)
- `src/motor_fea/viz/static/app.js` (fetch con params + reconstrucción)
- `tests/test_servidor.py` (params de `/diseno`)

`core/`, `normativa/`, `diseno_elemento.py`, `viz/diseno.py`, `viz/armado.py` **no se tocan**.
