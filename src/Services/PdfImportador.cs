using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Resultado de un intento de importación de PDF. <see cref="Imagen"/> es un
/// <see cref="Bitmap"/> de Avalonia (port: antes era WPF BitmapSource).
/// </summary>
public sealed record PdfImportResult(
    PdfReferencia? Meta, Bitmap? Imagen, string? Error, string? Aviso = null)
{
    public bool EsExito => Meta is not null && Imagen is not null && Error is null;
}

/// <summary>
/// Importación de planos PDF como overlay del lienzo CAD.
///
/// <para><b>FASE F pendiente</b> del port a Linux: la rasterización real usaba
/// <c>Docnet.Core</c> (PDFium nativo, solo-Windows) → BitmapSource. En Linux se
/// reemplazará por un rasterizador multiplataforma (PDFtoImage / bblanchon PDFium
/// linux-x64 → buffer BGRA → <see cref="Bitmap"/>). Por ahora es un stub que
/// informa que el overlay PDF no está disponible todavía, para no bloquear el
/// resto del editor CAD.</para>
/// </summary>
public static class PdfImportador
{
    private const double MetrosPorPxA96Dpi = 0.0254 / 96.0;

    public static Task<PdfImportResult> RasterizarPrimeraPaginaAsync(
        string path, int anchoObjetivoPx = 2400, bool invertColors = false)
        => Task.FromResult(new PdfImportResult(
            Meta: null,
            Imagen: null,
            Error: "El overlay de PDF aún no está disponible en la versión Linux " +
                   "(pendiente la fase de rasterización PDFium multiplataforma)."));
}
