# LosasPlus

Capa moderna de UI, edición, visualización, exportación y plugins sobre el motor de cálculo `Losas.exe`.

> **Crédito principal del motor de cálculo:** Francisco Eludino Perdomo. Programa Losas v5.00 (febrero 2011), método Pieper-Martens (TU Braunschweig). Contacto: fa.perdomo@gmail.com.
>
> **Autor del wrapper LosasPlus:** Emil Guillen De la Cruz · emilgdc@gmail.com · GitHub [@dashy2004](https://github.com/dashy2004) · YouTube [@emilguillen](https://www.youtube.com/@emilguillen) · Instagram [@emilguillendelacruz](https://www.instagram.com/emilguillendelacruz).
>
> Esta capa externa NO modifica ni redistribuye `Losas.exe`. Se ofrece como sugerencia/aporte al autor original; ver [`CARTA_AL_AUTOR.md`](CARTA_AL_AUTOR.md) para el contexto.

LosasPlus **no modifica** el binario original. Genera el archivo `.DL` en el formato documentado por el autor en `Losas.hlp`, permite lanzar `Losas.exe` con un clic (la ejecución del cálculo es manual desde la GUI nativa del programa) y luego parsea el `.TXT` resultante para enriquecer la presentación.

> **Por qué el cálculo es manual.** El binario original es Visual Smalltalk Enterprise 3.1: ignora argumentos de línea de comandos y sus controles no exponen patrones UI Automation programables. Tras 7 experimentos documentados en [`docs/RUNNER_BEHAVIOR.md`](docs/RUNNER_BEHAVIOR.md), automatizar la GUI sólo sería viable mediante simulación de mouse/teclado a coordenadas absolutas — frágil con DPI scaling, robaría el cursor del usuario y sería sensible al foco. Decidimos que LosasPlus sea un editor + visor honesto sobre el motor original, no una caja negra inestable.

## Funcionalidades

- Editor estructurado de losas con dropdown del catálogo de TIPO (Pieper-Martens, ampliable vía plugins).
- Edición libre del archivo `.DL` con sincronización bidireccional contra el modelo.
- Esquema visual del sistema con ejes X/Y, indicación de bordes continuos vs. simples, etiquetado de BALANCEO (S/N) y rotulado de momentos parseados (Mfx, Mfy, MSx, MSy).
- Preview del `.TXT` de salida embebido.
- Exportación a CSV (compatible con Excel-ES, separador `;`, BOM UTF-8).
- Panel de Reglamento de Construcción Dominicano (R-001, R-024, R-026, R-027) y referencias ACI 318-08 / ACI 318-19.
- Sandbox de plugins en C# Script (`.csx`) sobre Roslyn, con hooks `load`, `pre-dl`, `post-txt`, `custom-export`.

## Requisitos

- Windows 10/11 x64.
- .NET 8 SDK para compilar (https://dotnet.microsoft.com/download/dotnet/8.0).
- El motor original `Losas.exe` y sus DLLs/SLLs (incluidos en la suite Disest).

## Compilación

```
cd LosasPlus
dotnet restore
dotnet build -c Release
dotnet run --project src/LosasPlus.csproj
```

Para producir un ejecutable single-file:

```
dotnet publish src/LosasPlus.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

El ejecutable queda en `src/bin/Release/net8.0-windows/win-x64/publish/LosasPlus.exe`.

## Uso

1. Abrir LosasPlus.
2. En la barra superior, indicar la ruta a `Losas.exe` (auto-detectada si está en una carpeta padre).
3. Crear losas en la pestaña Editor o `Abrir .DL` para cargar uno existente.
4. `Guardar .DL` (genera el archivo en formato compatible — dispara el hook `pre-dl` de plugins).
5. `Lanzar Losas.exe` → abre el motor original. En su ventana: menú **File → Open**, seleccionar el `.DL` recién guardado, ejecutar el cálculo, guardar el `.TXT`.
6. Volver a LosasPlus, `Importar .TXT` → parsea los momentos, los inyecta en la grilla y dispara el hook `post-txt`.
7. `Exportar CSV` para llevar los resultados a Excel.

## Plugins

Coloca scripts `.csx` en la carpeta `plugins/`. Ver `plugins/README.md` y `plugins/ejemplo.csx`.

## Catálogo de tipos

`Models/Sistema.cs::TipoLosa.Catalogo` contiene el catálogo de los códigos TIPO usados por el motor (también espejado en `Resources/TiposLosa.json`). La convención del 1er dígito:

| Dígito | Significado                                                  |
|--------|--------------------------------------------------------------|
| `1x`   | Sin bordes empotrados (simplemente apoyada, posibles vuelos) |
| `2x`   | 1 borde continuo                                             |
| `3x`   | 2 bordes continuos (paralelos o adyacentes en `33`)          |
| `4x`   | 3 bordes continuos                                           |
| `5x`   | Variantes con vuelo / 4 bordes con orientación específica    |
| `6x`   | 4 bordes empotrados (perimetral)                             |
| `7x`   | Voladizos                                                    |

Las entradas con `verificado: false` en el JSON son inferencias tentativas (típicamente las variantes `13`, `14`, `23`, `24`, `34`, `43`, `44`, `53`, `54`, `63`, `64` que mapean a las Tablas 7-12 del PDF y cubren nudos de 3 lados o combinaciones con vuelo). Para corregirlas: cargar un `.DL` de prueba con ese tipo, ejecutar el motor, comparar momentos/apoyos contra la tabla correspondiente del PDF.

## Tests

Suite xUnit en `tests/LosasPlus.Tests/`, con fixtures reales en `tests/fixtures/` capturados de salidas de `Losas.exe`. Cubre:

- Parser de `.TXT`: encabezado, momentos, armaduras de vano X/Y, apoyos sobre soportes X/Y, integración con el modelo (`TxtParserTests`).
- Cobertura del catálogo: cada tipo presente en una fixture debe estar en `TipoLosa.Catalogo` (`TipoLosaCatalogTests`).

```
dotnet test -c Release
```

Para agregar nuevas fixtures: copiá el `.TXT` a `tests/fixtures/` y agregá un `[Fact]` o `[Theory]` en `TxtParserTests.cs`.

## Limitaciones del motor (declaradas por el autor en Losas.hlp)

- Carga viva ≤ 2/3 de la carga total (sin factorizar).
- Apoyos lineales rígidos (peralte de viga > 4·h, o cumplimiento del §13.6.1.6 ACI 318-08).
- No verifica fuerza cortante.
- No calcula deformaciones.

## Aviso legal

La distribución conjunta de este wrapper con el binario `Losas.exe` requiere autorización del autor original. Esta capa externa atribuye y respeta su autoría; cualquier publicación o redistribución debe contar con su consentimiento explícito.
