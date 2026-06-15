# Spec — #5a · MemoriaPlus/LosasPlus como cliente del motor (vía CLI)

**Fecha:** 2026-06-14
**Repo:** `~/Downloads/EstructurasRD-main` (suite .NET 8 / Avalonia 11.3), rama `ui/editor-planta`.
**Motor (otro repo, solo-lectura para 5a):** `~/Downloads/EstructurasRD-engine/motor-fea` (Python).
**Kickoff de origen:** `docs/superpowers/2026-06-14-5a-memoriaplus-cliente-motor-kickoff.md`.

---

## 1. Objetivo (definición de "hecho")

Añadir un camino por el cual el escritorio **invoque el motor Python para diseñar las losas
del nivel activo** y muestre los resultados (momentos + armado por losa) en la vista de resultados
que la app ya tiene, **sin tocar el camino de `Losas.exe` (F. Perdomo), que sigue siendo el
predeterminado**.

5a está **hecho** cuando:
1. Existe un comando "Calcular losas con el motor (FEA)" que, para cada losa del `Sistema` activo,
   construye el JSON de parámetros, invoca el motor por CLI (`--disenar-losa -`, JSON por stdin →
   JSON por stdout), y consume el resultado.
2. Los resultados del motor se traducen al modelo de dominio existente (`SalidaPerdomo`) reutilizando
   `SalidaPerdomoAdapter`, y se ven en la vista de resultados actual.
3. El usuario elige la condición de borde por corrida (defecto: apoyo simple).
4. Errores del motor (exit ≠ 0) por losa se reportan sin tumbar la app.
5. Tests .NET verdes (`dotnet test`), incluyendo tests del adaptador del motor y de conversión de
   unidades.

**Fuera de alcance (gaps documentados):** los 23 tipos Pieper-Martens (solo `simple`/`empotrado`);
notación exacta de despiece de barras; entrada batch en el CLI del motor; memoria `.docx` (#5b);
flujo UI end-to-end (#5c); empaquetado standalone del motor (sub-proyecto posterior).

---

## 2. Hallazgos que enmarcan el diseño (verificados en código)

### 2.1 La frontera de `Losas.exe` es importación de archivo, NO subproceso con pipe
`LosasPlus/ViewModels/MainViewModel.cs` lanza `Losas.exe` con `UseShellExecute=true` (sin redirección
de I/O); el usuario exporta manualmente un `.TXT` y lo reimporta con un file-picker
(`MemoriaPlus/.../MainViewModel.cs::ImportarTxtPerdomo` → `SalidaPerdomoAdapter.FromFile`). El motor,
en cambio, **sí** puede ser un pipe `stdin→stdout` real: la integración del motor es **más
automatizada** que la del legado. La costura real es el lado **importación**, no el de lanzamiento.

### 2.2 La superficie de losa del motor NO calza 1:1 con la app
`--disenar-losa` (en `motor_fea/diseno_losa.py` → `core/losa_fem.py`) es **una placa rectangular FEM
ACM** con bordes solo `simple` o `empotrado`. La app modela cada `Losa` con un `Tipo` (1–23,
clasificación de continuidad Pieper-Martens) que usa Perdomo. **El motor no tiene un camino
Pieper-Martens por tipo** (grep `pieper|martens` = 0 resultados). Por eso 5a NO traduce los 23
tipos; usa una condición de borde elegida por el ingeniero (defecto simple).

### 2.3 El downstream es reutilizable
`SalidaPerdomoAdapter.From(ParsedOutput, ...)` produce un `SalidaPerdomo` a partir del intermedio
`ParsedOutput`. Si traducimos el JSON del motor a `ParsedOutput`, **todo el downstream (adaptador,
dominio, tabla de resultados) funciona sin cambios**. Esta es la clave de que 5a sea "fino".

### 2.4 Contrato del motor (entrada/salida de `--disenar-losa`)
- **Entrada (params):** `{a, b, nx, ny, E, nu, t, q, fc, fy, recubrimiento, borde}` (SI: m, Pa, Pa,
  N/m², MPa, MPa, mm, `"simple"|"empotrado"`).
- **Salida:** `{w_central, mx_max, my_max, m_apoyo_max, mu_x, mu_y, mu_apoyo, franja_x, franja_y,
  franja_apoyo}`. Cada `franja_*` = `{as_requerido, as_minimo, as_diseno, numero_barra,
  espaciamiento, as_provista, cumple, disponer, ...}`. Momentos en N·m/m; `mu_*` en N·mm/m; `w` en m;
  `as_*` en mm²/m; `espaciamiento` en mm.
- El CLI lee JSON (ruta o `-`), imprime resultado, **exit 1 en error**.

### 2.5 Modelo de losa de la app (entrada disponible)
`Core/Models/Sistema.cs::Losa` ya tiene: `Id, Tipo, Carga (tonf/m²), Espesor (m), Lx (m), Ly (m),
Rec (m), CoordenadaX/Y` y campos de resultado `Mfx/Mfy/MSx/MSy`, `AsxVano/AsyVano`. El intermedio
`ParsedOutput`/`LosaResult` (en `SalidaPerdomoAdapter.cs`) lleva `Mfx/Mfy/MSx/MSy, Dx/Dy,
AsxReq/AsyReq, AsxProv/AsyProv, DisponerX/DisponerY`.

---

## 3. Decisiones de diseño (acordadas con el usuario)

| # | Decisión | Elección |
|---|---|---|
| D1 | Alcance | **Rebanada fina, motor como backend opcional**; Perdomo sigue por defecto; gap 23-tipos documentado. |
| D2 | Empaquetado/invocación | **Comando configurable, defecto `.venv/bin/python -m motor_fea.api.cli`**; sin empaquetado standalone en 5a. |
| D3 | Batching | **Un subproceso del CLI por losa** (`--disenar-losa -`). N arranques de Python; fiel al contrato CLI. |
| D4 | Condición de borde | **El ingeniero elige por corrida** (toggle `simple`/`empotrado`), **defecto apoyo simple**, aplicado a todas las losas. |
| D5 | Unidades | Mapeador y adaptador convierten en ambos sentidos (ver §6). |
| D6 | Errores | Por losa: exit ≠ 0 / stderr → se omite la losa, se continúa, se reporta en una lista de fallidas. La app nunca cae. |
| D7 | Materiales (f'c, fy, E) | Tomar de la config de materiales del proyecto si existe; si no, **defaults RD: f'c=210 kg/cm² (21 MPa), fy=4200 kg/cm² (420 MPa)**, **E = 4700√f'c MPa** (ACI 318). |
| D8 | Malla FEM | Defecto fijo `nx=ny=8`, no expuesto en UI en v1. |
| D9 | Visualización | El motor puebla el mismo `SalidaPerdomo` (vista de resultados existente), **solo en memoria**; no toca ningún `.TXT` importado en disco. `SalidaPerdomo.ArchivoTxt` (u otro marcador de fuente) indica origen "motor-fea". |

---

## 4. Arquitectura (componentes nuevos + reuso)

Tres piezas nuevas en `Core` + un comando en el ViewModel; el resto se reutiliza.

```
Sistema.Losas ──▶ [1] MapeadorLosaMotor ──▶ params JSON (por losa)
                       (Losa + materiales + borde → {a,b,nx,ny,E,nu,t,q,fc,fy,recub,borde})
                       (unidades app → SI)                        │
                                                                  ▼
                                                   [2] MotorFeaClient
                                          (lanza el motor; JSON in → JSON out;
                                           1 llamada por losa; exit code/stderr)
                                                                  │ result JSON (por losa)
                                                                  ▼
                                                   [3] MotorFeaAdapter
                                            (JSON del motor → ParsedOutput;
                                             unidades SI → app)
                                                                  │
                                                                  ▼
                                       ⟳ REUSO: SalidaPerdomoAdapter.From(ParsedOutput, ids)
                                                                  │
                                                                  ▼
                                          SalidaPerdomo ──▶ vista de resultados existente
```

### 4.1 `MotorFeaClient` (Core/Services)
Responsabilidad única: **hablar con el motor**. Construye el `ProcessStartInfo`
(`RedirectStandardInput/Output/Error = true`, `UseShellExecute = false`), escribe el params JSON en
stdin, lee el JSON de stdout, captura exit code + stderr.
- **Configurable:** comando + argumentos resueltos desde settings; defecto
  `python -m motor_fea.api.cli --disenar-losa -` apuntando al intérprete del `.venv` del motor.
- **Abstracción para test:** la ejecución de proceso se expone tras una interfaz
  (`IProcesoRunner` o `Func<string,Task<ResultadoProceso>>`) para poder testear el flujo **sin Python
  instalado**. Un único test de integración (guardado por "motor disponible") ejerce el binario real.
- **Una llamada por losa** (D3). Devuelve por losa: `{ok, jsonSalida, error}`.

### 4.2 `MapeadorLosaMotor` (Core/Services)
Función pura `Losa + MaterialesProyecto + Borde → ParamsLosaMotor` (objeto serializable a JSON).
Aplica conversiones de unidades app→SI (§6) y los defaults D7/D8.
- `a=Lx, b=Ly, t=Espesor, recubrimiento=Rec*1000(mm)`; `q=Carga·9806.65 (tonf/m² → N/m²)`;
  `nx=ny=8`; `fc, fy` en MPa; `E` en Pa; `borde` = toggle de la corrida.

### 4.3 `MotorFeaAdapter` (Core/Services)
Función pura `JSON del motor (por losa) + LosaId → LosaResult` (y agregación a `ParsedOutput`),
espejo de cómo `TxtParser` produce `ParsedOutput`. Convierte unidades SI→app (§6). Luego se llama
`SalidaPerdomoAdapter.From(parsedOutput, idsEsperados)` sin cambios.

### 4.4 Comando en el ViewModel (`MemoriaPlus/ViewModels/MainViewModel.cs`)
Vive **junto a `ImportarTxtPerdomo`** (es el paralelo del camino de importación, no del de
lanzamiento de `LosasPlus`).
`CalcularLosasConMotorAsync`: para cada `Losa` del `Sistema` activo → mapeador → client → adapter;
agrega a `ParsedOutput`; llama `SalidaPerdomoAdapter.From`; asigna a `SistemaActivo.SalidaPerdomo`;
expone la lista de losas fallidas y un status. Marca la fuente como "motor-fea".

### 4.5 Settings
`ComandoMotor` (path al intérprete/exe + args) y `BordeLosaDefecto` (`simple`). Persisten con la
config de la app existente.

---

## 5. Flujo de datos (una corrida)

1. Usuario abre nivel con losas y pulsa **"Calcular losas con el motor (FEA)"**.
2. Diálogo/selección de **borde** (defecto `simple`).
3. Por cada `Losa` en `SistemaActivo.Losas`:
   a. `MapeadorLosaMotor` → params JSON (unidades SI).
   b. `MotorFeaClient` lanza el motor, envía params por stdin, lee resultado por stdout.
   c. Si exit ≠ 0 → se registra la losa en `fallidas` y se continúa.
   d. `MotorFeaAdapter` → `LosaResult` (unidades app), se agrega a `ParsedOutput`.
4. `SalidaPerdomoAdapter.From(parsedOutput, ids)` → `SalidaPerdomo`.
5. `SistemaActivo.SalidaPerdomo = salida` (en memoria); la vista de resultados se actualiza.
6. Status: "N losas calculadas, M fallidas (ids…)".

---

## 6. Conversión de unidades

| Magnitud | Motor (SI) | App (Perdomo) | Conversión |
|---|---|---|---|
| Carga `q` | N/m² (Pa) | `Carga` tonf/m² | app→motor: ×9806.65 |
| Momento `mx/my/m_apoyo` | N·m/m | `Mfx/Mfy/MSx/MSy` tonf·m/m | motor→app: ÷9806.65 |
| Momento último `mu_*` | N·mm/m | `Mu` tonf·m/m | motor→app: ÷9.80665e6 |
| Acero `as_*` | mm²/m | cm²/m | motor→app: ÷100 |
| Peralte efectivo `d` | mm | `D` m | motor→app: ÷1000 |
| Espesor `t`, `Lx/Ly` | m | m | sin cambio |
| Recubrimiento | mm | `Rec` m | app→motor: ×1000 |
| `f'c`, `fy` | MPa | (config / default) | 21 MPa = 210 kg/cm²; 420 MPa = 4200 kg/cm² |
| Despiece `disponer` | `"#5 @ 150"` (mm) | `"Ø10 c/18"` (cm) | **se pasa tal cual**; notación distinta = trabajo futuro |

> Constante: 1 tonf·m = 9806.65 N·m (tonf = 1000 kgf, 1 kgf = 9.80665 N).

---

## 7. Manejo de errores

- **Exit ≠ 0 / JSON inválido / stderr:** la losa se omite, su id va a `fallidas`, la corrida sigue.
- **Motor no encontrado / intérprete inválido:** error claro al inicio ("no se pudo ejecutar el
  motor: <comando>"), sin procesar losas, sin caer.
- **Timeout opcional** por losa (defecto razonable, p. ej. 30 s) para no colgar la UI.
- Espejo conceptual de `SalidaPerdomo.LosasNoParseadas` (la app ya muestra losas no resueltas).

---

## 8. Estrategia de tests (`dotnet test`)

1. **`MapeadorLosaMotorTests`** — `Losa` conocida → params JSON esperado (incluye conversión
   `Carga`→`q`, `Rec`→mm, defaults f'c/fy/E, borde).
2. **`MotorFeaAdapterTests`** — JSON del motor (golden, p. ej. el ejemplo 5×5 del motor) → `LosaResult`
   con unidades app correctas (round-trip de momentos y acero).
3. **`MotorFeaClientTests`** — con `IProcesoRunner` mockeado: éxito, exit ≠ 0, stdout vacío, stderr;
   verifica que se arma stdin correcto y se interpreta la salida. **Sin Python.**
4. **Integración (guardada)** — si el motor está disponible, ejerce el binario real con una losa y
   compara contra el resultado conocido. Se omite si no hay motor.
5. Patrón espejo de `TxtParserTests` (fixtures + asserts de valores exactos).

---

## 9. Archivos previstos

**Nuevos (`Core`):**
- `Core/Services/MotorFeaClient.cs` (+ `IProcesoRunner`/`ResultadoProceso`).
- `Core/Services/MapeadorLosaMotor.cs` (+ `ParamsLosaMotor`).
- `Core/Services/MotorFeaAdapter.cs`.
- `Core/Models/` — DTOs de params/resultado del motor si conviene.

**Modificados:**
- `MemoriaPlus/ViewModels/MainViewModel.cs`: comando `CalcularLosasConMotorAsync` (junto a
  `ImportarTxtPerdomo`) + binding al botón.
- Vista: botón "Calcular losas con el motor (FEA)" + selección de borde + status/fallidas.
- Settings: `ComandoMotor`, `BordeLosaDefecto`.

**Reutilizados sin cambio:** `SalidaPerdomoAdapter`, `SalidaPerdomo`, modelo de dominio, vista de
resultados.

**Tests:** `tests/LosasPlus.Tests/` — `MapeadorLosaMotorTests`, `MotorFeaAdapterTests`,
`MotorFeaClientTests` (+ fixture golden JSON), integración guardada.

---

## 10. Limitaciones heredadas / gaps conocidos (v1)

- **23 tipos Pieper-Martens → solo `simple`/`empotrado`** (D4). El `Tipo` de la losa no se traduce;
  el ingeniero elige el borde. Cierre exacto = camino PM-por-tipo en el motor (cambio de motor, fuera
  de 5a).
- **Notación de despiece** difiere (`#5 @ 150` vs `Ø10 c/18`); se pasa tal cual.
- **N arranques de Python** (uno por losa); aceptable para 5a. Mejora futura = entrada batch en el
  CLI del motor (cambio de motor).
- **Combinación de cargas:** limitación heredada del motor (#1 §9) — `resolver` suma cargas sin
  distinguir caso; aquí solo aplica `q` de gravedad, así que no afecta a 5a.
- **Origen de f'c/fy/E:** si la app no expone materiales por sistema, se usan los defaults RD (D7).

---

## 11. Próximo paso

Invocar `superpowers:writing-plans` para escribir el plan de implementación detallado a partir de
este spec.
