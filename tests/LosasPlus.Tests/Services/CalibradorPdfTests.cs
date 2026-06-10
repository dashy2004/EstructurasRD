using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="CalibradorPdf"/> — la homotecia de calibración del
/// underlay PDF (UI1.2): corrige <see cref="PdfReferencia.Escala"/> y los
/// offsets conservando el pivote P₁ fijo en el lienzo. Extraída de
/// <c>CadEditorViewModel.AplicarCalibrarPdf</c> para compartirla con Planta 2D.
/// </summary>
public class CalibradorPdfTests
{
    private static PdfReferencia Pdf(double escala = 1.0, double ox = 0, double oy = 0)
        => new() { Escala = escala, OffsetX = ox, OffsetY = oy };

    [Fact]
    public void Factor_2_duplica_la_escala_y_corrige_los_offsets()
    {
        var pdf = Pdf();
        var args = new CalibracionPdfArgs(PivoteX: 2, PivoteY: 3, DistanciaActual: 1, DistanciaReal: 2);

        double? factor = CalibradorPdf.Calibrar(pdf, args);

        Assert.Equal(2.0, factor!.Value, 9);
        Assert.Equal(2.0, pdf.Escala, 9);
        Assert.Equal(2 - (2 - 0) * 2.0, pdf.OffsetX, 9);   // = -2
        Assert.Equal(3 - (3 - 0) * 2.0, pdf.OffsetY, 9);   // = -3
    }

    [Fact]
    public void El_pivote_queda_invariante_en_el_lienzo()
    {
        // Un punto interno del PDF que caía en el pivote antes de calibrar
        // debe seguir cayendo exactamente ahí después (homotecia con centro P₁).
        var pdf = Pdf(escala: 0.5, ox: 1.0, oy: 2.0);
        double pivoteX = 4.0, pivoteY = 5.0;
        double internoX = (pivoteX - pdf.OffsetX) / pdf.Escala;   // coord interna que cae en P₁
        double internoY = (pivoteY - pdf.OffsetY) / pdf.Escala;

        CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(pivoteX, pivoteY, 2.0, 7.0));

        Assert.Equal(pivoteX, pdf.OffsetX + internoX * pdf.Escala, 9);
        Assert.Equal(pivoteY, pdf.OffsetY + internoY * pdf.Escala, 9);
    }

    [Fact]
    public void Distancias_invalidas_devuelven_null_sin_mutar_el_pdf()
    {
        var pdf = Pdf(escala: 1.5, ox: 3, oy: 4);

        Assert.Null(CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(0, 0, 0.0, 5.0)));
        Assert.Null(CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(0, 0, 5.0, 0.0)));
        Assert.Null(CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(0, 0, -1.0, 5.0)));

        Assert.Equal(1.5, pdf.Escala, 9);
        Assert.Equal(3.0, pdf.OffsetX, 9);
        Assert.Equal(4.0, pdf.OffsetY, 9);
    }

    [Fact]
    public void Pdf_o_args_nulos_devuelven_null()
    {
        Assert.Null(CalibradorPdf.Calibrar(null, new CalibracionPdfArgs(0, 0, 1, 2)));
        Assert.Null(CalibradorPdf.Calibrar(Pdf(), null));
    }

    [Fact]
    public void Dos_calibraciones_inversas_se_componen_a_la_identidad()
    {
        var pdf = Pdf(escala: 1.0, ox: 0.7, oy: -1.3);
        var pivote = (X: 5.0, Y: 6.0);

        CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(pivote.X, pivote.Y, 1.0, 2.0));
        CalibradorPdf.Calibrar(pdf, new CalibracionPdfArgs(pivote.X, pivote.Y, 2.0, 1.0));

        Assert.Equal(1.0, pdf.Escala, 9);
        Assert.Equal(0.7, pdf.OffsetX, 9);
        Assert.Equal(-1.3, pdf.OffsetY, 9);
    }
}
