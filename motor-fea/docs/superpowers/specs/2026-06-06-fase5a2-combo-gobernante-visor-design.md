# Diseño — Fase 5A.2: combo gobernante en el visor

**Fecha:** 2026-06-06
**Estado:** aprobado en brainstorming (2 decisiones), pendiente de revisión del spec.
**Depende de:** 5A.1 (`core/casos.esfuerzos_por_caso`, `diseno_elemento.disenar_*_combos`, `_demanda_por_combo`) y 4b.2 (`viz/diseno`, estado `diseño` del visor).
**Alcance:** Fase 5A.2 — cerrar el lazo: el visor muestra el armado diseñado por **combinaciones LRFD** con el **combo gobernante** por elemento.

---

## 1. Objetivo

5A.1 metió las combinaciones en el motor (`disenar_*_combos`). 5A.2 las expone: `/diseno` pasa a diseñar por
combos y el visor muestra, al tocar un elemento, el **combo gobernante** y su **demanda factorada**. El modelo
de ejemplo se enriquece con casos (D + W) para que los combos se vean de verdad.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Ejemplo | **Gravedad D + lateral W**: la lateral `fx=10kN` existente pasa a caso `W`; se agrega gravedad `fz=−40kN` caso `D` en los nodos superiores. |
| Etiqueta | **Combo gobernante + demanda factorada**: `"8#6 · combo 4 · Pu=240 kN, Mu=85 kN·m · cumple"`. |

## 3. Arquitectura

| Unidad | Archivo | Cambio |
|---|---|---|
| Empaquetado | `src/motor_fea/viz/diseno.py` (mod) | `calcular_diseno` usa el pipeline de combos; agrega `combo` al DTO; la demanda = la del combo gobernante (factorada). |
| Ejemplo | `src/motor_fea/api/servidor.py` (mod) | `modelo_ejemplo`: lateral → `W`, + gravedad `D`. |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | `mostrarDiseno` muestra el combo; resumen LRFD al entrar. |

Reusa 5A.1 íntegro (`esfuerzos_por_caso`, `disenar_viga/columna_combos`, `_demanda_por_combo`). `/diseno` no
cambia de firma (llama a `calcular_diseno`). `core/`, `normativa/`, `aci318` **no se tocan**.

## 4. `calcular_diseno` (nuevo flujo, `viz/diseno.py`)

```python
def calcular_diseno(modelo, fc=21.0, fy=420.0, recubrimiento=0.04) -> dict
```
1. Validación igual que hoy (`fc/fy/rec>0`, `modelo.validar()`). `epc = esfuerzos_por_caso(modelo)` (un análisis
   por caso).
2. Por elemento: `(b,h)=_dimensiones`; chequeo de recubrimiento; `esf_por_caso = {caso: epc[caso][e.id] for caso in epc}`.
   - **columna:** `d = diseno_elemento.disenar_columna_combos(esf_por_caso, b, h, fc, fy, rec)`;
     `combo = d.combo_gobernante`; `long = armado._posiciones_columna(b, h, rec, d.numero_barra, d.n_barras)`;
     estribo por la regla ACI 25.7.2.1 (igual que hoy); `tipo,designacion,cumple = "columna", d.disponer, d.cumple`.
   - **viga:** `d = diseno_elemento.disenar_viga_combos(...)`; `combo = d.combo_flexion`;
     `num = d.flexion.numero_barra si d.flexion si no 5`, `n_inf = d.flexion.n_barras si d.flexion si no 2`;
     `long = armado._posiciones_viga(...)`; `s = d.estribo.espaciamiento/1000`; `cumple = d.cumple`.
3. **Demanda factorada del combo gobernante:** `dem = diseno_elemento._demanda_por_combo(esf_por_caso)`;
   `pu,mu,vu = dem[combo]`; `demanda = {"pu": abs(pu), "mu": abs(mu), "vu": abs(vu)}` (N, N·m, N). Se toma de
   `_demanda_por_combo` (siempre N/N·m) para evitar el mismatch de unidades de los dataclasses (columna `mu` en
   N·mm vs viga `mu` en N·m).
4. DTO por elemento: igual que 4b.2 **+ `"combo": combo`**.

**Borde:** sin cargas → `epc={}` → `esf_por_caso={}` → `disenar_*_combos({})` diseña mínimo y
`_demanda_por_combo({})` da combos en cero → `combo` = el primer combo ("1"), `demanda` en cero. Igual que hoy
para elementos sin fuerza.

## 5. Contrato `DisenoDTO` (5A.2)

```jsonc
{ "recubrimiento": 0.04,
  "elementos": [
    { "id":1,"i":1,"j":5,"tipo":"columna",
      "long":[ {"x":0.10,"y":0.10,"d":0.019} ], "estribo":{ "d":0.0095,"s":0.30,"w":0.22,"h":0.22 },
      "designacion":"8#6",
      "demanda":{ "pu":240000.0,"mu":85000.0,"vu":15000.0 },   // del combo gobernante (factorada)
      "combo":"4",                                              // combo gobernante (1.2D+1.0W)
      "cumple":true } ] }
```
Cambio vs 4b.2: nuevo campo `combo`; `demanda` pasa de ser del análisis crudo a la del **combo gobernante**.

## 6. Visor (`app.js`)

`mostrarDiseno(el)` antepone el combo:
```javascript
info.textContent = `${el.designacion} · combo ${el.combo} · ${dem} · ${el.cumple ? 'cumple' : 'NO cumple'}`;
```
(`dem` = `Pu/Mu` en kN para columna, `Mu/Vu` en kN para viga, como en 4b.2). `entrarDiseno` muestra
`"diseño LRFD — N/M cumplen"`. El resto del estado `diseño` (jaula coloreada por cumple, picking) **no cambia**.

## 7. Modelo de ejemplo (`servidor.py`)

El loop actual `for n in (5,6,7,8): m.cargas.append(CargaNodal(n, fx=10000.0))` pasa a:
```python
    for n in (5, 6, 7, 8):
        m.cargas.append(CargaNodal(n, fz=-40000.0, caso="D"))   # gravedad
        m.cargas.append(CargaNodal(n, fx=10000.0, caso="W"))    # viento
```
Aparecen combos como **4 (1.2D+1.0W)** y **1 (1.4D)** gobernando según el elemento. Las demás rutas no dependen
del caso: `/escena` es geometría; `/resultados` corre `resolver` sobre **todas** las cargas sumadas (vista de
servicio combinada); `/losa` es independiente; `/armado` es geometría.

## 8. Manejo de errores

| Situación | Respuesta |
|---|---|
| Modelo inválido / `fc,fy,rec ≤ 0` / recubrimiento incompatible | `ValueError` → HTTP 400 (igual que 4b.2). |
| Elemento sin fuerza en ningún caso | Diseño mínimo, `combo="1"`, demanda en cero. |
| Sección insuficiente (algún combo) | `cumple=False`; rojo en el visor; la etiqueta muestra el combo gobernante (el más sobrecargado). |

## 9. Testing

| Qué | Cómo |
|---|---|
| `viz/diseno.py` | `test_diseno_visual.py`: los tests existentes siguen (el `_portico()` todo-D ahora pasa por combos → `combo=="1"`); **+** un test con D+W → cada elemento tiene `combo` no vacío y, donde W gobierna, `combo` contiene un combo con W (p.ej. "4"). El campo `demanda` sigue con `pu/mu/vu ≥ 0`. |
| `api/servidor.py` | `test_servidor.py`: `/diseno` → `e["combo"]` presente; `modelo_ejemplo` (2 casos) sigue dando 8 elementos; `test_escena`/`test_resultados`/`test_losa` siguen verdes (cambian números, no forma). |
| Visor JS | Smoke: la etiqueta muestra `combo N`; el ejemplo D+W luce combos ≠ "1". |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (~188); sin regresión en las rutas/visor de Fases 2-4b.
2. `GET /diseno` devuelve, por elemento, el armado diseñado + `combo` gobernante + `demanda` factorada de ese combo.
3. En el visor: tocar un elemento muestra `"… · combo N · …"`; el ejemplo enriquecido (D+W) muestra combos reales.

## 10. Roadmap (fuera de este spec)

| Fase | Entrega |
|---|---|
| 5B / 5C / 5D | flexión biaxial; estribos de columna + confinamiento; panel UI fc/fy/rec. |
| Futuro | sismo auto-distribuido (caso E desde `sismo.py`); combinación per-station; mostrar el combo de cortante de viga además del de flexión. |

## 11. Archivos afectados

**Modificados**
- `src/motor_fea/viz/diseno.py` (pipeline de combos + `combo` en el DTO)
- `src/motor_fea/api/servidor.py` (`modelo_ejemplo`: D + W)
- `src/motor_fea/viz/static/app.js` (combo en la etiqueta)
- `tests/test_diseno_visual.py` (+test de combos)
- `tests/test_servidor.py` (combo en `/diseno`)

`core/`, `normativa/` y `viz/armado.py` **no se tocan**.
