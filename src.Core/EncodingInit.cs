using System.Runtime.CompilerServices;
using System.Text;

namespace LosasPlus;

/// <summary>
/// Registra el proveedor de páginas de código (cp1252) requerido para leer/escribir
/// los archivos del motor original Losas.exe. .NET Core / .NET 5+ ya no incluye estas
/// codificaciones en el runtime base; sin esta inicialización <c>Encoding.GetEncoding(1252)</c>
/// lanza <see cref="System.NotSupportedException"/>.
///
/// Se ejecuta una sola vez al cargar el ensamblado (ModuleInitializer, .NET 5+/C# 9+).
/// El mismo registro vale tanto para la app WPF como para los tests, porque ambos
/// referencian este ensamblado.
/// </summary>
internal static class EncodingInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
