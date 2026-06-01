using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Docnet.Core;
using Docnet.Core.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Resultado de un intento de importación de PDF. Inmutable; el llamador
/// inspecciona <see cref="EsExito"/> para decidir si actualiza el ViewModel
/// o muestra el <see cref="Error"/>. <see cref="Imagen"/> es un
/// <see cref="Bitmap"/> de Avalonia (port: antes era WPF BitmapSource).
/// </summary>
/// <param name="Meta">Metadata del PDF (nombre, dimensiones físicas), o <c>null</c> en error.</param>
/// <param name="Imagen">Bitmap rasterizado, o <c>null</c> en error.</param>
/// <param name="Error">Mensaje amigable de error, o <c>null</c> en éxito.</param>
/// <param name="Aviso">
/// Mensaje informativo (no error) para la barra de estado cuando el PDF se
/// cargó con un fallback (dimensiones ANSI D, o modo nativo 1:1).
/// <see cref="EsExito"/> puede ser <c>true</c> con <see cref="Aviso"/> poblado.
/// </param>
public sealed record PdfImportResult(
    PdfReferencia? Meta, Bitmap? Imagen, string? Error, string? Aviso = null)
{
    public bool EsExito => Meta is not null && Imagen is not null && Error is null;
}

/// <summary>
/// Importación de planos PDF como overlay del lienzo CAD (Iteración 4 del Epic
/// v1.2) — <b>port a Avalonia/Linux (Fase F)</b>.
///
/// <para>
/// Rasteriza la primera página vía <b>Docnet.Core</b> (PDFium nativo,
/// multiplataforma: trae <c>pdfium.so</c> para linux en <c>runtimes/</c>) en un
/// hilo de background. <c>GetImage()</c> devuelve bytes <b>BGRA crudos</b> —
/// <b>no usa SkiaSharp</b>, por lo que no altera el pin SkiaSharp 2.88 de Avalonia.
/// El buffer se copia a un <see cref="WriteableBitmap"/> Bgra8888 vía
/// <see cref="ILockedFramebuffer"/> (port: WPF <c>BitmapSource.Create</c>+<c>Freeze</c>).
/// </para>
///
/// <para>
/// El PDF se asume <b>no protegido por contraseña</b>; si lo está (o está
/// corrupto, o falta el nativo PDFium), Docnet lanza y el método devuelve un
/// <see cref="PdfImportResult"/> con un mensaje amigable en lugar de propagar.
/// </para>
/// </summary>
public static class PdfImportador
{
    /// <summary>
    /// Conversión de píxeles @ 96 DPI a metros físicos. 1 inch = 0.0254 m;
    /// Docnet reporta dimensiones de página en px @ 96 DPI con escalado 1.0.
    /// </summary>
    private const double MetrosPorPxA96Dpi = 0.0254 / 96.0;

    /// <summary>
    /// Rasteriza la primera página del PDF en <paramref name="path"/> con un
    /// ancho objetivo de <paramref name="anchoObjetivoPx"/> px (preserva el
    /// aspecto). Corre en <see cref="Task.Run"/> para no bloquear el hilo UI.
    /// </summary>
    /// <param name="path">Ruta absoluta al archivo .PDF.</param>
    /// <param name="anchoObjetivoPx">
    /// Resolución horizontal del bitmap. 2000–3000 mantienen los textos del
    /// plano legibles sin disparar la memoria.
    /// </param>
    /// <param name="invertColors">
    /// Si <c>true</c>, invierte los canales B/G/R del buffer (preservando Alpha)
    /// para un PDF en «modo oscuro»: fondo blanco → negro, líneas → claras.
    /// </param>
    public static Task<PdfImportResult> RasterizarPrimeraPaginaAsync(
        string path, int anchoObjetivoPx = 2400, bool invertColors = false) => Task.Run(() =>
    {
        try
        {
            // Paso 1 — lectura natural para descubrir las dimensiones físicas
            // del PDF (px @ 96 DPI). Define el tamaño del rect en metros.
            int natW, natH;
            using (var docNatural = DocLib.Instance.GetDocReader(path, new PageDimensions()))
            using (var pageNatural = docNatural.GetPageReader(0))
            {
                natW = pageNatural.GetPageWidth();
                natH = pageNatural.GetPageHeight();
            }

            // Fallback ANSI D — algunos PDFs estructurales no exponen /MediaBox
            // y Docnet reporta 0. Asignamos 36"×24" @ 96 DPI (formato D horizontal)
            // y avisamos para sugerir calibración manual.
            const int AnsiDWidthPx96 = 3456;
            const int AnsiDHeightPx96 = 2304;
            string? aviso = null;
            if (natW <= 0 || natH <= 0)
            {
                natW = AnsiDWidthPx96;
                natH = AnsiDHeightPx96;
                aviso = "✓ PDF importado (Utilizando escala estimada ANSI D; " +
                        "use la herramienta de calibración para ajustar).";
            }

            // Paso 2 — rasterizar por FACTOR de escala proporcional. El ctor
            // PageDimensions(double) preserva la geometría nativa y solo escala
            // proporcionalmente — funciona en portrait, landscape y PDFs con
            // cualquier matriz de transformación. Factor clampeado a [0.1, 5.0].
            double factor = Math.Clamp((double)anchoObjetivoPx / natW, 0.1, 5.0);

            // Cap de área — ningún bitmap supera 30 megapíxeles (≈120 MB BGRA32).
            // Protege contra OutOfMemory en la re-rasterización por zoom.
            const double MaxMegapixeles = 30_000_000.0;
            double areaPx = (double)natW * natH * factor * factor;
            if (areaPx > MaxMegapixeles)
                factor *= Math.Sqrt(MaxMegapixeles / areaPx);

            Bitmap? bmp;
            try
            {
                bmp = RasterizarPagina(path, new PageDimensions(factor), invertColors);
            }
            catch (Exception)
            {
                // Cualquier excepción nativa de PDFium/Docnet (incl. OOM en
                // factores altos) cae al salvavidas 1:1.
                bmp = null;
            }

            if (bmp is null)
            {
                // Salvavidas: render a tamaño natural (factor 1.0). Pierde el
                // control de resolución pero la app no crashea.
                try
                {
                    bmp = RasterizarPagina(path, new PageDimensions(1.0), invertColors);
                    aviso ??= "✓ PDF importado en modo nativo 1:1 (la resolución objetivo no fue compatible con este PDF).";
                }
                catch (Exception ex)
                {
                    return new PdfImportResult(null, null,
                        $"No se pudo rasterizar el PDF ni con factor de escala ni en modo 1:1 nativo.\n\nDetalle: {ex.Message}");
                }
            }

            if (bmp is null)
                return new PdfImportResult(null, null,
                    "El PDF se abrió pero no produjo una imagen rasterizada válida.");

            var meta = new PdfReferencia
            {
                NombreArchivo = Path.GetFileName(path),
                Ancho = natW * MetrosPorPxA96Dpi,
                Alto  = natH * MetrosPorPxA96Dpi,
            };
            return new PdfImportResult(meta, bmp, null, aviso);
        }
        catch (Exception ex)
        {
            // Docnet lanza Exception / IOException / DllNotFoundException (si falta
            // el nativo PDFium) para PDFs corruptos, protegidos o inaccesibles.
            // Capturamos amplio; nada sube al UI thread.
            return new PdfImportResult(null, null,
                $"No se pudo leer el PDF.\n\nDetalle: {ex.Message}");
        }
    });

    /// <summary>
    /// Abre el PDF con el descriptor de dimensiones dado, lee la primera página,
    /// aplica inversión BGRA opcional y devuelve un <see cref="Bitmap"/> de
    /// Avalonia. Devuelve <c>null</c> sólo si Docnet reporta dimensiones &lt;= 0
    /// o un buffer null/diminuto. Propaga excepciones de Docnet — el caller
    /// decide si caer al salvavidas 1:1.
    /// </summary>
    private static Bitmap? RasterizarPagina(string path, PageDimensions pd, bool invertColors)
    {
        using var doc = DocLib.Instance.GetDocReader(path, pd);
        using var page = doc.GetPageReader(0);
        int wPx = page.GetPageWidth();
        int hPx = page.GetPageHeight();
        if (wPx <= 0 || hPx <= 0) return null;
        byte[] bgra = page.GetImage();    // stride = wPx * 4 (32 bpp BGRA)
        if (bgra is null || bgra.Length < 4) return null;

        if (invertColors) InvertirBgra(bgra);

        // Port a Avalonia: BGRA crudo → WriteableBitmap. WPF usaba Bgra32 (alpha
        // recto, no premultiplicado) → AlphaFormat.Unpremul. Copiamos respetando
        // RowBytes del framebuffer (puede tener padding distinto a wPx*4).
        var wb = new WriteableBitmap(
            new PixelSize(wPx, hPx), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using (var fb = wb.Lock())
        {
            int srcStride = wPx * 4;
            if (fb.RowBytes == srcStride)
            {
                Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
            }
            else
            {
                for (int y = 0; y < hPx; y++)
                    Marshal.Copy(bgra, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
            }
        }
        return wb;
    }

    /// <summary>
    /// Invierte los canales B/G/R del buffer BGRA (modo oscuro CAD). El blanco
    /// (255,255,255) se vuelve negro y viceversa; el canal Alpha (índice +3) se preserva.
    /// </summary>
    private static void InvertirBgra(byte[] buf)
    {
        for (int i = 0; i + 3 < buf.Length; i += 4)
        {
            buf[i]     = (byte)(255 - buf[i]);      // B
            buf[i + 1] = (byte)(255 - buf[i + 1]);  // G
            buf[i + 2] = (byte)(255 - buf[i + 2]);  // R
        }
    }
}
