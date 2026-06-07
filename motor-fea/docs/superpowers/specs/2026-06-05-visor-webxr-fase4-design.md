# Diseño — Visor estructural WebXR (Fase 4: refuerzo 3D en secciones)

**Fecha:** 2026-06-05
**Estado:** aprobado en brainstorming (3 decisiones), ejecución autónoma autorizada por el usuario.
**Depende de:** Fases 1–3 (visor + deformada/modos + heatmaps de losa), todas implementadas.
**Alcance:** Fase 4 — dibujar el armado 3D (barras longitudinales + estribos) dentro de las secciones de columnas y vigas, con el hormigón semi-transparente.

---

## 1. Objetivo

Mostrar el **refuerzo** dentro de cada barra-sección del pórtico: barras longitudinales
como cilindros a lo largo del elemento y estribos como aros transversales, con la
sección de hormigón translúcida (efecto rayos-X). Sirve para revisión de ingeniería,
presentación, demo y educación (los cuatro propósitos de las fases previas).

## 2. Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Cantidad de acero | **Heurística ACI (ρ≈1% / As_mín).** Columna: 1% del área bruta (ACI 10.6.1.1). Viga: As mínimo a flexión (`as_minimo_flexion`). Tamaño/conteo desde `AREAS_BARRA_MM2`. Es un armado de ejemplo: el motor Python no calcula Pu/Mu por elemento. |
| Refuerzo dibujado | **Longitudinal + estribos/zunchos** (jaula completa). |
| Look de la sección | **Hormigón semi-transparente** + barras adentro. |
| Capa | **100% frontera:** no se toca `core/` ni `normativa/`. Reusa `escena` (b,h,tipo) y `aci318` (tabla de barras + As_mín). |

## 3. Arquitectura

| Unidad | Archivo | Responsabilidad |
|---|---|---|
| Empaquetado del armado | `src/motor_fea/viz/armado.py` (nuevo, **puro**) | `calcular_armado(modelo, fc=21.0, fy=420.0, recubrimiento=0.04) → ArmadoDTO`. Por elemento: deriva (b,h) y tipo (reusa `escena._dimensiones`/`_clasificar`), calcula barras longitudinales + estribo y sus posiciones. Solo usa `viz.escena` + `normativa.aci318`. |
| Endpoint | `src/motor_fea/api/servidor.py` (mod) | `GET /armado` → ArmadoDTO (procesa el `modelo` inyectado); `ValueError → 400`. |
| Visor | `src/motor_fea/viz/static/app.js` (mod) | Estado `refuerzo`: hormigón fantasma + jaula (longitudinales como cilindros, estribos como aros). |

`escena._dimensiones`/`_clasificar` se importan como helpers internos del paquete `viz`
(armado vive en el mismo paquete) para no duplicar la derivación (b,h)/clasificación
ni modificar `escena.py`.

## 4. Cantidad y disposición del acero (reusa `aci318`)

Tabla (de `aci318`): `AREAS_BARRA_MM2 = {3:71, 4:129, 5:199, 6:284, 7:387, 8:510}` (mm²).
Diámetro de una barra `#num`: `d = num · 25.4/8` mm (octavos de pulgada) → en metros `num·25.4/8/1000`.

**Columna** (longitudinal):
- `As_req = 0.01 · (b·h)` en mm² (mínimo ACI 10.6.1.1; `b,h` en mm).
- Barra base `#6` (área 284): `n = max(4, ceil(As_req/284))` redondeado **al múltiplo de 4** superior. Si `n > 12` → barra `#8` (área 510) y se recalcula `n` igual.
- Posiciones: `n` puntos equiespaciados por longitud de arco en el **perímetro** del
  rectángulo de semi-ejes `ox = b/2 − rec − d/2`, `oy = h/2 − rec − d/2`, empezando en
  la esquina `(−ox, −oy)`. (Para una columna cuadrada con `n=8` da 4 esquinas + 4 centros de cara.)

**Viga** (longitudinal):
- `d_útil = h − rec`. `As_min = as_minimo_flexion(b·1000, d_útil·1000, fc, fy)` (mm²).
- Barra `#5` (área 199): `n_inf = max(2, ceil(As_min/199))` barras inferiores.
- Fila inferior: `y = −(h/2 − rec − d/2)`, `x` equiespaciado (incluyendo extremos) en
  `[−(b/2 − rec − d/2), +(b/2 − rec − d/2)]`. Fila superior: `y = +(h/2 − rec − d/2)`,
  2 barras `#5` en los extremos de `x`.

**Estribo** (ambos):
- Barra `#3` (`d_est = 3·25.4/8/1000 = 0.009525` m).
- Rectángulo interno: `w = b − 2·rec`, `h_e = h − 2·rec`.
- Separación: columna `s = min(16·d_long, 48·d_est, min(b,h))` (ACI 25.7.2.1);
  viga `s = d_útil/2`. Acotada a `s ≥ 0.05` m.

## 5. Contrato `ArmadoDTO`

```jsonc
{
  "recubrimiento": 0.04,                       // m
  "elementos": [
    {
      "id": 1, "i": 1, "j": 5, "tipo": "columna",
      "long": [ { "x": 0.11, "y": 0.11, "d": 0.019 } ],     // posición en el plano de la sección (m) + Ø (m)
      "estribo": { "d": 0.0095, "s": 0.30, "w": 0.22, "h": 0.22 },  // Ø, separación, rectángulo interno (m)
      "designacion": "8#6 + E#3@0.30"
    }
  ]
}
```

- Posiciones `(x, y)` en el **marco local de la sección** (x = ancho b, y = alto h), en metros.
- Diámetros y separaciones en metros (consistente con el resto de la escena en m).
- `tipo ∈ {"columna", "viga"}` (de `escena._clasificar`).

## 6. Cálculo (`armado.py`)

`calcular_armado(modelo, fc=21.0, fy=420.0, recubrimiento=0.04) -> dict`:

1. **Validación.** `fc > 0`, `fy > 0`, `recubrimiento > 0`; `modelo.validar()` vacío
   (si no → `ValueError`, → 400 en el endpoint).
2. Por cada elemento: nodos `ni,nj` y sección `sec`; `tipo = _clasificar(ni, nj)`;
   `(b, h) = _dimensiones(sec)`. Si `b − 2·rec ≤ 0` o `h − 2·rec ≤ 0` → `ValueError`
   (recubrimiento incompatible con la sección).
3. Longitudinal y estribo según §4; arma el dict del elemento (§5).
4. Devuelve `{"recubrimiento": rec, "elementos": [...]}`.

**Pureza:** `armado.py` no toca HTTP ni three.js; usa solo `viz.escena` (helpers de
geometría) y `normativa.aci318` (tabla + As_mín). Se testea con asserts normales.

**Endpoint** (en `servidor.py`, antes del `app.mount(...)`):

```python
@app.get("/armado")
def armado():
    try:
        return calcular_armado(modelo)
    except ValueError as ex:
        raise HTTPException(status_code=400, detail=str(ex))
```

Procesa el `modelo` del visor (a diferencia de `/losa`, que es autónomo).

## 7. UI del visor

**Panel:** al cargar `/armado`, el selector `#estado` gana **`refuerzo: armado`** (valor `refuerzo`).

**Estado `refuerzo`:**
- Las cajas de hormigón se vuelven **semi-transparentes**: se baja la opacidad de los
  materiales compartidos `MAT.columna`/`MAT.viga` (`transparent=true`, `opacity≈0.25`).
  Al salir del estado se restaura (`opacity=1`, `transparent=false`).
- Aparece un `Group` de armado por elemento, **estático**, sobre la geometría **sin
  deformar** (posiciones base de `/escena`). Cada `Group` se ubica en el punto medio
  del elemento con su orientación (`lookAt(vj)`, igual que la caja) y **sin `scale.z`**
  (así los cilindros no se estiran). Dentro:
  - **Longitudinales:** `CylinderGeometry(d/2, d/2, L)` rotada al eje local Z
    (`rotateX(π/2)`), una por cada `(x, y)` de `long`.
  - **Estribos:** aros rectangulares (`LineLoop` de los 4 vértices `(±w/2, ±h_e/2)`)
    repetidos a lo largo de Z cada `s`, centrados en el tramo `[−L/2, +L/2]`.
- `#info`: `"armado de ejemplo (ρ≈1% col · As_mín viga) — N elementos"`.
- El armado **no oscila ni se deforma** (vista de detallado estática). La losa,
  deformada y modos siguen igual; al entrar a `refuerzo` se ocultan barras animadas
  (se muestran como fantasma) y la losa.

**Construcción:** la jaula se construye una vez al cargar `/armado` (`Group` global
`armadoGroup`, `visible=false`), reusando las posiciones base de los nodos. Entrar a
`refuerzo` la hace visible y fantasmea el hormigón; salir la oculta y restaura.

**VR:** el panel queda oculto en sesión inmersiva (como en fases previas); la jaula sí
se ve y se puede recorrer.

## 8. Manejo de errores

| Situación | Respuesta |
|---|---|
| `fc/fy/recubrimiento ≤ 0`, modelo inválido, o recubrimiento ≥ semi-sección | `calcular_armado` lanza `ValueError`; endpoint → HTTP 400. |
| `/armado` no responde | El visor mantiene escena + estados de Fases 2/3; el estado `refuerzo` simplemente no se agrega. No rompe la vista. |
| Sección no rectangular | `escena._dimensiones` ya cae a un default 0.20×0.20; el armado se dibuja sobre ese default. |

## 9. Testing

| Qué | Cómo | Notas |
|---|---|---|
| `viz/armado.py` | `test_armado.py` (nuevo): con `modelo_ejemplo()` → `elementos` (uno por elemento, 8); columnas con `len(long) ≥ 4` y `tipo=="columna"`, vigas con barras sup+inf y `tipo=="viga"`; toda posición dentro de la sección (`|x| ≤ b/2`, `|y| ≤ h/2`); `estribo.d/s/w/h > 0`; los diámetros longitudinales corresponden a la tabla `AREAS_BARRA_MM2` (Ø = num·25.4/8/1000); una sección mayor ⇒ `n` longitudinal ≥; `fc ≤ 0` → `ValueError`. | stdlib pura. |
| `api/servidor.py` | `test_servidor.py` (+1): `GET /armado` → 200; claves `{recubrimiento, elementos}`; `elementos` no vacío con `long`+`estribo`. | `importorskip` ya presente. |
| Visor JS | Smoke manual: estado `refuerzo`, ver hormigón translúcido + cilindros longitudinales + aros de estribo. | Sin unit test. |

**Criterio de aceptación:**

1. `PYTHONPATH=src:tests pytest -q` verde (134 de Fase 3 + ~6 frontera + 1 servidor ≈ **141**).
2. `GET /armado` devuelve un armado por elemento con `long` (≥4 en columnas) + `estribo`
   coherentes y posiciones dentro de la sección.
3. En el visor: el estado `refuerzo` muestra el hormigón semi-transparente con la jaula
   de acero (longitudinales + estribos) adentro.

## 10. Roadmap (fases siguientes — fuera de este spec)

| Fase | Entrega | Reusa |
|---|---|---|
| 4b | Armado atado a fuerzas reales (requiere esfuerzos por elemento, hoy ausentes en el motor Python), secciones no rectangulares, ganchos/dobleces y traslapes. | solver + aci318 |

## 11. Archivos afectados (Fase 4)

**Nuevos**
- `src/motor_fea/viz/armado.py`
- `tests/test_armado.py`

**Modificados**
- `src/motor_fea/api/servidor.py` (endpoint `GET /armado`)
- `src/motor_fea/viz/static/app.js` (estado `refuerzo` + jaula 3D)
- `tests/test_servidor.py` (+1 test de `/armado`)
- `README.md` (mención del armado 3D)

`core/` y `normativa/` **no se tocan**.
