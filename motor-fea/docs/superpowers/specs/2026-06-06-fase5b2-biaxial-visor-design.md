# Diseño — Fase 5B.2: flexión biaxial en el visor

**Fecha:** 2026-06-06
**Estado:** aprobado (modo autónomo — opción más completa); end-to-end.
**Depende de:** 5B.1 (`disenar_columna_combos` biaxial, `aci318.factor_biaxial`/`_capas_biaxial`, `_demanda_biaxial_por_combo`), 5A.2/5C (`viz/diseno`, etiqueta del visor).
**Alcance:** exponer la flexión biaxial — la columna muestra **My y Mz por separado** y la **utilización biaxial** del combo gobernante en `/diseno` y la etiqueta.

---

## 1. Objetivo

5B.1 diseña columnas biaxial internamente, pero el DTO/visor sigue mostrando una sola demanda escalar
(`_demanda_por_combo`, `mu=max|My|,|Mz|`). 5B.2 surfacea `Muy`, `Muz` y la **utilización** biaxial
`(Muy/φMny)+(Muz/φMnz)` del combo gobernante, en la columna.

## 2. Decisiones (modo autónomo)

| Decisión | Elección |
|---|---|
| Qué muestra la columna | **My, Mz y la utilización biaxial** del combo gobernante (la viga sigue mostrando Mu/Vu). |
| Dónde sale el dato | El **motor** lo reporta: `DisenoColumnaCombos` gana `muy`, `muz`, `utilizacion`; el visor los lee (no recomputa). |
| Alcance | **End-to-end** (motor + `/diseno` + etiqueta). |

## 3. Arquitectura

| Unidad | Archivo | Cambio |
|---|---|---|
| Resultado del diseño | `src/motor_fea/diseno_elemento.py` (mod) | `DisenoColumnaCombos` gana `muy: float`, `muz: float` (N·m), `utilizacion: float`; `disenar_columna_combos` los puebla con el combo gobernante. |
| DTO | `src/motor_fea/viz/diseno.py` (mod) | el elemento de columna gana `muy`, `muz` (N·m) y `utilizacion`; la viga los trae en 0. |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | etiqueta de columna: `Pu=… kN, My=… Mz=… kN·m (u=0.85)`. |

`core/`, `normativa/aci318.py`, `viz/armado.py` **no se tocan**.

## 4. Motor (`diseno_elemento.py`)

`DisenoColumnaCombos` agrega tres campos al final (después de `combo_cortante`):
```python
    combo_cortante: str
    muy: float          # N·m (combo gobernante)
    muz: float          # N·m
    utilizacion: float  # (Muy/φMny + Muz/φMnz) en la sección diseñada
```
En `disenar_columna_combos`, en cada `return` (con `gob` ya calculado y `capas_y,capas_z,pmax` de la sección final):
- `muy, muz = abs(biax[gob][1]), abs(biax[gob][2])` (N·m, del combo gobernante).
- `utilizacion = aci318.factor_biaxial(dem[gob][0], dem[gob][1], dem[gob][2], b_mm, h_mm, fc, fy, capas_y, capas_z)`
  (en la sección elegida — `≤1` si cumple, `>1` si insuficiente).

## 5. DTO (`viz/diseno.py`)

El elemento de columna gana:
```jsonc
{ …, "muy": 30000.0, "muz": 25000.0, "utilizacion": 0.85, … }
```
- columna: `muy = d.muy`, `muz = d.muz`, `utilizacion = d.utilizacion`.
- viga: `muy = 0.0`, `muz = 0.0`, `utilizacion = 0.0` (la etiqueta de viga no los usa).
La `demanda={pu,mu,vu}` se mantiene (no rompe los tests existentes).

## 6. Visor (`app.js`)

`mostrarDiseno`: para columna, en vez de `Pu=… Mu=…`, muestra `Pu=${kN(pu)} kN, My=${kN(muy)} Mz=${kN(muz)} kN·m (u=${u.toFixed(2)})`; la viga queda igual (`Mu/Vu`). El `combo` y el `estribo_txt` se mantienen.

## 7. Manejo de errores

Sin cambios respecto a 5A.2/5C (modelo inválido / params → 400). Columna insuficiente: `utilizacion > 1` y
`cumple=False`, rojo en el visor.

## 8. Testing

| Qué | Casos |
|---|---|
| `diseno_elemento` (`test_combinaciones_diseno.py`) | `disenar_columna_combos` trae `muy≥0`, `muz≥0`, `utilizacion>0`; una columna que cumple tiene `utilizacion≤1`; biaxial (My y Mz) tiene `muy>0` **y** `muz>0`. |
| `viz` (`test_diseno_visual.py`) | el DTO de columna trae `muy`/`muz`/`utilizacion`; viga los trae en 0; con carga en X **e** Y, una columna tiene `muy>0` y `muz>0`. |
| `servidor` (`test_servidor.py`) | `/diseno` columnas con `utilizacion` presente. |
| Visor JS | smoke — etiqueta de columna con `My=… Mz=… (u=…)`. |

**Criterio de aceptación:**
1. Suite verde (~199 + ~3 ≈ 202); sin regresión.
2. `/diseno` reporta `muy`, `muz`, `utilizacion` por columna; la etiqueta los muestra.

## 9. Roadmap (fuera de este spec)

| Fase | Entrega |
|---|---|
| 5D | panel UI fc/fy/recubrimiento editable + `/diseno` con query params. |
| Futuro | exponente α de PCA; combo de cortante de viga en la etiqueta; zonas `lo` de confinamiento. |

## 10. Archivos afectados

**Modificados**
- `src/motor_fea/diseno_elemento.py` (`DisenoColumnaCombos` + muy/muz/utilizacion)
- `src/motor_fea/viz/diseno.py` (DTO de columna)
- `src/motor_fea/viz/static/app.js` (etiqueta de columna)
- `tests/test_combinaciones_diseno.py`, `tests/test_diseno_visual.py`, `tests/test_servidor.py`

`core/`, `normativa/`, `viz/armado.py` **no se tocan**.
