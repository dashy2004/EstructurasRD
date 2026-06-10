> ⚠️ Estado real autogenerado → ver [/STATE.md](STATE.md) (este documento puede estar desactualizado).

# Estado actual — Suite LosasPlus / MemoriaPlus

> ⚠️ **Banner de corrección (2026-06-02):** este documento describe el snapshot
> WPF v0.7.0 y quedó desactualizado. La realidad verificada hoy es:
> la suite ya es **Avalonia multiplataforma** (no WPF), corre en **Linux** como
> plataforma primaria, expone **18 modos** de UI en 7 categorías, el **Lienzo
> CAD** y la **Vista 3D** están **implementados** (no son stubs), y la batería
> de tests es de **753/753 verde** corriendo en Linux (`net8.0`, sin `UseWPF`).
> Único placeholder real de UI: la pestaña **Aceros**. Backlog vivo de
> correcciones y del motor FEA nativo: ver **`PLAN_MAESTRO.md`**. Para build/run
> multiplataforma: **`BUILD-Linux.md`**. Las secciones de abajo se conservan
> como histórico WPF.

> Documento de snapshot del proyecto. Última actualización: **2026-05-18**.
> Versión: **v0.7.0** · Rama: `main` · Commit: `bef7166` · Working tree limpio.
> Build: **0 warnings, 0 errors** · Tests: **501/501 verde** (WPF; hoy 753 en Avalonia).

---

## 1. Qué es

Suite de diseño estructural en **.NET 8 / WPF** para ingeniería civil
dominicana. Dos aplicaciones de escritorio que comparten un núcleo común:

| App | Rol |
|---|---|
| **LosasPlus** | Calculadora / editor moderno sobre el motor `Losas.exe` del Ing. F. Perdomo (formato `.DL`, método Pieper-Martens). |
| **MemoriaPlus** | Generador de memorias de cálculo `.docx`. Standalone. |

Conforme a R-001, R-024 y ACI 318. Repositorio:
`https://github.com/dashy2004/LosasPlus`.

---

## 2. Arquitectura

Solución de **4 proyectos** sin dependencias circulares:

```
src.Core/            librería net8.0 — modelo, motor de cálculo, parsers.
  └─ (sin WPF, reusable)
src.UI.Shared/       net8.0-windows — Views/VMs/converters compartidos.
  └─ depende de: src.Core
src/                 LosasPlus.App (WPF) — depende de: src.Core, src.UI.Shared
src.Memoria/         MemoriaPlus.App (WPF) — depende de: src.Core, src.UI.Shared
tests/LosasPlus.Tests/   xUnit — 27 archivos, 501 tests.
```

**Comunicación entre las apps:**
- LosasPlus lanza `MemoriaPlus.exe` pasando un `.lpx.json` como argumento
  (handshake `GenerarMemoria` ↔ `TryAbrirProyectoDesdeArgs`). Verificado
  completo de ambos lados.
- LosasPlus ↔ motor `Losas.exe`: escribe/lee `.DL`, parsea el `.TXT` de
  salida. El motor es externo y no se redistribuye.

---

## 3. Estado de build y pruebas

| Métrica | Valor |
|---|---|
| Build (Debug + Release) | 0 warnings, 0 errors |
| Tests unitarios | 501 / 501 verde |
| Archivos de test | 27 |
| Binarios self-contained | `LosasPlus.exe` ~191 MB · `MemoriaPlus.exe` ~191 MB |
| Tag publicado | `v0.7.0` (previo: `v0.6.0`) |

**Rutas de los ejecutables (build de desarrollo):**
```
LosasPlus:   src/bin/Debug/net8.0-windows/LosasPlus.exe
MemoriaPlus: src.Memoria/bin/Debug/net8.0-windows/MemoriaPlus.exe
```

---

## 4. Capacidades implementadas (v0.7)

> **Novedades de v0.7** sobre v0.6: catálogo estricto de 23 tipos de losa
> con validación fail-fast (§4.10), y la Fase 1.A del importador DXF —
> capa de dominio del editor CAD (§4.11).

### 4.1 Cálculo estructural (`src.Core`)
- ✅ Condición 1D / 2D automática según relación Ly/Lx.
- ✅ Espesor `h_calc` con fórmulas separadas 1D (ACI 9.5.2.1) y 2D
  (ACI 9.5.3.2).
- ✅ **αfm completo (ACI 318 §9.5.3.3)**: `Iviga`, `Ilosa`, αx/αy/αm y
  estado OK/CHK. Portado del Excel "ESPESOR EQUIVALENTE" del ingeniero.
- ✅ Espesor equivalente vigueta+bloque (T-section) con `VigaTipo` y
  `Bovedilla 1D/2D` configurables.
- ✅ Cómputos métricos por losa: cantidad de bovedillas, V_bovedilla,
  V_total, V_concreto.
- ✅ Refuerzo distribuido por diámetro ASTM A615 (#3 a #8) con áreas
  nominales y cálculo de As total. **(UI en pestaña "Aceros" pendiente.)**
- ✅ Cargas `q_mamp`, `q_map`, `q_d`, `q_l`, `q_u` factorizadas.

### 4.2 Editor LosasPlus
- ✅ DataGrid de losas con cálculo en vivo y validación in-place.
- ✅ Catálogo estricto de **23 tipos** Pieper-Martens (10, 13, 14, 21–24,
  31–34, 40, 43, 44, 51–54, 60, 63, 64, 71, 72). Selector visual modal.
- ✅ Multi-select + panel bulk-apply (Lx/Ly/H/Carga/Tipo en lote).
- ✅ Undo/Redo con snapshots JSON (Ctrl+Z / Ctrl+Y).
- ✅ Bordes adicionales X/Y con modo conexión por click en IDs.
- ✅ Esquema 2D en vivo con textos de alto contraste.
- ✅ Sección "Proyecto activo" en el Explorador (renombrar proyecto y
  sistemas, guardar).

### 4.3 Persistencia y workflow
- ✅ Formato `.lpx.json` (Ctrl+N/O/S/Shift+S).
- ✅ Auto-backup timestamped sin pisar el original (prune a 20 copias).
- ✅ Proyectos recientes con filtro.
- ✅ Búsqueda global Ctrl+F (proyectos / niveles / losas).
- ✅ Atajos de teclado reasignables.

### 4.4 Doctor de archivos `.DL`
- ✅ Detecta 8 patrones de corrupción: JSON disfrazado de .DL, BOM,
  decimal con coma, IDs duplicados, geometría inválida, recubrimiento ≥
  espesor, bordes huérfanos, **tipo de losa no permitido**.
- ✅ Auto-repara los corregibles y exporta a `{stem}.reparado.DL` sin
  pisar el original.
- ✅ Modal con shading por severidad + acción contextual.

### 4.5 Validación normativa
- ✅ Engine con 5 reglas: tipo de losa válido, espesor mínimo R-001,
  carga viva R-001, espesor vs cálculo ACI, aspecto Pieper-Martens.
- ✅ Chip indicador en top bar + panel lateral con detalle y auto-fix.

### 4.6 Salida `.TXT` del motor
- ✅ Dos vistas: Texto con highlighting + Tabla editable estilo Excel.
- ✅ Exporta cambios a `{stem}.modificado.txt` sin pisar el original.

### 4.7 Configuración → Apariencia
- ✅ Tema Claro / Oscuro / Precision (aplica en vivo).
- ✅ Tipografía monoespaciada seleccionable.
- ✅ Color de acento: 8 swatches + hex personalizado.
- ✅ Presets nombrados en `%APPDATA%/LosasPlus/themes.json`.

### 4.8 Exportación de resultados
- ✅ CSV con 31 columnas por losa (input + motor + CalculoEngine).
- ✅ XLSX con hojas Resumen / Losas / Verificación ACI / Apoyos /
  Espejo .TXT / Esquema / Combinaciones.

### 4.9 MemoriaPlus
- ✅ Generación `.docx` con sustitución robusta de placeholders.
- ✅ Plurinivel (bloque NIVEL clonado por sistema).
- ✅ Tablas embebidas: losas, momentos, armaduras X/Y, apoyos.

### 4.10 Catálogo estricto de tipos de losa (nuevo en v0.7)
- ✅ Catálogo canónico de **23 tipos** Pieper-Martens (se retiraron los
  espurios 11, 41, 50).
- ✅ Validación fail-fast en todos los puntos de entrada: parser `.DL`
  (con remapeo de aliases 11→10, 50→60), `IDataErrorInfo` (marca roja
  en la celda Tipo), regla `TipoLosaValidoRule`, y el Doctor `.DL`
  (issue `DL106-TIPO-INVALIDO`).

### 4.11 Importador DXF — Fase 1.A del editor CAD (nuevo en v0.7)
- ✅ Capa de **dominio** del importador de planos (`src.Core`, sin WPF):
  modelos puros `PlanoReferencia` / `EntidadCad` (`LineaCad`,
  `PolilineaCad`, `TextoCad`, `ArcoCad`) + `PuntoCad`.
- ✅ `IPlanoImporter` + `DxfImportService` (netDxf 2023.11.10): lee
  LINE/POLYLINE/TEXT/MTEXT/CIRCLE/ARC, normaliza a metros, resiliente
  ante archivos vacíos/corruptos.
- ⏳ Pendiente: Fase 1.B (host visual WPF), Fase 2 (mapeo polígono→Losa),
  Fase 3 (editor de dibujo). Ver `PLAN_CAD_V1.md`.

---

## 5. Bug crítico resuelto en esta versión

**Tipos de losa sin validación** (corregido en commits `068a613`–`82e55fa`):
- El catálogo tenía 26 entradas, 3 espurias (11, 41, 50). Reducido a los
  **23 canónicos**.
- Se agregó validación fail-fast en todos los puntos de entrada: parser
  `.DL` (con remapeo de aliases 11→10, 50→60), regla de validación
  normativa, Doctor `.DL`, e `IDataErrorInfo` (marca roja en la celda).
- Un `.DL` con tipo inválido carga igual (no se pierde trabajo) pero la
  losa queda marcada en rojo y aparece en el panel de Validación.

---

## 6. Pendiente / Roadmap

### Pendiente (v0.8+)
- ⏳ **Editor CAD Fase 1.B**: host visual WPF (`CadCanvasHost` con
  `DrawingVisual`) para mostrar el plano DXF importado. La capa de
  dominio (Fase 1.A) ya está lista. Ver `PLAN_CAD_V1.md`.
- ⏳ **Editor CAD Fases 2 y 3**: mapeo de polígonos DXF → `Losa`, y
  editor de dibujo manual.
- ⏳ **UI de Aceros**: la pestaña sidebar "Aceros" hoy muestra un
  placeholder "Próximamente". El modelo Core está listo; falta la UI
  para As requerido vs provisto, separación de barras de empalme, y
  reportes por franja. Debe aparecer **después** de importar el `.TXT`.
- ⏳ Panel αfm visual en el Editor (chips OK/CHK + αx/αy/αm).
- ⏳ Editores globales de `VigaPrincipal` y `Bovedilla 1D/2D` en el
  panel lateral del Editor.

### Mejoras de arquitectura sugeridas (no bloqueantes)
- ⚠️ Evento `AtajosGuardados` en LosasPlus: emitido pero sin suscriptor
  — los atajos editados persisten pero no se aplican en vivo (MemoriaPlus
  sí lo hace). Conectar con el mismo patrón que `AparienciaCambiada`.
- ⚠️ `JsonSerializerOptions` duplicado en 7 clases de
  `src.Core/Persistence/` — extraer a un helper común.

### Visión v1.0+
- Editor visual de losas tipo CAD.
- Importer DXF/DWG.
- Installer MSI firmado (hoy los `.exe` no están firmados → SmartScreen).
- Suite ampliada: VigasPlus, ColumnasPlus, FundacionesPlus, etc.

---

## 7. Limitaciones conocidas

- Los `.exe` no están firmados digitalmente → Windows SmartScreen muestra
  el aviso "Windows protegió tu PC" en el primer arranque.
- La **densidad** de tablas (Configuración → Apariencia) persiste pero no
  aplica en vivo — requiere reiniciar.
- `LosasPlus.exe` requiere que el usuario tenga su copia legítima de
  `Losas.exe` (Ing. F. Perdomo). `MemoriaPlus.exe` es standalone.
- Tipos de losa **71 y 72 (voladizos)**: el motor `Losas.exe` anula
  intencionalmente una dimensión (Lx o Ly = 0) en el `.TXT` de salida —
  es comportamiento correcto del motor, no un error.

---

## 8. Cómo probar

### Usuario final
1. Descargar `LosasPlus.exe` de la Release v0.7.0 en GitHub
   (`https://github.com/dashy2004/EstructurasRD/releases`).
2. Ejecutar (SmartScreen → "Más información" → "Ejecutar de todas formas").
3. `File → Engine → Examinar Losas.exe…` y apuntar a la copia local.
4. `File → Abrir .DL legacy…` con un archivo `.DL` propio.

### Desarrollador
```bash
git clone https://github.com/dashy2004/EstructurasRD.git
cd EstructurasRD
dotnet build LosasPlus.sln -c Debug      # 0 warnings esperado
dotnet test  LosasPlus.sln               # 501/501 verde esperado
dotnet run --project src/LosasPlus.csproj
```

---

## 9. Historial reciente (sesión actual)

| Commit | Cambio |
|---|---|
| `bef7166` | Fase 1.A CAD: importador DXF en el dominio (src.Core) + tests |
| `82e55fa` | Tests del catálogo de 23 tipos + validación + remapeo |
| `6777495` | Borrar SVGs huérfanos + limpiar converters muertos |
| `fc1d0d6` | Validación fail-fast de tipos de losa |
| `068a613` | Catálogo canónico de 23 tipos |
| `c4f34c9` | README v0.6 + carta para Ing. Perdomo |
| `937353f` | Apariencia aplica en vivo (tema / tipografía / color) |
| `50c997d` | Aceros movido a pestaña "Próximamente" |
| `e607153` | SelectorTipoLosaWindow usa SVGs del usuario |
| `41941f0` | Doctor de archivos .DL |
| `988987c` | UI aceros comerciales (luego reubicada) |
| `6448e5e` | Abrir .DL navega a Editor + Proyecto activo en Explorador |
| `48b628b` | Restaurar iconos SVG del usuario |
| `afc419c` | Salida .TXT con dos pestañas Texto/Tabla |
| `eef264d` | Apariencia: paleta + presets nombrados |
| `4b31042` | Configuración: ocultar "Datos del ingeniero" en calculadora |
| `0b63583` | Exportar resultados del motor (CSV / XLSX completos) |
| `9df7d41` | Bug pack: renombrar Sistema + iconos consistentes |
| `418fb12` | ESPESOR EQUIVALENTE: αfm + bovedilla + acero en Core |