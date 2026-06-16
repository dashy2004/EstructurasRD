# Síntesis FEA · Columnas → malla estructural — Diseño (Rebanada B0)

**Fecha:** 2026-06-16
**Estado:** aprobado (alcance + diseño confirmados por el usuario)
**Predecesor:** `2026-06-15-modelo-canonico-edificio-design.md` (Rebanada A — modelo canónico)

## Problema

La Rebanada A entregó el modelo de **autoría** (`motor_fea.edificio`: Proyecto→Edificio→Nivel→Losa + verticales continuas) y el motor ya tenía la **malla FEA** (`motor_fea.core.modelo`: Nodo/ElementoFrame/Material/Seccion/Apoyo). **No existe el puente entre ambas:** hoy los flujos de integración arman la malla a mano. Sin síntesis, autorar un edificio no produce un análisis.

## Alcance (decisión del usuario)

**Solo columnas, geometría.** Esta rebanada traduce columnas continuas a una malla de barras con nodos compartidos, ancla zapatas como apoyos, mapea material y sección, y transporta las losas como geometría inerte para el visor.

**Fuera de alcance (rebanadas siguientes):**
- Muros (modelado como barra equivalente o placa — decisión propia).
- Bajada de cargas (losa → cargas nodales por área tributaria) — Rebanada B con su propia normativa.
- Multi-edificio fusionado en una sola malla.

## Arquitectura

Módulo nuevo `src/motor_fea/edificio/sintesis.py`. Función pura, sin I/O:

```
sintetizar(edificio: Edificio) -> ModeloEstructural
```

Depende de `motor_fea.edificio.modelo` (entrada) y `motor_fea.core.modelo` (salida). **Precondición:** el edificio proviene de un `Proyecto` válido (`validar() == []`); la síntesis no re-valida la autoría, pero **garantiza** que su salida pasa `ModeloEstructural.validar()`.

Re-exportada desde `motor_fea.edificio.__init__`.

## Las traducciones (unidades aisladas)

Cada una es una pieza con un propósito y testeable por separado. Todas usan SI (metros, pascales, newtons) — coherente con `core.modelo`.

### 1. Nodos por quiebre + nodos compartidos
Para una columna de `cota_base → cota_tope`, los **quiebres** son:
```
quiebres = sorted({cota_base, cota_tope} ∪ {n.cota : n ∈ niveles, cota_base < n.cota < cota_tope})
```
Un nodo por quiebre, en `(x, y, z) = (posicion.x, posicion.y, quiebre)`.

**Deduplicación:** un registro `(x, y, z)` cuantizado a tolerancia de mm (`round(·, 6)`) mapea a un único `Nodo`. Dos verticales que comparten una coordenada comparten el nodo. Esto habilita la conexión futura de vigas/diafragma y evita un edificio desconectado.

IDs de nodo: enteros secuenciales desde 1, asignados en orden de descubrimiento (columnas en orden de autoría, quiebres ascendentes).

### 2. Barras (ElementoFrame) entre quiebres consecutivos
Por cada par de quiebres consecutivos de una columna, un `ElementoFrame(nodo_i, nodo_j, material_id, seccion_id)`. Una columna 0→6 con niveles 0/3/6 produce 2 barras (0→3, 3→6) que comparten el nodo z=3. `vector_referencia` queda en su valor por defecto `(0,0,1)`; el solver B1 ya ajusta la orientación de barras verticales. IDs secuenciales desde 1 en orden de autoría.

### 3. Apoyos (zapata → empotramiento)
Si la columna tiene `zapata`, su **nodo base** (el de `cota_base`) recibe `Apoyo.empotrado(nodo_id)`. Columna sin zapata: sin apoyo (diferido). Un mismo nodo base compartido por dos columnas con zapata recibe un solo apoyo (deduplicado por nodo).

### 4. Material (string → Material)
Convención dominicana `H{n}`: `n` = f'c en kg/cm². Módulo elástico ACI 318:
```
E[kg/cm²] = 15100 · √n      →      E[Pa] = E[kg/cm²] · 98066.5
```
`nu = 0.2`, `densidad = 2400` (valores por defecto de `Material`). Catálogo deduplicado por string de material → un `Material` por string distinto; IDs secuenciales desde 1. Un string no reconocido (no `H{n}`) lanza `ValueError` legible.

### 5. Sección (base×peralte → Seccion)
Columna rectangular `base (b) × peralte (h)`:
```
area      = b · h
inercia_z = b · h³ / 12        (flexión alrededor del eje fuerte, h vertical en sección)
inercia_y = h · b³ / 12
J         = a · t³ · [1/3 − 0.21 · (t/a) · (1 − t⁴/(12 a⁴))]   con a = max(b,h), t = min(b,h)
```
La fórmula de torsión rectangular reproduce β≈0.1406 para sección cuadrada. Deduplicada por `(base, peralte)` cuantizado → una `Seccion` por par distinto; IDs secuenciales desde 1.

### 6. Losas (Losa → LosaViz)
Por cada nivel y cada losa del nivel, un `LosaViz(id, puntos)` con `puntos = nivel.puntos_losa_3d(losa)` (contorno en planta elevado a la cota del nivel). Dato inerte: el FEA lo ignora, el visor lo dibuja. IDs de `LosaViz` secuenciales desde 1 (independientes de los `Losa.id` de autoría, que pueden repetirse entre niveles).

## Garantías y errores
- La salida pasa `ModeloEstructural.validar()` (integridad referencial total).
- Determinismo: misma entrada → misma malla (IDs incluidos).
- Material no reconocido → `ValueError` con el string ofensor.
- Edificio sin columnas → malla con solo losas (válida, sin barras): la síntesis no inventa estructura.

## Testing
`tests/test_sintesis_fea.py`:
1. Columna 0→6 con niveles 0/3/6 + zapata → 3 nodos, 2 barras, nodo z=3 compartido, empotramiento en base.
2. Dos columnas que comparten coordenada base → nodo y apoyo deduplicados.
3. Material `H210` → E ≈ 2.146e10 Pa (tolerancia); string inválido → `ValueError`.
4. Sección cuadrada 0.30 → area/inercias correctas y J ≈ 0.1406·0.30⁴.
5. Losas → `LosaViz` con z = cota del nivel.
6. La salida de un edificio demo pasa `ModeloEstructural.validar()`.
7. Determinismo: dos síntesis del mismo edificio son iguales.

## Self-review (cobertura)
| Requisito | Cubierto en |
|---|---|
| Puente autoría→FEA | `sintetizar()` |
| Nodos compartidos | Traducción 1 (dedup por coordenada) |
| Columna continua → barras segmentadas | Traducción 2 |
| Zapata → apoyo | Traducción 3 |
| Material/sección desde autoría | Traducciones 4–5 |
| Losa para visor | Traducción 6 |
| Salida válida + determinista | Garantías + tests 6–7 |
