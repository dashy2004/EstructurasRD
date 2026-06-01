# LosasPlus / MemoriaPlus — Port a Linux (Avalonia)

Este documento cubre cómo compilar y ejecutar la suite en **Linux**, y el plan
de migración de la GUI de **WPF → Avalonia**.

## Por qué Avalonia

La GUI original está en **WPF + WinForms**, que **solo corren en Windows**. No
existe forma de ejecutar WPF en Linux. La ruta para una app Linux nativa es
portar la UI a **[Avalonia](https://avaloniaui.net/)**, el framework XAML
multiplataforma cuyo modelo es muy cercano a WPF. Avalonia corre en **Linux,
Windows y macOS**, así que el port no pierde Windows: lo unifica.

El motor de cálculo (`src.Core/`, `net8.0`) **ya es multiplataforma** y no
requiere cambios.

## Requisitos

- **.NET 8 SDK**. Si no está instalado:
  ```bash
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$PATH:$HOME/.dotnet"
  ```
- Para **ejecutar** la GUI: un escritorio X11 o Wayland. Avalonia usa SkiaSharp
  (no requiere `libgdiplus`). En Wayland corre vía XWayland por defecto.

## Estado del port (cabeza de puente)

| Componente | Estado en Linux |
|---|---|
| `src.Core` (motor de cálculo, parsers, generador .docx) | ✅ Compila y corre nativo |
| `src.UI.Shared` (Avalonia) | ✅ **Portada a Avalonia** (tema, 9 vistas, converters, servicios) |
| `src.Memoria` (MemoriaPlus, Avalonia) | ✅ **Portada y corre nativa en Linux** (las 4 pestañas + modos) |
| `src.Linux` (smoke harness Avalonia) | ✅ Compila y arranca (referencia/validación; obsoleto cuando se complete todo) |
| `src/` (LosasPlus.App) | 🟡 **Fase A (fundación) portada y arranca**; faltan vistas/VMs/CAD/PDF/OxyPlot (Fases B+) |

### LosasPlus.App — roadmap de fases (B+)

La app principal (12.6k líneas) es el port más complejo. La **Fase A** (csproj
Avalonia, 3 temas con switching, converters, `ThemeCustomizer`, App/Program y un
MainWindow placeholder) ya compila y arranca en Linux; el resto está excluido en
el csproj y se reincorpora por fases:

- **B** — estilos implícitos de `App.xaml` (TextBox/Button/ComboBox/DataGrid…) → ControlThemes.
- **C** — decoplar WPF de los ViewModels (`MainViewModel`, `CadEditorViewModel`,
  `VigaEditorViewModel`): diálogos → `AppServices`, `Losas.exe` vía `Process` opcional
  (no corre en Linux salvo Wine).
- **D** — vistas tratables (importadores, reglamento, DL doctor, paneles de tipos…).
- **E** — `CadCanvasHost` (2198 líneas): `DrawingVisual`/`OnRender` retenido → `Control.Render` inmediato de Avalonia. **Lo más duro.**
- **F** — overlay PDF (`Docnet.Core`/PDFium → PDFtoImage/bblanchon PDFium linux-x64) + `VigaEditorView` (`OxyPlot.Wpf` → `OxyPlot.Avalonia`).

### Ejecutar MemoriaPlus en Linux

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd EstructurasRD-main
dotnet run --project src.Memoria -c Release
# Atajos de smoke directo: -- --modo=calculos --tab=niveles
```

> **Nota SkiaSharp:** `Avalonia.Svg.Skia` se fija a `11.2.0.2` (no 11.3.x) porque
> la 11.3.0 saltó a SkiaSharp 3.116, incompatible con el SkiaSharp 2.88.9 que usa
> Avalonia 11.3 core (crash "native libSkiaSharp 88.1 incompatible").

## Compilar y ejecutar la cabeza de puente Avalonia

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd EstructurasRD-main
dotnet build LosasPlus.Linux.sln -c Release
dotnet run --project src.Linux -c Release
```

Debe abrir una ventana mostrando el proyecto de ejemplo "Torre Residencial
Ensanche Piantini" con las 10 losas y sus salidas calculadas por el motor real
(`h_calc`, `h_eq`, `qd`, `qu`, `αm`, estado αfm).

> La solución original `LosasPlus.sln` sigue apuntando a los proyectos WPF
> (solo Windows). Para Linux use **`LosasPlus.Linux.sln`**, que incluye
> `src.Core` + `src.Linux` y se ampliará con los proyectos portados.

## Guía de conversión WPF → Avalonia

Patrones aplicados / a aplicar al portar cada vista:

| WPF | Avalonia |
|---|---|
| `.xaml` + `xmlns="...winfx/2006/xaml/presentation"` | `.axaml` + `xmlns="https://github.com/avaloniaui"` |
| `pack://application:,,,/Assembly;component/ruta` | `avares://Assembly/ruta` |
| `Visibility` (`Visible/Collapsed`) + converters | `IsVisible` (bool) directo |
| `Style` con `ControlTemplate` + `Trigger` | `ControlTheme` + selectores con pseudo-clases (`:pointerover`, `:checked`, `:disabled`) |
| `IValueConverter` de `System.Windows.Data` | `Avalonia.Data.Converters.IValueConverter` (firma casi idéntica) |
| `Binding.DoNothing` | `Avalonia.Data.BindingOperations.DoNothing` |
| `Microsoft.Win32.OpenFileDialog/SaveFileDialog` | `TopLevel.StorageProvider` (API async) |
| `System.Windows.MessageBox` | diálogo propio / `MessageBox.Avalonia` |
| `DispatcherTimer`, `Dispatcher.Invoke` | `Avalonia.Threading.DispatcherTimer` / `Dispatcher.UIThread` |
| `BitmapImage`/`BitmapSource` | `Avalonia.Media.Imaging.Bitmap` |
| `RelayCommand` con `CommandManager.RequerySuggested` | evento `CanExecuteChanged` propio + `RaiseCanExecuteChanged()` |
| `SvgViewbox` (SharpVectors) | `Svg` control de `Avalonia.Svg.Skia` |
| `OxyPlot.Wpf` `PlotView` | `OxyPlot.Avalonia` |
| `Docnet.Core` (PDFium, render PDF) | build Linux de PDFium / Poppler — a evaluar |

Los **ViewModels** de `src.UI.Shared/ViewModels` y de `src.Memoria` están en su
mayoría libres de WPF (usan `ICommand`/`INotifyPropertyChanged` del BCL), así
que se reutilizan; el `MainViewModel` de MemoriaPlus sí usa diálogos
`Microsoft.Win32` y `MessageBox` inline, que se abstraen en un servicio de
diálogos antes de portarlo.

## Próximas fases

1. Portar `RelayCommand` + converters de `src.UI.Shared/Common` a Avalonia.
2. Introducir un `IDialogService` (file pickers + message box) multiplataforma.
3. Portar `src.Memoria` (MemoriaPlus) entero — es standalone, no necesita
   `Losas.exe` ni CAD/PDF/OxyPlot.
4. Portar `src.UI.Shared` (selector SVG, validación, búsqueda, configuración).
5. Portar `src/` (LosasPlus.App): canvas CAD, overlay PDF, gráficas OxyPlot.
6. Tests multiplataforma del Core + smoke de la GUI Avalonia.
