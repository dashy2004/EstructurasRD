# Compilación de LosasPlus

## Prerrequisitos

1. Windows 10/11 x64.
2. .NET 8 SDK instalado: https://dotnet.microsoft.com/download/dotnet/8.0
3. Verificar instalación:
   ```
   dotnet --version
   ```
   Debe responder algo como `8.0.x`.

## Build de desarrollo

Desde la raíz `LosasPlus/`:

```
dotnet restore
dotnet build
```

Para ejecutar:

```
dotnet run --project src/LosasPlus.csproj
```

## Build de release

```
dotnet build -c Release
```

Output en `src/bin/Release/net8.0-windows/`.

## Publicación single-file (recomendado para distribuir)

Genera un único `.exe` portable (con .NET embebido):

```
dotnet publish src/LosasPlus.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true
```

(En PowerShell reemplazar `^` por backtick `` ` ``).

Output: `src/bin/Release/net8.0-windows/win-x64/publish/LosasPlus.exe`.

Tamaño aproximado: 50–80 MB con compresión, sin dependencias externas (excepto Losas.exe del motor original).

## Estructura del proyecto

```
LosasPlus/
├── LosasPlus.sln
├── src/
│   ├── LosasPlus.csproj
│   ├── app.manifest
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── Models/
│   │   └── Sistema.cs
│   ├── Services/
│   │   ├── DLFileService.cs
│   │   ├── LosasRunner.cs
│   │   ├── TxtParser.cs
│   │   ├── CsvExporter.cs
│   │   ├── PluginHost.cs
│   │   └── ReglamentoService.cs
│   ├── ViewModels/
│   │   └── MainViewModel.cs
│   ├── Views/
│   │   ├── DiagramView.xaml / .cs
│   │   └── ReglamentoView.xaml / .cs
│   └── Resources/
│       ├── TiposLosa.json
│       └── Reglamento.json
├── plugins/
│   ├── README.md
│   └── ejemplo.csx
├── README.md
└── BUILD.md
```

## Despliegue junto al motor original

Recomendado: copiar `LosasPlus.exe` a la misma carpeta donde residen `Losas.exe`, `vvm31w.dll`, `vgui31w.sll`, etc. La aplicación detecta automáticamente `Losas.exe` adyacente al iniciar.

## Notas

- El proyecto requiere `net8.0-windows` (TFM con APIs WPF). No se compila en Linux/macOS porque WPF es Windows-only.
- Si al compilar aparece un error de versión de paquete, ejecutar `dotnet restore --force-evaluate`.
