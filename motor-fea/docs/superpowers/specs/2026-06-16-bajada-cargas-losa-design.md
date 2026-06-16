# Bajada de cargas · Losa → cargas de borde — Diseño (Rebanada C)

**Fecha:** 2026-06-16
**Estado:** aprobado (alcance + 2 decisiones de dominio confirmadas por el usuario)
**Predecesor:** `2026-06-16-sintesis-fea-columnas-design.md` (Rebanada B0 — columnas → malla)
**Origen:** port del tipo de dominio .NET `src/Core/Transmision/RepartoCargaLosa.cs` (EstructurasRD-main, Fase J «Liga G»).

## Problema

B0 entregó la malla FEA (nodos/barras/apoyos) pero **sin solicitaciones**: la malla es geométricamente correcta pero "muerta". El motor de barras no consume presión sobre un área (`q` en kN/m²); consume **cargas lineales sobre elementos** (kN/m). Falta el traductor losa→borde que reparte la carga superficial de cada paño a los bordes que lo apoyan.

## Alcance (decisiones del usuario)

Esta rebanada porta el **calculador puro de reparto** losa→bordes y lo adapta al modelo Python. **No** aplica todavía las cargas a las barras de la malla (eso es la rebanada siguiente, C2: borde→barra→carga nodal/de elemento).

**Decisión 1 — Carga muerta y viva SEPARADAS.** El reparto devuelve cargas de borde para `cargas.muerta` y para `cargas.viva` por aparte (no se colapsan en un `q` de servicio). Habilita combinaciones LRFD/servicio aguas abajo (1.2D+1.6L, D+L, …) sin perder la distinción.

**Decisión 2 — Rectángulos (clásico) + fallback polígono.**
- Paño rectangular → método clásico de **áreas tributarias a 45°** (fiel al .NET).
- Paño no rectangular → aproximación **por perímetro** documentada (ver abajo), nunca silenciosa.

**Fuera de alcance (rebanadas siguientes):**
- Aplicar el reparto a la malla (borde → `ElementoFrame` → carga). Rebanada C2.
- Combinaciones de carga (factores LRFD). Se construyen sobre la salida D/L separada.
- Tributaria por *straight skeleton* para polígonos arbitrarios (la aproximación por perímetro la sustituye por ahora).

## Arquitectura

Módulo nuevo `src/motor_fea/edificio/cargas.py`. Funciones puras, sin I/O. Unidades: `q` en kN/m² (las de `CargasLosa`), longitudes en m → fuerzas en kN, líneas en kN/m. Re-exportado desde `motor_fea.edificio.__init__`.

```
repartir_losa(losa: Losa) -> RepartoLosa
```

Internamente se apoya en el núcleo fiel al .NET:

```
_reparto_rectangular(a: float, b: float, q: float) -> tuple[CargaBorde, CargaBorde]
```

## Tipos de salida

```python
class FormaCarga(Enum):
    TRIANGULAR  = "triangular"    # borde corto de un rectángulo
    TRAPEZOIDAL = "trapezoidal"   # borde largo de un rectángulo
    UNIFORME    = "uniforme"      # fallback poligonal

@dataclass(frozen=True)
class CargaBorde:
    indice_borde: int          # lado del contorno (0..n-1), en orden de `puntos`
    longitud: float            # m
    forma: FormaCarga
    fuerza_total: float        # kN   = q · área tributaria del borde
    linea_uniforme_equivalente: float  # kN/m = fuerza_total / longitud
    intensidad_pico: float     # kN/m = intensidad máxima del triángulo/trapecio

@dataclass(frozen=True)
class RepartoDireccion:        # un caso de carga (muerta O viva)
    bordes: tuple[CargaBorde, ...]   # uno por lado del contorno
    carga_total: float               # kN = q · área del paño

@dataclass(frozen=True)
class RepartoLosa:
    losa_id: int
    rectangular: bool
    muerta: RepartoDireccion
    viva: RepartoDireccion
```

## El método (unidades aisladas)

### 1. Detección de rectángulo
`puntos` es rectángulo (alineado a ejes) si tiene exactamente 4 vértices cuyo conjunto de `x` tiene 2 valores distintos y el de `y` también, y los 4 puntos son justo las 4 esquinas `{(x₀,y₀),(x₀,y₁),(x₁,y₀),(x₁,y₁)}` (cualquier orden de giro). Entonces `lx = |x₁−x₀|`, `ly = |y₁−y₀|`. Rectángulos rotados → caen al fallback (documentado).

### 2. Reparto rectangular (port fiel del .NET, áreas tributarias 45°)
Para `a = min(lx,ly)` (corto), `b = max(lx,ly)` (largo), e intensidad pico común `pico = q·a/2`:
```
Borde corto  (long a): TRIANGULAR   F = q·a²/4              w = F/a = q·a/4
Borde largo  (long b): TRAPEZOIDAL  F = q·(a/4)·(2b−a)      w = F/b
```
Las 4 áreas suman `a·b` (conservación). Cada uno de los 4 lados del contorno recibe el `CargaBorde` que le toca según su longitud (los dos de longitud `a` → triángulo; los dos de longitud `b` → trapecio). `carga_total = q·lx·ly`.

**Anclas de regresión (del test .NET):** paño 5×5, q=10 → F_borde=62.5, w=12.5, pico=25, total=250. Paño 4×8, q=10 → corto F=40,w=10; largo F=120,w=15,pico=20; total=320. Simétrico en el orden de lados.

### 3. Fallback poligonal (por perímetro)
Para un contorno no rectangular: área `A` por la fórmula del zapatero (shoelace, valor absoluto); fuerza total `Q = q·A` repartida **proporcional a la longitud de cada lado**:
```
por lado i:  F_i = Q · long_i / perímetro    w_i = F_i / long_i = Q / perímetro   (UNIFORME)
pico_i = w_i
```
Conserva la fuerza total y es isótropo. **No es tributaria real** — es una simplificación conservadora; se documenta en el docstring y se sustituirá por *straight skeleton* si se necesita.

### 4. Degenerados
`q ≤ 0` o área nula → `RepartoDireccion` con `bordes=()` y `carga_total=0` (espejo del "reparto nulo" del .NET). Se evalúa por caso: si `muerta=0` y `viva>0`, solo muerta es nula.

## Garantías y errores
- **Conservación:** `Σ F_bordes == carga_total` (rectángulo: 2·corto+2·largo; polígono: Σ F_i), por caso de carga.
- **Determinismo:** misma losa → mismo reparto (orden de bordes = orden de `puntos`).
- **Simetría:** el reparto rectangular no depende de si el lado mayor es `lx` o `ly`.
- Contorno < 3 puntos → no debería llegar (lo veta `Proyecto.validar()`); si llega, `ValueError` legible.

## Testing
`tests/test_bajada_cargas.py`:
1. **Port .NET — cuadrado** 5×5, q=10: F=62.5, w=12.5, pico=25, total=250.
2. **Port .NET — rectangular** 4×8, q=10: corto F=40/w=10; largo F=120/w=15/pico=20; total=320; formas TRIANGULAR/TRAPEZOIDAL correctas por lado.
3. **Conservación** rectángulo: Σ de los 4 bordes == carga_total == 320.
4. **Simetría** en el orden de lados (lx↔ly).
5. **D y L separadas:** losa con muerta=4, viva=2 → dos `RepartoDireccion` con totales independientes (q·A cada uno).
6. **Degenerado:** viva=0 → `muerta.bordes` no vacío, `viva.bordes==()` y `viva.carga_total==0`.
7. **Fallback poligonal** (L-shape o triángulo): todos los bordes UNIFORME, `rectangular==False`, Σ F == q·A.
8. **Determinismo:** dos repartos de la misma losa son iguales.

## Self-review (cobertura)
| Requisito | Cubierto en |
|---|---|
| Port fiel del cálculo .NET | `_reparto_rectangular` + tests 1–4 (valores .NET) |
| D/L separadas (decisión 1) | `RepartoLosa.muerta`/`.viva` + test 5 |
| Rectángulo clásico + fallback (decisión 2) | Detección §1 + reparto §2/§3 + tests 2,7 |
| Conservación de carga | Garantía + tests 3,7 |
| Manejo de degenerados | §4 + test 6 |
| Determinismo | Garantía + test 8 |
