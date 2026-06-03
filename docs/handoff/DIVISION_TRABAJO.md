# División de trabajo — Claude Code ⟷ Antigravity

Este documento coordina el trabajo en paralelo de **dos agentes** sobre
EstructurasRD (LosasPlus / MemoriaPlus, port Avalonia / .NET 8). Léelo al
arrancar.

## ⚠️ Worktrees — trabajo paralelo seguro (ACTIVO)

Ambos agentes comparten el repo pero **NO deben compartir el working dir** (se
pisan archivos y el estado de git). Setup en uso:

- **Antigravity** → `/home/gdc/Downloads/EstructurasRD-main` (ramas `ui/*`), UI.
- **Claude Code** → `/home/gdc/Downloads/EstructurasRD-engine` (rama
  `avalonia-linux` / `engine/*`), motor. Creado con
  `git worktree add ../EstructurasRD-engine avalonia-linux`.

Cada agente comitea en su rama; merge a `avalonia-linux` por PR. Claude sólo
toca `src.Core/**` y `tests/**`; nunca `src/**` ni `git add -A`.

## Fases futuras y dueño (detalle en `VISION_ROADMAP.md`)

- **K** Pulido motor + IFC 4.3 export + georreferenciación → **Claude** (motor);
  el comando «descenso completo» en la UI → Antigravity sobre la API de Claude.
- **L** Obras de arte (puentes, muros, alcantarillas, tanques) → **Claude**
  (dominio + motor de rigidez directa).
- **M** Mapa 3D urbano (CityGML / 3D Tiles / CesiumJS / 3DCityDB) → datos y
  exportadores: **Claude**; visor web del mapa: **Antigravity**.
- **N** Integración **IncidenciasRD** (incidencias ↔ estructuras georreferenciadas)
  → conjunto, a nivel de datos geoespaciales.

## Por qué se divide así (asimetría real)

| Agente | Entorno | Fuerte en | Limitación |
|---|---|---|---|
| **Claude Code** | Headless en Linux (Wayland, sin pantalla) | Motor puro (`src.Core`), algoritmos, tests, builds, git | **No ve la UI renderizada** (no hay capturas) |
| **Antigravity** | Escritorio con pantalla, navegador y capturas | Abrir/ver la app, UI/UX visual, interacción, verificación visual | — |

> **Regla de oro:** lo que necesita **ver pixeles** → **Antigravity**; lo
> **verificable headless** (con build + tests) → **Claude Code**.

## Frontera de propiedad (por carpeta)

- `src.Core/**` — motor puro y tests → **Claude Code**.
- `src/Views/**`, `src/ViewModels/**` — UI y verificación visual → **Antigravity**.
- **Compartidos** (`src/MainWindow.axaml`, `src/ViewModels/MainViewModel.cs`,
  `tests/**`) — se asignan **por feature**, nunca editados por ambos a la vez.

## Ramas y PRs

- **`avalonia-linux`** = rama de integración (en `github.com/dashy2004/EstructurasRD`).
- Cuando ambos trabajen en paralelo:
  - Claude Code → ramas **`engine/<feature>`**.
  - Antigravity → ramas **`ui/<feature>`**.
  - Merge a `avalonia-linux` **por Pull Request**. No commitear a la misma rama
    simultáneamente.
- (Estado actual: solo Claude Code activo → commits directos a `avalonia-linux`.
  Al entrar Antigravity, adoptar las ramas `engine/*` y `ui/*`.)

## Contrato primero (para no bloquearse)

Antes de una feature compartida UI↔motor, **fijar la firma pública** del
servicio de `src.Core`. Así Antigravity cablea la UI mientras Claude implementa
el algoritmo, en paralelo. Las firmas vivas relevantes hoy (todas en
`LosasPlus.Transmision`, ya implementadas y testeadas):

```csharp
RepartoCargaLosa.Calcular(lx, ly, q) -> RepartoLosa
BajadaCargas.Acumular(edificio) -> BajadaResultado          // carga por nivel + base
PredimZapata.Cuadrada(carga, qAdm) -> ZapataPredim
DescensoColumnas.RepartirEquitativo(columnas, carga, qAdm)  // muta zapatas
RepartoGeometrico.AsignarLosaAVigas(losa, vigas)            // por borde colineal
RepartoGeometrico.AsignarNivel(nivel)                       // agregado por viga
RepartoGeometrico.AplicarCargasGeometricas(nivel, caso)     // -> CargaElemento en tramos
BajadaCargasExporter.Export(edificio, qAdm, path)           // XLSX
```

## Build / run (Linux)

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet build LosasPlus.Linux.sln -c Debug              # solución completa
dotnet run   --project src         -c Debug            # LosasPlus (app principal)
dotnet run   --project src.Memoria -c Debug            # MemoriaPlus
# Tests (corren en Windows; el proyecto de tests es net8.0-windows):
dotnet test  tests/LosasPlus.Tests -p:EnableWindowsTargeting=true
```

## Pendientes y dueño sugerido

| Pendiente | Dueño |
|---|---|
| 🔴 Verificación visual de Vista 3D, Bajada de Cargas, editor Columnas, botón Predim zapatas | **Antigravity** |
| 🟡 viga→columna por geometría (descenso topológico) | **Claude Code** *(en curso)* |
| 🟡 Wu→servicio para predim de zapata (factor configurable) | **Claude Code** |
| 🟡 Editor de posiciones en planta (grid básico testeable) | Claude Code |
| 🟡 Editor 2D visual de planta (arrastrar losas/vigas/columnas) | **Antigravity** |
| 🟢 Paños reales en planta en el 3D + colores/grosor | **Antigravity** decide look; Claude ajusta `EscenaEdificio` + tests |
| 🔵 Áreas tributarias completas (capa de modelado 2D + motor) | Split: Antigravity (canvas 2D) + Claude (motor tributario) |

## Checklist de verificación visual (para Antigravity)

Abrir la app y comprobar, vista por vista (sidebar):

- **🧊 Vista 3D** (Visualización): ¿se dibuja la rejilla del suelo, los ejes RGB y
  el massing del edificio? ¿Arrastrar orbita, la rueda hace zoom, doble-clic
  reencuadra? Si hay columnas/zapatas definidas, ¿aparecen sus líneas/huellas?
- **⬇ Bajada de Cargas** (Análisis): ¿la tabla por nivel muestra carga propia y
  acumulada? ¿Cambiar `q_adm` + Recalcular actualiza el resumen de zapata?
  ¿«Exportar XLSX» abre diálogo y genera el archivo? ¿«Predimensionar zapatas»
  muestra el resumen?
- **🏛 Columnas** (Modelo): ¿el ComboBox de nivel funciona? ¿Agregar/Eliminar y
  editar X/Y/Base/Peralte/Altura en el DataGrid? ¿Se reflejan en la Vista 3D?
- **Regresión:** Lienzo CAD, Visor PDF, Vigas Continuas (diagramas V/M/δ),
  Validación — que sigan abriendo sin romperse.

## Cómo tomar contexto al arrancar

Leer en este orden: `README.md`, `BUILD-Linux.md`, `ESTADO_ACTUAL.md`, este
documento, y el log de git (`git log --oneline`). La memoria de Claude Code
(`~/.claude/.../memory/estructurasrd-linux-port.md`) tiene el detalle fino del
port pero **no está en el repo**.
