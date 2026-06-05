# Diseño — Visor estructural WebXR (Fase 3: heatmaps en losas + tocar→valor)

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito
**Depende de:** Fase 1 (`2026-06-05-visor-webxr-design.md`) y Fase 2 (`2026-06-05-visor-webxr-fase2-design.md`), ambas implementadas.
**Alcance:** Fase 3 — mapa de color (deflexión + momentos Mx/My) sobre la superficie de una losa, con relieve deflectado y "tocar un punto → valor".

---

## 1. Objetivo

Mostrar el comportamiento de una **losa** en el visor: su superficie coloreada por
un campo escalar (deflexión o momento flector), abombada según la deformada, y la
posibilidad de **tocar un punto y leer el valor** interpolado del campo activo.
Sirve para revisión de ingeniería, presentación, demo y educación (los cuatro
propósitos de las fases previas). Reusa el FEM de losas existente (`losa_fem`).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Magnitudes | **Deflexión + momentos Mx/My.** Requiere un cambio aditivo y chico en `losa_fem` (campo nodal de momentos); el momento gobierna el acero. |
| Convivencia con el pórtico | **Estados de losa en el panel actual.** Al elegir un estado de losa se oculta el pórtico y se muestra la losa; una sola app reusa panel, raycaster y bucle. |
| Render de la superficie | **Relieve deflectado + color.** La superficie cae `z = −w·exag` (reusa el slider de Fase 2) y se colorea por el campo activo. |
| Fuente de la losa | **Autónoma.** `ModeloEstructural` no tiene losa; `losa_fem` es un análisis rectangular independiente. El endpoint sirve una losa de ejemplo con parámetros por defecto. |

## 3. Arquitectura

La losa es un análisis autónomo. Fase 3 = **un cambio aditivo en `core/`** + una unidad
de frontera nueva + cambios en el visor. `normativa/` y el pipeline de diseño
(`diseno_losa.py`) **no se tocan**.

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Campo nodal de momentos | `src/motor_fea/core/losa_fem.py` (mod, **aditivo**) | `ResultadoLosa` gana `momentos_nodales: dict[(i,j) → (mx, my)]`, promediando a cada nodo los momentos de los elementos adyacentes. |
| Empaquetado para el visor | `src/motor_fea/viz/resultados_losa.py` (nuevo, **puro**) | `calcular_resultados_losa(...) → LosaDTO`: malla + campos por nodo (deflexión, Mx, My) con unidades, min/max y `factor_sugerido`. Solo usa `core`. |
| Endpoint | `src/motor_fea/api/servidor.py` (mod) | `GET /losa` → LosaDTO (losa de ejemplo por defecto); `ValueError → 400`. |
| Visor | `src/motor_fea/viz/static/{app.js, index.html}` (mod) | Estados de losa en el panel + superficie coloreada con relieve + picking tocar→valor. |

## 4. Cambio en `core` (mínimo y aditivo)

`resolver_losa_rectangular` ya recorre los elementos calculando `momentos_elemento`
en el centro `(0.5, 0.5)` para quedarse con los máximos. Se extiende **ese mismo
loop** para acumular el `(Mx, My)` del centro de cada elemento en los 4 nodos de su
celda, y al terminar se promedia por la cantidad de elementos adyacentes:

```
momentos_nodales[(i,j)] = ( Σ mx_centro(elem adyacentes) / n_adyacentes,
                            Σ my_centro(elem adyacentes) / n_adyacentes )
```

- **No** añade evaluaciones nuevas del FEM: reusa el `(mx, my)` del centro que el loop
  ya calcula. Nodo interior promedia 4 elementos; borde 2; esquina 1.
- `desplazamientos_w`, `w_central` y los máximos (`mx_max`, `my_max`, `mxy_max`,
  `m_apoyo_max`) quedan **idénticos** → `diseno_losa.py` y sus tests no cambian.
- El nuevo campo es aditivo en la dataclass (`momentos_nodales: dict[tuple[int,int],
  tuple[float, float]] = field(default_factory=dict)`), retro-compatible.

## 5. Contrato `LosaDTO`

```jsonc
{
  "a": 5.0, "b": 5.0, "nx": 8, "ny": 8,            // grilla: nodo (i,j) en (x=i·a/nx, y=j·b/ny), z=0 base
  "factor_sugerido": 180.0,                         // relieve z = −w·exag (fórmula de Fase 2)
  "campos": {
    "deflexion":  { "unidad": "mm",     "min": 0.0,  "max": 12.3, "valores": { "4,4": 12.3, … } },
    "momento_mx": { "unidad": "kN·m/m", "min": -3.1, "max": 8.7,  "valores": { "4,4": 8.7, … } },
    "momento_my": { "unidad": "kN·m/m", "min": -3.1, "max": 8.7,  "valores": { … } }
  }
}
```

- Claves de nodo `"i,j"` como **string** (consistente con Fase 2).
- Deflexión en **mm** (`w·1000`); momentos en **kN·m/m** (`N·m/m ÷ 1000`). La `unidad`
  viaja por campo para que el visor etiquete.
- `min`/`max` por campo: para normalizar la rampa de color en el cliente sin recorrer todo.
- `factor_sugerido = 0.08 · √(a²+b²) / max|w|` (en metros); `1.0` si `max|w| = 0`.

## 6. Cálculo en el servidor (`resultados_losa.py`)

`calcular_resultados_losa(a=5.0, b=5.0, nx=8, ny=8, E=2.0e10, nu=0.2, t=0.2,
q=10000.0, borde="simple") -> dict`:

1. **Validación.** `a, b, t, q > 0`; `nx, ny ≥ 1`; `borde ∈ {"simple", "empotrado"}`.
   Si no → `ValueError` (→ 400 en el endpoint).
2. `res = resolver_losa_rectangular(a, b, nx, ny, E, nu, t, q, borde)`.
3. **Campos por nodo** (recorriendo `(i,j)` con `i∈[0,nx]`, `j∈[0,ny]`):
   - `deflexion["i,j"] = res.desplazamientos_w[(i,j)] · 1000`  (mm)
   - `momento_mx["i,j"] = res.momentos_nodales[(i,j)][0] / 1000`  (kN·m/m)
   - `momento_my["i,j"] = res.momentos_nodales[(i,j)][1] / 1000`  (kN·m/m)
4. `min`/`max` por campo sobre sus valores.
5. `factor_sugerido` con la fórmula de §5, usando `max|w|` en metros.

**Pureza:** `resultados_losa.py` no toca HTTP ni three.js; usa solo `core` (`losa_fem`).
Se testea con asserts normales.

**Endpoint** (en `servidor.py`, registrado **antes** del `app.mount(...)`):

```python
@app.get("/losa")
def losa():
    try:
        return calcular_resultados_losa()
    except ValueError as ex:
        raise HTTPException(status_code=400, detail=str(ex))
```

Sin parámetros: sirve la losa de ejemplo por defecto (5×5 m, 8×8, t=0.20 m,
q=10 kN/m², simplemente apoyada). Parámetros por query (`/losa?a=…`) quedan fuera
de alcance (Fase 3b).

## 7. UI del visor

**Panel** (al cargar `/losa`, el selector `#estado` gana opciones):
`losa: deflexión`, `losa: momento Mx`, `losa: momento My` (valores `losa-deflexion`,
`losa-momento_mx`, `losa-momento_my`).

**Render de la losa** (estado `losa-*`):
- Se ocultan las barras del pórtico (`mesh.visible = false`); en estados de pórtico se
  oculta la losa. Geometrías disjuntas, nunca superpuestas.
- Superficie como `BufferGeometry`: grilla `(nx+1)×(ny+1)` vértices, 2 triángulos por
  celda. Vértice `(i,j)` en `(x=i·a/nx, y=j·b/ny, z=−w·exag)`. `exag` se inicializa al
  `factor_sugerido` (rango `0 … factor_sugerido×5`, como Fase 2).
- **Color por vértice** (`MeshStandardMaterial({ vertexColors: true })`) según el campo
  activo, normalizado con `min/max`:
  - *deflexión* → rampa **secuencial**: `t=(v−min)/(max−min)`, `hue = (1−t)·240/360`
    (azul→rojo), `Color.setHSL(hue, 1, 0.5)`.
  - *momento* → rampa **divergente** centrada en 0: `M=max(|min|,|max|)`, `s=v/M`;
    `s<0` → lerp blanco→azul por `|s|`; `s≥0` → lerp blanco→rojo por `s`.
- `#info` muestra la leyenda del campo: `"momento Mx: −3.1 … 8.7 kN·m/m"`.

**Tocar → valor** (`pointerdown` sobre el canvas, fuera de VR):
- `Raycaster.setFromCamera(ndc, camera)` (reusa el patrón de raycasting del visor) →
  intersección con la malla de la losa.
- Del punto `(x, y)` (las coords planas no cambian con el relieve, que solo mueve `z`)
  se localiza la celda `ci=⌊x/(a/nx)⌋`, `cj=⌊y/(b/ny)⌋` (clamp a la grilla) y la
  posición natural `fx, fy ∈ [0,1]`.
- **Interpolación bilineal** de los 4 nodos de la celda:
  `v = (1−fx)(1−fy)·v[ci,cj] + fx(1−fy)·v[ci+1,cj] + fx·fy·v[ci+1,cj+1] + (1−fx)fy·v[ci,cj+1]`.
- `#info` muestra `"Mx = 5.4 kN·m/m @ (2.1, 3.0) m"`.

**Encuadre.** Al alternar entre el grupo pórtico y el grupo losa se reencuadra la
cámara a la bbox de lo visible (helper `encuadrar(min, max)` reutilizado).

**Estática.** La losa es un caso de carga estático: no oscila (sin término `sin`); el
bucle solo la mantiene renderizada. Los modos siguen oscilando solo en estados de
pórtico.

**VR:** el panel y el picking por puntero quedan ocultos/inactivos en sesión inmersiva
(como en Fase 2); la superficie sí se ve y se puede recorrer.

## 8. Manejo de errores

| Situación | Respuesta |
|---|---|
| Parámetros de losa inválidos (`a≤0`, `nx<1`, `borde` desconocido…) | `calcular_resultados_losa` lanza `ValueError`; endpoint → HTTP 400. |
| `/losa` no responde | El visor muestra el pórtico (de `/escena`) y los estados de Fase 2; los estados de losa simplemente no se agregan. No rompe la vista. |
| `max|w| = 0` (sin deflexión) | `factor_sugerido = 1.0`; no divide por cero. |
| Toque fuera de la losa | No hay intersección; `#info` mantiene la leyenda del campo. |

## 9. Testing

| Qué | Cómo | Notas |
|---|---|---|
| `core/losa_fem.py` | `test_losa_fem.py` (+1): losa cuadrada simplemente apoyada 4×4 → `momentos_nodales` cubre los `(4+1)²=25` nodos; en el nodo central `(2,2)` `mx ≈ my` (simetría); momento interior no nulo. `desplazamientos_w` y los máximos no cambian. | stdlib pura. |
| `viz/resultados_losa.py` | `test_resultados_losa.py` (nuevo): DTO con `campos == {deflexion, momento_mx, momento_my}`; deflexión central > 0; `len(valores) = (nx+1)(ny+1)`; `min/max` finitos; `factor_sugerido` finito y positivo; momento interior no nulo; unidades `"mm"`/`"kN·m/m"`; parámetros inválidos → `ValueError`. | stdlib pura; malla chica (4×4) para velocidad. |
| `api/servidor.py` | `test_servidor.py` (+1): `GET /losa` → 200; claves `{a,b,nx,ny,factor_sugerido,campos}`; 3 campos. | `importorskip` ya presente. |
| Visor JS | Smoke manual: elegir `losa: deflexión/momento`, ver el heatmap con relieve, tocar la losa y leer el valor. | Sin unit test. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (125 de Fase 2 + ~1 core + ~6 frontera + 1
   servidor ≈ **133**).
2. `GET /losa` devuelve `a/b/nx/ny`, `factor_sugerido` y `campos` con deflexión y
   momentos Mx/My, cada uno con `min/max` coherentes y un valor por nodo.
3. En el visor: los estados de losa muestran la superficie coloreada con relieve, y
   tocar la losa muestra el valor interpolado del campo activo en `#info`.

## 10. Roadmap (fases siguientes — fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 3b | Mxy, momento de apoyo como campo, parámetros de losa por query (`/losa?a=…`). | `losa_fem` |
| 4 | Barras de refuerzo 3D dentro de secciones. | `aci318` |

## 11. Archivos afectados (Fase 3)

**Nuevos**
- `src/motor_fea/viz/resultados_losa.py`
- `tests/test_resultados_losa.py`

**Modificados**
- `src/motor_fea/core/losa_fem.py` (campo `momentos_nodales` en `ResultadoLosa` — aditivo)
- `src/motor_fea/api/servidor.py` (endpoint `GET /losa`)
- `src/motor_fea/viz/static/app.js` (estados de losa + superficie/heatmap + picking)
- `src/motor_fea/viz/static/index.html` (si hace falta, etiqueta de valor; puede reusar `#info`)
- `tests/test_losa_fem.py` (+1 test de `momentos_nodales`)
- `tests/test_servidor.py` (+1 test de `/losa`)
- `README.md` (mención del heatmap de losas)
