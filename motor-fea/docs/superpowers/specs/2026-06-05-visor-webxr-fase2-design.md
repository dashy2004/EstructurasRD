# Diseño — Visor estructural WebXR (Fase 2: deformada + modos)

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming, pendiente de revisión del spec escrito
**Depende de:** Fase 1 (`2026-06-05-visor-webxr-design.md`, ya implementada).
**Alcance:** Fase 2 — deformada estática + animación de modos 1–3 en el visor.

---

## 1. Objetivo

Extender el visor WebXR para mostrar, además de la geometría, el **comportamiento
estructural**: la deformada bajo carga (snapshot escalado) y las **formas modales
1–3 oscilando**. Un selector cambia entre estados; un slider controla la
exageración; el período real de cada modo se muestra como texto. Sirve para
revisión de ingeniería, presentación, demo y educación (los cuatro propósitos de
Fase 1).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Cargas/masas | **Peso propio (ρ·A·L) + extras del modelo.** Las cargas verticales del modelo aportan masa sísmica (`\|fz\|/g`) y a la deformada. |
| Estados | Selector `sin deformar · deformada · modo 1 · modo 2 · modo 3` + slider de exageración + play/pausa + etiqueta de período. |
| Dónde se calcula | **En el servidor** (reusa `solver.resolver` y `modal.modos`). El visor solo anima en el cliente. |
| Animación de modos | Período de **display fijo (~2 s)**, no el ω real (una estructura rígida vibraría invisiblemente rápido). El `T` real se muestra como texto. |

## 3. Arquitectura

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Cálculo de resultados | `src/motor_fea/viz/resultados.py` (nuevo) | `calcular_resultados(modelo, n_modos=3) -> dict` (ResultadosDTO). Deriva peso propio, arma deformada y modos reusando los motores. |
| Endpoint | `src/motor_fea/api/servidor.py` (mod) | `GET /resultados` → ResultadosDTO; `ValueError → 400`. |
| Modelo de ejemplo | `src/motor_fea/api/servidor.py` (mod) | Carga lateral modesta en el pórtico de ejemplo para que la deformada sea visible. |
| Visor | `src/motor_fea/viz/static/{app.js, index.html}` (mod) | Panel de control + fetch de `/resultados` + animación por frame; refactor a barra "caja unitaria escalable". |

Todo permanece en la **capa de frontera**; `core/` y `normativa/` no se tocan.

## 4. Contrato `ResultadosDTO`

```jsonc
{
  "deformada": {
    "factor_sugerido": 120.0,                 // exageración inicial (visible al abrir)
    "desplazamientos": { "5": [ux, uy, uz], "6": [ux, uy, uz] }  // solo traslaciones, por nodo_id (str)
  },
  "modos": [
    {
      "indice": 1, "periodo": 0.42, "frecuencia": 2.38, "omega": 14.9,
      "factor_sugerido": 80.0,
      "forma": { "5": [ux, uy, uz], "6": [ux, uy, uz] }
    }
    // hasta 3 modos; [] si el modelo no tiene masa en GDL libres
  ]
}
```

- Claves de nodo como **string** (consistente con `resultado_a_dict` en `contrato.py`).
- `factor_sugerido = 0.08 · diagonal_bbox / max|desplazamiento|` (1.0 si el máximo es 0).

## 5. Cálculo en el servidor (`resultados.py`)

Constante `G = 9.81` (m/s²).

1. **Peso propio.** Por elemento `e` con material `mat` y sección `sec`, longitud `L`
   entre sus nodos: `m_el = mat.densidad · sec.area · L`. Se reparte mitad a cada
   nodo (`nodo_i`, `nodo_j`), acumulando en un dict `masa_nodal: {nodo_id: kg}`.
   Y genera cargas gravitatorias `fz = −m_el·G/2` por nodo.
2. **Masa sísmica.** `masa_nodal[n] += |c.fz| / G` para cada carga `c` del modelo
   con componente vertical (suma a lo derivado del peso propio).
3. **Deformada.** Modelo aumentado = modelo original + las cargas de peso propio
   (sumadas a `model.cargas`). `solver.resolver(modelo_aumentado)` → de cada nodo
   se toman las 3 primeras componentes `(ux, uy, uz)` del desplazamiento.
4. **Modos.** `modal.modos(modelo, masa_nodal_filtrada, n_modos=3)`. Si `modal`
   lanza `ValueError` (sin masa en GDL libres, p.ej. todo empotrado) → `modos: []`.
   La deformada se devuelve igual.
5. **factor_sugerido** se calcula por estado (deformada y cada modo) con la fórmula
   de §4, usando la diagonal del bbox de los nodos.
6. Modelo inválido (`modelo.validar()` no vacío) → `ValueError` (→ 400 en el endpoint).

**Pureza:** `resultados.py` no toca HTTP ni three.js; usa solo `core` (modelo,
solver, modal). Se testea con asserts normales.

## 6. UI del visor

**Panel de control** (overlay HTML; oculto en VR inmersivo):

- `<select id="estado">`: opciones `sin-deformar`, `deformada`, y `modo-1..N`
  (N = nº de modos recibidos; si `modos: []`, solo las dos primeras).
- `<input type="range" id="exag">`: exageración; se inicializa con el
  `factor_sugerido` del estado activo; rango `0 … factor_sugerido×5`.
- Botón `play/pausa` (solo afecta a los modos).
- `<span id="info">`: `T = 0.42 s` en modos; `estático` en deformada; vacío en
  sin-deformar.

**Bucle de animación** (cada frame, con `t` = tiempo acumulado):

- *sin deformar*: posición de nodo = base (de `/escena`).
- *deformada*: `pos = base + desp · exag` (estático).
- *modo N*: `pos = base + forma · exag · sin(2π · t / T_DISPLAY)`, con
  `T_DISPLAY = 2.0 s`. En pausa, `t` no avanza (la fase se congela).

**Redibujo de barras (refactor de Fase 1).** Cada barra pasa a `BoxGeometry(b, h, 1)`
(longitud unitaria en Z) y se guarda `{ mesh, i, j }`. Cada frame, dadas las
posiciones deformadas `vi`, `vj` de sus nodos: `mesh.position = punto medio`,
`mesh.lookAt(vj)`, `mesh.scale.z = vi.distanceTo(vj)`. Así la geometría se
mantiene correcta aunque la deformación cambie las longitudes.

**VR:** el panel HTML no se ve en sesión inmersiva, pero la animación continúa: el
usuario entra al edificio y lo ve oscilar en el estado seleccionado. Controles
dentro de VR quedan fuera de alcance (fase posterior).

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| Modelo inválido | `calcular_resultados` lanza `ValueError`; endpoint → HTTP 400. |
| Sin masa en GDL libres | `modos: []`; la deformada se devuelve igual. |
| Desplazamiento máximo = 0 (sin carga efectiva) | `factor_sugerido = 1.0`; no divide por cero. |
| `/resultados` no responde | El visor muestra la geometría (de `/escena`) y un aviso; no rompe la vista. |

## 8. Testing

| Qué | Cómo | Notas |
|---|---|---|
| `viz/resultados.py` | Tests puros con `modelos_ref.voladizo()` (ρ=2400, A=0.09, L=3 → m_el=648 kg): masa nodal derivada (324 kg en cada extremo); deformada bajo peso propio con `uz<0` en la punta; ≥1 modo con `periodo>0` y períodos ascendentes; `factor_sugerido` finito y positivo; modelo todo-empotrado → `modos==[]` con deformada presente; modelo inválido → `ValueError`. | Corre con stdlib pura. |
| `api/servidor.py` | Append a `test_servidor.py`: `GET /resultados` → 200, claves `deformada`+`modos`, `len(modos) ≤ 3`. | `importorskip` ya presente en el módulo. |
| Visor JS | Smoke manual ampliado: cambiar estado, mover el slider, ver los modos oscilar y el `T`. | Sin unit test. |

**Criterio de aceptación:**

1. `PYTHONPATH=src pytest -q` verde (117 de Fase 1 + ~6 resultados + 1 servidor ≈ **124**).
2. `GET /resultados` del pórtico de ejemplo devuelve `deformada` con desplazamientos
   no nulos y `modos` con 3 entradas de período positivo.
3. En el visor: el selector cambia el estado, el slider escala la exageración, los
   modos oscilan suavemente y se muestra el período real.

## 9. Roadmap (fases siguientes — fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 3 | Mapas de color en losas (momento/deflexión) + tocar→valor. | `losa_fem` |
| 4 | Barras de refuerzo 3D dentro de secciones. | `aci318` |

## 10. Archivos afectados (Fase 2)

**Nuevos**
- `src/motor_fea/viz/resultados.py`
- `tests/test_resultados.py`

**Modificados**
- `src/motor_fea/api/servidor.py` (endpoint `/resultados` + carga lateral en el ejemplo)
- `src/motor_fea/viz/static/app.js` (panel + animación + refactor caja unitaria)
- `src/motor_fea/viz/static/index.html` (panel de control)
- `tests/test_servidor.py` (+1 test de `/resultados`)
- `README.md` (mención de la vista de resultados)
