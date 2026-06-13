using System;
using LosasPlus.Models;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Matemática pura del encuadre de Planta 2D (UI1.7): bounding box del
/// contenido en metros y transform de ajuste (escala/traslación) para un
/// viewport en píxeles. Sin dependencias de UI — el <c>PlantaCanvas</c> solo
/// aplica el resultado sobre sus campos <c>_scale/_tx/_ty</c> (precedente:
/// <see cref="CalibradorPdf"/>).
/// </summary>
public static class EncuadrePlanta
{
    /// <summary>Tope del fit — evita zooms absurdos con contenido minúsculo.</summary>
    public const double EscalaMax = 200.0;

    /// <summary>Escala para contenido sin extensión (un punto) — el default del lienzo.</summary>
    public const double EscalaFallback = 40.0;

    /// <summary>
    /// Bounding box del contenido de la planta, en metros: losas y muros de
    /// TODOS los sistemas, vigas (origen/extremo) y centros de columnas. Con
    /// <paramref name="incluirUnderlays"/>, une además el rect del PDF
    /// (<c>Offset + (Ancho, Alto)·Escala</c>) y el del DXF (bbox de entidades
    /// mapeado con el flip-Y de <c>PlanoAPlanta</c>). <c>null</c> si no hay
    /// nada que medir.
    /// </summary>
    public static RectM? CalcularExtents(Nivel? nivel, PlanoReferencia? plano,
                                         PdfReferencia? pdf, bool incluirUnderlays)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        bool hay = false;

        void Punto(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            hay = true;
        }

        if (nivel is not null)
        {
            foreach (var col in nivel.Columnas)
                Punto(col.CoordenadaX, col.CoordenadaY);

            foreach (var viga in nivel.Vigas)
            {
                Punto(viga.OrigenX, viga.OrigenY);
                Punto(viga.ExtremoX, viga.ExtremoY);
            }

            foreach (var sistema in nivel.Sistemas)
            {
                foreach (var losa in sistema.Losas)
                {
                    Punto(losa.CoordenadaX, losa.CoordenadaY);
                    Punto(losa.CoordenadaX + losa.Lx, losa.CoordenadaY + losa.Ly);
                }
                foreach (var muro in sistema.Muros)
                {
                    Punto(muro.PuntoInicio.X, muro.PuntoInicio.Y);
                    Punto(muro.PuntoFin.X, muro.PuntoFin.Y);
                }
            }
        }

        if (incluirUnderlays)
        {
            // Gate por extensión del bbox (no por Entidades): un plano vacío
            // tiene Min == Max y no aporta nada que encuadrar.
            if (plano is not null && (plano.MaxX > plano.MinX || plano.MaxY > plano.MinY))
            {
                Punto(plano.OffsetX + plano.MinX * plano.Escala, plano.OffsetY);
                Punto(plano.OffsetX + plano.MaxX * plano.Escala,
                      plano.OffsetY + plano.Alto * plano.Escala);
            }
            if (pdf is { EstaVacio: false })
            {
                Punto(pdf.OffsetX, pdf.OffsetY);
                Punto(pdf.OffsetX + pdf.Ancho * pdf.Escala, pdf.OffsetY + pdf.Alto * pdf.Escala);
            }
        }

        return hay ? new RectM(minX, minY, maxX - minX, maxY - minY) : null;
    }

    /// <summary>
    /// Transform de ajuste: escala (px/m) y traslación (px) que centran
    /// <paramref name="rect"/> en un viewport de
    /// <paramref name="anchoPx"/>×<paramref name="altoPx"/> con un margen
    /// fraccional por lado. La escala se clampa a <see cref="EscalaMax"/>;
    /// un rect sin extensión en ambos ejes usa <see cref="EscalaFallback"/>.
    /// </summary>
    public static (double Escala, double Tx, double Ty) CalcularEncuadre(
        RectM rect, double anchoPx, double altoPx, double margen = 0.05)
    {
        const double eps = 1e-9;
        double util = 1.0 - 2.0 * margen;

        double porAncho = rect.Ancho > eps ? anchoPx * util / rect.Ancho : double.PositiveInfinity;
        double porAlto  = rect.Alto  > eps ? altoPx  * util / rect.Alto  : double.PositiveInfinity;

        double escala = Math.Min(porAncho, porAlto);
        if (double.IsPositiveInfinity(escala)) escala = EscalaFallback;
        escala = Math.Min(escala, EscalaMax);

        double tx = anchoPx / 2.0 - rect.CentroX * escala;
        double ty = altoPx  / 2.0 - rect.CentroY * escala;
        return (escala, tx, ty);
    }
}
