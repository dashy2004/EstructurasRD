# Diseño — Fase 4b.2: armado diseñado en el visor

**Fecha:** 2026-06-06
**Estado:** aprobado en brainstorming (2 decisiones), pendiente de revisión del spec.
**Depende de:** 4b.1 (`diseno_elemento.disenar_viga`/`disenar_columna`) y Fase 4 (`viz/armado` — geometría + estado `refuerzo` del visor).
**Alcance:** Fase 4b.2 — servir y dibujar el armado **diseñado por fuerzas** (no de ejemplo), con la demanda
(Pu/Mu/Vu) y el `cumple` por elemento.

---

## 1. Objetivo

Mostrar en el visor el armado **real diseñado** a partir de los esfuerzos del análisis: por cada columna/viga,
el refuerzo que `diseno_elemento` calcula para su demanda, coloreado por `cumple`, con la demanda visible al
tocar el elemento. Cierra el lazo análisis → esfuerzos → diseño → visualización.

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Estado | **Estado nuevo `diseño: armado`** (endpoint `/diseno`); se mantiene el `refuerzo: armado` de ejemplo. |
| Demanda/cumple | **Color por cumple + tocar→etiqueta**: la jaula se colorea por `cumple`; tocar un elemento muestra su designación + demanda en `#info`. |

## 3. Arquitectura

Reusa `diseno_elemento` (4b.1) para los conteos y `viz/armado` (Fase 4) para la geometría de posiciones.

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Geometría reutilizable | `src/motor_fea/viz/armado.py` (mod, **refactor**) | Extraer `_posiciones_columna(b,h,rec,num,n)` y `_posiciones_viga(b,h,rec,num,n_inf)`; `_armado_columna`/`_armado_viga` pasan a llamarlos (comportamiento idéntico). |
| Empaquetado del diseño | `src/motor_fea/viz/diseno.py` (nuevo, **puro**) | `calcular_diseno(modelo, fc, fy, rec) → DisenoDTO`. |
| Endpoint | `src/motor_fea/api/servidor.py` (mod) | `GET /diseno` → DisenoDTO. |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | Estado `diseño`: jaula diseñada coloreada por cumple + tocar→etiqueta. |

## 4. Contrato `DisenoDTO`

Misma forma que el `ArmadoDTO` de Fase 4 **+ `demanda` y `cumple`** por elemento:
```jsonc
{
  "recubrimiento": 0.04,
  "elementos": [
    { "id": 1, "i": 1, "j": 5, "tipo": "columna",
      "long": [ {"x":0.10,"y":0.10,"d":0.019} ],
      "estribo": { "d":0.0095, "s":0.30, "w":0.22, "h":0.22 },
      "designacion": "8#6",
      "demanda": { "pu": 200000.0, "mu": 30000.0, "vu": 15000.0 },   // N, N·m, N
      "cumple": true }
  ]
}
```
- El conteo/tamaño de `long` y la `designacion` salen del **diseño por fuerzas** (`disenar_columna`/`disenar_viga`).
- `demanda = {pu=|axial|, mu=max|My|,|Mz|, vu=max|Vy|,|Vz|}` del diagrama del elemento (N, N·m, N).
- `cumple` = el del diseño (sección suficiente para la demanda).
- Estribo de columna: separación de la regla ACI 25.7.2.1 (el diseño de estribos de columna queda para Fase 5).

## 5. Cálculo (`viz/diseno.py`)

`calcular_diseno(modelo, fc=21.0, fy=420.0, recubrimiento=0.04) -> dict`:

1. **Validación.** `fc/fy/rec > 0`; `modelo.validar()` vacío (si no → `ValueError`). `resolver(modelo)` →
   `esfuerzos_elementos`.
2. Por elemento: nodos `ni,nj`, sección; `(b,h)=_dimensiones`; si `b−2·rec ≤ 0` o `h−2·rec ≤ 0` → `ValueError`.
   `esf = esfuerzos[e.id]`; `tipo = _clasificar(ni,nj)`.
   - **columna:** `d = disenar_columna(esf, b, h, fc, fy, rec)`; `long = _posiciones_columna(b, h, rec,
     d.numero_barra, d.n_barras)`; `s = max(0.05, min(16·Ø_long, 48·Ø_estribo, min(b,h)))`; `cumple=d.cumple`,
     `designacion=d.disponer`.
   - **viga:** `d = disenar_viga(esf, b, h, fc, fy, rec)`; `num = d.flexion.numero_barra si d.flexion si no 5`,
     `n_inf = d.flexion.n_barras si d.flexion si no 2`; `long = _posiciones_viga(b, h, rec, num, n_inf)`;
     `s = d.estribo.espaciamiento / 1000` (mm→m); `cumple=d.cumple`, `designacion=d.disponer`.
3. `demanda = {pu: abs(esf.axial), mu, vu}` recorriendo `esf.diagrama(21)` (`mu=max|My|,|Mz|`, `vu=max|Vy|,|Vz|`).
4. Devuelve `{recubrimiento, elementos: [...]}` con cada elemento `{id,i,j,tipo,long,estribo,designacion,demanda,cumple}`.

**Pureza:** usa `core` (solver), `viz.escena`/`viz.armado` (geometría) y `diseno_elemento` (4b.1, que envuelve
`aci318`). No toca HTTP ni three.js. Unidades del DTO: metros (posiciones/estribo) y N/N·m (demanda).

**Endpoint** (`servidor.py`, antes del mount): `GET /diseno` → `calcular_diseno(modelo)`; `ValueError → 400`.

## 6. Visor — estado `diseño: armado`

- Al cargar `/diseno`, el selector gana `diseño: armado` (valor `diseno`).
- Comportamiento como `refuerzo` (hormigón **fantasma** + jaula estática sobre la geometría sin deformar), pero
  la jaula viene de `/diseno`.
- **Color por cumple:** las barras longitudinales se pintan **gris acero** si `el.cumple`, **rojo** si no
  (sección insuficiente para su demanda). Estribos en verde.
- **Tocar → etiqueta** (`pointerdown`, fuera de VR, solo en estado `diseño`): raycast a las **cajas de sección**
  (que llevan el `id` del elemento); del `id` se busca el elemento en el DTO y `#info` muestra
  `"8#6 · Pu=200 kN, Mu=30 kN·m · cumple"` (columna) / `"3#5+2#5 · Mu=.. kN·m, Vu=.. kN · cumple"` (viga).
  Reusa el patrón de picking de Fase 3 (losa) y de selección por puntero.
- `#info` al entrar: resumen `"diseño por fuerzas — N/M cumplen"`.
- La construcción de la jaula se hace una vez al cargar (`disenoGroup`, oculto); entrar al estado la muestra y
  fantasmea el hormigón; salir restaura (vía el `resetOverlays` existente, extendido para ocultar `disenoGroup`).

**Convivencia:** el `refuerzo` (ejemplo) y el `diseño` (real) son estados distintos; `resetOverlays` apaga
losa/armado/diseño/fantasma al cambiar de estado.

## 7. Manejo de errores

| Situación | Respuesta |
|---|---|
| Modelo inválido / `fc,fy,rec ≤ 0` / recubrimiento ≥ semi-sección | `calcular_diseno` lanza `ValueError`; endpoint → HTTP 400. |
| `/diseno` no responde | El visor mantiene escena + estados de Fases 2/3/4; el estado `diseño` no se agrega. |
| Sección insuficiente (a flexión/cortante/columna) | `cumple=False`; el elemento se dibuja en rojo y la etiqueta lo indica. |

## 8. Testing

| Qué | Cómo | Notas |
|---|---|---|
| `viz/armado.py` (refactor) | `test_armado.py` (sin cambios) sigue verde → comportamiento idéntico de `_armado_columna`/`_armado_viga`. | regresión. |
| `viz/diseno.py` | `test_diseno_visual.py` (nuevo): con `modelo_ejemplo` → `elementos` (8) con `long`/`estribo`/`designacion`/`demanda`/`cumple`; posiciones dentro de la sección; `demanda` con `pu/mu/vu` (≥0); `cumple` es `bool`; columnas con `len(long)≥4`; un modelo con sección chica bajo demanda grande → `cumple=False` en ese elemento. | stdlib pura. |
| `api/servidor.py` | `test_servidor.py` (+1): `GET /diseno` → 200; claves `{recubrimiento, elementos}`; `elementos` no vacío con `demanda`+`cumple`. | `importorskip` ya presente. |
| Visor JS | Smoke manual: estado `diseño`, jaula coloreada por cumple, tocar un elemento → designación + demanda. | Sin unit test. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (169 + ~6 de diseño + 1 de servidor ≈ **176**); `test_armado.py` sin regresión.
2. `GET /diseno` devuelve, por elemento, el armado **diseñado** (conteo/tamaño por fuerzas) + `demanda` (Pu/Mu/Vu) + `cumple`.
3. En el visor: el estado `diseño` muestra la jaula coloreada por `cumple`, y tocar un elemento muestra su
   designación + demanda en `#info`.

## 9. Roadmap (fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 5 | Combinaciones de carga (envolvente), biaxial, estribos de columna + confinamiento, editar fc/fy/rec desde un panel. | `combinaciones`, `aci318` |

## 10. Archivos afectados

**Nuevos**
- `src/motor_fea/viz/diseno.py`
- `tests/test_diseno_visual.py`

**Modificados**
- `src/motor_fea/viz/armado.py` (refactor: extraer `_posiciones_columna`/`_posiciones_viga` — comportamiento idéntico)
- `src/motor_fea/api/servidor.py` (endpoint `GET /diseno`)
- `src/motor_fea/viz/static/app.js` (estado `diseño` + color por cumple + tocar→etiqueta)
- `tests/test_servidor.py` (+1 test de `/diseno`)
- `README.md` (mención del armado diseñado)

`core/` y `normativa/` **no se tocan**.
