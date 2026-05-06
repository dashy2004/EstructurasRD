# Plugins de LosasPlus

Coloca archivos `.csx` (C# Script) en esta carpeta. Cada plugin se compila y ejecuta dentro del wrapper.

## API disponible

El script recibe un objeto global `PluginContext` con los miembros:

- `Sistema Sistema` — sistema actual de losas (puede ser `null` antes de cargar un .DL).
- `string Hook` — hook activo: `"load"`, `"pre-dl"`, `"post-txt"`, `"custom-export"`.
- `string OutputTxt` — contenido del .TXT generado por Losas.exe (sólo en `post-txt`).
- `void Log(string text)` — agregar mensajes al panel de log.
- `void RegisterTipo(int codigo, string descripcion)` — añadir tipo de losa al catálogo.

## Imports permitidos

`System`, `System.Linq`, `System.Collections.Generic`, `System.IO`, `System.Text`, `System.Globalization`, `LosasPlus.Models`, `LosasPlus.Services`.

## Advertencia de seguridad

Roslyn Scripting **no es un sandbox de seguridad fuerte**. Sólo ejecutar plugins de fuentes de confianza. La aplicación puede pedir confirmación antes de cargar un plugin nuevo.

## Ejemplo

Ver `ejemplo.csx` en esta carpeta.
