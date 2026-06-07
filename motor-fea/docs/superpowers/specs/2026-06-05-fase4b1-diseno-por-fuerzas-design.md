# Diseño — Fase 4b.1: diseño de columnas y vigas por fuerzas (motor)

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming (descomposición + 3 decisiones), pendiente de revisión del spec.
**Depende de:** `esfuerzos_elementos` (esfuerzos por elemento, ya implementado) y `aci318` (primitivos ACI).
**Alcance:** Fase 4b.1 — el **motor de diseño**: rutinas ACI de diseño (faltantes) + un orquestador que diseña cada
columna/viga a partir de sus esfuerzos reales. **Sin visor ni endpoint** (eso es 4b.2).

---

## 1. Objetivo

Cerrar los huecos de diseño de `aci318` (diseñador de estribos, diagrama de interacción + diseñador de
columna P-M, selección de barras) y orquestar, por elemento, el diseño del refuerzo a partir de la demanda
real (Pu, Mu, Vu) que da `esfuerzos_elementos`. Es la base para que el visor (4b.2) muestre armado **diseñado
por carga**, no de ejemplo.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Descomposición | Fase 4b = **4b.1 (este spec, motor)** + 4b.2 (visor). |
| Columna | **Diagrama P-M completo**: barrer el eje neutro c, construir la envolvente φPn–φMn, iterar barras (ρ 1%→8%) hasta cumplir. `Mu = max(\|My\|,\|Mz\|)` (uniaxial, eje peor). |
| Demanda | Las fuerzas de **un análisis** (`esfuerzos_elementos`), asumidas factoradas. Combinaciones = futuro. |

## 3. Arquitectura

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Rutinas de diseño ACI | `src/motor_fea/normativa/aci318.py` (mod, **aditivo**) | `seleccionar_barras`, `disenar_estribo_viga`, `diagrama_interaccion`, `momento_capacidad`, `disenar_columna_pm` + sus dataclasses. Reusan `cortante_*`, `as_*_flexion`, `punto_interaccion`, `axial_maxima_diseno`, `AREAS_BARRA_MM2`. |
| Orquestador por elemento | `src/motor_fea/diseno_elemento.py` (nuevo) | `disenar_viga(esf, b, h, fc, fy, rec)` y `disenar_columna(esf, b, h, fc, fy, rec)`: extraen la demanda de `EsfuerzosElemento`, convierten unidades y llaman a `aci318`. |
| Tests | `tests/test_diseno_marco.py` (nuevo) | Casos cerrados / auto-consistentes. |

**Unidades:** `aci318` trabaja en **N, mm, MPa**. `EsfuerzosElemento` da **N, N·m** (modelo SI) y la geometría
viene en **m**. El orquestador convierte en la frontera (`×1000`: m→mm, N·m→N·mm; fc/fy ya en MPa).

## 4. Rutinas de diseño en `aci318.py` (aditivas)

Helper de diámetro (mm): `_diametro_barra(num) = num · 25.4 / 8`.

### 4.1 Selección de barras longitudinales
```python
@dataclass(frozen=True)
class SeleccionBarras:
    numero_barra: int
    n_barras: int
    as_provista: float     # mm²
    cumple: bool           # cubre As y entra en el ancho disponible

def seleccionar_barras(as_req, ancho_disponible, num=5) -> SeleccionBarras
```
`n = max(2, ceil(as_req/AREAS_BARRA_MM2[num]))`; `as_provista = n·area`. `cumple` si `as_provista ≥ as_req` **y**
las `n` barras entran: `n·Ø + (n−1)·25 ≤ ancho_disponible` (separación libre mínima 25 mm). `as_req` NaN
(sección insuficiente a flexión) → `cumple=False`.

### 4.2 Diseñador de estribos (gap de cortante)
```python
@dataclass(frozen=True)
class DisenoEstribo:
    numero_barra: int
    n_ramas: int
    espaciamiento: float   # mm
    av: float              # mm²
    vs_requerido: float    # N
    cumple: bool
    disponer: str

def disenar_estribo_viga(vu, bw, d, fc, fyt=420.0, num_estribo=3, n_ramas=2, lam=1.0) -> DisenoEstribo
```
Algoritmo (ACI 318-19 §22.5 / §9.6.3): `vu=|vu|`; `Vc=cortante_concreto(bw,d,fc,lam)`; `φ=PHI_CORTANTE`;
`Av=n_ramas·AREAS_BARRA_MM2[num_estribo]`; `s_max=min(d/2, 600)`; `Vs_max=cortante_acero_maximo(bw,d,fc)`.
- `vu ≤ 0.5·φ·Vc` → no requeridos por resistencia; `s=s_max`, `vs_req=0`.
- si no, `Vs_req = vu/φ − Vc`: `≤0` → mínimos (`s=s_max`); `> Vs_max` → **insuficiente** (`cumple=False`,
  `disponer="SECCIÓN INSUFICIENTE A CORTANTE"`); en otro caso `s = Av·fyt·d/Vs_req`, acotado a `≤ s_max`.
- `s` se redondea **hacia abajo** a múltiplo de 25 mm y se acota `≥ 50`. `cumple` se fija re-verificando con
  `verificar_viga_cortante(vu, bw, d, fc, Av, fyt, s, lam)`.

### 4.3 Columna P-M
```python
@dataclass(frozen=True)
class DisenoColumna:
    pu: float; mu: float        # N, N·mm (demanda)
    numero_barra: int; n_barras: int; rho: float
    cumple: bool; disponer: str

def diagrama_interaccion(b, h, fc, fy, capas, n=40) -> list[PuntoInteraccion]
def momento_capacidad(phi_pn_demanda, diagrama) -> float       # φMn (N·mm) interpolado al nivel axial
def disenar_columna_pm(pu, mu, b, h, fc, fy, recubrimiento, num=8) -> DisenoColumna
```
- **`diagrama_interaccion`**: barre `c` linealmente de `0.05·h` a `2·h` en `n` puntos, llamando
  `punto_interaccion(c, b, h, fc, fy, capas)`.
- **`momento_capacidad(phi_pn, diagrama)`**: ordena los puntos por `phi_pn` e interpola **`abs(phi_mn)`** en el
  nivel axial `phi_pn` demandado; fuera del rango de `phi_pn` → `0.0` (rama monótona en φPn; documentado).
- **`disenar_columna_pm`**: `pu,mu=|pu|,|mu|`; `Ag=b·h`; arranca en `n = max(4, ceil(0.01·Ag/area))`;
  mientras `ρ = n·area/Ag ≤ 0.08`: arma 2 capas `[(rec+Ø/2, As/2), (h−rec−Ø/2, As/2)]`, construye el diagrama,
  y si `pu ≤ axial_maxima_diseno(Ag, As, fc, fy)` **y** `mu ≤ momento_capacidad(pu, diagrama)` → cumple
  (`disponer=f"{n}#{num}"`); si no, `n+=1`. Si supera ρ=8% sin cumplir → `cumple=False`,
  `disponer="SECCIÓN INSUFICIENTE"`.

## 5. Orquestador por elemento (`diseno_elemento.py`)

```python
@dataclass(frozen=True)
class DisenoViga:
    mu: float; vu: float                 # N·m, N (demanda)
    flexion: SeleccionBarras | None
    estribo: DisenoEstribo
    cumple: bool; disponer: str

def disenar_viga(esf, b, h, fc=21.0, fy=420.0, recubrimiento=0.04) -> DisenoViga
def disenar_columna(esf, b, h, fc=21.0, fy=420.0, recubrimiento=0.04) -> DisenoColumna
```
- **`_demanda(esf, n=21) -> (mu, vu)`**: recorre `esf.diagrama(n)`; `mu = max|My|,|Mz|`, `vu = max|Vy|,|Vz|`
  (en N·m y N).
- **`disenar_viga`**: `mu, vu = _demanda(esf)`; `b_mm=b·1000`, `d_mm=(h−rec)·1000`; `as_req, insuf =
  as_requerido_flexion(mu·1000, b_mm, d_mm, fc, fy)`; `as_dis = NaN si insuf si no max(as_req, as_minimo_flexion(...))`;
  `flexion = seleccionar_barras(as_dis, (b−2·rec)·1000)` (o `None` si insuf); `estribo =
  disenar_estribo_viga(vu, b_mm, d_mm, fc, fy)`; `cumple = (not insuf) and flexion.cumple and estribo.cumple`.
- **`disenar_columna`**: `pu = abs(esf.axial)` (N); `mu, _ = _demanda(esf)`; devuelve
  `aci318.disenar_columna_pm(pu, mu·1000, b·1000, h·1000, fc, fy, rec·1000)`.

`b, h` y `rec` en **metros** (consistente con `escena`/visor); la conversión a mm vive acá. `esf` es un
`EsfuerzosElemento` de `esfuerzos_elementos`.

## 6. Manejo de errores

| Situación | Respuesta |
|---|---|
| Sección insuficiente a flexión (`as_requerido_flexion` → NaN) | `DisenoViga.flexion=None`, `cumple=False`, `disponer` lo indica. |
| `Vs_req > Vs_max` (cortante) | `DisenoEstribo.cumple=False`, `disponer="SECCIÓN INSUFICIENTE A CORTANTE"`. |
| Columna no cumple con ρ≤8% | `DisenoColumna.cumple=False`, `disponer="SECCIÓN INSUFICIENTE"`. |
| Parámetros inválidos (`fc≤0`, `fy≤0`, `b≤0`, `h≤0`, `rec` ≥ semi-sección) | `ValueError`. |

(El patrón "bandera, no excepción" para insuficiencia replica el de `diseno_losa`/`verificar_viga_*`.)

## 7. Testing (`tests/test_diseno_marco.py`, casos cerrados/auto-consistentes)

| Qué | Caso | Esperado |
|---|---|---|
| Estribo — sin requerir | `Vu` bajo (≤0.5φVc) | `vs_requerido==0`, `cumple`, `s==s_max`. |
| Estribo — diseñado | `Vu` que da `Vs_req>0` razonable | `s` finito ≤ d/2, y `verificar_viga_cortante` con ese `(Av,s)` cumple. |
| Estribo — insuficiente | `Vu` enorme (`Vs_req>Vs_max`) | `cumple=False`, disponer indica insuficiencia. |
| Selección de barras | `as_req` dado | `n≥2`, `as_provista≥as_req`, diámetros de `AREAS_BARRA_MM2`. |
| Columna P-M — auto-consistente | sección con `capas` conocidas: tomar `c` medio, `Pu=φPn(c)`, `Mu_borde = momento_capacidad(Pu, diagrama)±ε` | `Mu` apenas por debajo → la sección de prueba cumple; apenas por arriba → necesita más barras (`disenar_columna_pm` sube `n` o marca insuficiente). |
| Columna — escala con la demanda | `Mu` mayor ⇒ `n_barras` ≥. | monotonía. |
| Orquestador viga | voladizo `fz=P` (L=3, 0.30×0.30) | `disenar_viga` → `mu≈P·L`, `vu≈P`; `flexion.cumple`, `estribo.cumple`. |
| Orquestador columna | columna por Z con axial `fz=−P` + lateral | `disenar_columna` → `pu≈P`, devuelve un `DisenoColumna` con `cumple` coherente. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (153 + ~8 ≈ **161**).
2. `disenar_viga`/`disenar_columna` producen armado que **cumple** la demanda real del análisis del voladizo, y
   marcan insuficiencia cuando la demanda excede la capacidad con ρ≤8% / Vs_max.

## 8. Roadmap (fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 4b.2 | Endpoint `/diseno` + DTO con el armado diseñado + demanda (Pu/Mu/Vu), y el visor mostrando la jaula real con etiquetas. | `diseno_elemento` + `viz/armado` |
| 5 | Biaxial, combinaciones de carga (envolvente multi-caso), confinamiento sísmico. | `combinaciones`, `combinacion_modal` |

## 9. Archivos afectados

**Nuevos**
- `src/motor_fea/diseno_elemento.py`
- `tests/test_diseno_marco.py`

**Modificados**
- `src/motor_fea/normativa/aci318.py` (rutinas de diseño + dataclasses — aditivo; no cambia los primitivos existentes)

`core/`, la capa de frontera (`viz/`, `api/`) y `diseno_losa.py` **no se tocan**.
