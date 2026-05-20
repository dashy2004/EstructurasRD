namespace LosasPlus.Models.Cad;

/// <summary>
/// Plano de referencia importado de un archivo <c>.PDF</c> — el equivalente
/// rasterizado del calco DXF para arquitecturas que sólo circulan en PDF
/// (Iteración 4 del Epic v1.2).
///
/// <para>
/// Es un agregado <b>puro de dominio</b>: nombre del archivo, dimensiones
/// físicas (derivadas de los puntos PostScript del PDF · 0.0254 / 96 m/px) y
/// los tres factores de ajuste espacial (<see cref="Escala"/>,
/// <see cref="OffsetX"/>, <see cref="OffsetY"/>) — el mismo bloque-transform
/// que <see cref="PlanoReferencia"/> usa para el DXF.
/// </para>
///
/// <para>
/// El <b>bitmap rasterizado</b> (un <c>BitmapSource</c> de WPF) se conserva
/// fuera de este modelo — en el ViewModel del proyecto <c>src/</c> — para
/// preservar la promesa de que <c>src.Core</c> no referencia tipos de WPF.
/// </para>
/// </summary>
public sealed class PdfReferencia
{
    /// <summary>Nombre del archivo .PDF de origen (sin ruta).</summary>
    public string NombreArchivo { get; init; } = "";

    /// <summary>Ancho físico de la primera página, en metros.</summary>
    public double Ancho { get; init; }

    /// <summary>Alto físico de la primera página, en metros.</summary>
    public double Alto { get; init; }

    /// <summary>True si el PDF no reporta dimensiones físicas válidas.</summary>
    public bool EstaVacio => Ancho <= 0 || Alto <= 0;

    // ---- Transformación de bloque — ajuste espacial del underlay PDF -------

    /// <summary>
    /// Factor de escala uniforme aplicado al bloque entero del PDF al
    /// dibujarlo en el lienzo. 1.0 = tamaño físico nativo del PDF.
    /// </summary>
    public double Escala { get; set; } = 1.0;

    /// <summary>Desplazamiento horizontal del bloque PDF, en metros.</summary>
    public double OffsetX { get; set; }

    /// <summary>Desplazamiento vertical del bloque PDF, en metros.</summary>
    public double OffsetY { get; set; }
}
