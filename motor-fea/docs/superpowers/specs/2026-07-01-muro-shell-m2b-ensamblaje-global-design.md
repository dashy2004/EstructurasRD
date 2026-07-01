# M2b — Ensamblaje global del elemento shell Design

## Contexto y objetivo

M2a entregó `core/shell.py::rigidez_shell(nodos_xy, E, nu, t, gamma=None) -> 24×24`,
el elemento shell plano **en coordenadas locales** (plano x-y del elemento), con
6 GDL/nodo en orden `[ux, uy, uz, θx, θy, θz]`. Hoy ese código es **inerte**: el
solver (`core/solver.py::ensamblar_global`) solo itera `ElementoFrame` (2 nodos,
12×12). Ningún muro usa el shell.

**Objetivo M2b:** dar al shell una puerta de entrada al modelo y al ensamblaje
global, de modo que un cuadrilátero de 4 nodos ubicado arbitrariamente en el
espacio 3D contribuya su rigidez al sistema global — sin tocar la física local de
M2a y sin regresión en la suite (~298 tests verde).

**Fuera de alcance (queda para M3):** mallar un muro lleno real en una rejilla de
shells y reemplazar la síntesis B1 (columna ancha). M2b entrega el *mecanismo*;
M3 lo *usa* a escala.

## Arquitectura

El patrón es idéntico al del frame (`ensamblar_global`, líneas 174-186), extendido
de 2→4 nodos:

```
K_global_elem = Tᵀ · K_local · T        # T ortogonal ⇒ preserva energía
```

Tres piezas nuevas, todas en `core/`:

### 1. Tríada local y proyección 2D (`_marco_shell`)

Dado el cuadrilátero por sus 4 nodos 3D `[p0, p1, p2, p3]` (antihorario):

- `ex = normalize(p1 − p0)`                — primer lado como eje x local
- `n  = normalize((p1 − p0) × (p2 − p0))`  — normal al plano del elemento (eje z local)
- `ey = n × ex`                            — completa la tríada dextrógira

Proyección de cada nodo al plano local (origen en `p0`):
`xy_a = ( dot(p_a − p0, ex), dot(p_a − p0, ey) )`. Por construcción `xy_0 = (0,0)`
y `xy_1 = (|p1−p0|, 0)`. Se reutiliza `_cross/_norm/_dot` de `solver.py`.

> **Guarda de planaridad:** M2a exige rectángulo eje-alineado para la placa. El
> muro mallado (M3) genera rectángulos, así que la proyección de un cuadrilátero
> plano rectangular cae exactamente en ese caso. Si los 4 nodos no son coplanares
> o el rectángulo no es eje-alineado tras proyectar, se lanza `ValueError` (la
> misma guarda que ya tiene `rigidez_shell`).

### 2. Transformación 24×24 (`_transformacion_24`)

`T` block-diagonal con **8 bloques** de `R = [ex, ey, ez]` (filas = ejes locales),
uno por cada terna (traslación y rotación) de los 4 nodos. Análogo directo de
`_transformacion_12` (4 bloques) escalado a 8.

### 3. Tipo `ElementoShell` en el modelo + rama en `ensamblar_global`

- `modelo.py`: `@dataclass ElementoShell` con `id`, `nodos: tuple[int,int,int,int]`,
  `material_id`, `seccion_id` (de la sección se toma el espesor `t`). Nueva lista
  `modelo.elementos_shell` (default `[]`), validada en `validar()` (nodos existen,
  4 distintos).
- `solver.py::ensamblar_global`: tras el bucle de frames, un segundo bucle sobre
  `elementos_shell` que arma `K_local` (via `rigidez_shell` sobre la proyección),
  la transforma con `Tᵀ·K_local·T`, y dispersa la 24×24 en los 4 nodos × 6 GDL.

El resto del solver (`_vector_cargas`, `resolver`, restricciones, reacciones) es
agnóstico al tipo de elemento: opera sobre `n_gdl` y el índice de nodos, que no
cambian. **No requiere modificación.**

## Espesor del shell

`rigidez_shell` necesita `t`. Se toma de la sección referenciada
(`seccion.espesor` si existe; si la sección actual no lo tiene, se añade el campo
con default y se documenta). Decisión de implementación a fijar en el plan tras
inspeccionar `Seccion`.

## Slices TDD

**Slice 1 — Transformación e invariancia (riesgo aislado).**
`_marco_shell` + `_transformacion_24` + `rigidez_shell_global(nodos3d, E, nu, t)`.
Tests:
- Un cuadrilátero **en el plano z=0** con `rigidez_shell_global` reproduce
  exactamente la 24×24 de `rigidez_shell` (T = identidad de rotación salvo
  reordenamiento; energía idéntica).
- **Invariancia por rotación rígida del elemento:** el mismo rectángulo rotado por
  una R arbitraria en 3D tiene el mismo espectro de energía — 6 modos de cuerpo
  rígido con energía ≈ 0 y rango 18 en la reducida — que el elemento sin rotar.
- Simetría de la 24×24 global.

**Slice 2 — Modelo + ensamblaje.**
`ElementoShell`, `modelo.elementos_shell`, validación, y la rama de shells en
`ensamblar_global`. Tests:
- Modelo con 1 shell + apoyos: `ensamblar_global` produce K simétrica del tamaño
  correcto; el sub-bloque de los 4 nodos del shell es no nulo.
- `validar()` rechaza shell con nodo inexistente o nodos repetidos.

**Slice 3 — Resolución end-to-end + no-regresión.**
Tests:
- Un muro-shell mínimo (1 elemento) empotrado en su base bajo carga lateral en la
  cabeza resuelve con desplazamiento en la dirección esperada y reacciones que
  equilibran la carga (ΣF = 0).
- `pytest -q` completo sin regresión (frames intactos: los tests existentes no
  tocan `elementos_shell`, que default a `[]`).

## Criterios de aceptación

- `rigidez_shell` (M2a) **sin cambios**; toda la lógica global vive en funciones nuevas.
- Stdlib puro (solo `math` + módulos `core`); sin NumPy, sin I/O.
- `T` verificada ortogonal por el test de invariancia (energía preservada bajo rotación).
- Suite completa verde, frames sin regresión.
- Documentado: tríada local, proyección 2D, guarda de planaridad/rectangularidad,
  origen del espesor.

## Riesgos

- **Único riesgo técnico real:** signos/orden de la tríada y del block-diagonal de
  `T`. Mitigado por el test de invariancia del Slice 1 (si T no es ortogonal o los
  bloques están mal, la energía rotada difiere de la local y el test falla).
- **Deuda conocida (no M2b):** la placa ACM rectangular limita el shell a
  cuadriláteros que proyecten a rectángulos eje-alineados. Aceptable porque M3
  malla en rectángulos. La membrana sí es isoparamétrica general.
