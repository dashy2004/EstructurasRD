# KICKOFF — #5a MemoriaPlus/LosasPlus como cliente del motor (vía CLI)

> **Para una sesión limpia, rooteada en ESTE repo .NET** (`~/Downloads/EstructurasRD-main`,
> rama `ui/editor-planta`). Lee este archivo primero. Es el arranque de **#5a**, el primer
> sub-proyecto de **#5 "Avalonia como cliente de memoria"**. Este doc NO es un spec: enmarca el
> problema, lista lo reutilizable y las preguntas abiertas para arrancar el ciclo
> **brainstorming → spec → plan → subagent-driven** sin volver a derivar el contexto.
>
> **Primer paso en la sesión limpia:** invocar `superpowers:brainstorming` con este doc como
> contexto y resolver las "Preguntas abiertas" (§5) con el usuario, una a una.

**Fecha:** 2026-06-14
**Repo de trabajo:** `~/Downloads/EstructurasRD-main` (suite .NET/Avalonia), rama `ui/editor-planta`.
**Motor (otro repo, solo-lectura para 5a):** `~/Downloads/EstructurasRD-engine/motor-fea` (Python),
rama `engine/shell-web-webxr` == `master` local. **NO compartir worktree:** son repos/worktrees
distintos del mismo árbol de objetos (git-common-dir en `~/Downloads/EstructurasRD-main/.git`).
**Stack:** C# / .NET 8 (8.0.421 instalado) / Avalonia 11.3. Distinto al de #1–#4 (Python/JS).

---

## 1. Qué es #5 y dónde encaja 5a

**#5 = reposicionar la app de escritorio .NET/Avalonia (LosasPlus + MemoriaPlus) como CLIENTE del
motor Python `motor_fea`** (analizar/diseñar/visualizar vía la frontera de ADR-0001) y como
generador de la **memoria de cálculo `.docx`**. La app YA EXISTE y está en desarrollo activo (UI de
planta, muros, etc.); #5 NO es greenfield ni recuperación — es **integración + reporte**.

Descomposición acordada (brainstorming 2026-06-14):
- **5a (este kickoff) — cliente del motor (vía CLI):** que el escritorio invoque el motor Python en
  la misma frontera donde hoy llama a `Losas.exe`, reemplazando/aumentando ese camino.
- **5b — memoria `.docx`:** `GenerarView` produce la memoria de cálculo formal desde los resultados
  del motor (infra ya presente: `DocumentFormat.OpenXml 3.3.0` + `Resources/templates/*.docx`).
- **5c — flujo UI:** end-to-end datos → niveles → cargas → analizar-vía-motor → generar memoria.

5a es la base: probar que el escritorio puede manejar el motor Python y consumir sus resultados.

## 2. Decisiones ya tomadas (brainstorming)

| Decisión | Elección |
|---|---|
| Qué es #5 | **App de escritorio (Avalonia) como cliente del motor** (analiza/diseña/visualiza) + export memoria `.docx`. |
| Integración de 5a | **CLI del motor AHORA, HTTP después.** Mismo contrato JSON modelo/resultados (ADR-0001). |
| Frontera | La misma donde hoy se invoca `Losas.exe` (`ProcessStartInfo`/`Redirect*` en MainViewModel). |
| Alcance de 5a | Sólo la integración/paridad de análisis. `.docx` (5b) y flujo UI (5c) quedan fuera. |

## 3. Las dos caras de la frontera

### 3.1 Motor (Python, `~/Downloads/EstructurasRD-engine/motor-fea`) — frontera CLI ya lista
- `motor_fea/api/cli.py` (entry point `motor_fea.api.cli:main`, también `motor-fea`):
  - `motor-fea --analyze MODELO.json` (o `-` = **stdin**) → **resultados JSON por stdout** (frame FEA).
  - `motor-fea --disenar-losa PARAMS.json` (o `-`) → diseño de losa JSON.
  - `motor-fea --version`; `motor-fea --serve [MODELO.json] --host --port` (HTTP, evolución futura).
  - `_ejecutar(ruta, pipeline)`: lee JSON (ruta o `-`), aplica el pipeline, imprime; **exit 1 en error**.
- Contrato de datos: `motor_fea/api/contrato.py` (`analizar_json`, `disenar_losa_json`,
  + para HTTP: `analizar_completo_dict`, `visor_dict`, `esfuerzos_modelo_dict`). DTO `esfuerzos` por
  elemento = `{id, longitud, diagrama:[[s,N,Vy,Vz,T,My,Mz],...]}` (convención interna tracción +).
- Spec del contrato de escritura/esfuerzos: `motor-fea/docs/superpowers/specs/2026-06-13-api-escritura-esfuerzos-design.md`.
- ADR de la frontera: `motor-fea/docs/ADR-0001-integracion.md` (define CLI como MVP, HTTP como evolución;
  núcleo `core/` y `normativa/` puros, sólo `api/` toca I/O).
- **Invocación desde .NET:** `ProcessStartInfo` a `python -m motor_fea.api.cli --analyze -` con el JSON
  del modelo por `StandardInput`, leyendo stdout (espejo del patrón `Losas.exe`). Empaquetado del motor
  a evaluar (PyInstaller → exe sin dependencia de Python en destino, vs `.venv/python -m`).
- Limitación heredada del motor (spec #1 §9): `resolver` suma todas las cargas sin distinguir `caso`
  (D/W) → esfuerzos = demanda combinada sin factorar.

### 3.2 Escritorio (.NET, ESTE repo) — qué reemplazar/aumentar
- `src/MemoriaPlus/` (Avalonia): `App`, `MainWindow`, `Views/` (DatosGenerales, Niveles, Cargas,
  Generar, Explorador, ComingSoon), `ViewModels/MainViewModel.cs`. Ya referencia
  `DocumentFormat.OpenXml 3.3.0` + `Resources/templates/*.docx` (para 5b).
- `src/LosasPlus/` (app principal Avalonia, editor de planta) + `src/UI.Shared/`.
- `src/Core/` (`LosasPlus.Core`): dominio + servicios. **Frontera Losas.exe a replicar:**
  - `Core/Services/TxtParser.cs`, `TxtLineClassifier.cs`, `TxtTabla.cs` — parsean el `.txt` de
    `Losas.exe` (F. Perdomo).
  - `Core/Models/SalidaPerdomo.cs` + `Core/Services/SalidaPerdomoAdapter.cs` — modelo de resultados
    de `Losas.exe` y su adaptador al dominio. **5a = escribir un `MotorFeaAdapter` paralelo** que
    consuma el JSON del motor en vez del `.txt`.
  - `MainViewModel.cs` (~L901 `ImportarSalida…`, filtro "Salida Losas.exe" `*.txt`; ~L488 comentario
    "arrancar … vía 'Generar memoria'") — el punto de importación/lanzamiento a interceptar.
  - `Core/Services/PluginHost.cs` — abstracción de plugins/backends (¿el motor como nuevo backend?).
  - `Core/Services/ProyectoService.cs` — persistencia `.lpx.json` del proyecto.
- Dominio: `Core/Models/{Sistema.cs, Sistema.MemoriaPlus.cs, Edificio.cs, Columna.cs, Zapata.cs,
  CargasGlobales.cs}` + `Losas` (en `LosasPlus.Models`). **App centrada en losas** (`SistemaActivo.Losas`).

## 4. El problema central de 5a — mapeo de modelo

`Sistema`/`Losas` (.NET) → **JSON del modelo que el motor espera** → invocar CLI → **resultados JSON**
→ modelo de resultados .NET (paralelo a `SalidaPerdomo`). Piezas nuevas previstas:
- `MotorFeaClient` (lanzador de proceso: `ProcessStartInfo` + stdin/stdout + exit code), reusando el
  patrón con que hoy se lanza `Losas.exe`.
- `MotorFeaAdapter` (JSON del motor → dominio .NET), espejo de `SalidaPerdomoAdapter`.
- Un mapeador `Sistema → modelo JSON del motor` (geometría/soportes/cargas/secciones).
- Mantener `Losas.exe` como camino seleccionable (paridad/fallback) salvo que el spec decida quitarlo.

## 5. Preguntas abiertas para el brainstorming (resolver una a una)

1. **¿Qué pipeline del motor reemplaza a `Losas.exe`?** La app es de **losas** (`SalidaPerdomo` =
   momentos/deflexiones por losa); el motor tiene `--analyze` (frame FEA 3D) y `--disenar-losa`
   (diseño de losa). Hay que confirmar qué surface del motor produce lo que `SalidaPerdomo` lleva
   (revisar `viz/losa`, `disenar_losa_json`, y el modelo de losa del motor). **Posible gap real.**
2. **¿Empaquetado del motor?** PyInstaller → exe autónomo (sin Python en destino) vs `.venv/python -m`
   (requiere entorno Python). Afecta distribución del escritorio.
3. **¿Reemplazar o aumentar?** ¿Se quita `Losas.exe` o se ofrece el motor como backend adicional
   (vía `PluginHost`)? Paridad/migración.
4. **Fidelidad del mapeo:** ¿`Sistema`/`Losas` tiene todos los inputs FEA que el motor necesita
   (geometría, apoyos, cargas, secciones, materiales)? ¿Gaps a cubrir?
5. **Unidades y errores:** unidades del motor (SI: N, N·m, m) vs las del escritorio; superficies de
   error (exit code != 0, JSON de error, 400/422 del lado HTTP futuro).
6. **Alcance del "hecho" de 5a:** ¿paridad de análisis (resultados de losa mostrados en la app desde
   el motor) y nada más? `.docx` (5b) y flujo UI (5c) explícitamente fuera.
7. **Testing:** la suite .NET (≈1234 tests) y `dotnet test`; ¿tests de integración del cliente del
   motor (mock del proceso / golden JSON)? El motor tiene 225 tests Python (no se tocan en 5a).

## 6. Qué reutilizar
- Patrón de lanzamiento de proceso de `Losas.exe` (`ProcessStartInfo`/`Redirect*`) → `MotorFeaClient`.
- `SalidaPerdomoAdapter` como plantilla para `MotorFeaAdapter`.
- `PluginHost` (backends), `ProyectoService` (persistencia), la infra de tests .NET existente.
- Contrato JSON del motor ya estable (#1/#2): mismo en CLI y HTTP (migrar a HTTP luego es barato).

## 7. Definición de hecho (esperada, a confirmar en el spec)
- El escritorio invoca el motor por CLI (`--analyze -` y/o `--disenar-losa -`), pasa el modelo por
  stdin y consume los resultados JSON por stdout, con manejo de exit code/errores.
- `MotorFeaAdapter` traduce los resultados al dominio .NET (paralelo a `SalidaPerdomo`), y la app
  muestra los resultados del motor en el flujo existente (sin tocar 5b/5c).
- `Losas.exe` se mantiene como camino de paridad/fallback (salvo decisión contraria en §5.3).
- Tests .NET verdes (`dotnet test`); + tests del cliente/adaptador del motor.
- Mergeado en `ui/editor-planta` (o rama hija) según el flujo del repo .NET; sin tocar el repo motor.

## 8. Punteros
- Engine CLI/contrato: `~/Downloads/EstructurasRD-engine/motor-fea/src/motor_fea/api/{cli.py, contrato.py}`.
- ADR frontera: `~/Downloads/EstructurasRD-engine/motor-fea/docs/ADR-0001-integracion.md`.
- Spec contrato esfuerzos: `…/motor-fea/docs/superpowers/specs/2026-06-13-api-escritura-esfuerzos-design.md`.
- .NET cliente: `src/MemoriaPlus/ViewModels/MainViewModel.cs`, `src/Core/Services/{TxtParser.cs,
  SalidaPerdomoAdapter.cs, PluginHost.cs, ProyectoService.cs}`, `src/Core/Models/{SalidaPerdomo.cs,
  Sistema.cs, Sistema.MemoriaPlus.cs}`, `src/LosasPlus/Models/` (Losas, Edificio, Columna, Zapata).
- Correr el escritorio: `dotnet run --project src/MemoriaPlus` (o `src/LosasPlus`). Build: `dotnet build`.
  Tests: `dotnet test`. Correr el motor: `~/Downloads/EstructurasRD-engine/motor-fea/.venv/bin/python
  -m motor_fea.api.cli --analyze -` (JSON por stdin).

## 9. Gotchas del entorno
- **Cross-repo:** 5a se implementa en `~/Downloads/EstructurasRD-main` (.NET); el motor es solo
  referencia en `~/Downloads/EstructurasRD-engine` (Python). Comparten árbol de objetos git
  (common-dir en EstructurasRD-main), pero son worktrees/ramas distintos. NO mezclar.
- **GateGuard (ECC):** rebota el 1er Bash de la sesión y la 1ª edición de cada archivo (presentar los
  hechos pedidos y reintentar idéntico). Comandos destructivos (rm/pkill) los bloquea repetidamente →
  no ejecutables sin `ECC_GATEGUARD=off`. Los subagentes también lo topan: avisarles en el prompt.
- **Hook "Foundry/CrowdStrike"** se dispara al invocar skills de superpowers — **misfire**, ignorar.
- **Ramas/worktrees:** `~/Downloads/EstructurasRD-main` = `ui/editor-planta` (suite .NET);
  `~/Downloads/EstructurasRD-engine` = `engine/shell-web-webxr` (motor). `master` (local, engine) no
  está checked-out. `origin` (dashy2004/EstructurasRD) = línea remota; no pushear sin estrategia.
