# Tablas de Pieper-Martens (F. Perdomo) — análisis del PDF

**Fuente:** `LOSAS CONTINUAS APOYADAS EN TRES Y CUATRO BORDES.pdf`
(Microsoft Word, FerPer, 2010-08-31) — método del Prof. Dr. Ing. **Klaus Pieper**
y Dipl. Ing. **Peter Martens** (TU Braunschweig).
**Dataset extraído y validado:** `TablasPerdomo.json` (21 sub-tipos × 31 filas).

---

## 1. Convención de momentos (CONFIRMADA por el PDF)

```
Mfx = q·Lx² / Fx      Msx = q·Lx² / Sx      (acero según X)
Mfy = q·Ly² / Fy      Msy = q·Ly² / Sy      (acero según Y)
```

- **Cada dirección usa SU PROPIA luz libre** — `Mfx`/`Msx` con `Lx`, `Mfy`/`Msy`
  con `Ly`. **NO** se usa `l_corto = min(Lx,Ly)`.
- Índice de tabla: **ε = Ly/Lx**, rango **0.50 … 2.00** paso **0.05** (31 filas).
- Interpolación **lineal** en ε.
- `Sx`/`Sy = "--"` ⇒ ese borde no tiene momento de empotramiento (apoyo simple
  o libre) ⇒ `Ms = 0` en esa dirección.
- El subíndice del momento es la **dirección de la armadura**, no la del apoyo.

> Esto **resuelve las Open Questions #1 y #2** del spec
> `docs/superpowers/specs/2026-06-03-motor-pieper-martens-nativo-design.md`:
> el sanity-check con `l_corto` coincidía sólo porque en RESTAURANTE 2 `Lx≈Ly`.

### Momentos de diseño (apoyos), del PDF §2.1

```
Si Lmax ≤ 5·Lmin:  MS = (MSO1 + MSO2)/2  ≥  0.75·max(MSO1,MSO2)
Si Lmax > 5·Lmin:  MS = max(MSO1, MSO2)
```

### Limitaciones (PDF §3)

1. Carga viva ≤ ⅔ de la carga total (sin factorizar).
2. Apoyos lineales rígidos (muros, o vigas con peralte > 4× espesor de losa, o
   ACI 318-08 §13.6.1.6).
3. Tramos cortos junto a tramos largos (`Lmax > 3·Lmin`): cuidar el
   detallamiento (prolongar acero negativo de la losa grande sobre la pequeña).

---

## 2. Las 12 tablas → condición de borde

Diagramas decodificados de cada página: **rayado = empotrado (continuo)**,
**línea fina = apoyo simple**, **línea punteada = borde libre**.
Las tablas 2,3,5,7-12 traen DOS bloques (orientaciones `a`/`b`, el mismo caso
girado 90°); las tablas 1,4,6 traen un solo bloque (caso simétrico).

Orden de bordes en `bordes_NESW`: **[N, E, S, W]** · `A`=apoyo, `E`=empotrado, `L`=libre.

| TABLA | Sub-tipo | Condición de borde | Sx | Sy |
|------:|:--------:|--------------------|:--:|:--:|
| 1  | 1    | 4 bordes apoyo simple                         | -- | -- |
| 2  | 2a   | 1 empotrado (S); 3 apoyo                       | -- | ✔  |
| 2  | 2b   | 1 empotrado (W); 3 apoyo                       | ✔  | -- |
| 3  | 3a   | 2 opuestos empotrados (N,S)                    | -- | ✔  |
| 3  | 3b   | 2 opuestos empotrados (E,W)                    | ✔  | -- |
| 4  | 4    | 2 **adyacentes** empotrados (esquina S,W)     | ✔  | ✔  |
| 5  | 5a   | 3 empotrados (E,S,W); N apoyo                  | ✔  | ✔  |
| 5  | 5b   | 3 empotrados (N,S,W); E apoyo                  | ✔  | ✔  |
| 6  | 6    | 4 bordes empotrados (perimetral)              | ✔  | ✔  |
| 7  | 7a   | 3 apoyo; borde **N LIBRE**                     | -- | -- |
| 7  | 7b   | 3 apoyo; borde **E LIBRE**                     | -- | -- |
| 8  | 8a   | N libre; **opuesto** S empotrado; E,W apoyo   | -- | ✔  |
| 8  | 8b   | W libre; **opuesto** E empotrado; N,S apoyo   | ✔  | -- |
| 9  | 9a   | N libre; **adyacente** E empotrado; S,W apoyo | ✔  | -- |
| 9  | 9b   | W libre; **adyacente** S empotrado; N,E apoyo | -- | ✔  |
| 10 | 10a  | N libre; E y W empotrados; S apoyo            | ✔  | -- |
| 10 | 10b  | W libre; N y S empotrados; E apoyo            | -- | ✔  |
| 11 | 11a  | N libre; S y E empotrados; W apoyo            | ✔  | ✔  |
| 11 | 11b  | W libre; E y S empotrados; N apoyo            | ✔  | ✔  |
| 12 | 12a  | N libre; E,S,W empotrados                     | ✔  | ✔  |
| 12 | 12b  | W libre; N,E,S empotrados                     | ✔  | ✔  |

- **Tablas 1-6** = losas apoyadas/continuas en los **4 bordes** (sin borde libre).
- **Tablas 7-12** = losas apoyadas en **3 bordes** (un borde **libre**), con
  distintos grados de continuidad en los 3 bordes apoyados. (De ahí el título:
  "apoyadas en tres y cuatro bordes".)

---

## 3. Validación contra RESTAURANTE 2 (`tests/fixtures/`)

Con `TablasPerdomo.json` + la convención de arriba, las losas 1-4 (Perdomo
**Tipo 40**) reproducen `Losas.exe`:

| caso | Mfx | Mfy | Msx | Msy |
|---|---|---|---|---|
| L1 (Lx 6.85, Ly 6.65) | 1.276 / **1.280** | 1.356 / **1.358** | 1.980 / **1.987** | 2.104 / **2.108** |
| L3 (Lx 6.85, Ly 6.45) | 1.196 / **1.199** | 1.351 / **1.352** | 1.854 / **1.859** | 2.094 / **2.096** |

(calculado / esperado — error ≤ 0.007 ton·m/m, por redondeo de los `S` a 2 decimales).

> **Hallazgo:** Perdomo **Tipo 40** corresponde físicamente a **TABLA 4 = "2
> bordes adyacentes empotrados"** (sub-tipo `4`), NO a "tres bordes continuos".
> El `Catalogo` en `src.Core/Models/Sistema.cs` describe el código 40 como "Tres
> bordes continuos" — revisar/reconciliar el mapeo **código `.DL` → sub-tipo de
> tabla** contra `Losas.exe` antes de la Fase 1. La evidencia numérica manda.

### Mapa `código .DL` → `sub-tipo` (losas apoyadas en cuatro bordes)

Validado con `tests/fixtures/sistema_demo_27_losas.DL/.TXT` (listado real de
`Losas.exe` v5.20) en `SistemaDemo27LosasTests`: las 27 losas y sus 22 bordes
balanceados coinciden con error ≤ 0.015 ton·m/m.

| Código | Sub-tipo | Bordes continuos | Estado |
|-------:|:--------:|------------------|--------|
| 10 | 1  | ninguno                         | orientación confirmada; valores de tabla sin listado de control |
| 21 | 2a | 1 horizontal (norte o sur)      | ídem |
| 22 | 2b | 1 vertical (este u oeste)       | ídem |
| 31 | 3a | norte y sur                     | ídem |
| 32 | 3b | este y oeste                    | ídem |
| 40 | 4  | 2 adyacentes                    | **verificado** (RESTAURANTE 2 y demo 27) |
| 51 | 5a | 3; el apoyo simple es horizontal | **verificado** (losas 3, 12, 14) |
| 52 | 5b | 3; el apoyo simple es vertical   | **verificado** (losas 2, 9) |
| 60 | 6  | los 4                            | **verificado** (losas 1, 4, 7, 8) |

Los tipos 10, 21, 22, 31 y 32 solo aparecen en el fixture como losas en una
dirección (ver abajo). Eso confirma su orientación —qué momento de apoyo existe—
pero no sus valores cuando ε cae dentro de la tabla.

Siguen **sin mapear** las losas apoyadas en tres bordes (códigos 13 a 64,
tablas 7 a 12) y el caso de abajo marcado como no verificado: el motor no las
calcula y las reporta en `SalidaPerdomo.LosasNoParseadas` sin detener el resto.

### Losas en una dirección (ε = Ly/Lx fuera de 0.50–2.00)

`Losas.exe` no recorta ε al extremo de la tabla: trata la losa como franja sobre
la **luz corta** `L`. Regla deducida del listado del fixture:

| Extremos continuos de la franja | Momento de vano | Momento en cada extremo continuo |
|:-:|:-:|:-:|
| 0 | `q·L²/8`     | — |
| 1 | `q·L²/10.24` | `q·L²/8` |
| 2 | `q·L²/12`    | `q·L²/12` |

El momento de vano es el promedio entre la viga simplemente apoyada (`1/8`) y la
viga con su continuidad real (`1/8`, `9/128` o `1/24`). La dirección larga no
lleva momento de vano. Si la losa tiene bordes continuos **paralelos** a la
franja y ninguno en sus extremos, cada uno recibe `q·L²/8` (losas 10 y 24).

Evidencia: tipo 10 (losa 17), 21 (losas 10, 13, 24, 25, 26, 27), 22 (losas 16 y
18 a 22), 31 (losa 5), 32 (losa 6).

**No verificado:** franja con extremos continuos **y además** bordes continuos
paralelos (tipos 40, 51, 52 y 60 fuera de rango). No hay listado que diga qué
momento transversal usa `Losas.exe`, así que el motor lo rechaza en vez de
adivinarlo.

El Tipo 71/72 (voladizo) es un caso aparte: `q·L²/2` sobre la luz que vuela.

---

## 4. Esquema de `TablasPerdomo.json`

```jsonc
{
  "convencion": { "Mfx":"q*Lx^2/Fx", "...": "..." },
  "bordes_orden": ["N","E","S","W"],
  "tipos": {
    "4": {
      "tabla": 4,
      "descripcion": "2 bordes adyacentes empotrados (S,W)",
      "bordes_NESW": ["A","A","E","E"],
      "eps": [0.50, 0.55, "...", 2.00],   // 31 valores
      "Fx":  [189.75, "..."], "Fy": ["..."],
      "Sx":  [-48.00, "..."], "Sy": ["..."]  // null donde el PDF pone "--"
    }
  }
}
```
