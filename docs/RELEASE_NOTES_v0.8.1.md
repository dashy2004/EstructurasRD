# Notas de Lanzamiento — EstructurasRD v0.8.1

### Liga D (UX) + Liga E (PDF + Config + Diagramas 3D) + Liga F1 (Bug fixes)

Release acumulativo sobre **v0.8.0** que consolida tres bloques de
iteración (Liga D, Liga E, Liga F1) más dos arreglos puntuales: el
hotfix del crash de inicio del `PdfViewerControl` y la conversión del
panel **Elemento Activo** en flotante movible + minimizable.

> Build: **0 warnings / 0 errors**.  Suite: **853 / 853 tests verdes**.
> Rama de release: `feat/saf-export-interop1`.

---

## 1. Liga D — UX Optimization

### D1 · Sidebar categorizada
Los 19 modos de navegación se agrupan ahora en **7 categorías
horizontales con encabezados de bloque** (Proyecto, Losas Legacy,
Modelo Estructural, Análisis, Visualización, Normativa, Sistema). El
mismo `GroupName="SidebarNav"` mantiene la mutua-exclusión de los
RadioButtons; los separadores se renderizan con `Border` de 1 px y los
encabezados con `FontWeight=SemiBold` + `Foreground={FgMuted}`. Sin
romper navegación por teclado.

### D2 · Fix overlap "Elemento Activo" ↔ Toolbar Planta
El panel "Elemento Activo" pasa de `HorizontalAlignment=Right
Margin=0,12,24,0` → `HorizontalAlignment=Left Margin=24,12,0,0`. Las
toolbars flotantes de Planta Estructural y Vista 3D conservan su
posición top-right; el panel queda en top-left sin colisión geométrica.

### D3 · "Acerca de" v0.8.0 + metadata MIVHED
Rediseño completo del modo "Acerca de" con branding **EstructurasRD
v0.8.1**, tabla de reglamentos soportados (ACI 318-19 ✅, R-001 ✅,
MIVHED-T1-V1 🧪 *en estudio*, MIVHED-V1-T2 🧪) y tres botones de acción:

- **🚀 Ver release v0.8.1 en GitHub** (`Process.Start UseShellExecute`).
- **📄 Abrir MIVHED Tomo 1 Vol 1** (resolución de ruta vía
  `Environment.GetFolderPath(UserProfile)`).
- **📄 Abrir MIVHED Vol 1 Tomo 2**.

Las dos entradas MIVHED se añaden además al `Reglamento.json` (v3) en
cabeza del array, con `pdfPath` apuntando al OneDrive del usuario.

---

## 2. Liga E — Quick wins

### E1 · PDF Viewer embebido del Reglamento
Nuevo `UserControl PdfViewerControl` con barra superior (◀ anterior · 
TextBox página · siguiente ▶ · Slider zoom 50%-200%) y `Image`
embedded. Usa `Docnet.Core` reutilizando el pipeline existente del
`PdfImportador`:

- `PdfImportador.ContarPaginasAsync(path)`
- `PdfImportador.RasterizarPaginaAsync(path, indicePag, anchoObjetivoPx)`

El `ReglamentoView` lo instancia inline cuando la entrada
seleccionada tiene `pdfPath` no vacío. El botón "📄 Abrir PDF externo"
se preserva como alternativa para abrir en Adobe/Edge nativo.

### E2 · Configuración extendida (Cálculos & datos)
Cuarto tab nuevo en el modo Configuración:

- **Decimales de formato** (Slider 1-4).
- **Cultura** (es-DO / es-ES / en-US).
- **Sistema de unidades** (SI ✅, Imperial deshabilitado).
- **Auto-backup** (Off/5/10/15/30 min).

Persistencia vía nuevo `PreferenciasService` → 
`%AppData%\LosasPlus\preferencias.json`. Botón "Restaurar valores por
defecto" disponible.

### E3 · Indicadores de conexión nodal en Planta
Nuevo `_layerNodos` en `PlantaEstructuralCanvas` que pinta círculos
fijos en píxeles (radio 8 px) sobre cada nodo del
`GrafoProyectadoBuilder.Construir(proyecto)`:

- **🟢 Verde** si `ElementosIncidentes.Count >= 2` (nodo conectado a
  viga + columna mínimo).
- **🟠 Naranja** si `Count == 1` (nodo flotante).

CheckBox en la toolbar flotante "🔵 Mostrar conexiones nodal" controla
la visibilidad — útil para detectar a simple vista vigas sin columna o
columnas huérfanas.

### E4 · Diagramas M/V/Δ 3D reales sobre vigas
La parábola hardcoded `M(t) = 4·t·(1-t)` en `SyncEscenaService.
ConstruirCintaMomento` se reemplaza por una interpolación sobre el
**envolvente real** de `VigaContinuaEngine.Resolver(viga,
proyecto.Combinaciones).Envolvente.Puntos`. El parámetro
`perfilNormalizado: IReadOnlyList<float>?` se pasa al método; si es
null/vacío hace fallback a la parábola por compatibilidad.

ComboBox **"📊 Diagrama:"** en la toolbar de Vista 3D con 4 opciones
(Ninguno, Momento, Cortante, Deflexión) bindeado a `ModoDiagrama3D`.

---

## 3. Hotfix — Crash de inicio del PdfViewerControl

Causa raíz: el `Slider` de zoom con `Value=1.0` default disparaba
`ValueChanged` **durante `InitializeComponent`**, antes de que las
referencias `x:Name="ImgPagina"` e `x:Name="LblZoom"` quedaran
resueltas. Resultado: `NullReferenceException` al primer ciclo de
binding.

Fix: guard defensivo en `OnZoomChanged`:

```csharp
if (ImgPagina is null || LblZoom is null) return;
```

Commit `e00e528`.

---

## 4. Panel "Elemento Activo" movible + minimizable

Refactor del UserControl:

- **Header arrastrable** (cursor `SizeAll`) — `MouseDown` captura el
  mouse, `MouseMove` actualiza `Margin` del UserControl.
- **Doble-click en header** o botón **[—]/[□]** alterna minimizar
  (colapsa el Body, queda sólo la barra de tipo + Id ~30 px).

Estado local del drag: `_isDragging`, `_dragStartPoint`,
`_initialMargin`, `_isMinimized`. La posición persiste durante la
sesión; persistencia entre sesiones queda para iteración futura.

---

## 5. Liga F1 — Bug fixes post-release v0.8.0

### F1 · Labels de "HERRAMIENTAS PLANTA" invisibles
El `Style` implícito global de `RadioButton` en `MainWindow.xaml`
contaminaba los RadioButtons de la toolbar de Planta y sobrescribía
`Foreground` con `FgSecondary` (insuficiente contraste sobre
`#E62A2A2A`).

Fix: `Style="{x:Null}"` en cada uno de los 4 RadioButtons y el
CheckBox de E3 → texto blanco legible.

### F2 · Ejes A/B/C × 1/2/3 invisibles en Planta
`PenGrilla` usaba `SolidColorBrush(#808080) { Opacity=0.5 }` sobre
fondo `#1A1A1A` — prácticamente invisible.

Fix: color `#B0B0B0`, `Opacity=0.85`, `Thickness=1.2`. Etiquetas de
ejes ahora claramente legibles.

### F4 · "Losas.exe:" + "Pegar de Excel" sólo en modos Losas
El TextBlock + TextBox de path al `Losas.exe` y el MenuItem "Pegar de
Excel" vivían en la top action bar sin filtro de modo, apareciendo en
Columnas / Vigas / Zapatas / Vista 3D / Planta donde son
irrelevantes.

Fix: 5 `DataTrigger`s acumulativos por modo (`Editor, PlanoCad,
DLEditor, Salida, Aceros`) con `Visibility=Visible`; default
`Collapsed`.

### F3 · Columnas en posición opuesta (analizado — no fix incluido)
Hipótesis: las columnas con coordenadas espejo eran del proyecto seed
sin `PosX/PosY` definidas (fallback a grilla artificial). El sistema de
coords actual con `ScaleTransform(1, -1)` es matemáticamente coherente.
Pendiente de verificación empírica del usuario tras este release.

---

## 6. Estado de los módulos estructurales

Sin cambios respecto a v0.8.0 — todos los módulos siguen operativos
**en fase de prueba**. La validación contra software comercial
(ETABS, SAFE, RFEM) sigue siendo prerrequisito antes de aprobar
cualquier diseño para construcción.

---

## 7. Visión del producto (sin cambios)

- 🎯 Modelo estructural integrado con transmisión de cargas
  losa→viga→columna→zapata (Liga G futura).
- 🎯 Detallado de acero comercial (áreas crudas → barras `#N`).
- 🎯 Diagramas M/V/Δ 3D reales ✅ (Liga E4 ya cerrada).
- 🎯 Interoperabilidad BIM: SAF 2.2.0 export ✅; import + IFC 4 +
  Revit pendientes.
- 🎯 Adopción del nuevo Código de Construcción dominicano (MIVHED) —
  metadata + links ya integrados; adopción en motores en Liga H
  futura.

---

## 8. Cómo abrir y probar

1. Descomprimir `EstructurasRD-v0.8.1-win-x64.zip`.
2. Ejecutar `LosasPlus.exe`.
3. Verificar:
   - Modo "Acerca de" muestra v0.8.1 + 3 botones de acción.
   - Modo "Planta Estructural" → ejes A/B/C × 1/2/3 visibles, labels
     de toolbar legibles, CheckBox "Mostrar conexiones nodal" pinta
     círculos verdes/naranjas.
   - Modo "Reglamento" → seleccionar cualquier entrada con PDF →
     viewer embebido muestra la primera página con navegación + zoom.
   - Modo "Vista 3D" → ComboBox "📊 Diagrama" con 4 opciones reales
     sobre vigas.
   - Modo "Configuración" → tab "🔧 Cálculos & datos" → cambiar
     decimales, cerrar y reabrir → preferencia persistida.
   - Panel "Elemento Activo": arrastrar header lo mueve; doble-click
     en header o botón [—] lo minimiza.
   - "Losas.exe:" + "Pegar de Excel" sólo en modos Editor / PlanoCad /
     DLEditor / Salida / Aceros.

---

## 9. Crédito y firma técnica

- Marca paraguas: **EstructurasRD**
- Módulo histórico: **LosasPlus** (motor Pieper-Martens de F. Perdomo, 2011)
- Suite + integración 3D + SAF + Planta Estructural: **Emil Guillén De La Cruz**
- Stack: WPF .NET 8 + MVVM puro + OxyPlot 2.2.0 +
  HelixToolkit.Wpf.SharpDX 3.1.2 + Docnet.Core + EPPlus +
  StructuralAnalysisFormat SDK 1.7.3
- Build: 0 warnings / 0 errors / 853 tests verdes
- Rama: `feat/saf-export-interop1`
