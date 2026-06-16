# Síntesis FEA · Muros → columna ancha equivalente — Diseño (Rebanada B1)

**Fecha:** 2026-06-16
**Estado:** aprobado (enfoque "columna ancha equivalente" confirmado por el usuario)
**Predecesor:** `2026-06-16-sintesis-fea-columnas-design.md` (B0 — columnas → malla)

## Problema

B0 sintetiza columnas pero **salta los muros** (`if not isinstance(col, Columna): continue`). El `ModeloEstructural`/solver solo ensambla `ElementoFrame` (no hay placas en el modelo ensamblado), así que el muro se modela como **columna ancha equivalente**: una barra vertical en la línea-centro del muro, con sección rectangular `espesor × longitud` orientada para que su eje fuerte quede en el plano del muro. Reutiliza todo el camino de B0 (nodos compartidos, material, zapata→apoyo).

## Alcance (decisión del usuario)

Muro (`linea=((x1,y1),(x2,y2))`, `espesor`, `cota_base→cota_tope`, `material`, `zapata`) → barra(s) vertical(es) en el **centroide** de la línea. **No** placa/shell (requeriría integrar un tipo de elemento nuevo en el ensamblaje — fuera de alcance).

**Fuera de alcance:** acoplamiento muro-losa por todo el borde, aberturas (puertas/ventanas), muros no rectos, conexión a las columnas de sus extremos por brazos rígidos.

## Las traducciones

### 1. Posición = centroide de la línea
Nodo en `((x1+x2)/2, (y1+y2)/2, z)` por cada quiebre `z` (reusa `_quiebres`: extremos + cotas de nivel intermedias). Dedup por coordenada igual que B0 (un muro y una columna que compartan centroide comparten nodo).

### 2. Sección rectangular `espesor (t) × longitud (L)`
`L = hypot(x2−x1, y2−y1)`. Con el eje fuerte en el plano del muro:
```
area      = t · L
inercia_z = t · L³ / 12     (FUERTE, en el plano del muro)
inercia_y = L · t³ / 12     (débil, fuera del plano)
J         = _torsion_rectangular(t, L)
```
Deduplicada por `("muro", round(t), round(L))` (no colisiona con la clave `(base, peralte)` de columnas). IDs secuenciales compartidos.

### 3. Orientación (`vector_referencia`)
`vector_referencia = (−dy, dx, 0)` con `(dx, dy) = (x2−x1, y2−y1)`. En `triada_local`, para una barra vertical esto alinea el eje local `ey` con la línea del muro, de modo que `inercia_z` (EIz, eje fuerte) gobierna la flexión **en el plano**. (Las columnas siguen con `(0,0,1)`, idéntico a B0.)

### 4. Barras y apoyo
Una `ElementoFrame(ni, nj, mat, sec, vector_referencia)` por par de quiebres consecutivos. Zapata → `Apoyo.empotrado` en el nodo base (centroide a `cota_base`), deduplicado por nodo igual que B0.

## Garantías
- La salida sigue pasando `ModeloEstructural.validar()` y es determinista.
- Columnas inalteradas: su rama no cambia de comportamiento (mismo `vector_referencia` por defecto, mismas secciones/IDs).
- Un muro de longitud nula (línea degenerada) no debería llegar (lo veta la validación de autoría); si llega, `_torsion_rectangular`/sección darían 0 — aceptable.

## Testing
`tests/test_sintesis_muros.py`:
1. **Muro → barra en el centroide:** línea `((0,0),(4,0))`, t=0.2, 0→3 → nodo en `(2,0,·)`, 1 barra.
2. **Sección:** `area=0.8`, `inercia_z≈1.0667` (t·L³/12, fuerte), `inercia_y≈0.002667` (débil).
3. **Orientación:** muro diagonal `((0,0),(3,4))` → `vector_referencia==(−4,3,0)` (⟂ a la línea, eje fuerte en el plano).
4. **Zapata → apoyo** en el nodo base del muro (centroide a `cota_base`).
5. **Muros + columnas conviven:** salida pasa `validar()`, determinista.
6. **Muro atraviesa nivel intermedio:** 0→6 con nivel en 3 → 2 barras que comparten el nodo z=3.

## Self-review
| Requisito | Cubierto en |
|---|---|
| Muro → barra equivalente en centroide | Traducción 1 + tests 1,6 |
| Sección t×L con eje fuerte en el plano | Traducción 2 + test 2 |
| Orientación correcta | Traducción 3 + test 3 |
| Zapata → apoyo | Traducción 4 + test 4 |
| No rompe columnas (B0) / válido / determinista | Garantías + test 5 |
