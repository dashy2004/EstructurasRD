# Diseño — Fase 5B.1: flexión biaxial de columnas (motor)

**Fecha:** 2026-06-06
**Estado:** aprobado (modo autónomo — opción más completa); motor-first.
**Depende de:** 4b.1 (`aci318` P-M: `punto_interaccion`, `diagrama_interaccion`, `momento_capacidad`, `axial_maxima_diseno`), 5A.1 (`disenar_columna_combos`, demanda por combo).
**Alcance:** Fase 5B.1 — el **motor**: diseñar columnas a flexión **biaxial** (P-M-M) reemplazando el `Mu=max|My|,|Mz|` uniaxial por el contorno de carga de Bresler con barras de perímetro. Visor (My/Mz/utilización) = 5B.2.

---

## 1. Objetivo

`disenar_columna_combos` (5A.1) colapsa la flexión a `Mu=max(|My|,|Mz|)` (uniaxial, modelo de 2 capas). Las
columnas de esquina/borde flexionan en **dos** ejes a la vez. 5B.1 diseña biaxial: barras de perímetro,
capacidad P-M sobre cada eje, y el contorno `(Muy/φMny)^α + (Muz/φMnz)^α ≤ 1`.

## 2. Decisiones (modo autónomo, opción más completa)

| Decisión | Elección |
|---|---|
| Método | **Contorno de carga de Bresler** `(Muy/φMny)^α + (Muz/φMnz)^α ≤ 1`, **α=1.0** (lineal, conservador; el α de PCA 1.15–2.0 sería menos conservador — futuro). |
| Layout | **Barras de perímetro** (proyectadas a capas por eje), reemplaza el modelo de 2 capas para columnas. |
| Alcance | **Motor primero (5B.1)**; el visor mostrando My/Mz/utilización = 5B.2. |

## 3. Arquitectura

| Unidad | Archivo | Cambio |
|---|---|---|
| Primitivas biaxiales | `src/motor_fea/normativa/aci318.py` (aditivo) | `_perimetro_columna`, `_capas_biaxial`, `factor_biaxial`. |
| Orquestador | `src/motor_fea/diseno_elemento.py` (mod) | demanda biaxial por combo; `disenar_columna_combos` diseña biaxial; gobernante por utilización biaxial. |
| Tests | `tests/test_diseno_marco.py`, `tests/test_combinaciones_diseno.py` | nuevos + actualización de los uniaxiales. |

`core/`, `viz/`, `api/` **no se tocan** (5B.2 hará el visor).

## 4. Primitivas biaxiales (`aci318.py`, aditivo)

### 4.1 Posiciones de perímetro
```python
def _perimetro_columna(n, ox, oy) -> list[tuple[float, float]]
```
n posiciones `(py, pz)` equiespaciadas por longitud de arco en el perímetro de semi-ejes `(ox, oy)`,
empezando en `(-ox, -oy)` (misma lógica que `viz.armado._perimetro`, reimplementada acá para no depender de viz).

### 4.2 Capas por eje
```python
def _capas_biaxial(b, h, rec, num, n) -> tuple[list, list]
```
Reparte n barras `#num` en el perímetro (`ox=b/2−rec−Ø/2`, `oy=h/2−rec−Ø/2`); las agrupa por coordenada y
devuelve `(capas_y, capas_z)`:
- `capas_z`: agrupadas por `pz`, `di = h/2 − pz` (profundidad desde la fibra extrema en z) → **capacidad de My** (profundidad h, ancho b).
- `capas_y`: agrupadas por `py`, `di = b/2 − py` (profundidad desde la fibra extrema en y) → **capacidad de Mz** (profundidad b, ancho h).

### 4.3 Factor de utilización biaxial
```python
def factor_biaxial(pu, muy, muz, b, h, fc, fy, capas_y, capas_z, alfa=1.0) -> float
```
`φMny = momento_capacidad(pu, diagrama_interaccion(b, h, fc, fy, capas_z))`;
`φMnz = momento_capacidad(pu, diagrama_interaccion(h, b, fc, fy, capas_y))`;
devuelve `(muy/φMny)^α + (muz/φMnz)^α` (∞ si alguna capacidad ≤ 0). (pu, muy, muz en N, N·mm.)

## 5. Orquestador (`diseno_elemento.py`)

- **Demanda biaxial:** `_escalares_biaxial_por_caso → {caso: (P, My, Mz, V)}` (P con signo, My=max|My|, Mz=max|Mz|
  sobre la longitud, V=max|Vy|,|Vz|); `_demanda_biaxial_por_combo → {combo: (Pu, Muy, Muz, Vu)}` (combina cada
  componente con `combinaciones_resistencia`). Se mantiene `_demanda_por_combo` (uniaxial) para el estribo (Vu).
- **`disenar_columna_combos`** (rewrite del core P-M): por cada `n` (4→ρ8%), `capas_y,capas_z=_capas_biaxial`,
  y exige que **todos** los combos cumplan `pu ≤ axial_maxima_diseno` **y** `factor_biaxial(pu,|Muy|,|Muz|,…) ≤ 1`.
  El primer `n` que cubre todos es el diseño. `mu` (en `DisenoColumnaCombos`) se reporta como el momento
  resultante `√(Muy²+Muz²)` del combo gobernante (N·mm); el resto de los campos igual.
- **Gobernante biaxial:** `_gobernante_columna` pasa a usar `factor_biaxial` (mayor utilización; desempate por
  `pu`). El estribo (5C) y `combo_cortante` no cambian.

`DisenoColumnaCombos` **no cambia de forma** (sus campos `pu`/`mu` ya no los lee el visor; el visor lee la
demanda de `_demanda_por_combo`). 5B.2 agregará My/Mz/utilización al DTO.

## 6. Manejo de errores

| Situación | Respuesta |
|---|---|
| Ninguna sección (ρ≤8%) cubre el contorno | `cumple=False`, `disponer="SECCIÓN INSUFICIENTE"`, gobernante = mayor utilización. |
| φMny o φMnz ≤ 0 (pu fuera del diagrama) | `factor_biaxial=∞` → no cumple ese combo. |
| Params inválidos | `ValueError` (igual que hoy). |

## 7. Simplificaciones documentadas

- **α=1.0** (contorno lineal, conservador); el exponente de PCA (función de Pu/Po) = futuro.
- Barras de perímetro equiespaciadas por arco (no por requerimiento de separación real).
- `n_ramas`/cross-ties del estribo siguen como 5C.

## 8. Testing

| Qué | Casos |
|---|---|
| `aci318` (`test_diseno_marco.py`) | `_capas_biaxial`: una sección cuadrada simétrica da `capas_y`/`capas_z` simétricas; `factor_biaxial`: con `muz=0` se reduce al caso uniaxial (`muy/φMny`); biaxial (`muy=muz>0`) da utilización mayor que cada uniaxial; auto-consistente: un punto del diagrama con `muy=φMny, muz=0` da factor≈1. |
| `diseno_elemento` (`test_combinaciones_diseno.py`) | `disenar_columna_combos`: una columna con momento en **ambos** ejes (My y Mz por cargas en X e Y) requiere `n_barras ≥` que la misma con momento en un solo eje; retrocompat: momento en un solo eje reproduce ~el diseño uniaxial; `cumple`/insuficiencia coherentes. **Actualizar** los tests 5A.1 que asumían uniaxial (los `combo_gobernante`/`n_barras` pueden cambiar con perímetro+biaxial). |

**Criterio de aceptación:**
1. `PYTHONPATH=src:tests pytest -q` verde (~195 + ~6 ≈ 201; algunos tests 5A.1 actualizados); sin regresión en viga/estribo/4b/5A.2/5C.
2. `disenar_columna_combos` diseña biaxial (contorno de Bresler, barras de perímetro): una columna con My y Mz requiere más acero que uniaxial.

## 9. Roadmap (fuera de este spec)

| Fase | Entrega |
|---|---|
| 5B.2 | `/diseno` + visor mostrando My/Mz y la utilización biaxial del combo gobernante. |
| 5D | panel UI fc/fy/recubrimiento. |
| Futuro | exponente α de PCA; compatibilidad de deformaciones biaxial real (eje neutro inclinado). |

## 10. Archivos afectados

**Modificados**
- `src/motor_fea/normativa/aci318.py` (aditivo: `_perimetro_columna`, `_capas_biaxial`, `factor_biaxial`)
- `src/motor_fea/diseno_elemento.py` (demanda biaxial + `disenar_columna_combos` biaxial + gobernante)
- `tests/test_diseno_marco.py`, `tests/test_combinaciones_diseno.py`

`core/`, `viz/`, `api/` **no se tocan**.
