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
/// <param name="Aviso">
/// Mensaje informativo a mostrar en la barra de estado cuando el PDF se cargó
/// con un fallback (ej. dimensiones por defecto ANSI D porque Docnet no las
/// reportó). <c>null</c> en éxito limpio. No es un error: <see cref="EsExito"/>
/// puede ser <c>true</c> aún cuando <see cref="Aviso"/> está poblado.
/// </param>
public sealed record PdfImportResult(
    PdfReferencia? Meta, BitmapSource? Imagen, string? Error, string? Aviso = null)
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
    /// <param name="invertColors">
    /// Si <c>true</c>, invierte los canales B/G/R del buffer rasterizado
    /// (preservando el canal Alpha) para producir un PDF en «modo oscuro»:
    /// fondo blanco → negro, líneas oscuras → blancas resplandecientes.
    /// Parche v1.2.2.
    /// </param>
    public static Task<PdfImportResult> RasterizarPrimeraPaginaAsync(
        string path, int anchoObjetivoPx = 2400, bool invertColors = false) => Task.Run(() =>
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

            // Fallback ANSI D — algunos PDFs estructurales no exponen /MediaBox
            // explícitamente y Docnet reporta 0 para Width/Height. Asignamos
            // 36" × 24" @ 96 DPI (3456 × 2304 px ≈ 91.4 × 60.96 cm — formato D
            // horizontal estándar) y emitimos un aviso para que la UI sugiera
            // al usuario calibrar manualmente con la herramienta de calibración.
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

            // Paso 2 — rasterizar por FACTOR de escala proporcional (Parche v1.2.3).
            //
            // Reemplaza el approach anterior con PageDimensions(int, int), que
            // generaba buffers vacíos en PDFs con transformaciones de impresión
            // complejas (/CropBox /Rotate matrices) y exigía dim1 ≤ dim2
            // internamente. El constructor PageDimensions(double) preserva la
            // geometría nativa del PDF y solo escala proporcionalmente — funciona
            // en portrait, landscape y PDFs con cualquier matriz de transformación.
            //
            // Factor calculado para apuntar al anchoObjetivoPx, clampeado a
            // [0.1, 5.0]: el techo 5.0 evita ampliar PDFs diminutos hasta
            // absurdo; el piso 0.1 (Epic v1.3) permite reducir PDFs nativamente
            // gigantes sin forzar bitmaps enormes durante la re-rasterización
            // dinámica por zoom.
            double factor = Math.Clamp((double)anchoObjetivoPx / natW, 0.1, 5.0);

            // Cap de área (Epic v1.3) — ningún bitmap supera 30 megapíxeles
            // (≈120 MB en BGRA32). Protege contra OutOfMemory cuando la
            // re-rasterización por zoom pide resoluciones altas sobre PDFs
            // de gran formato. Si el factor produjo un área excesiva, se
            // reduce proporcionalmente preservando la relación de aspecto.
            const double MaxMegapixeles = 30_000_000.0;
            double areaPx = (double)natW * natH * factor * factor;
            if (areaPx > MaxMegapixeles)
                factor *= Math.Sqrt(MaxMegapixeles / areaPx);

            BitmapSource? bmp;
            try
            {
                bmp = RasterizarPagina(path, new PageDimensions(factor), invertColors);
            }
            catch (Exception)
            {
                // Captura ancha: cualquier excepción nativa de PDFium/Docnet
                // (incluida OutOfMemory en factores altos) cae al salvavidas 1:1.
                bmp = null;
            }

            if (bmp is null)
            {
                // Salvavidas: renderizado a tamaño natural (factor 1.0). Pierde
                // el control de resolución objetivo pero la app no crashea y el
                // PDF se visualiza al menos a su tamaño nativo.
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
            // Docnet lanza excepciones genéricas (Exception / IOException /
            // DllNotFoundException si falta PDFium) para PDFs corruptos,
            // protegidos con contraseña o archivos inaccesibles. Capturamos
            // amplio y devolvemos un mensaje amigable; nada sube al UI thread.
            return new PdfImportResult(null, null,
                $"No se pudo leer el PDF.\n\nDetalle: {ex.Message}");
        }
    });

    /// <summary>
    /// Abre el PDF con el descriptor de dimensiones dado, lee la primera
    /// página, aplica inversión BGRA opcional y devuelve un
    /// <see cref="BitmapSource"/> congelado. Devuelve <c>null</c> sólo si
    /// Docnet reporta dimensiones &lt;= 0 o un buffer null/diminuto.
    /// Propaga excepciones de Docnet — el caller decide si caer al
    /// salvavidas 1:1.
    ///
    /// <para>
    /// Nota Parche v1.2.4: se removió la heurística <c>EsBufferVacio</c>
    /// que escaneaba los primeros 4 KB del buffer en busca de bytes en
    /// cero. Producía falsos positivos en PDFs con margen transparente
    /// o <c>/CropBox</c> legítimo (alpha=0 en la esquina superior izquierda
    /// es común en planos profesionales). La protección que ofrecía contra
    /// PDFs que devolvían buffer todo-cero sin lanzar excepción era muy
    /// rara, mientras que el falso positivo bloqueaba todos los PDFs con
    /// margen transparente.
    /// </para>
    /// </summary>
    private static BitmapSource? RasterizarPagina(string path, PageDimensions pd, bool invertColors)
    {
        using var doc = DocLib.Instance.GetDocReader(path, pd);
        using var page = doc.GetPageReader(0);
        int wPx = page.GetPageWidth();
        int hPx = page.GetPageHeight();
        if (wPx <= 0 || hPx <= 0) return null;
        byte[] bgra = page.GetImage();    // stride = wPx * 4 (32 bpp BGRA)
        if (bgra is null || bgra.Length < 4) return null;

        if (invertColors) InvertirBgra(bgra);
        var bmp = BitmapSource.Create(
            wPx, hPx, 96, 96, PixelFormats.Bgra32, null, bgra, wPx * 4);
        bmp.Freeze();   // seguro en background; obligatorio antes del UI thread
        return bmp;
    }

    /// <summary>
    /// Invierte los canales B/G/R del buffer BGRA (Parche v1.2.2 — modo
    /// oscuro CAD). El blanco (255,255,255) se vuelve negro (0,0,0) y
    /// viceversa; el canal Alpha (índice +3) se preserva.
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
