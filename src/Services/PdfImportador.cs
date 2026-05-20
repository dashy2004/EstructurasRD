using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Resultado de un intento de importación de PDF. Inmutable; el llamador
/// inspecciona <see cref="EsExito"/> para decidir si actualiza el ViewModel
/// o si muestra el <see cref="Error"/> en un <c>MessageBox</c>.
/// </summary>
/// <param name="Meta">Metadata del PDF (nombre, dimensiones físicas), o <c>null</c> en error.</param>
/// <param name="Imagen">Bitmap rasterizado y <b>congelado</b>, o <c>null</c> en error.</param>
/// <param name="Error">Mensaje amigable de error, o <c>null</c> en éxito.</param>
public sealed record PdfImportResult(PdfReferencia? Meta, BitmapSource? Imagen, string? Error)
{
    public bool EsExito => Meta is not null && Imagen is not null && Error is null;
}

/// <summary>
/// Servicio de importación de planos arquitectónicos en PDF (Iteración 4
/// del Epic v1.2). Rasteriza la primera página vía <b>Docnet.Core</b>
/// (PDFium nativo) en un hilo de background, devuelve los bytes como
/// <see cref="BitmapSource"/> ya <c>.Freeze()</c>-eado para que pueda
/// cruzarse al hilo UI sin excepciones de Thread Ownership.
///
/// <para>
/// El PDF se asume <b>no protegido por contraseña</b>; si lo está, Docnet
/// lanzará y el método devolverá un <see cref="PdfImportResult"/> con un
/// mensaje amigable en lugar de propagar la excepción.
/// </para>
/// </summary>
public static class PdfImportador
{
    /// <summary>
    /// Factor de conversión de píxeles @ 96 DPI a metros físicos.
    /// 1 inch = 0.0254 m; Docnet reporta dimensiones de página en px @ 96 DPI
    /// cuando se abre con escalado 1.0.
    /// </summary>
    private const double MetrosPorPxA96Dpi = 0.0254 / 96.0;

    /// <summary>
    /// Rasteriza la primera página del PDF en <paramref name="path"/> con
    /// un ancho objetivo de <paramref name="anchoObjetivoPx"/> píxeles
    /// (preserva la relación de aspecto). Toda la operación corre en un
    /// <see cref="Task.Run"/> para no bloquear el hilo UI.
    /// </summary>
    /// <param name="path">Ruta absoluta al archivo .PDF.</param>
    /// <param name="anchoObjetivoPx">
    /// Resolución horizontal del bitmap rasterizado. Valores entre 2000 y
    /// 3000 mantienen los textos del plano legibles sin disparar la memoria.
    /// </param>
    public static Task<PdfImportResult> RasterizarPrimeraPaginaAsync(
        string path, int anchoObjetivoPx = 2400) => Task.Run(() =>
    {
        try
        {
            // Paso 1 — lectura natural (sin parámetros) para descubrir las
            // dimensiones físicas del PDF (px @ 96 DPI). Esto define el
            // tamaño del rect en metros que el lienzo usará al dibujar.
            int natW, natH;
            using (var docNatural = DocLib.Instance.GetDocReader(path, new PageDimensions()))
            using (var pageNatural = docNatural.GetPageReader(0))
            {
                natW = pageNatural.GetPageWidth();
                natH = pageNatural.GetPageHeight();
            }

            if (natW <= 0 || natH <= 0)
                return new PdfImportResult(null, null,
                    "El PDF no reporta dimensiones válidas en su primera página.");

            // Paso 2 — reabrir con dimensiones objetivo (en px enteros)
            // preservando la relación de aspecto. PageDimensions(int, int) es
            // la API que Docnet.Core 2.6 expone — no acepta escalas double.
            double escala = Math.Max(0.1, (double)anchoObjetivoPx / natW);
            int targetW = Math.Max(1, (int)Math.Round(natW * escala));
            int targetH = Math.Max(1, (int)Math.Round(natH * escala));
            using var doc = DocLib.Instance.GetDocReader(path, new PageDimensions(targetW, targetH));
            using var page = doc.GetPageReader(0);

            int wPx = page.GetPageWidth();
            int hPx = page.GetPageHeight();
            byte[] bgra = page.GetImage();   // stride = wPx * 4 (32 bpp BGRA)

            // Docnet entrega los bytes en BGRA (no RGBA) — usar Bgra32.
            var bmp = BitmapSource.Create(
                wPx, hPx, 96, 96, PixelFormats.Bgra32, null, bgra, wPx * 4);
            bmp.Freeze();   // seguro en background; obligatorio antes del UI thread

            var meta = new PdfReferencia
            {
                NombreArchivo = Path.GetFileName(path),
                Ancho = natW * MetrosPorPxA96Dpi,
                Alto  = natH * MetrosPorPxA96Dpi,
            };
            return new PdfImportResult(meta, bmp, null);
        }
        catch (Exception ex)
        {
            // Docnet lanza excepciones genéricas (Exception / IOException /
            // DllNotFoundException si falta PDFium) para PDFs corruptos,
            // protegidos con contraseña o archivos inaccesibles. Capturamos
            // amplio y devolvemos un mensaje amigable; nada sube al UI thread.
            return new PdfImportResult(null, null,
                $"No se pudo leer el PDF.\n\nDetalle: {ex.Message}");
        }
    });
}
