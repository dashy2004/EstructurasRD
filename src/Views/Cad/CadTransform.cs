using Avalonia;
using LosasPlus.Models.Cad;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Transformación <b>mundo ↔ pantalla</b> del plano DXF — un único par de
/// funciones reversibles, exactamente inversas la una de la otra.
///
/// <para>
/// El render del plano (<c>DibujarPlano</c>) y el hit-test
/// (<c>HitTestPoligono</c>) deben compartir EXACTAMENTE la misma matemática de
/// coordenadas; de lo contrario, con <see cref="Escala"/> ≠ 1 o
/// <see cref="OffsetX"/>/<see cref="OffsetY"/> ≠ 0 (el panel «AJUSTE
/// ESPACIAL»), un clic se mapea al punto-mundo equivocado y la losa
/// seleccionada no coincide con la del cursor.
/// </para>
///
/// <para>El pipeline mundo→pantalla, en orden, es:</para>
/// <list type="number">
///   <item>Ajuste espacial del bloque DXF: <c>x·esc + offsetX</c>,
///         <c>y·esc + offsetY</c> (metros).</item>
///   <item>Flip-Y de la capa del plano respecto de <c>maxY·esc + offsetY</c>
///         (DXF crece hacia arriba; la pantalla, hacia abajo).</item>
///   <item>Metros → píxeles del lienzo (× <see cref="PxPorMetro"/>).</item>
///   <item>Zoom/pan del viewport: × <see cref="ScaleX"/>/<see cref="ScaleY"/>
///         + <see cref="Tx"/>/<see cref="Ty"/>.</item>
/// </list>
///
/// <para>
/// Es un tipo <b>puro</b> (un <c>readonly struct</c> de parámetros): no depende
/// del <c>Control</c> vivo, así que es comprobable por unidad — un punto de ida
/// y vuelta <c>PantallaAMundo(MundoAPantalla(p)) ≈ p</c> debe recuperar el
/// punto original con esc≠1 y offset≠0.
/// </para>
/// </summary>
public readonly struct CadTransform
{
    /// <summary>Píxeles del lienzo por metro (antes del zoom/pan).</summary>
    public double PxPorMetro { get; }

    /// <summary>Escala uniforme del bloque DXF (panel «Ajuste espacial»).</summary>
    public double Escala { get; }

    /// <summary>Desplazamiento horizontal del bloque DXF (m).</summary>
    public double OffsetX { get; }

    /// <summary>Desplazamiento vertical del bloque DXF (m).</summary>
    public double OffsetY { get; }

    /// <summary>Y máxima del plano (m) — pivote del flip-Y de la capa.</summary>
    public double MaxY { get; }

    /// <summary>Zoom horizontal del viewport.</summary>
    public double ScaleX { get; }

    /// <summary>Zoom vertical del viewport.</summary>
    public double ScaleY { get; }

    /// <summary>Pan horizontal del viewport (px).</summary>
    public double Tx { get; }

    /// <summary>Pan vertical del viewport (px).</summary>
    public double Ty { get; }

    public CadTransform(double pxPorMetro, double escala, double offsetX, double offsetY,
                        double maxY, double scaleX, double scaleY, double tx, double ty)
    {
        PxPorMetro = pxPorMetro;
        Escala     = escala;
        OffsetX    = offsetX;
        OffsetY    = offsetY;
        MaxY       = maxY;
        ScaleX     = scaleX;
        ScaleY     = scaleY;
        Tx         = tx;
        Ty         = ty;
    }

    /// <summary>Pivote del flip-Y en metros ajustados: <c>maxY·esc + offsetY</c>.</summary>
    private double MaxYAjustada => MaxY * Escala + OffsetY;

    /// <summary>Mundo (m, sistema DXF) → pantalla (px del lienzo, ya con zoom/pan).</summary>
    public Point MundoAPantalla(PuntoCad mundo)
    {
        double pxX = (mundo.X * Escala + OffsetX) * PxPorMetro;
        double pxY = (MaxYAjustada - (mundo.Y * Escala + OffsetY)) * PxPorMetro;
        return new Point(pxX * ScaleX + Tx, pxY * ScaleY + Ty);
    }

    /// <summary>Pantalla (px del lienzo) → mundo (m, sistema DXF). Inverso exacto de <see cref="MundoAPantalla"/>.</summary>
    public PuntoCad PantallaAMundo(Point pantalla)
    {
        double pxX = (pantalla.X - Tx) / ScaleX;
        double pxY = (pantalla.Y - Ty) / ScaleY;
        double mundoX = (pxX / PxPorMetro - OffsetX) / Escala;
        double mundoY = (MaxYAjustada - pxY / PxPorMetro - OffsetY) / Escala;
        return new PuntoCad(mundoX, mundoY);
    }
}
