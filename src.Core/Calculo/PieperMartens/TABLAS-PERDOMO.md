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

### Pendiente: mapa completo `código .DL` → `sub-tipo`

Sólo el 40 está verificado numéricamente (faltan fixtures para los demás). El
mapeo código→borde del catálogo da el patrón N/E/S/W; cruzarlo con la columna
"condición de borde" de §2 da el sub-tipo candidato, pero **debe confirmarse con
`Losas.exe`** caso por caso. El Tipo 71 (voladizo/franja en una dirección) es un
caso especial de una dirección, no sale de estas tablas.

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
