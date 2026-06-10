# F3 — Pieper-Martens nativo 21/21 subtipos (design spec)

- **Fecha:** 2026-06-10
- **Estado:** Aprobado (diseño) → pendiente de plan de implementación
- **Rama de trabajo propuesta:** `engine/f3-pieper-martens-21` (off `engine/f0-verdad-de-estado`, F0 cerrada)
- **Fase del roadmap:** F3 de `docs/superpowers/roadmap-fases-F0-F9.md` (← F0; independiente de F1/F4)
- **Baseline verde:** .NET 1106 / Python 208 (ver `STATE.md`)

---

## 1. Contexto y problema

El motor nativo Pieper-Martens (que reemplaza a `Losas.exe` como fuente del `SalidaPerdomo`) hoy solo calcula **3 de los 23 códigos** del catálogo, y falla mal:

1. **Mapeo incompleto:** `CodigoASubtipo` en `src.Core/Calculo/PieperMartens/TablaPieperMartens.cs:76-79` contiene una sola entrada (`[40] = "4"`). Cualquier otro código (salvo los voladizos 71/72, interceptados antes en `MomentosCalculator.cs:35-39`) lanza `NotSupportedException` desde `TablaPieperMartens.cs:73-74` (`SubtipoDeCodigoDL`, `TablaPieperMartens.cs:70-74`).
2. **Aborta el sistema entero:** el lazo por losa de `SistemaPieperMartensCalculator.Calcular` (`src.Core/Calculo/PieperMartens/SistemaPieperMartensCalculator.cs:42-50`) llama a `_momentos.Calcular(losa)` (línea 44) **sin captura por losa**; la excepción sube por `MomentosCalculator.cs:42` hasta el `catch` global de `MainViewModel.CalcularNativo` (`src/ViewModels/MainViewModel.cs:1472`) — la 1ª losa no soportada anula el cálculo de TODAS. Contrasta con el patrón correcto por-losa de `MotorFeaService.CalcularSistemaConMotorAsync` (`src.Core/Services/MotorFeaService.cs:304-310`: `catch` por losa + `Debug.WriteLine` + `continue`).
3. **Mensaje engañoso:** `src.Core/Validation/Rules/TipoLosaValidoRule.cs:44-50` declara "catálogo de 23 tipos Pieper-Martens **soportados por la aplicación**" y "el motor de cálculo no puede procesar tipos **fuera** de este catálogo" — implicando que los 23 de adentro sí se procesan, cuando el cálculo nativo solo mapea 1.
4. **Tablas cargadas pero muertas:** `TablasPerdomo.json` (recurso embebido, `src.Core/Calculo/PieperMartens/TablasPerdomo.json`) contiene **21 subtipos** (`"1","2a","2b","3a","3b","4","5a","5b","6","7a","7b","8a","8b","9a","9b","10a","10b","11a","11b","12a","12b"`); solo `"4"` es alcanzable.
5. **UI con descenso equitativo:** la ruta geométrica por área tributaria ya existe (`src.Core/Transmision/DescensoColumnas.cs:92-109` `PredimensionarGeometrico` + `src.Core/Transmision/RepartoGeometrico.cs:168-193` `AsignarVigasAColumnas`) y está cableada SOLO en `src/Views/Planta2DEditorView.axaml.cs:565-568`. La UI principal sigue en equitativo: `src/ViewModels/BajadaCargasViewModel.cs:158` (`DescensoColumnas.RepartirEquitativo`) y `src/ViewModels/ColumnasEditorViewModel.cs:137` (`DescensoColumnas.PuDemandaKN(cargaEnBase, numColumnas)`).

## 2. Objetivo y no-objetivos

**Objetivo:** que el motor nativo procese los **23 códigos** del catálogo (`Sistema.cs` → `TipoLosa.CodigosValidos`, `src.Core/Models/Sistema.cs:659-660`) sin `NotSupportedException`, con degradación **por losa** (una losa mala no aborta el sistema), mensajes de validación veraces, los **21/21 subtipos** del JSON alcanzables, y la UI principal usando el descenso geométrico por área tributaria.

**No-objetivos (FUERA de F3):**
- **Nunca tocar `Losas.exe` ni su import** (restricción permanente del repo).
- Reparto viga→columna por **reacciones reales** (hoy 50/50: `RepartoGeometrico.cs:176` `mitad = carga.FuerzaTotal / 2.0`, comentario en `:166`). → **F4** (necesita reacciones del solver).
- Corregir las descripciones/patrones NESW del `Catalogo` en `src.Core/Models/Sistema.cs:583-644` (metadatos visuales; cambiarlos es cambio de comportamiento de UI fuera de alcance — se documenta su falta de fiabilidad en §4.3 y queda diferido).
- Solver Python / peso propio / continuidad de paneles. → **F4**.
- Momento de ménsula adicional de los vuelos (q·l_vuelo²/2 del volado en sí): el modelo `Losa` no tiene luz de vuelo; los códigos x3/x4 calculan el **panel** con su tabla (ver §4.3). Diferido hasta tener fixtures de Losas.exe.

**NOTA de validación (decidida con el usuario):** la validación final de fixtures contra `Losas.exe` (solo corre en Windows) **queda para el usuario**. F3 ancla lo verificable en Linux: regresión exacta del código 40 y el voladizo 71 (RESTAURANTE 2), propiedades internas del JSON, biyección del mapeo y tests parametrizados de los 23 códigos. Si una fixture futura contradice una entrada del mapeo, el fix es 1 línea del diccionario.

## 3. Diseño detallado

### 3.1 GATE A — captura por-losa en `SistemaPieperMartensCalculator`

Imitar `MotorFeaService.cs:304-310`. En `SistemaPieperMartensCalculator.Calcular` (`SistemaPieperMartensCalculator.cs:42-50`):

- **Paso 1 (momentos):** envolver `_momentos.Calcular(losa)` en `try/catch (Exception)`; al fallar: `Debug.WriteLine`, registrar `losa.Id` en `salida.LosasNoParseadas` (lista existente en `src.Core/Models/SalidaPerdomo.cs:80`, semántica "losas sin resultado" — se amplía su doc-comment) y `continue`.
- **Paso 2 (armaduras de vano, `SistemaPieperMartensCalculator.cs:56-67`):** saltar losas sin fila (`filaPorLosa.TryGetValue`), hoy indexa directo en `:58`.
- **Paso 3 (apoyos, `AgregarApoyos`, `SistemaPieperMartensCalculator.cs:103-123`):** saltar bordes que referencien una losa omitida — `BalanceoMomentos.Balancear` indexa `momentos[losaI]`/`momentos[losaJ]` (`src.Core/Calculo/PieperMartens/BalanceoMomentos.cs:37-38`) y lanzaría `KeyNotFoundException`.
- `CalcularYAplicar` (`SistemaPieperMartensCalculator.cs:83-101`) no cambia: itera `salida.Momentos`, las losas omitidas simplemente no se actualizan.
- `MainViewModel.CalcularNativo` (`MainViewModel.cs:1457-1474`) **no se toca**: su `catch` de `:1472` deja de dispararse por tipos sin mapear porque la excepción ya no sube.

Este gate va **PRIMERO**: aísla cada subtipo para poder validar el mapeo código a código.

### 3.2 GATE B — mensaje veraz en `TipoLosaValidoRule`

Reescribir `Descripcion` y `ClausulaCita` (`TipoLosaValidoRule.cs:44-50`) para que describan **pertenencia al catálogo del formato .DL**, sin afirmar qué subconjunto "soporta"/"procesa" el motor (verdadero antes y después de completar el mapeo):

- `Descripcion`: «La losa usa el código de tipo {N}, que no pertenece al catálogo de 23 tipos de borde del formato .DL (10, 13, 14, 21–24, 31–34, 40, 43, 44, 51–54, 60, 63, 64, 71, 72). Corregí el tipo en el editor o en el archivo .DL antes de calcular.»
- `ClausulaCita`: «Catálogo de patrones de borde de Pieper-Martens — tipos del formato .DL (Losas v5.21).»

Los tests existentes (`tests/LosasPlus.Tests/ValidationEngineTests.cs:291-331`) no aseveran sobre el texto de `Descripcion` (solo `Codigo`/`Severidad`/ubicación), así que el cambio es seguro; se agrega un test del texto.

### 3.3 Mapeo completo `CodigoASubtipo` — tabla y justificación

**Convención derivada (código .DL = `d1 d2`):**

- **`d1` = número de TABLA del PDF de Perdomo (1–6)**; `d1 = 7` = voladizo one-way (fuera de tablas).
- **`d2 = 0`** → tabla de **bloque único** (casos simétricos; el PDF trae UN bloque solo para las tablas **1, 4 y 6** — `TABLAS-PERDOMO.md:50-52`).
- **`d2 = 1 / 2`** → orientación **a / b** de las tablas de **dos bloques** (2, 3, 5: el mismo caso girado 90°).
- **`d2 = 3 / 4`** → caso de **borde libre** (losas apoyadas en TRES bordes, tablas 7–12 del mismo PDF — su título: «LOSAS CONTINUAS APOYADAS EN TRES Y CUATRO BORDES»): **tabla `d1 + 6`, orientación a / b**.

**Evidencia (4 líneas independientes, verificadas en este repo):**

- **E1 — ancla numérica:** `40 → "4"` está **verificado contra `Losas.exe`** (RESTAURANTE 2, error ≤ 0.007 ton·m/m): `src.Core/Calculo/PieperMartens/TABLAS-PERDOMO.md:86-102` y `tests/LosasPlus.Tests/PieperMartens/SistemaPieperMartensCalculatorTests.cs`. Eso fija `d1 = nº de tabla` (40 = tabla 4, NO "tres bordes continuos").
- **E2 — estructura del set de códigos:** los códigos `x0` existen **solo** para las familias 1, 4 y 6 (10, 40, 60) = exactamente las 3 tablas de bloque único; los `x1/x2` existen **solo** para las familias 2, 3 y 5 (21/22, 31/32, 51/52) = exactamente las 3 tablas de dos bloques (`Sistema.cs:583-644`). La correspondencia es perfecta y no puede ser casual.
- **E3 — biyección de cardinalidades:** 23 códigos = **9** sin vuelo + **12** con vuelo (x3/x4) + **2** voladizos (71/72); 21 subtipos del JSON = **9** de 4 bordes apoyados (tablas 1–6) + **12** de borde libre (tablas 7–12 × a/b). La única lectura que hace alcanzables los 21/21 (objetivo explícito del roadmap, sección F3) es x3/x4 ↔ tablas 7–12. Además el orden de continuidad de 7–12 replica el de 1–6: [0, 1-opuesto, 1-adyacente, 2-opuestos, 2-adyacentes, 3] empotrados sobre los 3 bordes apoyados (`TABLAS-PERDOMO.md:55-82`).
- **E4 — simetría de rotación verificada en el JSON:** para los 9 pares a/b, en losa cuadrada (ε = 1.0, fila tabulada) `F(b)` = `F(a)` con X↔Y intercambiados (diferencia < 2%, verificado numéricamente sobre `TablasPerdomo.json`), y los patrones de nulos de Sx/Sy del JSON coinciden 21/21 con la columna "condición de borde" de `TABLAS-PERDOMO.md:55-77`. Confirma que a/b son el mismo caso girado 90° → fija la semántica de orientación de `d2`.

**Por qué NO se deriva de los patrones NESW del `Catalogo` (`Sistema.cs:583-644`):** están demostrados poco fiables — (a) 40 descrito como "Tres bordes continuos" (`Sistema.cs:614`) cuando la evidencia numérica lo fija en tabla 4 = 2 adyacentes (hallazgo de `TABLAS-PERDOMO.md:98-102`: «La evidencia numérica manda»); (b) 44, 54 y 64 comparten el patrón idéntico `[E,V,E,V]` (`Sistema.cs:618-619`, `:628-629`, `:636-637`), imposible si fueran casos distintos. Donde catálogo y estructura coinciden (10, 21, 22, 31, 32, 51, 60), la confianza sube.

**Lectura alternativa descartada:** «x3/x4 = caso base + vuelo usando la tabla base d1» dejaría los 12 subtipos 7a–12b **inalcanzables** (contradice el objetivo 21/21 del roadmap) y se apoya justamente en las descripciones del catálogo demostradas poco fiables. Riesgo acotado: si una fixture de Losas.exe contradice una entrada, el fix es 1 línea.

**TABLA DE MAPEO COMPLETA (23 códigos):**

| Código | Subtipo | Tabla P-M — condición de borde | Justificación | Confianza |
|---:|:---:|---|---|---|
| 10 | `"1"` | T1 — 4 apoyos simples | E2 (bloque único) + catálogo concuerda | alta |
| 21 | `"2a"` | T2a — 1 empotrado horizontal (N/S) → Sy | E2 + E4; catálogo concuerda (N empotrado ≡ S por espejo) | alta |
| 22 | `"2b"` | T2b — 1 empotrado vertical (E/W) → Sx | E2 + E4; catálogo concuerda | alta |
| 31 | `"3a"` | T3a — 2 opuestos N,S | E2 + E4; catálogo concuerda | alta |
| 32 | `"3b"` | T3b — 2 opuestos E,W | E2 + E4; catálogo concuerda | alta |
| 40 | `"4"` | T4 — 2 adyacentes empotrados | **E1: verificado vs Losas.exe** | verificada |
| 51 | `"5a"` | T5a — 3 empotrados, apoyo horizontal | E2 + E4; patrón del catálogo concuerda (espejo N↔S) | alta |
| 52 | `"5b"` | T5b — 3 empotrados, apoyo vertical | E2 + E4 (d2=2 → bloque b) | alta |
| 60 | `"6"` | T6 — perimetral (4 empotrados) | E2 (bloque único) + catálogo concuerda | alta |
| 13 | `"7a"` | T7a — 3 apoyos; N libre | E3 (tabla d1+6=7, d2=3→a) | media — pendiente fixture |
| 14 | `"7b"` | T7b — 3 apoyos; E libre | E3 (d2=4→b) | media — pendiente fixture |
| 23 | `"8a"` | T8a — libre N; opuesto S empotrado | E3 | media — pendiente fixture |
| 24 | `"8b"` | T8b — libre W; opuesto E empotrado | E3 | media — pendiente fixture |
| 33 | `"9a"` | T9a — libre N; adyacente E empotrado | E3 | media — pendiente fixture |
| 34 | `"9b"` | T9b — libre W; adyacente S empotrado | E3 | media — pendiente fixture |
| 43 | `"10a"` | T10a — libre N; E,W empotrados | E3 | media — pendiente fixture |
| 44 | `"10b"` | T10b — libre W; N,S empotrados | E3 | media — pendiente fixture |
| 53 | `"11a"` | T11a — libre N; S,E empotrados | E3 | media — pendiente fixture |
| 54 | `"11b"` | T11b — libre W; E,S empotrados | E3 | media — pendiente fixture |
| 63 | `"12a"` | T12a — libre N; E,S,W empotrados | E3 | media — pendiente fixture |
| 64 | `"12b"` | T12b — libre W; N,E,S empotrados | E3 | media — pendiente fixture |
| 71 | — (voladizo) | one-way, Msy = q·Ly²/2 | YA implementado (`MomentosCalculator.cs:35-39,58-66`); verificado vs Losas.exe | verificada |
| 72 | — (voladizo) | one-way, Msx = q·Lx²/2 | simétrico del 71, ya implementado | alta |

El diccionario `CodigoASubtipo` queda con **21 entradas** (biyección exacta con los 21 subtipos del JSON); 71/72 **no** entran al diccionario porque `MomentosCalculator.EsVoladizo` los resuelve antes de la tabla (`MomentosCalculator.cs:35-39`).

### 3.4 Descenso geométrico por área tributaria en la UI principal

Cablear la ruta que ya existe (`DescensoColumnas.PredimensionarGeometrico`, `DescensoColumnas.cs:92-109`; `RepartoGeometrico.AsignarVigasAColumnas`, `RepartoGeometrico.cs:168-193`), hoy solo usada por `Planta2DEditorView.axaml.cs:565-568`. Estrategia **geométrico-con-fallback** (los modelos sin vigas en planta conservan el comportamiento actual, y los tests existentes — fixtures sin vigas — siguen verdes):

- **`BajadaCargasViewModel.PredimensionarZapatas` (`BajadaCargasViewModel.cs:150-188`):** intentar `PredimensionarGeometrico(nivel, PresionAdmisible)` por cada nivel del edificio; si **ningún** nivel asigna carga (sin vigas con geometría), caer a `RepartirEquitativo` (línea 158 actual). `ResumenZapatas` declara el modo usado («reparto geométrico por área tributaria» vs el actual «reparto equitativo, Wu» de `:187`). Las filas `ZapatasDiseno` consumen `r.CargaAxial` igual (ambas rutas reportan Wu en ton; conversión a N en `:164`).
- **`ColumnasEditorViewModel.TomarPuDelDescenso` (`ColumnasEditorViewModel.cs:127-138`):** nuevo helper puro `DescensoColumnas.PuDemandaGeometricoKN(Nivel, Columna)` (busca la columna en `AsignarVigasAColumnas` y convierte ton→kN con `KN_por_Ton`, `DescensoColumnas.cs:33`; 0 si no recibe carga). El VM lo usa para la columna `Seleccionada`; sin selección o sin carga geométrica, cae al equitativo actual (`:137`).
- **Aproximación documentada:** misma que Planta2D — la carga geométrica es la del **nivel** vía sus vigas (no el acumulado multi-nivel); el reparto exacto por reacciones reales va a **F4** (`RepartoGeometrico.cs:166`).

### 3.5 Plan de commits (rama `engine/f3-pieper-martens-21`)

- **C0 — docs:** este spec + el plan.
- **C1 — GATE A:** test de captura por-losa (rojo) → implementación → verde.
- **C2 — GATE B:** test del mensaje veraz (rojo) → implementación → verde.
- **C3 — mapeo 21/21:** tests parametrizados (21 pares + 23 códigos sin excepción + biyección + simetría a/b) (rojos) → diccionario completo → verdes + regresión RESTAURANTE 2 intacta.
- **C4 — UI Bajada de Cargas:** test geométrico (rojo) → wiring con fallback → verde.
- **C5 — UI Editor de Columnas:** test del helper core + test del VM (rojos) → helper + wiring → verdes.
- **C6 — cierre:** actualizar región curada de `STATE.md` (quitar el issue F3, reescribir el de descenso como «reparto viga→columna 50/50 → F4») + `./estado-real.sh` re-estampa.

## 4. Testing / verificación

- **TDD estricto:** cada cambio de código va precedido de su test en rojo (ver plan).
- **Regresión obligatoria:** `tests/LosasPlus.Tests/PieperMartens/SistemaPieperMartensCalculatorTests.cs` (RESTAURANTE 2: momentos, balanceo y aceros del 40/71) debe seguir pasando sin tocar — el mapeo nuevo no altera la entrada `[40]="4"`.
- **Parametrizado 23/23:** `MomentosCalculator.Calcular` no lanza para ningún código de `TipoLosa.CodigosValidos` y devuelve momentos finitos ≥ 0 (Fx/Fy del JSON son positivos; Ms usa `Math.Abs`, `MomentosCalculator.cs:46-47`).
- **Biyección 21/21:** los 21 códigos no-voladizo mapean a 21 subtipos **distintos**, todos presentes en el JSON (`Factores(st, 1.0)` no lanza).
- **Propiedad del JSON:** simetría de rotación de los 9 pares a/b en ε = 1.0 (tolerancia 2%) — guarda contra futuros swaps de orientación.
- **UI:** tests de VM (sin Avalonia) con el fixture geométrico de `tests/LosasPlus.Tests/PredimensionarGeometricoTests.cs:25-54` (4 columnas, C04 recibe Wu = 60 t); los tests equitativos existentes (`BajadaCargasViewModelTests.cs:87-112`, `ColumnasEditorViewModelTests.cs:88-105`, `BajadaCargasZapataDisenoTests.cs`) siguen verdes por el fallback.
- Cierre: `dotnet test LosasPlus.Linux.sln` ≥ 1106 passed / 0 failed; `./estado-real.sh` exit 0.

## 5. Criterios de aceptación (del roadmap F3)

1. **Ningún código del catálogo lanza `NotSupportedException`** — test parametrizado 23/23 verde.
2. **Degradación por-losa:** un sistema con una losa de tipo no mapeado (p.ej. 99, fuera de catálogo) produce el `SalidaPerdomo` de las demás losas y registra la omitida; ningún borde que la referencie lanza.
3. **Mensaje veraz:** `TipoLosaValidoRule` ya no afirma "soportados por la aplicación" ni "el motor no puede procesar"; describe pertenencia al catálogo del formato .DL.
4. **21/21 subtipos alcanzables** (biyección verificada por test).
5. **UI con descenso geométrico:** Bajada de Cargas y Editor de Columnas usan área tributaria cuando hay geometría de vigas, con fallback equitativo.
6. Regresión RESTAURANTE 2 intacta; suites verdes; `STATE.md` re-estampado y región curada actualizada.
7. `Losas.exe` y su import: **intactos**.

## 6. Riesgos y mitigaciones

- **Mapeo x3/x4 sin fixture (confianza media):** mitigado con (a) tabla de confianza explícita en §3.3, (b) validación final contra `Losas.exe` delegada al usuario en Windows, (c) corrección = 1 línea del diccionario por código, (d) GATE A garantiza que un mapeo erróneo nunca tumba el sistema.
- **Captura por-losa demasiado amplia (`catch (Exception)`):** mismo trade-off ya aceptado en `MotorFeaService.cs:304-310`; el id queda registrado en `LosasNoParseadas` y el detalle en `Debug.WriteLine` — no se oculta, no se aborta.
- **Cambiar `ResumenZapatas` rompe asserts de texto:** los tests existentes solo verifican `Contains("3 columna", ...)` (`BajadaCargasViewModelTests.cs`) — se conserva el prefijo «N columna(s)».
- **Doble ruta (geométrico/equitativo) confunde:** el resumen declara el modo; el fallback reproduce exactamente el comportamiento previo cuando no hay vigas.
- **Tentación de "arreglar" el `Catalogo` de `Sistema.cs`:** prohibido por no-objetivos; solo se documenta (§3.3) — cambiarlo altera UI/validación fuera de alcance.
