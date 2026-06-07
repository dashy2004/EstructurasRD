# Motor nativo Pieper-Martens — Diseño

**Fecha:** 2026-06-03
**Rama:** `engine/columnas-diseno`
**Estado:** Diseño aprobado en brainstorming · pendiente tablas de Perdomo + revisión de spec

---

## 1. Objetivo

Calcular **dentro de la aplicación** (C# nativo, sin Python ni `Losas.exe`) el
diseño completo de un sistema de losas en dos direcciones por el **método de
Pieper-Martens**, reproduciendo los resultados de `Losas.exe` (F. Perdomo
Ver. 5.21):

- Momentos flectores por losa: `Mfx`, `Mfy`, `-Msx`, `-Msy`.
- Balanceo de momentos negativos en bordes compartidos entre losas contiguas.
- Armaduras de vano (X/Y) y sobre apoyos (X/Y) según ACI 318-08.

Los resultados se muestran en la **UI y en la Memoria de cálculo**. **No** se
requiere reproducir el archivo `.TXT` byte por byte; sí se requiere que los
**números coincidan** con `Losas.exe`.

### Decisiones tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Fuente de coeficientes | **Tablas de Perdomo** (las aporta el usuario) → coincidencia exacta |
| Rol del motor | **Reemplazar `Losas.exe`** (cálculo completo nativo) |
| Fidelidad de salida | **Números idénticos**, salida a UI/Memoria (no `.TXT` byte-a-byte) |

---

## 2. Contexto: qué ya existe (no se reescribe)

| Pieza | Ubicación | Rol |
|---|---|---|
| `AcerosLosaDesigner` | `src.Core/Calculo/AcerosLosaDesigner.cs` | Diseño ACI 318: `Mu, d, f'c, fy → As → barra @ esp`. **Exacto, reusar tal cual.** |
| `Losa` / `Sistema` | `src.Core/Models/Sistema.cs` | Modelo del `.DL`: `Tipo, Carga(=Wu), Espesor, Lx, Ly, Rec`, nulos `Mfx/Mfy/MSx/MSy`, `BordesX/BordesY` (`BordeAdic{BI,BJ,Balanceo}`). |
| `TipoLosa.Catalogo` | `src.Core/Models/Sistema.cs` | 23 tipos Pieper-Martens → condiciones de borde (N/E/S/W) + descripción. |
| `SalidaPerdomo` | `src.Core/Models/SalidaPerdomo.cs` | **Frontera de integración.** Contiene `Momentos`, `ArmadurasX/YCentro`, `ArmadurasX/YApoyos`. Lo consume la Memoria. |
| `SalidaPerdomoAdapter` | `src.Core/Services/SalidaPerdomoAdapter.cs` | Hoy llena `SalidaPerdomo` desde el `.TXT`. El motor nativo será una **fuente alternativa** del mismo `SalidaPerdomo`. |

**Punto de integración:** el motor nativo produce un `SalidaPerdomo`. Todo lo
aguas abajo (Memoria, UI, exporters CSV/XLSX) ya sabe consumirlo.

---

## 3. Componentes nuevos

Namespace/carpeta: `src.Core/Calculo/PieperMartens/`

### 3.1 `TablaPieperMartens` (datos + interpolación)

- **Qué hace:** dado un `TIPO` (caso) y `ε = ly/lx`, devuelve los factores de
  momento para vano X, vano Y, apoyo X, apoyo Y.
- **Forma del dato:** por cada uno de los 23 tipos, una curva de factores
  tabulados en ε (típicamente ε ∈ [1.0, 2.0] paso 0.05, más el caso ε→∞).
  Interpolación **lineal** entre puntos de ε.
- **Convención de momento:** Pieper-Martens expresa `M = w · l_corto² / f`,
  donde `f` es el factor tabulado y `l_corto = min(Lx, Ly)`. (A confirmar
  contra las tablas de Perdomo — ver §6 Open Questions.)
- **Dependencias:** ninguna. Función pura, fácilmente testeable.
- **TODO bloqueante:** los **valores numéricos** de la tabla salen de las
  tablas de Perdomo (pendientes). La clase y su interfaz se construyen ya; los
  datos se cargan cuando el usuario los adjunte. Hasta entonces, un stub con
  los pocos puntos derivables del `.TXT` (ver §5) permite que los tests de
  RESTAURANTE 2 corran.

### 3.2 `MomentosCalculator`

- **Qué hace:** por losa, calcula `Mfx, Mfy, MSx, MSy` =
  `factor(tipo, ε, dirección) · w · l_corto²`.
- **Entrada:** una `Losa` (Tipo, Carga, Lx, Ly).
- **Salida:** los 4 momentos (ton·m/m).
- **Casos especiales:** tipos de una dirección (p. ej. `71`: franja larga
  `Lx=13.65, Ly=1.20` → solo `MSy ≠ 0`). La tabla los codifica con ceros.
- **Valida contra:** `.TXT` "CÁLCULO DE MOMENTOS" (5 filas).

### 3.3 `BalanceoMomentos`

- **Qué hace:** para cada borde compartido declarado en `BordesX`/`BordesY`
  con `Balanceo = "S"`, promedia los momentos negativos de las dos losas:
  `MuI-J = (MuI + MuJ) / 2`. Con `Balanceo = "N"` no promedia (toma el que
  corresponde, típicamente el mayor — confirmar contra `.TXT`).
- **No necesita la tabla** — solo aritmética sobre momentos ya calculados.
- **Valida contra:** `.TXT` "ARMADURAS … SOBRE LOS APOYOS" (`MuI`, `MuJ`,
  `MuI-J`).

### 3.4 `SistemaPieperMartensCalculator` (orquestador)

- **Qué hace:** pipeline completo →
  1. `MomentosCalculator` por cada losa.
  2. `BalanceoMomentos` en bordes compartidos.
  3. `AcerosLosaDesigner` para vano X/Y y apoyos X/Y (reuso).
  4. Ensambla un `SalidaPerdomo` (`Momentos`, `ArmadurasX/YCentro`,
     `ArmadurasX/YApoyos`).
- **Salida:** `SalidaPerdomo` idéntico en forma al que produce el parser del
  `.TXT`, listo para Memoria/UI.
- **Valida contra:** el `.TXT` de RESTAURANTE 2 completo.

---

## 4. Flujo de datos

```
.DL  --(DLFileService, ya existe)-->  Sistema { Losas[], BordesX[], BordesY[] }
                                            |
                          SistemaPieperMartensCalculator
                                            |
        +---------------+-------------------+-------------------+
        v               v                   v                   v
 MomentosCalculator  BalanceoMomentos   AcerosLosaDesigner  (ensamblado)
 (usa TablaPieperM.) (promedia apoyos)  (ACI 318, ya existe)
        +---------------+-------------------+-------------------+
                                            |
                                       SalidaPerdomo
                                            |
                              Memoria de calculo  +  UI
```

---

## 5. Validación: RESTAURANTE 2 (fixture de oro)

`RESTAURANTE 2.DL` + `RESTAURANTE 2.TXT` -> `tests/fixtures/`. Cada fase tiene
tests que comparan contra estos números reales de `Losas.exe`.

**Sistema:** 5 losas, `f'c=0.210`, `fy=4.200 ton/cm²`.

**Momentos esperados (ton·m/m):**

| Losa | Tipo | w | Lx | Ly | Mfx | Mfy | -Msx | -Msy |
|---|---|---|---|---|---|---|---|---|
| 1 | 40 | 0.720 | 6.850 | 6.650 | 1.280 | 1.358 | 1.987 | 2.108 |
| 2 | 40 | 0.720 | 6.850 | 6.650 | 1.280 | 1.358 | 1.987 | 2.108 |
| 3 | 40 | 0.720 | 6.850 | 6.450 | 1.199 | 1.352 | 1.859 | 2.096 |
| 4 | 40 | 0.720 | 6.850 | 6.450 | 1.199 | 1.352 | 1.859 | 2.096 |
| 5 | 71 | 0.720 | 13.650 | 1.200 | 0.000 | 0.000 | 0.000 | 0.518 |

**Armaduras vano esperadas:** losas 1-4 -> `Ø3/8" @ 19 cm` (As 3.600, AsReal
3.737) en X (d=14.50) y Y (d=16.50); losa 5 X -> `Ø3/8" @ 32 cm`.

**Apoyos (balanceados) esperados:** p. ej. borde `2-4` X -> `MuI=1.987,
MuJ=1.859, MuI-J=1.923`; borde `5-2` Y -> `MuI=0.518, MuJ=2.108, MuI-J=0.518`
(balanceo `N` => no promedia).

**Factores derivados (solo sanity-check del formato de tabla, NO la fuente):**
con `l_corto = min(Lx,Ly)` y `f = w·l²/M`:

| Losa | ε=ly/lx | f(Mfx) | f(Mfy) | f(Msx) | f(Msy) |
|---|---|---|---|---|---|
| 1 | 1.030 | 24.88 | 23.45 | 16.02 | 15.11 |

Cuando lleguen las tablas de Perdomo, estos factores deben reproducirse.

---

## 6. Open Questions (resolver con las tablas de Perdomo)

1. **Eje vs. span corto:** ¿`Mfx` se indexa por el eje geométrico X o por el
   span corto? En losa 1, `Mfy(1.358) > Mfx(1.280)` y `Ly(6.65) < Lx(6.85)`
   => el momento mayor cae en el span más corto, consistente. Confirmar la
   convención exacta de la tabla.
2. **`l` de referencia:** ¿`l_corto = min(Lx,Ly)` o cada dirección usa su
   propio `L`? El sanity-check de §5 asume `l_corto`.
3. **Balanceo `N`:** confirmar la regla cuando `Balanceo="N"` (¿toma el menor?
   el de la losa I? en `5-2` da 0.518 = MuI).
4. **`d` (canto útil):** el `.TXT` usa `d` distinto por dirección (X: 14.50,
   Y: 16.50 para H=20) => capas de acero apiladas. Confirmar `d = H − rec −
   db − db/2` para la capa Y. `AcerosLosaDesigner.CantoUtil` ya modela capas.
5. **Carga `Wu`:** el `.TXT` usa `Carga` directo como `Mu` (sin factor extra)
   => `0.720` ya es `Wu`. Confirmado por el modelo (`Carga Wu`).

---

## 7. Fases de entrega (TDD estricto)

1. **Fase 1 — Momentos:** `TablaPieperMartens` (interfaz + stub) +
   `MomentosCalculator`. Meta: las 5 filas de momentos coinciden.
2. **Fase 2 — Balanceo:** `BalanceoMomentos`. Meta: tablas de apoyos coinciden.
3. **Fase 3 — Armaduras + integración:** `SistemaPieperMartensCalculator`
   (reusa `AcerosLosaDesigner`) -> `SalidaPerdomo`; comando "Calcular nativo" en
   la UI. Meta: armaduras coinciden y se ven en pantalla/Memoria.

Cada fase: RED (test contra `.TXT`) -> GREEN -> REFACTOR -> build+test verde.

---

## 8. No-objetivos (YAGNI)

- Reproducir el `.TXT` byte por byte (formato/encoding corrupto).
- Reemplazar el motor FEM (Python) — queda como está; este motor es
  ortogonal y específico a Pieper-Martens.
- Cargas no uniformes, análisis sísmico, o tipos fuera del catálogo de 23.
