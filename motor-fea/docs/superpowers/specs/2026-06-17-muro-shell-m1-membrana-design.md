# M1 — Elemento de membrana (Q4 tensión plana)

**Fecha:** 2026-06-17
**Estado:** diseño aprobado, pendiente de plan de implementación
**Rama destino:** `engine/shell-web-webxr` (o rama nueva `engine/muro-shell-m1`)

## Contexto y motivación

El refinamiento "muro como shell" busca, en su alcance completo, modelar muros
de cortante con su **rigidez en el plano** real, recuperar su **campo de
esfuerzos** y diseñar su acero. Hoy la síntesis FEA (rebanada B1) representa el
muro como una **columna ancha equivalente** (sección rectangular t×L con eje
fuerte orientado al plano del muro). Esa simplificación captura razonablemente la
flexión de un muro esbelto en voladizo, pero pierde el campo de esfuerzos en el
plano, el comportamiento de muros bajos (cortante-dominados) y el acoplamiento.

El trabajo total se descompone en cinco sub-rebanadas independientes, cada una
con spec + TDD propio:

| # | Rebanada | Entrega |
|---|---|---|
| **M1** | **Elemento de membrana** (este spec) | Rigidez + recuperación de esfuerzos en el plano, a nivel de elemento. Autónomo. |
| M2 | Integración al solver del edificio | Tipo "shell" ensamblable en `ModeloEstructural` (6 GDL/nodo); compatibilidad con nodos de columna/viga (drilling DOF). |
| M3 | Síntesis muro → malla de shells | `sintetizar()` mallea el muro en lugar de la columna ancha; conecta la malla a columnas y niveles. |
| M4 | Recuperación de esfuerzos + diseño | Esfuerzos del muro → fuerzas de diseño → acero (ACI). |
| M5 | Visor | Muro mallado: deformada + heatmap de esfuerzos en WebXR. |

**Decisión de dominio:** se diseña para **muros llenos** (sin aberturas)
primero; el elemento y el futuro mallado quedan preparados para extender a muros
con aberturas (acoplados) en una tanda posterior.

M1 es la pieza fundacional: un elemento autónomo y testeable de forma aislada,
sin tocar el solver ni la síntesis del edificio, igual que `core/placa.py`
(flexión de placa ACM) precedió y habilitó a `core/losa_fem.py`.

## Alcance de M1

### Dentro de alcance
- Un módulo nuevo `src/motor_fea/core/membrana.py`, en el mismo estilo que
  `core/placa.py`: stdlib puro (sin NumPy), unidades SI, funciones puras.
- Elemento **Q4 isoparamétrico bilineal de tensión plana** (plane stress), 4
  nodos, 2 GDL/nodo (ux, uy) → 8 GDL.
- Matriz de rigidez 8×8 por cuadratura de Gauss 2×2.
- Recuperación del campo de esfuerzos (σxx, σyy, τxy) en un punto natural.
- Admite cuadriláteros generales (no solo rectángulos) vía jacobiano — un muro
  inclinado o de planta no rectangular funciona sin trabajo extra.

### Fuera de alcance de M1 (rebanadas posteriores)
- **Drilling DOF** (rotación θz en el plano) y compatibilidad con nodos frame de
  6 GDL → M2. M1 entrega un elemento de 2 GDL/nodo; el acople es un problema de
  *ensamblaje*, no del elemento.
- Ensamblaje en el solver del edificio, condiciones de borde, resolución global → M2.
- Mallado del muro, conexión a columnas/niveles → M3.
- Diseño de acero, normativa ACI → M4.
- Cualquier cosa de visor → M5.
- Aberturas/muros acoplados, plasticidad, no linealidad, plane *strain*.

## Decisión de formulación

Se eligió la **opción A — Q4 bilineal de tensión plana** sobre las alternativas
(B: Q4 con drilling DOF tipo Allman/GQ12; C: Q4 + modos incompatibles de Wilson).

**Por qué A:**
- Mínima y robusta: pasa el patch test de esfuerzo constante y reproduce
  tracción/cortante puros contra solución analítica.
- Espeja el patrón ya validado del motor (`placa.py`: elemento simple primero,
  integración después).
- Aísla el problema del drilling DOF en M2, donde es un problema de acople real,
  en vez de contaminar el elemento base.

**Hipótesis de tensión plana (plane stress):** correcta para muros, cuyo espesor
es pequeño frente a las dimensiones en el plano (σzz ≈ 0). Plane strain (ε fuera
del plano ≈ 0) sería para sólidos confinados (presas, túneles) y no aplica.

**Limitación conocida y aceptada:** el Q4 bilineal sufre *shear locking* parásito
en flexión en el plano para muros muy esbeltos con malla gruesa. Se acepta en M1
porque (a) M3 mallará el muro en varios elementos por dimensión, mitigándolo, y
(b) la opción C (modos incompatibles) queda como mejora futura si la verificación
de M4 lo exige. M1 documenta la limitación; no la resuelve.

## Diseño técnico

Módulo `src/motor_fea/core/membrana.py`. Convención de unidades SI: longitudes en
m, E en Pa, t en m → rigidez en N/m, esfuerzos en N/m² (Pa).

### Constitutiva de tensión plana

```
constitutiva_plana(E: float, nu: float) -> list[list[float]]
```

Devuelve la matriz D (3×3) de tensión plana:

```
D = E/(1−ν²) · [[1,  ν,  0       ],
                [ν,  1,  0       ],
                [0,  0,  (1−ν)/2 ]]
```

relacionando esfuerzos [σxx, σyy, τxy] con deformaciones [εxx, εyy, γxy].

### Funciones de forma y B

Funciones de forma bilineales Q4 en coordenadas naturales (ξ, η) ∈ [−1,1]²:

```
N_a(ξ,η) = ¼ (1 + ξ_a ξ)(1 + η_a η),  a = 1..4
```

con esquinas (ξ_a, η_a) = (−1,−1), (1,−1), (1,1), (−1,1) — orden antihorario,
consistente con el orden de nodos de `placa.py`.

La matriz B (3×8) (deformaciones a partir de los 8 GDL) se obtiene de las
derivadas de N respecto a (x, y), usando el jacobiano del mapeo
isoparamétrico:

```
J = [[∂x/∂ξ, ∂y/∂ξ], [∂x/∂η, ∂y/∂η]]     (2×2)
[∂N/∂x; ∂N/∂y] = J⁻¹ · [∂N/∂ξ; ∂N/∂η]
```

`detJ` debe ser positivo (nodos en orden antihorario); se valida y se lanza
`ValueError` si `detJ ≤ 0` (elemento degenerado o mal orientado).

### Rigidez del elemento

```
rigidez_membrana(nodos_xy: list[tuple[float, float]],
                 E: float, nu: float, t: float) -> list[list[float]]
```

`nodos_xy` = 4 pares (x, y) en metros, orden antihorario. Devuelve K (8×8):

```
K = t · ∫∫ Bᵀ D B dA = t · Σ_g  w_g · Bᵀ(ξ_g,η_g) D B(ξ_g,η_g) · detJ(ξ_g,η_g)
```

por Gauss 2×2 (puntos ±1/√3, pesos 1). Orden de GDL local:
`[ux1, uy1, ux2, uy2, ux3, uy3, ux4, uy4]`.

### Recuperación de esfuerzos

```
esfuerzos_elemento(nodos_xy, E, nu, d_elem, xi=0.0, eta=0.0)
    -> tuple[float, float, float]
```

`d_elem` = 8 GDL nodales en el mismo orden que `rigidez_membrana`. Devuelve
(σxx, σyy, τxy) = D · B(ξ,η) · d_elem, en Pa. El centro (0,0) es el punto de
muestreo por defecto (punto de superconvergencia del Q4).

### Utilidad

`matvec(K, x)` (producto matriz·vector) si hace falta para los tests de cuerpo
rígido — o se reutiliza el existente; no se duplica.

## Arquitectura e interfaces

- **Dependencias de M1:** solo `math` de stdlib. NO depende de `solver.py`
  (a diferencia de `placa.py`, que lo usa para invertir C; el Q4 no necesita
  inversión de matriz porque integra B directamente). Esto deja M1 totalmente
  autónomo.
- **Consumidores de M1:** ninguno todavía. M2 importará `rigidez_membrana` y
  `esfuerzos_elemento` para ensamblar y recuperar. M1 no modifica ningún archivo
  existente — solo añade `membrana.py` y `tests/test_membrana.py`.
- **Aislamiento:** el elemento se entiende y prueba sin el resto del motor; su
  contrato son tres funciones puras con entradas/salidas numéricas.

## Estrategia de pruebas (TDD, RED→GREEN por test)

`tests/test_membrana.py`. Cada test se escribe RED primero y se implementa hasta
GREEN, sin regresiones en la suite (hoy 276 verde).

1. **Constitutiva:** `constitutiva_plana` reproduce los valores cerrados de D
   para E, ν conocidos (incluye el término (1−ν)/2 del cortante).
2. **Modos de cuerpo rígido:** traslación uniforme en x y en y → vector de
   fuerzas K·u ≈ 0 (energía nula). Verifica que no hay rigidez espuria.
3. **Rango de K:** K simétrica; rango = 8 − 3 (los 3 modos rígidos del plano:
   2 traslaciones + 1 rotación). Se comprueba que exactamente 3 modos rígidos
   anulan la energía (K·u ≈ 0 para traslación-x, traslación-y y rotación
   infinitesimal en torno al centroide).
4. **Patch test de esfuerzo constante:** imponer un campo de desplazamiento lineal
   (u = a + b·x + c·y) en los 4 nodos → los esfuerzos recuperados en el centro
   reproducen el esfuerzo constante exacto. Criterio de convergencia del FEM.
5. **Tracción uniaxial pura:** panel cuadrado L×L×t bajo desplazamiento impuesto
   que genera tracción uniaxial → σxx = E·εxx con εyy según ν, σyy=τxy=0;
   comparar con solución analítica (tolerancia numérica).
6. **Cortante puro:** campo de cortante → τxy constante correcto, σxx=σyy=0.
7. **Elemento no rectangular:** un cuadrilátero general (p.ej. trapecio) con
   `detJ` variable produce K simétrica y pasa el patch test (valida el jacobiano).
8. **Guarda de degeneración:** nodos en orden horario o colineales → `ValueError`.

## Criterios de aceptación

- `membrana.py` añadido; `test_membrana.py` con los 8 tests en verde.
- Suite completa sigue verde (276 + nuevos), sin regresiones.
- Sin NumPy, sin I/O, stdlib puro, anotaciones de tipo y docstrings al estilo de
  `placa.py`.
- La limitación de shear locking queda documentada en el docstring del módulo.

## Riesgos y mitigaciones

- **Shear locking en flexión en el plano:** documentado; mitigado por mallado en
  M3; opción C disponible como mejora futura.
- **Orden de nodos / signo del jacobiano:** validado explícitamente (test 7, 8).
- **Drilling DOF / acople con frames:** explícitamente diferido a M2; M1 no lo
  intenta para no contaminar el elemento base.
