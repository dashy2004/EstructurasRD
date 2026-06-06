# Diseño — Fase 5C: estribos de columna + confinamiento sísmico

**Fecha:** 2026-06-06
**Estado:** aprobado (modo autónomo — opción más completa); pendiente de revisión inline.
**Depende de:** 4b.1 (`aci318` cortante/P-M), 5A.1 (`disenar_columna_combos`, `_demanda_por_combo`), 4b.2/5A.2 (`viz/diseno`, visor).
**Alcance:** Fase 5C **end-to-end** — diseño del estribo de columna (cortante con axial + confinamiento ACI 18.7.5) en el motor, el `/diseno` y el visor.

---

## 1. Objetivo

Hoy la columna no tiene estribo diseñado: `disenar_columna_combos` no lo calcula y el visor usa la regla de
detallado 25.7.2.1 como placeholder. 5C lo diseña de verdad: cortante de columna (Vc con axial, ACI §22.5.6.1)
**y** confinamiento sísmico (Ash, ACI §18.7.5.4), tomando el más exigente; lo expone en `/diseno` y la etiqueta
del visor.

## 2. Decisiones (modo autónomo, opción más completa)

| Decisión | Elección |
|---|---|
| Contenido | **Cortante + confinamiento sísmico** (estribo completo). |
| Alcance | **End-to-end** (motor + `/diseno` + visor) en un spec. |

## 3. Arquitectura

| Unidad | Archivo | Cambio |
|---|---|---|
| Primitivas ACI | `src/motor_fea/normativa/aci318.py` (aditivo) | `cortante_concreto_columna`, `confinamiento_ash`, `disenar_estribo_columna` + `DisenoEstriboColumna`. |
| Orquestador | `src/motor_fea/diseno_elemento.py` (mod) | `disenar_columna_combos` diseña el estribo; `DisenoColumnaCombos` gana `estribo` + `combo_cortante`. |
| Empaquetado | `src/motor_fea/viz/diseno.py` (mod) | el estribo de columna del DTO sale del diseño real (no de la heurística). |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | la etiqueta de columna muestra el estribo (`E#3@100`). |

`core/` no se toca.

## 4. Primitivas ACI (`aci318.py`, aditivo)

### 4.1 Vc con axial (§22.5.6.1 / §22.5.7.1)
```python
def cortante_concreto_columna(bw, d, fc, nu, ag, lam=1.0) -> float
```
`Nu` axial (N, **compresión +**). Compresión: `factor = 1 + Nu/(14·Ag)`; tracción (`Nu<0`):
`factor = max(0, 1 + Nu/(3.5·Ag))`. `Vc = 0.17·factor·λ·√fc·bw·d`.

### 4.2 Confinamiento (§18.7.5.4)
```python
def confinamiento_ash(s, bc, fc, fyt, ag, ach) -> float
```
`ratio = max(0.3·(Ag/Ach − 1)·(fc/fyt), 0.09·(fc/fyt))`; devuelve `Ash_req = s·bc·ratio` (mm²).

### 4.3 Diseñador del estribo de columna
```python
@dataclass(frozen=True)
class DisenoEstriboColumna:
    numero_barra: int; n_ramas: int; espaciamiento: float   # mm
    av: float; ash_provista: float                          # mm²
    vs_requerido: float                                     # N
    cumple: bool; disponer: str; gobierna: str             # "cortante"|"confinamiento"|"detallado"

def disenar_estribo_columna(vu, pu, b, h, fc, db_long, recubrimiento,
                            fyt=420.0, n_ramas=2, lam=1.0) -> DisenoEstriboColumna
```
(`vu, pu` en N; `b, h, db_long, recubrimiento` en mm.) **Escala la barra del estribo (#3→#4→#5)** hasta que
el Ash provisto confine a la separación mínima práctica; si ni #5 confina, devuelve #5 con `cumple=False`.
Algoritmo (por cada barra candidata):
1. `d = h − rec`; `bc = b − 2·rec`; `Ag = b·h`; `Ach = (b−2·rec)·(h−2·rec)`; `Av = n_ramas·AREAS_BARRA_MM2[num_estribo]`.
2. **Cortante:** `Vc = cortante_concreto_columna(b, d, fc, pu, Ag, lam)` (pu compresión +); `Vs_max =
   cortante_acero_maximo(b, d, fc)`; `Vs_req = vu/φ − Vc`. Si `Vs_req > Vs_max` → insuficiente
   (`cumple=False`, `disponer="SECCIÓN INSUFICIENTE A CORTANTE"`). Si `Vs_req ≤ 0` → `s_cortante = ∞` (no rige).
   Si no `s_cortante = Av·fyt·d/Vs_req`.
3. **Confinamiento:** `ratio = max(0.3·(Ag/Ach−1)·(fc/fyt), 0.09·(fc/fyt))`; `s_conf = Av/(ratio·bc)`
   (de `Ash≥ratio·bc·s`); límite de separación `s_so = min(0.25·min(b,h), 6·db_long, 150)` (ACI §18.7.5.3;
   `so` por `hx` se toma 150 — simplificación). `s_confinamiento = min(s_conf, s_so)`.
4. **Detallado** (§25.7.2.1): `s_det = min(16·db_long, 48·_diametro_barra(num_estribo), min(b,h))`.
5. `s = min(s_cortante, s_confinamiento, s_det)`, redondeado abajo a múltiplo de 25, `≥50`. `gobierna` = cuál
   de los tres dio el mínimo. `Ash_provista = Av`.
6. `cumple = (Vs_req ≤ Vs_max) y (Av ≥ ratio·bc·s)` (Ash provista cubre la requerida a `s`).
   `disponer = f"E#{num_estribo} {n_ramas}R @ {s:.0f}"`.

## 5. Orquestador (`diseno_elemento.py`)

`disenar_columna_combos`: tras el diseño longitudinal P-M, diseña el estribo para el **combo de mayor |Vu|**
(reusa `_demanda_por_combo`): `vu_g = max|Vu|` sobre combos, `pu` = el axial de ese combo, `db_long =
_diametro_barra(d.numero_barra)`; `estribo = aci318.disenar_estribo_columna(vu_g, pu, b_mm, h_mm, fc,
db_long, rec_mm, fy)`. `DisenoColumnaCombos` gana `estribo: aci318.DisenoEstriboColumna` y `combo_cortante: str`.
`cumple` global = P-M cumple **y** `estribo.cumple`.

(Unidades: `_demanda_por_combo` da Vu en N, Pu en N; `b,h,rec` se pasan en mm.)

## 6. Visor (`viz/diseno.py` + `app.js`)

- `viz/diseno.py`: el `estribo` del DTO de columna pasa a salir del diseño real:
  `{"d": armado._diametro_m(d.estribo.numero_barra), "s": d.estribo.espaciamiento/1000, "w": b−2·rec, "h": h−2·rec}`.
  (Reemplaza la regla heurística 25.7.2.1.) La jaula del visor ya dibuja estribos a `estribo.s` → muestra la
  separación diseñada (más ajustada por confinamiento). Se agrega `estribo_txt = d.estribo.disponer` al DTO del
  elemento (columna); vacío para vigas.
- `app.js`: `mostrarDiseno` para columnas agrega `· ${el.estribo_txt}` cuando no es vacío.

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| `Vs_req > Vs_max` (cortante de columna) | `cumple=False`, `disponer="SECCIÓN INSUFICIENTE A CORTANTE"`, `gobierna="cortante"`. |
| Params inválidos (`b,h,fc,fyt,rec ≤ 0`) | `ValueError`. |
| Confinamiento incumplible con la barra elegida | `s` cae al mínimo (50) y `cumple` lo refleja si `Av < ratio·bc·50`. |

## 8. Simplificaciones documentadas (fuera de alcance)

- Separación **confinada** en toda la altura (no zonas `lo`/centro separadas) — conservador.
- `so` dependiente de `hx` se toma 150; término de alto axial `(c)` (§18.7.5.4) omitido.
- `n_ramas=2` fijo (perímetro); cross-ties según layout = futuro.

## 9. Testing

| Qué | Casos |
|---|---|
| `aci318` (`test_diseno_marco.py`) | `cortante_concreto_columna`: con compresión > sin axial; tracción reduce/anula. `confinamiento_ash`: proporcional a `s`. `disenar_estribo_columna`: gobernado por confinamiento (s pequeña), por cortante (Vu alto), insuficiente (Vu enorme → no cumple). |
| `diseno_elemento` (`test_combinaciones_diseno.py`) | `disenar_columna_combos` trae `estribo.espaciamiento>0`, `combo_cortante` poblado, `cumple` refleja el estribo. |
| `viz` (`test_diseno_visual.py`) | el estribo de columna del DTO sale del diseño (con confinamiento, `s` ≤ la heurística previa); `estribo_txt` no vacío en columnas. |
| `servidor` (`test_servidor.py`) | `/diseno` sigue 8 elementos; columnas con `estribo` real. |
| Visor JS | smoke — etiqueta de columna con `E#3@s`. |

**Criterio de aceptación:**
1. `PYTHONPATH=src:tests pytest -q` verde (~188 + ~7 ≈ **195**); sin regresión.
2. Las columnas en `/diseno` traen estribo diseñado (cortante + confinamiento), su separación y `cumple` reales.
3. El visor dibuja la separación de estribos diseñada y la etiqueta muestra `E#3@s` en columnas.

## 10. Archivos afectados

**Modificados**
- `src/motor_fea/normativa/aci318.py` (aditivo: Vc-columna, Ash, `disenar_estribo_columna`, `DisenoEstriboColumna`)
- `src/motor_fea/diseno_elemento.py` (estribo en `disenar_columna_combos`)
- `src/motor_fea/viz/diseno.py` (estribo real + `estribo_txt` en el DTO de columna)
- `src/motor_fea/viz/static/app.js` (estribo en la etiqueta de columna)
- `tests/test_diseno_marco.py`, `tests/test_combinaciones_diseno.py`, `tests/test_diseno_visual.py`, `tests/test_servidor.py`

`core/` **no se toca**.
