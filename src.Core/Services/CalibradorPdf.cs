using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Homotecia de calibración del underlay PDF (UI1.2 — un solo lienzo): dado el
/// gesto de dos puntos (P₁ pivote + distancia trazada en el lienzo) y la
/// distancia real que introduce el usuario, corrige
/// <see cref="PdfReferencia.Escala"/> y los offsets conservando P₁ fijo:
/// <code>
///   f        = DistanciaReal / DistanciaActual
///   Offset'  = P₁ − (P₁ − Offset) · f
///   Escala'  = Escala · f
/// </code>
/// Cualquier punto interno del PDF que caía en P₁ antes sigue cayendo en P₁
/// después. Compartida por el editor CAD (<c>CadEditorViewModel</c>) y por
/// Planta 2D — un solo camino de aplicación para ambos lienzos.
/// </summary>
public static class CalibradorPdf
{
    /// <summary>Distancia mínima para considerar válido el gesto/dato (m).</summary>
    public const double DistanciaMinima = 1e-9;

    /// <summary>
    /// Aplica la calibración sobre <paramref name="pdf"/> <b>in situ</b>.
    /// Devuelve el factor aplicado, o <c>null</c> si los argumentos son
    /// inválidos — en ese caso no muta nada.
    /// </summary>
    public static double? Calibrar(PdfReferencia? pdf, CalibracionPdfArgs? args)
    {
        if (pdf is null || args is null) return null;
        if (args.DistanciaActual <= DistanciaMinima || args.DistanciaReal <= DistanciaMinima)
            return null;

        double factor = args.DistanciaReal / args.DistanciaActual;
        pdf.OffsetX = args.PivoteX - (args.PivoteX - pdf.OffsetX) * factor;
        pdf.OffsetY = args.PivoteY - (args.PivoteY - pdf.OffsetY) * factor;
        pdf.Escala *= factor;
        return factor;
    }
}
