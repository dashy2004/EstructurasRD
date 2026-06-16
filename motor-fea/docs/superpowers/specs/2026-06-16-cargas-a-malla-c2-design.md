# Cargas de losa → malla FEA (cargas nodales equivalentes) — Diseño (Rebanada C2)

**Fecha:** 2026-06-16
**Estado:** aprobado (alcance + decisiones técnicas asumidas; usuario no programa)
**Predecesor:** `2026-06-16-bajada-cargas-losa-design.md` (Rebanada C — reparto losa→bordes)

## Problema

C entregó `repartir_losa(losa) -> RepartoLosa` (cargas de borde en kN). B0 entregó la malla (`ModeloEstructural`: nodos/barras/apoyos). Falta el último eslabón: **colgar esas cargas de la malla** para que el solver produzca deformada. El modelo core **solo admite cargas nodales** (`CargaNodal`, sin cargas distribuidas) y B0 **no sintetiza vigas** — solo columnas verticales. Por tanto las cargas de borde deben convertirse en **cargas nodales equivalentes** sobre los nodos existentes (tops de columna a la cota del nivel).

## Alcance (decisiones técnicas)

Función **separada y componible** (no se mete en `sintetizar()`, para no romper B0 ni mismodelar):

```
cargas_de_losas(edificio: Edificio, modelo: ModeloEstructural) -> list[CargaNodal]
```

No muta `modelo`; devuelve la lista que el caller añade: `modelo.cargas.extend(cargas_de_losas(edi, modelo))`. Re-exportada desde `motor_fea.edificio`.

**Decisiones:**
1. **Equivalente nodal 50/50.** Las distribuciones triangular/trapezoidal/uniforme son simétricas respecto al centro del borde → resultante en el punto medio → mitad de `fuerza_total` a cada nodo extremo del borde. (Solo resultante de fuerza; sin momentos de empotramiento — el modelo no tiene vigas que los reciban.)
2. **Gravedad → `fz < 0`** (vertical = `GDL.UZ`).
3. **Casos D/L separados** (de C): muerta → `caso="D"`, viva → `caso="L"`. Ambos en `CASOS_CARGA`.
4. **Unidades kN→N.** `CargasLosa`/reparto en kN; el core es SI en newtons (E en Pa). Factor ×1000 al colgar.
5. **Esquina sin nodo → `ValueError`** nombrando losa y coordenada (falla fuerte: en B0, sin vigas, una esquina de losa sin columna no tiene cómo bajar su carga).

**Fuera de alcance (siguientes):**
- Vigas perimetrales reales (barra horizontal borde-a-borde) + cargas distribuidas de elemento.
- Columnas a mitad de borde (hoy solo se cargan los **extremos** del borde).
- Peso propio de losa por espesor·densidad (hoy solo `CargasLosa.muerta`/`.viva` autoradas).

## Algoritmo

1. Mapa `coord → node_id` desde `modelo.nodos`, clave `(round(x,6), round(y,6), round(z,6))` (idéntica a `sintetizar`).
2. Acumulador `accum: {(node_id, caso): fz_N}`.
3. Por cada `nivel` (en orden) y cada `losa` del nivel:
   - `rep = repartir_losa(losa)`.
   - Para `(caso, dir)` en `[("D", rep.muerta), ("L", rep.viva)]`:
     - Por cada `borde` de `dir.bordes`: extremos = `puntos[i]`, `puntos[(i+1)%n]`, elevados a `z = nivel.cota`. Cada extremo → `node_id` (o `ValueError`). `accum[(node_id, caso)] -= borde.fuerza_total/2 · 1000`.
4. Emitir `CargaNodal(nodo_id, fz=accum, caso=caso)` por entrada, **ordenado por `(node_id, caso)`** (determinismo).

Casos con `dir.bordes == ()` (q≤0) no aportan nada.

## Garantías
- **Conservación:** Σ `fz` del caso D == `−1000 · Σ q_muerta·A_paño` (ídem L). Cada borde reparte exactamente su `fuerza_total`.
- **Determinismo:** misma entrada → misma lista (orden por nodo/caso).
- **No-mutación** de `modelo`; salida válida para `esfuerzos_por_caso`.
- Esquina de losa sin nodo → `ValueError` con losa + coordenada.

## Testing
`tests/test_cargas_a_malla.py` (edificio con **columnas en las 4 esquinas**):
1. **Paño cuadrado** 5×5, muerta=10, 4 columnas: cada esquina recibe `fz = −62500 N` (2 bordes × 62.5/2 × 1000); Σ D = −250000.
2. **D y L separados:** muerta=10/viva=4 → Σ caso D = −250000, Σ caso L = −100000.
3. **Conservación rectangular** 4×8, muerta=10: Σ D = −320000; esquina = −80000 (corto 40/2 + largo 120/2, ×1000).
4. **Esquina sin columna → `ValueError`** (losa 5×5 con solo 2 columnas).
5. **Determinismo:** dos llamadas dan listas iguales.
6. **Integración:** `m.cargas.extend(...)` → `m.validar()==[]` y `esfuerzos_por_caso(m)` corre con casos D y L.

## Self-review
| Requisito | Cubierto en |
|---|---|
| Borde → nodal equivalente (50/50) | algoritmo §3 + tests 1,3 |
| D/L separados con caso | decisión 3 + test 2 |
| Conservación (kN→N) | garantía + tests 1–3 |
| Falla fuerte si esquina sin apoyo | decisión 5 + test 4 |
| Componible, no rompe B0 | función separada + test 6 |
| Determinista | garantía + test 5 |
