using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Caché compartida de <see cref="JsonSerializerOptions"/> para los servicios de
/// persistencia de configuración (themes, recents, atajos, apariencia,
/// plantillas, perfil). Antes cada servicio declaraba su propia instancia con
/// una configuración idéntica copiada y pegada; centralizarla elimina esa
/// duplicación y comparte un único caché interno de metadata de serialización.
///
/// <para>
/// Un <see cref="JsonSerializerOptions"/> queda de sólo lectura tras su primer
/// uso, así que una instancia <c>static readonly</c> compartida es segura
/// mientras nadie la mute — ningún servicio lo hace. <c>ProyectoSerializer</c>
/// queda aparte a propósito: su configuración es distinta (Populate, encoder
/// relajado, WhenWritingNull, etc.) y propia del formato de proyecto.
/// </para>
/// </summary>
internal static class JsonConfigHelper
{
    /// <summary>
    /// Opciones JSON estándar de los servicios de configuración de archivo:
    /// salida indentada y nombres de propiedad en camelCase.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
