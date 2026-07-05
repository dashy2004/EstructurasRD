# M2c — Recuperación de esfuerzos y momentos del shell desde la solución global

**Fecha:** 2026-07-04 · **Rama:** `engine/muro-shell-m1` · **Etapa previa:** M2b (ensamblaje global, 310 tests)

## Objetivo

Convertir los desplazamientos globales que produce `solver.resolver()` en
resultados de ingeniería por panel de muro: esfuerzos de membrana en el plano
(σxx, σyy, τxy, en Pa) y momentos de flexión de placa (Mx, My, Mxy, en N·m/m),
ambos **en coordenadas locales del panel** y evaluados en el **centro** del
elemento (punto de superconvergencia del Q4). Es el insumo directo de M4
(diseño de acero ACI) y M5 (heatmaps en el visor).

## API

### `core/shell.py`

```python
def esfuerzos_shell_global(nodos3d, E, nu, t, d24):
    """((σxx, σyy, τxy), (Mx, My, Mxy)) locales en el centro del panel.

    d24 = 24 desplazamientos GLOBALES [ux,uy,uz,θx,θy,θz ×4 nodos] en el
    orden de rigidez_shell_global. ValueError si len(d24) != 24.
    """
```

Pasos internos (espejo exacto del camino de rigidez M2b, pero para
desplazamientos): tríada local (`_marco_shell`) → proyección 2D
(`_proyectar_2d`) → `u_local = T·d24` (`_transformacion_24`) → separar bloques:

- membrana: `[u_local[6a+0], u_local[6a+1]]` (8 GDL) → `membrana.esfuerzos_elemento(..., xi=0, eta=0)`
- placa: `[u_local[6a+2], u_local[6a+3], u_local[6a+4]]` (12 GDL) → `placa.momentos_elemento(lx, ly, ..., fx=0.5, fy=0.5)`
- drilling `θz`: no participa (es estabilización numérica, no esfuerzo físico).

### `core/solver.py`

```python
@dataclass
class EsfuerzosShell:
    elemento_id: int
    membrana: tuple[float, float, float]   # (σxx, σyy, τxy) Pa, locales, centro
    momentos: tuple[float, float, float]   # (Mx, My, Mxy) N·m/m, locales, centro

def esfuerzos_shells(modelo, resultado) -> dict[int, EsfuerzosShell]:
```

Post-procesamiento puro (espejo de `esfuerzos_elementos` para frames): por cada
`ElementoShell`, junta los 24 GDL globales de sus 4 nodos desde
`resultado.desplazamientos` y llama `shell.esfuerzos_shell_global`. Import
diferido de `shell` (mismo motivo M2b: ciclo placa→solver).

## Decisiones

1. **Centro solamente** (sin malla de puntos): YAGNI — M4 diseña con el valor
   central por panel y M5 pinta un color por elemento. Extensible después vía
   los parámetros (ξ,η)/(fx,fy) que ya exponen membrana/placa.
2. **Locales, no globales**: el diseño del muro (cortante en el plano, flexión
   fuera del plano) se razona en el plano del muro; el visor rota si hace falta.
3. **Sin von Mises todavía**: combinación de esfuerzos pertenece a M4 (criterio
   de diseño), no a la recuperación.

## Tests (TDD, `tests/test_shell_esfuerzos.py`)

1. `d24` de largo incorrecto → `ValueError`.
2. Panel plano en XY (local=global) con campo de extensión pura en x:
   σxx = E·εx/(1−ν²), σyy = ν·σxx, τxy = 0 (analítico, tensión plana).
3. Panel plano en XY: los momentos que devuelve `esfuerzos_shell_global`
   coinciden con llamar `placa.momentos_elemento` directo con los mismos GDL
   de placa (mapeo identidad — valida la extracción de bloques).
4. **Invariancia**: el mismo campo local rotado a un plano vertical (muro en
   XZ) devuelve exactamente los mismos esfuerzos y momentos locales que el
   caso XY (valida el camino T·u).
5. **Integración solver**: muro vertical 2×3 m (un panel, base empotrada,
   ν=0) con compresión axial P en el tope → `esfuerzos_shells` da
   σyy ≈ −P/(ancho·t) y los demás ≈ 0.
