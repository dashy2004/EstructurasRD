# Diseño — Fase 5A.1: combinaciones de carga en el diseño (motor)

**Fecha:** 2026-06-06
**Estado:** aprobado en brainstorming (3 decisiones), pendiente de revisión del spec.
**Depende de:** 4b.1 (`diseno_elemento`, `aci318` P-M / estribos), esfuerzos por elemento, y `normativa/combinaciones.py` (combos LRFD ya existentes).
**Alcance:** Fase 5A.1 — el **motor**: separar cargas por caso, analizar por caso, y diseñar por **combo gobernante**. Sin visor/endpoint (eso es 5A.2).

---

## 1. Objetivo

Hoy el diseño usa las fuerzas de **un solo análisis** como demanda factorada. 5A.1 conecta las combinaciones
LRFD (ACI 318-19 §5.3.1, ya implementadas en `combinaciones.py` pero **desconectadas**) al diseño: cada carga
se etiqueta con su caso (D/L/W/E…), se corre un análisis por caso, y cada elemento se diseña para **todos** los
combos, reportando el **combo gobernante**.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Casos | **Campo `caso: str = "D"` en `CargaNodal`** (retrocompatible; todo lo existente queda como caso "D"). |
| Combinación | **Diseño por combo gobernante** (no envolvente por componente): por cada combo se arma el set (P,M,V) que actúa junto y se diseña; cumple solo si **todos** cumplen; se reporta el gobernante. Preserva la correlación P-M. |
| Alcance | **Motor primero (5A.1)**; visor/`/diseno` con el combo gobernante = 5A.2. |

## 3. Arquitectura

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Caso en la carga | `src/motor_fea/core/modelo.py` (mod) | `CargaNodal` gana `caso: str = "D"`; `ModeloEstructural.validar()` chequea `caso ∈ {D,L,Lr,S,R,W,E}`. |
| Análisis por caso | `src/motor_fea/core/casos.py` (nuevo) | `esfuerzos_por_caso(modelo) → dict[str, dict[int, EsfuerzosElemento]]`. |
| Diseño por combo | `src/motor_fea/diseno_elemento.py` (mod, **aditivo**) | `disenar_viga_combos` / `disenar_columna_combos` + `DisenoVigaCombos` / `DisenoColumnaCombos`. Las funciones de un solo caso (4b.1) **quedan intactas**. |
| Tests | `tests/test_combinaciones_diseno.py` (nuevo) | Casos cerrados / auto-consistentes. |

`normativa/aci318.py` **no se toca** (las primitivas P-M / estribos / barras se reusan tal cual).

## 4. Modelo de cargas (`core/modelo.py`)

`CargaNodal` agrega `caso: str = "D"`. `ModeloEstructural.validar()` agrega: por cada carga, si
`caso ∉ {"D","L","Lr","S","R","W","E"}` → error `"carga en nodo N: caso 'X' inválido"`. Default `"D"` ⇒ todos
los modelos/tests/ejemplos existentes siguen válidos y se comportan como un único caso muerto.

## 5. Análisis por caso (`core/casos.py`)

```python
def esfuerzos_por_caso(modelo: ModeloEstructural) -> dict[str, dict[int, EsfuerzosElemento]]
```
- `casos = sorted({c.caso for c in modelo.cargas})` (si no hay cargas → `{}`).
- Por cada `caso`: sub-modelo con las mismas listas (nodos/elementos/materiales/secciones/apoyos) y
  `cargas` filtradas al caso (`dataclasses.replace(modelo, cargas=[c for c in modelo.cargas if c.caso==caso])`);
  `resolver(sub)` + `esfuerzos_elementos(sub, resultado)` → esfuerzos por elemento para ese caso.
- Devuelve `{caso: {elem_id: EsfuerzosElemento}}`. Reusa `resolver` y `esfuerzos_elementos` sin cambios
  (cada caso es un análisis lineal independiente; la combinación es posterior, a nivel de esfuerzos).

## 6. Demanda por combo (`diseno_elemento.py`)

Helper interno que arma, por elemento, el set (Pu, Mu, Vu) **por combo**:

```python
def _demanda_por_combo(esf_por_caso: dict[str, EsfuerzosElemento]) -> dict[str, tuple[float, float, float]]
```
1. Por caso, demanda escalar (reusa el scan tipo `_demanda`): `P_caso = esf.axial` (**con signo**),
   `M_caso = max|My|,|Mz|` sobre la longitud, `V_caso = max|Vy|,|Vz|`.
2. Por componente, `combinaciones_resistencia(**{caso: valor})` → `dict[combo→U]`. Como las etiquetas de caso
   (`D,L,Lr,S,R,W,E`) **coinciden con los kwargs** de la función, el mapeo es directo; los casos ausentes
   default a 0 y `roof=max(Lr,S,R)` lo maneja la función.
3. Zippeando por nombre de combo: `{combo: (Pu, Mu, Vu)}` con `Pu=combos_P[combo]` (signo),
   `Mu=combos_M[combo]`, `Vu=combos_V[combo]`.

**Convención:** el axial se combina **con signo** (captura reversibilidad de E y tracción); M y V se combinan
por magnitud de caso (la reversibilidad ± de W/E ya la expande `combinaciones_resistencia`). El diseño usa
`abs(Pu)`, `abs(Mu)`, `abs(Vu)` por combo. Simplificación documentada: combinación de **escalares por
componente** (máx sobre la longitud por caso, luego combino), no estación-por-estación (refinamiento futuro).

## 7. Diseño por combo gobernante (`diseno_elemento.py`, aditivo)

Dataclasses nuevas (en `diseno_elemento`, para que `aci318` no conozca el concepto de combo):
```python
@dataclass(frozen=True)
class DisenoColumnaCombos:
    pu: float; mu: float            # del combo gobernante (N, N·mm)
    numero_barra: int; n_barras: int; rho: float
    cumple: bool; disponer: str
    combo_gobernante: str

@dataclass(frozen=True)
class DisenoVigaCombos:
    mu: float; vu: float            # del/los combo(s) gobernante(s) (N·m, N)
    flexion: aci318.SeleccionBarras | None
    estribo: aci318.DisenoEstribo
    cumple: bool; disponer: str
    combo_flexion: str; combo_cortante: str
```

```python
def disenar_columna_combos(esf_por_caso, b, h, fc=21.0, fy=420.0, recubrimiento=0.04, num=8) -> DisenoColumnaCombos
def disenar_viga_combos(esf_por_caso, b, h, fc=21.0, fy=420.0, recubrimiento=0.04) -> DisenoVigaCombos
```

- **Columna:** `demandas = _demanda_por_combo(...)` → `{combo: (pu, mu, vu)}` (se usa `abs(pu)`, `abs(mu)`).
  Se itera el nº de barras (ρ 1%→8%, como `disenar_columna_pm`) y, para cada `n`, se construye el diagrama
  P-M una vez y se exige que **todos** los combos `(|pu|,|mu|)` caigan dentro (`|pu| ≤ axial_maxima_diseno` y
  `|mu| ≤ momento_capacidad(|pu|, diagrama)`). El primer `n` que cubre todos es el diseño;
  `combo_gobernante` = el combo con mayor relación demanda/capacidad a ese `n` (el que liga). Si ningún
  `n≤ρ8%` cubre todos → `cumple=False`, `disponer="SECCIÓN INSUFICIENTE"`, `combo_gobernante` = el que falla.
- **Viga:** por combo, `as_req(|mu_combo|)` y `vs_req(|vu_combo|)`. `As_diseño = max` sobre combos (con
  `combo_flexion` = el de mayor As), estribo `s = min` sobre combos (con `combo_cortante` = el de mayor Vu);
  `seleccionar_barras`/`disenar_estribo_viga` con esos valores gobernantes. `cumple` = todos los combos cumplen
  (flexión y cortante). Sección insuficiente a flexión en el combo gobernante → `flexion=None`, `cumple=False`.

Reusa íntegramente las primitivas de `aci318` (`diagrama_interaccion`, `momento_capacidad`,
`axial_maxima_diseno`, `as_requerido_flexion`, `seleccionar_barras`, `disenar_estribo_viga`).

## 8. Manejo de errores

| Situación | Respuesta |
|---|---|
| `caso` no en {D,L,Lr,S,R,W,E} | `ValueError` desde `validar()`. |
| Caso referenciado sin cargas | No aporta (sus efectos = 0 en todos los combos). |
| Insuficiencia en algún combo (flexión/cortante/columna) | `cumple=False` + `combo_gobernante`/`disponer` lo señalan (bandera, no excepción). |
| Tracción gobernante en columna | Se diseña con `abs(Pu)` (magnitud de compresión) — limitación documentada, igual que 4b.1; diseño de columnas a tracción = futuro. |

## 9. Testing (`tests/test_combinaciones_diseno.py`, casos cerrados)

| Qué | Caso | Esperado |
|---|---|---|
| `esfuerzos_por_caso` | voladizo con cargas en 2 casos (D y L) | devuelve `{"D":{…}, "L":{…}}`; el esf de cada caso ≈ el del análisis de ese caso solo. |
| Combo gobernante viga | voladizo `fz` en D y L | `disenar_viga_combos` → `Mu ≈ 1.2·M_D + 1.6·M_L`, `combo_flexion == "2"`. |
| Reversibilidad E | caso E lateral en una columna | aparece un combo `(-)`; `combo_gobernante` puede ser uno reversible; no rompe. |
| Combo gobernante columna | columna con axial en D y momento lateral en L cuyo combo exige **más barras** que cualquier caso solo | `n_barras(combos) ≥ n_barras(D solo)`; `combo_gobernante` poblado. |
| Insuficiencia | demanda combinada que excede ρ8% | `cumple=False`, `combo_gobernante` = el que falla. |
| `caso` inválido | `CargaNodal(..., caso="X")` | `modelo.validar()` reporta error → `ValueError` al resolver/diseñar. |
| Retrocompat | modelo sin `caso` (todo "D") | `disenar_viga_combos` ≈ diseñar con `1.4·M_D` (combo "1"); el camino de 4b.1 (un caso) sigue idéntico. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (177 + ~8 ≈ **185**); los tests de 4b.1/4b.2 (un caso) sin regresión.
2. `disenar_viga_combos`/`disenar_columna_combos` diseñan para todos los combos LRFD y reportan el gobernante;
   un modelo de un solo caso "D" reproduce el diseño con el combo 1.4D.

## 10. Roadmap (fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 5A.2 | `/diseno` por combos + el visor mostrando el **combo gobernante** en la etiqueta del elemento. | `casos`, `diseno_elemento` (combos), `viz/diseno` |
| 5B/5C/5D | biaxial, estribos de columna + confinamiento, panel UI fc/fy/rec. | — |
| 5 (futuro) | sismo auto-distribuido (de `sismo.py`) como caso E; combinación per-station. | `sismo`, `combinacion_modal` |

## 11. Archivos afectados

**Nuevos**
- `src/motor_fea/core/casos.py`
- `tests/test_combinaciones_diseno.py`

**Modificados**
- `src/motor_fea/core/modelo.py` (`CargaNodal.caso` + `validar`)
- `src/motor_fea/diseno_elemento.py` (aditivo: `_demanda_por_combo`, `disenar_*_combos`, dataclasses Combos)

`normativa/`, `api/` y `viz/` **no se tocan** en 5A.1.
