# LosasPlus / MemoriaPlus — Build multiplataforma (Avalonia)

La GUI original era **WPF + WinForms** (solo Windows). Se portó a
**[Avalonia](https://avaloniaui.net/)** (XAML multiplataforma, modelo cercano a
WPF), que corre en **Linux, Windows y macOS**. **Linux/Avalonia es ahora la
plataforma primaria de desarrollo**; Windows y macOS se producen *publicando* el
mismo codebase Avalonia — no hay una rama WPF separada que mantener.

El motor de cálculo (`src.Core/`, `net8.0`) siempre fue multiplataforma.

> **Estado: 🏁 Hito 1 alcanzado** — paridad con el snapshot 2026-05-22. La app
> Avalonia compila 0/0 y corre en Linux con todo: proyectos, losas,
> cargas/combinaciones, vigas (OxyPlot 2D), validación, reglamento, **Lienzo CAD
> (render + interacción)**, **importador PDF (PDFium)** y export CSV/XLSX/memoria.

## Requisitos

- **.NET 8 SDK**. Si no está instalado:
  ```bash
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
  ```
- **Prefijar todos los comandos** (los shells no-interactivos no sourcean el perfil):
  ```bash
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
  ```
- Para **ejecutar** la GUI: un escritorio X11 o Wayland. Avalonia renderiza con
  SkiaSharp (no necesita `libgdiplus`).

## Compilar y ejecutar en Linux

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd EstructurasRD-main

# Compilar TODA la solución. SIEMPRE con --no-incremental: el build incremental
# OCULTA los errores AVLN2000 del compilador XAML de Avalonia.
dotnet build LosasPlus.Linux.sln --no-incremental

# Ejecutar las apps
dotnet run --project src           -c Release   # LosasPlus  (app principal)
dotnet run --project src.Memoria   -c Release   # MemoriaPlus (generador de memorias)
```

`LosasPlus.Linux.sln` contiene los 5 proyectos vivos: `src.Core` (motor),
`src.UI.Shared` (tema + vistas compartidas), `src.Memoria` (MemoriaPlus),
`src` (LosasPlus) y `src.Linux` (un smoke-harness de validación).

## Publicar para Windows / macOS / Linux (desde Linux)

El **mismo** proyecto Avalonia produce binarios para los tres OS — esto sustituye
al viejo build WPF. .NET hace cross-publish: descarga el runtime-pack del RID
destino y resuelve los nativos (SkiaSharp, PDFium) por RID.

```bash
export DOTNET_ROOT="$HOME/.dotnet" PATH="$PATH:$HOME/.dotnet"
cd EstructurasRD-main

# Windows x64 (self-contained: no requiere .NET instalado en el destino)
dotnet publish src/LosasPlus.csproj -c Release -r win-x64   --self-contained -o publish/win-x64

# Linux x64 / macOS (Apple Silicon o Intel)
dotnet publish src/LosasPlus.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
dotnet publish src/LosasPlus.csproj -c Release -r osx-arm64 --self-contained -o publish/osx-arm64
dotnet publish src/LosasPlus.csproj -c Release -r osx-x64   --self-contained -o publish/osx-x64
```

- El publish `win-x64` genera `LosasPlus.exe` + `libSkiaSharp.dll` + `pdfium.dll`
  (verificado desde Linux: los nativos Windows se incluyen correctamente).
- Quitá `--self-contained` (o `--self-contained false`) para un build
  framework-dependent (más liviano, requiere .NET 8 en el destino).
- MemoriaPlus se publica igual (`src.Memoria/MemoriaPlus.App.csproj`).
- **Verificar que corre** en el OS destino requiere ese OS (o una VM); el publish
  desde Linux solo garantiza que los artefactos se generan.

## Gotchas críticos del entorno

- **`--no-incremental` siempre** al compilar — el incremental oculta AVLN2000 (XAML).
- **Pin `Avalonia.Svg.Skia 11.2.0.2`** (NO 11.3.x): la 11.3.0 usa SkiaSharp 3.116,
  incompatible con el SkiaSharp **2.88.9** que usa Avalonia 11.3 core → crash
  "native libSkiaSharp 88.1 incompatible [116.0,117.0)". Toda dependencia gráfica
  nueva debe respetar SkiaSharp 2.88.
- **PDF: `Docnet.Core` (no PDFtoImage)** — `GetImage()` devuelve BGRA crudo sin
  SkiaSharp, así que no rompe el pin. Trae `pdfium.so`/`.dll`/`.dylib` por RID en
  `runtimes/` (el RID Linux es el genérico `linux`, cubre x86_64).
- **Wayland**: no hay utilidad de screenshot fiable (grim/xdotool/import); la
  verificación de GUI headless se hace por "build 0/0 + app que arranca sin
  excepción" (`timeout 8 ./src/bin/Debug/net8.0/LosasPlus`; exit 124 = corrió OK).
- **ctor con controles `x:Name`** → llamar `InitializeComponent()` (generado), NO
  `AvaloniaXamlLoader.Load(this)` (sólo el primero puebla los campos `x:Name`).

## Guía de conversión WPF → Avalonia (referencia — ya aplicada)

| WPF | Avalonia |
|---|---|
| `.xaml` + `xmlns="...winfx/2006/xaml/presentation"` | `.axaml` + `xmlns="https://github.com/avaloniaui"` |
| `pack://application:,,,/Assembly;component/ruta` | `avares://Assembly/ruta` |
| `Visibility` (`Visible/Collapsed`) | `IsVisible` (bool) directo |
| `Style` + `ControlTemplate` + `Trigger` | `ControlTheme` + selectores con pseudo-clases (`:pointerover`, `:checked`) |
| `IValueConverter` (`System.Windows.Data`) | `Avalonia.Data.Converters.IValueConverter` |
| `Microsoft.Win32.OpenFileDialog/SaveFileDialog` | `TopLevel.StorageProvider` (async) |
| `System.Windows.MessageBox` | servicio de diálogos propio (`AppServices`) |
| `DispatcherTimer` / `Dispatcher.Invoke` | `Avalonia.Threading.DispatcherTimer` / `Dispatcher.UIThread` |
| `BitmapSource` / `BitmapImage` | `Avalonia.Media.Imaging.Bitmap` / `WriteableBitmap` |
| `RelayCommand` + `CommandManager.RequerySuggested` | `CanExecuteChanged` propio + `RaiseCanExecuteChanged()` |
| `SvgViewbox` (SharpVectors) | `Svg` de `Avalonia.Svg.Skia` |
| `OxyPlot.Wpf` `PlotView` | `OxyPlot.Avalonia` (2.1.0-Avalonia11) |
| `Docnet.Core` → `BitmapSource` | `Docnet.Core` → BGRA → `WriteableBitmap` |
| render retenido (`DrawingVisual`/`OnRender`) | render inmediato (`Control.Render(DrawingContext)` + `InvalidateVisual`) |
| `Mouse*` events / `CaptureMouse` | `Pointer*` events / `IPointer.Capture` |
| `LostKeyboardFocus` (`e.NewFocus`) | `LostFocus` + `Dispatcher.Post` + `FocusManager.GetFocusedElement` |
| `Rect.IsEmpty` / `Rect.Empty` | `default(Rect)` + test `Width/Height <= 0` |

## Pendientes (post-Hito 1)

- **Tests (`tests/LosasPlus.Tests`) siguen Windows-only** (`UseWPF=true`); por eso
  NO están en `LosasPlus.Linux.sln`. Hacerlos multiplataforma (quitar UseWPF,
  separar lo que dependa de tipos de vista) es un cleanup aparte.
- `LosasPlus.sln` (solución Windows histórica) se conserva; el desarrollo y la
  publicación multiplataforma usan `LosasPlus.Linux.sln`.
- **Fase H** — convergencia de features de v0.8.1 sobre Avalonia (sidebar de 7
  categorías, PdfViewerControl, config extendida, diagramas M/V/Δ reales, export
  SAF…) → camino al Hito 2.
- **Fase I** — 3D multiplataforma sin `HelixToolkit.Wpf.SharpDX` (DirectX/Windows):
  evaluar Veldrid / OpenTK.
