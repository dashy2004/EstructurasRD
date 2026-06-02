# Memoria de trabajo — Claude Code (sesión port Avalonia, 2026-06-01)

Registro de todo lo realizado por **Claude Code** en esta sesión sobre
EstructurasRD (LosasPlus / MemoriaPlus). Rama: **`avalonia-linux`** en
`github.com/dashy2004/EstructurasRD`. Convención de commits: «Fase X.N: …».

## 0. Contexto

La suite (motor `src.Core` + apps WPF) venía portada de **WPF → Avalonia 11 /
.NET 8** con **Linux como plataforma primaria** (Hito 1: Fases A–G, paridad del
snapshot 2026-05-22). Esta sesión cubrió: **converger Fase H (Hito 2)**, **Fase I
(3D sin SharpDX)** y **Fase J (transmisión de cargas)**, más el setup de GitHub
y la coordinación con Antigravity.

## 1. Metodología de verificación

Como Claude Code corre **headless** (Linux/Wayland, sin pantalla), cada
incremento se verificó así:
- **Build 0/0** de `LosasPlus.Linux.sln` (y `--no-incremental` cuando hay XAML,
  que de lo contrario oculta errores AVLN2000).
- **Verificación funcional en Linux** con una mini-consola throwaway
  (`/tmp/safcheck`) que referencia `src.Core` y ejecuta asserts reales (≈150
  checks en total a lo largo de la sesión).
- **Tests xUnit** que compilan (el proyecto de tests es `net8.0-windows`, así
  que se ejecutan en Windows; aquí se valida que compilen + la lógica vía la
  mini-consola).
- Para UI: build 0/0 + **app viva N s sin excepciones** (no hay capturas
  headless → el render se delega a Antigravity).

## 2. Fase H — convergencia v0.8.1 (→ Hito 2)

Tres ítems ya estaban (sidebar 7 cat., PdfViewerControl, diagramas M/V/δ del
`VigaEditorView`). El único pendiente:

- **H5 (`f0ad138`)** — `SafExporter`: export del modelo de vigas al **SAF**
  (Structural Analysis Format, IDEA StatiCa) — libro Excel con una hoja por
  objeto (materiales/secciones normalizados, nodos, miembros, apoyos, casos,
  combinaciones, cargas). Menú Export + tests. 21/21 checks.

## 3. Fase I — viewport 3D **sin SharpDX**

Decisión clave: el visor 3D original usaba SharpDX/Direct3D (solo-Windows). En
vez de Veldrid/OpenTK se eligió **proyección 3D→2D por software + DrawingContext
de Avalonia** (como el lienzo CAD): multiplataforma, sin toolchain GL, y la
matemática es PURA y testeable headless.

- **I.1 (`9fde74f`)** `CamaraOrbital` (System.Numerics, LookAt + perspectiva,
  Orbitar/Zoom/Encuadrar) + `PrimitivasEscena` (Ejes/Rejilla/Caja). 14/14.
- **I.2 (`87d67d1`)** `EscenaEdificio.Construir(Edificio)` → massing por niveles
  (cuadrado de área equivalente a su `Cota`). 10/10.
- **I.3 (`2c24d8e`)** `Proyector3D` (clip→NDC→píxel, descarte tras cámara,
  recorte de segmento contra el plano de cámara). 6/6.
- **I.4 (`d223591`)** `Vista3DControl` (Control + Render(DrawingContext)),
  ratón orbitar/zoom/reencuadrar, en el nav «🧊 Vista 3D».
- **I.5 (`dfb35ae`)** columnas dibujadas en la escena (segmentos verticales en
  su posición de planta). **I.6 (`b49ab38`)** zapatas dibujadas (huella en base).

## 4. Fase J — transmisión de cargas (losa→viga→columna→zapata)

Todo en `src.Core/Transmision/`, puro y testeado.

- **J.1 (`104aef5`)** `RepartoCargaLosa` — reparto losa→borde por áreas
  tributarias (triángulo/trapecio), conserva q·Lx·Ly.
- **J.2 (`4edf075`)** `BajadaCargas` — acumulación por niveles hasta la base.
- **J.3 (`f7792c6`)** `PredimZapata.Cuadrada` — A = P/q_adm.
- **J.4 (`fe8ffe4`)** vista UI «⬇ Bajada de Cargas» (VM testeable + DataGrid +
  q_adm editable + predim).
- **J.5 (`38c829e`)** entidades `Columna`/`Zapata`. **J.6 (`612341d`)**
  `Nivel.Columnas` + serialización `.lpx.json` aditiva (round-trip).
- **J.7 (`3c60c59`)** `DescensoColumnas.RepartirEquitativo` (1ª aprox).
- **J.8 (`0822584`)** `BajadaCargasExporter` → XLSX. **J.9 (`8b361e7`)** botón
  export en la UI.
- **J.10 (`120ee80`)** editor de Columnas (UI). **J.11 (`72a9286`)** botón
  «Predimensionar zapatas». **J.12 (`ee131d5`)** selector de nivel.
- **Geometría en planta (desbloquea el descenso topológico real):**
  - **J.13 (`d910259`)** `Losa.CoordenadaX/Y`. **J.14 (`e2a6370`)**
    `Viga.OrigenX/Y` + `AnguloGrados` + extremos computados. (Ambos aditivos,
    round-trip verificado.)
  - **J.15 (`a82a464`)** `RepartoGeometrico.AsignarLosaAVigas` — asigna cada
    borde a la viga colineal+solapada.
  - **J.16 (`5ae1fce`)** `AsignarNivel` — agrega por viga (viga compartida suma).
  - **J.17 (`0c8bbb2`)** `AplicarCargasGeometricas` — carga→`CargaElemento` en
    tramos. **Lazo losa→viga→análisis cerrado** (reacciones = carga total, end-to-end
    con `VigaContinuaEngine`).
  - **J.18 (`5781fe7`)** `AsignarVigasAColumnas` — viga→columna por proximidad de
    extremos, acumula axial, conserva carga.
  - **J.19 (`9e76c45`)** `DescensoColumnas.PredimensionarGeometrico` — zapata por
    axial REAL.
  - **J.20 (`cf39850`)** `PredimZapata.CuadradaDesdeUltima` — conversión Wu→servicio.

**Resultado:** descenso **topológico completo por geometría**
losa→viga→análisis→columna→zapata, todo puro y verificado.

## 5. Infraestructura / colaboración

- **README (`66f576a`)** actualizado a Avalonia/multiplataforma + Fases I/J.
- **GitHub:** repo renombrado `LosasPlus`→**`EstructurasRD`**; rama de integración
  `avalonia-linux` vía SSH.
- **`docs/DIVISION_TRABAJO.md` (J.18)** — reparto con **Antigravity**: Claude =
  `src.Core` (motor); Antigravity = `src/Views`/`ViewModels` (UI + verificación
  visual). Frontera por carpeta, ramas `engine/*` / `ui/*`, contrato-primero.

## 6. ⚠️ Coordinación con Antigravity

Antigravity trabaja en el **mismo directorio** (`ui/verificacion-visual-compat`,
con `MainWindow.axaml.cs`/`MainViewModel.cs` modificados, montando un harness
`RunVisualVerificationAsync`/`EXPORT_SCREENSHOTS`). Yo **nunca** toqué `src/**`
ni usé `git add -A` (solo archivos específicos de `src.Core`/`tests`), así que su
WIP quedó intacto. **Para paralelo seguro: un `git worktree` por agente.**

## 7. Pendientes (handoff)

- **Claude (motor):** integrar el descenso geométrico (J.15–J.20) a la UI
  (botón/acción que lo dispare); editor de posiciones en planta (grid testeable);
  reparto exacto por reacciones del motor (hoy viga→columna es mitad/mitad).
- **Antigravity (UI):** verificación visual de Vista 3D / Bajada de Cargas /
  Columnas; editor 2D de planta (arrastrar elementos); paños reales en el 3D.

## 8. Lecciones técnicas registradas

- `Control.OnDoubleTapped` NO es virtual en Avalonia → `e.ClickCount==2`.
- Proyecto de tests `net8.0-windows` → no corre headless; usar mini-consola
  contra `src.Core` para verificar en Linux.
- Serialización aditiva: colección get-only + `PreferredObjectCreationHandling
  = Populate` → proyectos viejos cargan vacío sin migración; computadas con
  `[JsonIgnore]`.
- `Losa.Carga` es **Wu** (última); el peso propio ya está en `Qd` vía
  `CargasGlobales` — no re-derivar.
- `Proyecto.AsegurarEstructura()` materializa `Edificios[0].Niveles[0]`
  (idempotente) — útil en tests/headless.
