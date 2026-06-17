# M2a — Elemento shell plano local (24×24)

**Fecha:** 2026-06-17
**Estado:** diseño aprobado, pendiente de plan de implementación (implementar en sesión limpia)
**Rama destino:** `engine/muro-shell-m1` (continúa la cadena muro→shell)
**Depende de:** M1 (`core/membrana.py`, mergeado en esta rama) y `core/placa.py` (existente).

## Contexto

Refinamiento "muro como shell", alcance elegido: **shell completo** (membrana en
el plano + placa fuera del plano + drilling DOF), integrado al solver de 6 GDL
del edificio. M2 se descompone en tres sub-rebanadas:

| # | Sub-rebanada | Entrega |
|---|---|---|
| **M2a** | **Elemento shell plano local (24×24)** (este spec) | Rigidez local 24×24 en el plano del muro: membrana(M1)+placa(`placa.py`)+drilling. Autónomo. |
| M2b | Transformación 3D + tipo shell en `ModeloEstructural` | `Tᵀ·k·T` 24×24 → global; ensamblaje junto a frames. |
| M2c | Recuperación de esfuerzos desde la solución global | Desplazamientos globales → GDL locales → esfuerzos en el plano + momentos de placa. |

M2a es la pieza fundacional: el elemento local, testeable de forma aislada sin
tocar el solver (como M1). M2b/M2c lo integran.

## Alcance de M2a

### Dentro de alcance
- Módulo nuevo `src/motor_fea/core/shell.py`, estilo `placa.py`/`membrana.py`
  (stdlib puro, sin NumPy, SI).
- Función `rigidez_shell(nodos_xy, E, nu, t) -> list[list[float]]` que devuelve
  la **rigidez local 24×24** del shell plano cuadrilátero de 4 nodos.
- 6 GDL/nodo en orden **idéntico al frame** del modelo
  (`GDL.UX,UY,UZ,RX,RY,RZ` = ux, uy, uz, θx, θy, θz):
  - `ux, uy` → membrana (en el plano), de M1.
  - `uz, θx, θy` → placa de flexión (fuera del plano), de `placa.py`
    (donde `w=uz`, `θx=rx`, `θy=ry`).
  - `θz` → drilling (rotación en torno a la normal local), término de
    estabilización acoplado a la membrana.
- Reutiliza, sin reimplementar: `membrana.rigidez_membrana` (y sus helpers de
  forma/jacobiano/B) y `placa.rigidez_placa`.

### Fuera de alcance de M2a (sub-rebanadas posteriores)
- Transformación local→global 3D, tipo de dato del elemento en `ModeloEstructural`,
  ensamblaje en el solver → **M2b**.
- Recuperación de esfuerzos/momentos desde la solución → **M2c**.
- Cargas, apoyos, resolución, mallado del muro, diseño, visor → M3+.
- Acoplamiento membrana–placa por curvatura (no existe en shell **plano**: están
  desacoplados; eso es lo que hace válido el ensamblaje en bloques).

## Decisión de formulación

### Estructura en bloques (shell plano)
En un shell **plano**, la membrana (en el plano) y la flexión de placa (fuera del
plano) **no se acoplan**. La rigidez 24×24 se ensambla de dos bloques de 12×12
independientes, mapeados al orden de 6 GDL/nodo:

- **Bloque membrana+drilling (12×12):** GDL `(ux, uy, θz)` de los 4 nodos.
- **Bloque placa (12×12):** GDL `(uz, θx, θy)` de los 4 nodos.
- Los bloques cruzados membrana↔placa son **cero**.

### Drilling DOF — penalización de Hughes–Brezzi
El Q4 de membrana de M1 no tiene rotación propia θz. Para (a) que θz no sea un
mecanismo libre y (b) poder conectar con la θ de columnas/vigas en M2b, se añade
una **rigidez de drilling**. Se elige la formulación de **penalización rotacional
de Hughes–Brezzi/Zienkiewicz**, que es la mínima *correcta*:

```
E_drill = ½ γ ∫∫ ( θz − ω(u) )² dA ,   con ω(u) = ½ (∂uy/∂x − ∂ux/∂y)
```

donde `ω` es la rotación "verdadera" del campo de membrana (deducida de las
derivadas de las funciones de forma, ya disponibles en `membrana._matriz_B`),
`θz` es el GDL independiente de drilling, y `γ` un factor de penalización
(p.ej. `γ = E·t`, documentado y único para el elemento).

**Por qué esta forma y no una diagonal simple:** una rigidez diagonal `k·θz²`
**no se anula** bajo rotación rígida en el plano (`ux=−θy, uy=θx, θz=θ`), porque
penalizaría `θz=θ≠0` → produciría momento espurio y rompería el modo de cuerpo
rígido. La forma de Hughes–Brezzi se anula exactamente cuando `θz = ω` (rotación
rígida: `ω=θ`, `θz=θ` → energía nula), preservando los 6 modos rígidos. El precio
es que **acopla** los GDL de membrana `(ux,uy)` con `θz` — por eso el bloque es
membrana+drilling de 12×12, no membrana 8×8 + drilling 4×4 separados.

Integración del término de drilling: cuadratura de Gauss 2×2 (consistente con
`membrana`), usando `B_ω = [½∂N/∂x para uy, −½∂N/∂y para ux]` y las funciones de
forma `N` para `θz`.

### Orden de GDL local (contrato con M2b)
Por nodo `a` (0..3), los 6 GDL locales en el orden global del frame:
```
[ux_a, uy_a, uz_a, θx_a, θy_a, θz_a]
```
El GDL global del shell `g(a, d) = 6·a + d`, con `d ∈ {0:ux,1:uy,2:uz,3:θx,4:θy,5:θz}`.
Mapeos a los bloques fuente:
- Membrana M1 (orden `[ux,uy]×4`): local `2a+{0,1}` → shell `6a+{0,1}`.
- Placa (orden `[w,θx,θy]×4`): local `3a+{0,1,2}` → shell `6a+{2,3,4}`.
- Drilling: shell `6a+5`.

## Diseño técnico

Módulo `src/motor_fea/core/shell.py`. Unidades SI.

```
rigidez_shell(nodos_xy: list[tuple[float, float]],
              E: float, nu: float, t: float,
              gamma: float | None = None) -> list[list[float]]
```

`nodos_xy` = 4 pares (x, y) **en el plano local del elemento**, orden antihorario
(M2b proveerá estas coordenadas proyectando el muro 3D a su plano). `gamma` = factor
de drilling; si `None`, usa `E·t` por defecto. Devuelve K (24×24) simétrica.

Pasos internos:
1. `Km = rigidez_membrana(nodos_xy, E, nu, t)` (8×8).
2. `Kp = rigidez_placa(...)` de `placa.py` (12×12). Nota: `placa.rigidez_placa`
   toma `(lx, ly, E, nu, t)` para un rectángulo; M2a debe alimentarlo con la
   geometría rectangular equivalente del elemento (los muros llenos de M3 se
   mallan en rectángulos; un quad general de placa queda fuera de alcance M2a y
   se documenta como limitación — la membrana sí es isoparamétrica general).
3. `Kd` = bloque membrana+drilling 12×12 = `Km` embebido en los GDL (ux,uy) +
   término de penalización de Hughes–Brezzi en (ux,uy,θz).
4. Ensamblar `Km+drilling` (12×12) y `Kp` (12×12) en la 24×24 según el mapeo de GDL.

### Helper de drilling
```
_rigidez_drilling(nodos_xy, E, t, gamma) -> list[list[float]]
```
Devuelve la 12×12 en GDL `(ux,uy,θz)×4` con la penalización de Hughes–Brezzi
(Gauss 2×2). Se suma a `Km` embebido en sus GDL (ux,uy) para formar el bloque
membrana+drilling.

## Arquitectura e interfaces
- **Dependencias:** `membrana` (M1) y `placa` (existente), ambos del mismo paquete
  `core`. Solo `math` de stdlib además.
- **Consumidores:** ninguno aún. M2b importará `rigidez_shell` para transformar y
  ensamblar.
- **No modifica** archivos existentes; añade `shell.py` + `tests/test_shell.py`.

## Estrategia de pruebas (TDD, RED→GREEN por test)

`tests/test_shell.py`:

1. **Forma y simetría:** `rigidez_shell` devuelve 24×24 simétrica.
2. **Bloques desacoplados membrana↔placa:** las entradas cruzadas entre GDL de
   membrana `(ux,uy)` y GDL de placa `(uz,θx,θy)` son exactamente 0.
3. **Reducción a M1:** el sub-bloque de GDL `(ux,uy)×4` de la 24×24, con el término
   de drilling desactivado (`gamma=0`), coincide con `rigidez_membrana`.
4. **Reducción a placa:** el sub-bloque de GDL `(uz,θx,θy)×4` coincide con
   `rigidez_placa`.
5. **Modos de cuerpo rígido (6):** los 6 modos rígidos del shell plano local
   (3 traslaciones ux,uy,uz; 2 rotaciones fuera del plano θx,θy; 1 rotación en el
   plano con `ux=−θ·y, uy=θ·x, θz=θ`) dan energía ≈ 0. **Crítico:** el modo de
   rotación en el plano valida que el drilling de Hughes–Brezzi se anula.
6. **Drilling no nulo:** con `gamma>0`, un patrón de drilling diferencial
   (`θz` no uniforme, nodos fijos) da energía estrictamente positiva — el GDL θz
   ya no es mecanismo.
7. **Rango:** rango de K = 24 − 6 (exactamente 6 modos de energía nula),
   verificado por positividad de la 18×18 reducida tras fijar 6 GDL que matan los
   modos rígidos (pivotes de eliminación gaussiana > 0).

## Criterios de aceptación
- `shell.py` añadido; `test_shell.py` con los 7 tests en verde.
- Suite completa verde (288 + nuevos), sin regresiones.
- Stdlib puro, sin NumPy, sin I/O; docstrings/anotaciones al estilo del paquete.
- El docstring documenta: shell **plano** (membrana/placa desacopladas), drilling
  como estabilización de Hughes–Brezzi (no física), y la limitación de placa
  rectangular (la membrana es isoparamétrica general).

## Riesgos y mitigaciones
- **Modo de rotación en el plano espurio:** mitigado eligiendo Hughes–Brezzi (no
  diagonal); el test 5 lo verifica explícitamente.
- **Placa solo rectangular:** documentado; M3 mallará muros llenos en rectángulos,
  donde aplica. Quad general de placa = mejora futura.
- **Valor de `gamma`:** parametrizado con defecto `E·t`; si M2b/M2c muestran
  sensibilidad, se ajusta en un único punto.
