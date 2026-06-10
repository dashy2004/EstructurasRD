# F1 — Gobernar la verdad visual · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminar el riesgo de gráficas en blanco migrando los 4 diagramas que siguen en `oxy:PlotView` al patrón PNG probado (`VigaPng`, `InteraccionPng`, `SeccionPng`, `SeccionColumnaPng`, cada uno con su test de píxeles), y unificar el lienzo de planta (EditorUnificadoView sin pestañas, Auto-Conectar + import DXF/PDF en la base).

**Architecture:** El `PlotModel` de cada diagrama NO cambia; cambia el camino de render: el VM expone una prop `byte[]?` generada con `DiagramaPng.Render` (OxyPlot-core → ImageSharp, headless) y la vista la muestra en `<Image>` vía el converter global `BytesToBitmap` (`src/App.axaml:122`). Es el mismo patrón ya verde de `EsfuerzosPng`/`DeflexionPng`. El lienzo unificado es XAML-only sobre comandos existentes de `CadEditor`.

**Tech Stack:** .NET 8 / Avalonia 11, OxyPlot core + OxyPlot.ImageSharp, xUnit (tests de píxeles con `Image.Load<Rgba32>`), bash.

**Spec de referencia:** `docs/superpowers/specs/2026-06-10-f1-verdad-visual-design.md`

**TDD obligatorio:** en C1–C3 el test se escribe PRIMERO, se ve fallar (rojo: error de compilación — la prop no existe), se implementa, se ve pasar. C4 es XAML-only sin test posible (no hay `Avalonia.Headless` en tests): se verifica con build + greps + pase manual.

---

## Estructura de archivos

- **Modificar** `src/ViewModels/Vigas/VigaEditorViewModel.cs` — props `VigaPng`/`SeccionPng` + render + limpieza.
- **Modificar** `src/ViewModels/ColumnasEditorViewModel.cs` — props `InteraccionPng`/`SeccionColumnaPng` + render.
- **Modificar** `src/Views/Vigas/VigaEditorView.axaml` — PlotView `:287` y `:257` → `<Image>`; retirar `xmlns:oxy`.
- **Modificar** `src/Views/ColumnasEditorView.axaml` — PlotView `:209` y `:204` → `<Image>`; retirar `xmlns:oxy`.
- **Modificar** `tests/LosasPlus.Tests/DiagramaPngTests.cs` — +2 tests (VigaPng, SeccionPng).
- **Crear** `tests/LosasPlus.Tests/ColumnasEditorPngTests.cs` — 3 tests (InteraccionPng ×2, SeccionColumnaPng).
- **Modificar** `src/Views/Planta2DEditorView.axaml` — 3 botones (DXF/PDF/Auto-Conectar).
- **Modificar** `src/Views/EditorUnificadoView.axaml` — sin TabControl.
- **Modificar** `docs/plan-unificar-cad-planta2d.md` (solo puntero al tope) y `STATE.md` (región curada, al cierre).

Restricciones permanentes: **nunca** tocar `Losas.exe` ni su import; **no** borrar `CadView`/`CadCanvasHost`; **no** cambiar colores/estilos de los `PlotModel`.

---

## Task 1: Rama + baseline verde

**Files:** ninguno.

- [ ] **Step 1: Confirmar baseline verde y árbol limpio ANTES de tocar nada**

Run:
```bash
cd /home/gdc/Downloads/EstructurasRD-engine
git status --porcelain
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
( cd motor-fea && .venv/bin/python -m pytest -q 2>&1 | tail -2 )
```
Expected: `git status` vacío (si hay restos de F0 sin commit, detener y reportar); `Passed: 1106`; `208 passed`. Si NO está verde, detener y reportar.

- [ ] **Step 2: Crear la rama de trabajo**

Run:
```bash
git rev-parse --abbrev-ref HEAD   # esperado: engine/f0-verdad-de-estado
git checkout -b engine/f1-verdad-visual
```

---

## Task 2: C1 — `VigaPng` (TDD)

**Files:**
- Modify: `tests/LosasPlus.Tests/DiagramaPngTests.cs`
- Modify: `src/ViewModels/Vigas/VigaEditorViewModel.cs`
- Modify: `src/Views/Vigas/VigaEditorView.axaml`

- [ ] **Step 1: Escribir el test PRIMERO** — añadir a `DiagramaPngTests.cs` (reusa `VigaResoluble()` `:21-30` y `ContieneColor` `:33-46`):

```csharp
    [Fact]
    public async Task VigaPng_dibuja_eje_y_carga_distribuida()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaResoluble());
        int n = 0;
        var vm = new VigaEditorViewModel(proyecto, () => n++, () => proyecto.Edificios[0].Niveles[0]);
        await vm.RecalcularAsync();

        Assert.NotNull(vm.VigaPng);
        // Eje de la viga = ColorViga rgb(51,58,69); banda de carga distribuida = ColorCarga rgb(181,138,0).
        Assert.True(ContieneColor(vm.VigaPng!, 51, 58, 69), "El PNG del modelo de viga debe dibujar el eje (LineSeries).");
        Assert.True(ContieneColor(vm.VigaPng!, 181, 138, 0), "El PNG del modelo de viga debe dibujar la banda de carga (RectangleAnnotation).");
    }
```

- [ ] **Step 2: Verlo FALLAR (rojo)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | grep -E "error|Failed!|Passed!" | head -5
```
Expected: `error CS1061` — `VigaEditorViewModel` no contiene `VigaPng`. (Rojo por compilación cuenta como rojo.)

- [ ] **Step 3: Implementar en el VM** (`src/ViewModels/Vigas/VigaEditorViewModel.cs`):
  1. Junto a las constantes `:215-217`, añadir: `private const int PngAltoViga = 320;`
  2. Tras la prop `DeflexionPng` (`:227-233`), añadir la prop (mismo patrón):
```csharp
    private byte[]? _vigaPng;
    /// <summary>PNG del modelo físico de la viga para mostrar como imagen.</summary>
    public byte[]? VigaPng
    {
        get => _vigaPng;
        private set { _vigaPng = value; OnPropertyChanged(); }
    }
```
  3. En `ConstruirSeries()` tras `:574` (`DeflexionPng = ...`): `VigaPng = DiagramaPng.Render(ModeloViga, PngAncho, PngAltoViga);`
  4. En `LimpiarDiagramas()` junto a `:588-589`: `VigaPng = null;`

- [ ] **Step 4: Verlo PASAR (verde)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: `Passed: 1107` / `Failed: 0`.

- [ ] **Step 5: Cablear la vista** — en `src/Views/Vigas/VigaEditorView.axaml:287` reemplazar
`<oxy:PlotView Model="{Binding ModeloViga}" Background="Transparent"/>` por:
```xml
                    <Image Source="{Binding VigaPng, Converter={StaticResource BytesToBitmap}}"
                           Stretch="Uniform" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
```
(NO retirar `xmlns:oxy` todavía: la línea `:257` sigue usándolo hasta C3.)

- [ ] **Step 6: Build limpio + commit C1**

Run:
```bash
dotnet build LosasPlus.Linux.sln 2>&1 | tail -3
git add tests/LosasPlus.Tests/DiagramaPngTests.cs src/ViewModels/Vigas/VigaEditorViewModel.cs src/Views/Vigas/VigaEditorView.axaml
git commit -m "feat(render): VigaPng — modelo fisico de viga via DiagramaPng + test de pixeles

oxy:PlotView (bug: no pinta series) -> <Image> con PNG de OxyPlot-core.
Mismo patron que EsfuerzosPng/DeflexionPng. F1 spec 2026-06-10.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Expected: 0 errores; commit creado.

---

## Task 3: C2 — `InteraccionPng` (TDD)

**Files:**
- Create: `tests/LosasPlus.Tests/ColumnasEditorPngTests.cs`
- Modify: `src/ViewModels/ColumnasEditorViewModel.cs`
- Modify: `src/Views/ColumnasEditorView.axaml`

- [ ] **Step 1: Escribir los tests PRIMERO** — crear `tests/LosasPlus.Tests/ColumnasEditorPngTests.cs`:

```csharp
using LosasPlus.Models;
using LosasPlus.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests de pixeles de los PNG del editor de columnas (patron DiagramaPng):
/// el diagrama P-M y la seccion transversal deben dibujarse de verdad —
/// son 2 de las 4 graficas que oxy:PlotView dejaba en blanco (F1).
/// </summary>
public class ColumnasEditorPngTests
{
    private static ColumnasEditorViewModel VmConColumna()
    {
        var ed = new Edificio();
        var niv = new Nivel();
        niv.Columnas.Add(new Columna { Id = 1, Nombre = "C-1", Base = 0.40, Peralte = 0.40, Altura = 3.0 });
        ed.Niveles.Add(niv);
        var vm = new ColumnasEditorViewModel(() => ed, () => niv);
        vm.Seleccionada = niv.Columnas[0];
        return vm;
    }

    /// <summary>¿Hay algún píxel cercano (±tol por canal) al color RGB dado?</summary>
    private static bool ContieneColor(byte[] png, int r, int g, int b, int tol = 24)
    {
        using var img = Image.Load<Rgba32>(png);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var p = img[x, y];
                if (System.Math.Abs(p.R - r) <= tol &&
                    System.Math.Abs(p.G - g) <= tol &&
                    System.Math.Abs(p.B - b) <= tol)
                    return true;
            }
        return false;
    }

    [Fact]
    public void InteraccionPng_dibuja_la_curva_PM()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.InteraccionPng);
        // Curva de diseño = #3B82F6 = rgb(59,130,246).
        Assert.True(ContieneColor(vm.InteraccionPng!, 59, 130, 246), "El PNG de interacción debe dibujar la curva φPn-φMn (azul).");
    }

    [Fact]
    public void Sin_seleccion_InteraccionPng_es_null()
    {
        var vm = VmConColumna();
        vm.Seleccionada = null;
        Assert.Null(vm.InteraccionPng);
    }
}
```

- [ ] **Step 2: Verlo FALLAR (rojo)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | grep -E "error|Failed!|Passed!" | head -5
```
Expected: `error CS1061` — `ColumnasEditorViewModel` no contiene `InteraccionPng`.

- [ ] **Step 3: Implementar en el VM** (`src/ViewModels/ColumnasEditorViewModel.cs`):
  1. Añadir `using LosasPlus.Rendering;` al bloque de usings.
  2. Constantes (junto a los campos privados del VM): `private const int PngAnchoInteraccion = 900; private const int PngAltoInteraccion = 600;`
  3. Tras la prop `ModeloSeccionColumna` (`:220`): `public byte[]? InteraccionPng { get; private set; }`
  4. En `RecalcularDiseno()` tras `ConstruirPlot();` (`:247`): `InteraccionPng = DiagramaPng.Render(ModeloInteraccion, PngAnchoInteraccion, PngAltoInteraccion);` — `Render(null,…)` ya devuelve `null` (`DiagramaPng.cs:33`), cubre la rama sin selección.
  5. Junto a `OnPropertyChanged(nameof(ModeloInteraccion));` (`:250`): `OnPropertyChanged(nameof(InteraccionPng));`

- [ ] **Step 4: Verlo PASAR (verde)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: `Passed: 1109` / `Failed: 0`.

- [ ] **Step 5: Cablear la vista** — en `src/Views/ColumnasEditorView.axaml:209` reemplazar
`<oxy:PlotView Model="{Binding ModeloInteraccion}" .../>` por:
```xml
                            <Image Source="{Binding InteraccionPng, Converter={StaticResource BytesToBitmap}}"
                                   Stretch="Uniform" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
```
(El `Border Height="300"` `:208` se conserva. `xmlns:oxy` aún no se retira: `:204` lo usa hasta C3. La vista compila bindings — `x:CompileBindings="True"` `:8` — la prop `byte[]?` compila igual.)

- [ ] **Step 6: Build limpio + commit C2**

Run:
```bash
dotnet build LosasPlus.Linux.sln 2>&1 | tail -3
git add tests/LosasPlus.Tests/ColumnasEditorPngTests.cs src/ViewModels/ColumnasEditorViewModel.cs src/Views/ColumnasEditorView.axaml
git commit -m "feat(render): InteraccionPng — diagrama P-M de columnas via DiagramaPng + tests

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Expected: 0 errores; commit creado.

---

## Task 4: C3 — `SeccionPng` + `SeccionColumnaPng` (TDD)

**Files:**
- Modify: `tests/LosasPlus.Tests/DiagramaPngTests.cs`, `tests/LosasPlus.Tests/ColumnasEditorPngTests.cs`
- Modify: `src/ViewModels/Vigas/VigaEditorViewModel.cs`, `src/ViewModels/ColumnasEditorViewModel.cs`
- Modify: `src/Views/Vigas/VigaEditorView.axaml`, `src/Views/ColumnasEditorView.axaml`

- [ ] **Step 1: Escribir los 2 tests PRIMERO**

En `DiagramaPngTests.cs` (mismo fixture del Task 2 Step 1; el tramo usa `Base=0.30`/`Peralte=0.50` por defecto — `src.Core/Vigas/TramoViga.cs:23-26`):
```csharp
    [Fact]
    public async Task SeccionPng_dibuja_estribo_y_barras()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaResoluble());
        int n = 0;
        var vm = new VigaEditorViewModel(proyecto, () => n++, () => proyecto.Edificios[0].Niveles[0]);
        await vm.RecalcularAsync();

        Assert.NotNull(vm.SeccionPng);
        // Estribo = DarkRed rgb(139,0,0); barras = DarkBlue rgb(0,0,139).
        Assert.True(ContieneColor(vm.SeccionPng!, 139, 0, 0), "El PNG de la sección debe dibujar el estribo (annotation).");
        Assert.True(ContieneColor(vm.SeccionPng!, 0, 0, 139), "El PNG de la sección debe dibujar las barras (ScatterSeries).");
    }
```

En `ColumnasEditorPngTests.cs`:
```csharp
    [Fact]
    public void SeccionColumnaPng_dibuja_estribo_y_barras()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.SeccionColumnaPng);
        Assert.True(ContieneColor(vm.SeccionColumnaPng!, 139, 0, 0), "El PNG de la sección de columna debe dibujar el estribo (DarkRed).");
        Assert.True(ContieneColor(vm.SeccionColumnaPng!, 0, 0, 139), "El PNG de la sección de columna debe dibujar las barras (DarkBlue).");
    }
```

- [ ] **Step 2: Verlos FALLAR (rojo)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | grep -E "error|Failed!|Passed!" | head -5
```
Expected: `error CS1061` por `SeccionPng` y `SeccionColumnaPng`.

- [ ] **Step 3: Implementar en `VigaEditorViewModel.cs`:**
  1. Constantes junto a `:215-217`: `private const int PngAnchoSeccion = 600; private const int PngAltoSeccion = 640;`
  2. Prop `SeccionPng` (mismo patrón que `VigaPng` de C1).
  3. En `ConstruirSeries()` tras la línea de `VigaPng` (C1): `SeccionPng = DiagramaPng.Render(ModeloSeccion, PngAnchoSeccion, PngAltoSeccion);`
  4. En el setter de `TramoSeleccionado`, tras `ConstruirModeloSeccion();` (`:147`): `SeccionPng = DiagramaPng.Render(ModeloSeccion, PngAnchoSeccion, PngAltoSeccion);` (la sección cambia al seleccionar otro tramo sin recálculo).
  5. En `LimpiarDiagramas()`: `SeccionPng = null;`

- [ ] **Step 4: Implementar en `ColumnasEditorViewModel.cs`:**
  1. Constantes: `private const int PngAnchoSeccionCol = 600; private const int PngAltoSeccionCol = 440;`
  2. Prop: `public byte[]? SeccionColumnaPng { get; private set; }`
  3. En `RecalcularDiseno()` junto a la línea de `InteraccionPng` (C2): `SeccionColumnaPng = DiagramaPng.Render(ModeloSeccionColumna, PngAnchoSeccionCol, PngAltoSeccionCol);`
  4. Junto a `OnPropertyChanged(nameof(ModeloSeccionColumna));` (`:252`): `OnPropertyChanged(nameof(SeccionColumnaPng));`

- [ ] **Step 5: Verlos PASAR (verde)**

Run:
```bash
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
```
Expected: `Passed: 1111` / `Failed: 0`.

- [ ] **Step 6: Cablear las vistas y retirar `xmlns:oxy`:**
  - `VigaEditorView.axaml:257` → `<Image Source="{Binding SeccionPng, Converter={StaticResource BytesToBitmap}}" Stretch="Uniform" VerticalAlignment="Stretch" HorizontalAlignment="Stretch"/>` (conserva `Grid.Row="1"`).
  - `ColumnasEditorView.axaml:204` → `<Image Source="{Binding SeccionColumnaPng, Converter={StaticResource BytesToBitmap}}" Stretch="Uniform" .../>` (el `Border Height="220"` `:203` se conserva).
  - Retirar la línea `xmlns:oxy="clr-namespace:OxyPlot.Avalonia;assembly=OxyPlot.Avalonia"` de AMBAS vistas (`VigaEditorView.axaml:3`, `ColumnasEditorView.axaml:3`) — quedan 0 usos.

- [ ] **Step 7: Verificación estructural + commit C3**

Run:
```bash
dotnet build LosasPlus.Linux.sln 2>&1 | tail -3
grep -Ec "<oxy:PlotView|xmlns:oxy" src/Views/Vigas/VigaEditorView.axaml src/Views/ColumnasEditorView.axaml
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
git add tests/LosasPlus.Tests/DiagramaPngTests.cs tests/LosasPlus.Tests/ColumnasEditorPngTests.cs \
        src/ViewModels/Vigas/VigaEditorViewModel.cs src/ViewModels/ColumnasEditorViewModel.cs \
        src/Views/Vigas/VigaEditorView.axaml src/Views/ColumnasEditorView.axaml
git commit -m "feat(render): SeccionPng + SeccionColumnaPng — 0 oxy:PlotView en editores de viga/columna

Las secciones tambien contienen ScatterSeries (barras de refuerzo), la clase
exacta del bug del renderer; migran al patron PNG (decision spec F1 2.2).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Expected: build 0 err; el grep = `0` en ambos archivos (OJO: se grepea el ELEMENTO `<oxy:PlotView` y el `xmlns:oxy` — NO la palabra suelta "oxy": el comentario de `VigaEditorView.axaml:290-292` contiene el texto "oxy:PlotView" y se conserva); `Passed: 1111`; commit creado.

---

## Task 5: C4 — Lienzo unificado (XAML-only)

**Files:**
- Modify: `src/Views/Planta2DEditorView.axaml`
- Modify: `src/Views/EditorUnificadoView.axaml`
- Modify: `docs/plan-unificar-cad-planta2d.md` (solo puntero al tope)

> Sin test automatizado posible (no hay `Avalonia.Headless` en `tests/LosasPlus.Tests`): verificación = build + greps + pase visual manual (Task 7 Step 2).

- [ ] **Step 1: Cablear Auto-Conectar + import en la base** — en `src/Views/Planta2DEditorView.axaml`, dentro del StackPanel de acciones (`:45-52`), ANTES de `BtnRecalcular` (`:48`), insertar (la vista es `x:CompileBindings="False"` `:5`; DataContext = `MainViewModel`, que expone `CadEditor` — mismos bindings que `CadView.axaml:31-36,145-147`):

```xml
                    <Button Content="📂 DXF" Command="{Binding CadEditor.ImportarDxfCommand}"
                            ToolTip.Tip="Importar plano DXF como referencia (underlay) para calcar la estructura."/>
                    <Button Content="📑 PDF" Command="{Binding CadEditor.ImportarPdfCommand}"
                            ToolTip.Tip="Importar plano PDF como referencia (underlay)."/>
                    <Button Content="🤖 Auto-Conectar" Command="{Binding CadEditor.AutoAlinearSistemasCommand}"
                            ToolTip.Tip="Alinea losas vecinas y genera los bordes de continuidad."/>
```

- [ ] **Step 2: Retirar la pestaña CAD** — en `src/Views/EditorUnificadoView.axaml` reemplazar el bloque `<TabControl …>…</TabControl>` (`:12-19`) por:

```xml
    <!-- Paso 5 (unificación) COMPLETADO en F1: un solo lienzo (base Planta 2D) con
         underlay DXF/PDF, muros, import y Auto-Conectar. El CadView completo sigue
         disponible en el modo PlanoCad del shell (MainWindow.axaml). Reversible:
         las vistas CAD no se borran. Ver docs/plan-unificar-cad-planta2d.md. -->
    <v:Planta2DEditorView DataContext="{Binding}"/>
```
y retirar `xmlns:cad="clr-namespace:LosasPlus.Views.Cad"` (`:4`, queda sin uso). El comentario viejo (`:9-11`) se sustituye por el de arriba.

- [ ] **Step 3: Puntero en el plan previo** — insertar al tope de `docs/plan-unificar-cad-planta2d.md` (sin tocar el cuerpo):

```markdown
> ✅ Pasos 4-5 ejecutados en F1 (2026-06-10) — ver `docs/superpowers/specs/2026-06-10-f1-verdad-visual-design.md` §2.4/§4.4. Este plan queda como referencia histórica.
```

- [ ] **Step 4: Verificación estructural + commit C4**

Run:
```bash
dotnet build LosasPlus.Linux.sln 2>&1 | tail -3
grep -c "TabItem" src/Views/EditorUnificadoView.axaml
grep -c "AutoAlinearSistemasCommand\|ImportarDxfCommand\|ImportarPdfCommand" src/Views/Planta2DEditorView.axaml
dotnet test LosasPlus.Linux.sln 2>&1 | tail -3
git add src/Views/Planta2DEditorView.axaml src/Views/EditorUnificadoView.axaml docs/plan-unificar-cad-planta2d.md
git commit -m "feat(ui): lienzo unificado — EditorUnificadoView sin pestañas; DXF/PDF/Auto-Conectar en Planta 2D

Pasos 4-5 del plan de unificacion (2-3 ya estaban en PlantaCanvas). CadView
sigue intacto en el modo PlanoCad del shell; nada se borra.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Expected: build 0 err; `TabItem` = `0`; el segundo grep = `3`; `Passed: 1111`; commit creado.

---

## Task 6: C5 — Cierre: re-estampar STATE.md + spec/plan F1

**Files:**
- Modify: `STATE.md` (región curada, a mano), regenerados `build.log`/`test.log` (por el script)
- Commit de: `docs/superpowers/specs/2026-06-10-f1-verdad-visual-design.md`, `docs/superpowers/plans/2026-06-10-f1-verdad-visual.md`

- [ ] **Step 1: Re-estampar la verdad de estado**

Run:
```bash
./estado-real.sh; echo "exit=$?"
sed -n '/AUTO:START/,/AUTO:END/p' STATE.md
```
Expected: `exit=0`; la región AUTO muestra `1111 passed / 0 failed` (.NET) y `208 passed` (Python).

- [ ] **Step 2: Actualizar la región CURADA de `STATE.md`** — reemplazar la línea del issue (hoy `STATE.md:23`):

`- **4 gráficas en blanco** (...) → **F1**.`

por:

```markdown
- ~~4 gráficas en blanco~~ — **resuelto en F1 (2026-06-10)**: los 4 diagramas (`VigaPng`, `SeccionPng`, `InteraccionPng`, `SeccionColumnaPng`) se renderizan a PNG vía `DiagramaPng` y se muestran en `<Image>`; 0 `oxy:PlotView` en `VigaEditorView`/`ColumnasEditorView`. Tests de píxeles verdes.
```

(El resto de la región curada NO se toca; el script solo reescribe entre marcadores.)

- [ ] **Step 3: Commit C5**

Run:
```bash
git add STATE.md docs/superpowers/specs/2026-06-10-f1-verdad-visual-design.md \
        docs/superpowers/plans/2026-06-10-f1-verdad-visual.md
git ls-files --error-unmatch build.log test.log >/dev/null 2>&1 && git add build.log test.log
git commit -m "chore(estado): cerrar F1 — STATE.md re-estampado (1111 .NET / 208 Py) + spec/plan F1

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Expected: commit creado; working tree limpio (`git status --porcelain` vacío).

---

## Task 7: Verificación final (criterios de aceptación del spec §6)

**Files:** ninguno.

- [ ] **Step 1: Recorrer los criterios verificables por máquina**

Run:
```bash
echo "1/2. props + tests PNG:"; grep -rl "VigaPng\|InteraccionPng\|SeccionPng\|SeccionColumnaPng" tests/LosasPlus.Tests/DiagramaPngTests.cs tests/LosasPlus.Tests/ColumnasEditorPngTests.cs
echo "3. 0 oxy en vistas migradas:"; grep -Ec "<oxy:PlotView|xmlns:oxy" src/Views/Vigas/VigaEditorView.axaml src/Views/ColumnasEditorView.axaml
echo "4. 0 TabItem:"; grep -c "TabItem" src/Views/EditorUnificadoView.axaml
echo "5. CadView intacto:"; git log --oneline -1 -- src/Views/Cad/CadView.axaml | wc -l; grep -c "cad:CadView" src/MainWindow.axaml
echo "6. suites:"; dotnet test LosasPlus.Linux.sln 2>&1 | tail -1; ( cd motor-fea && .venv/bin/python -m pytest -q 2>&1 | tail -1 )
echo "7. Losas.exe intacto:"; git diff engine/f0-verdad-de-estado..HEAD --stat | grep -ci "losas.exe" || echo "0 cambios"
echo "commits F1:"; git log --oneline -6
```
Expected: tests presentes; `0` y `0` de oxy; `0` TabItem; CadView sin cambios en F1 y aún hosteado en MainWindow (`grep` = 1); `Passed: 1111` y `208 passed`; `0 cambios` sobre Losas.exe; commits C1–C5 visibles.

- [ ] **Step 2: Pase visual MANUAL del usuario (criterio no automatizable)**

Pedir al usuario que corra la app y confirme: (a) los 6 diagramas de Vigas/Columnas se ven (modelo, esfuerzos, deflexión, sección viga, P-M, sección columna); (b) en Planta 2D los botones DXF/PDF/Auto-Conectar operan y el underlay se dibuja; (c) el modo Plano CAD sigue funcionando igual. Si algo se ve mal (p. ej. contraste de un PNG en tema oscuro), anotarlo como follow-up — NO ajustar colores en F1 (spec §2.3).

- [ ] **Step 3: Marcar F1 como cerrada**

F1 cumplida. Siguientes fases por apalancamiento: F2 (pipeline CAD/DXF determinístico, desbloqueada por F1) y F3/F4 (correctitud de motor, independientes). Cada una con su propio spec + plan.

---

## Self-Review (cobertura del spec)

- §4.1 `VigaPng`/`SeccionPng` (VM + vista + setter TramoSeleccionado + LimpiarDiagramas) → Task 2 + Task 4 Steps 3,6. ✔
- §4.2 `InteraccionPng`/`SeccionColumnaPng` (VM + vista, rama null sin selección) → Task 3 + Task 4 Steps 4,6. ✔
- §4.3 tests de píxeles, los 5 (VigaPng eje+annotation; SeccionPng estribo+barras; InteraccionPng curva; InteraccionPng null; SeccionColumnaPng estribo+barras), TDD rojo→verde → Tasks 2-4 Steps 1-2/4-5. ✔
- §2.2 decisión secciones (ScatterSeries = clase del bug) reflejada en mensaje de commit C3 → Task 4 Step 7. ✔
- §4.4 lienzo unificado (paso 4 Auto-Conectar+import, paso 5 sin TabControl, puntero al plan previo, CadView intacto) → Task 5. ✔
- §4.5 commits C1–C5 → Tasks 2-6, uno por commit. ✔
- §5 verificación (conteos 1107/1109/1111, greps, estado-real.sh) → Steps de cada task + Task 6 Step 1 + Task 7 Step 1. ✔
- §6 criterios 1-7 → Task 7 Step 1 (máquina) + Step 2 (manual). ✔
- §7 riesgos: headless (pase manual Step 2), interactividad perdida (documentado en spec, sin acción), colisión de color (colores afirmados distantes), fuentes (ya mitigado en DiagramaPng), import no se pierde (criterio 5), SeccionPng stale (Task 4 Step 3.4). ✔
- No-objetivos respetados: no se toca pipeline CAD batch, ni calibrar PDF, ni colores de PlotModel, ni Losas.exe, ni se borra vista alguna. ✔
