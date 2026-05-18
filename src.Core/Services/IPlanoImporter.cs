using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Importa un plano de arquitectura desde un archivo de intercambio CAD y lo
/// traduce a un <see cref="PlanoReferencia"/> — modelo puro de dominio con
/// coordenadas en metros, sin dependencias de UI.
///
/// <para>
/// La interfaz desacopla a los consumidores de la librería de lectura
/// concreta (hoy <see cref="DxfImportService"/> sobre netDxf). Cambiar de
/// librería implica una sola implementación nueva, sin tocar el resto.
/// </para>
/// </summary>
public interface IPlanoImporter
{
    /// <summary>
    /// Lee el archivo indicado y devuelve su <see cref="PlanoReferencia"/>.
    /// </summary>
    /// <param name="rutaArchivo">Ruta absoluta al archivo a importar.</param>
    /// <returns>El plano importado, con coordenadas normalizadas a metros.</returns>
    /// <exception cref="System.ArgumentException">La ruta es nula o vacía.</exception>
    /// <exception cref="System.IO.FileNotFoundException">El archivo no existe.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// El archivo está vacío, corrupto o no es un formato CAD válido.
    /// </exception>
    PlanoReferencia Importar(string rutaArchivo);
}
