using LosasPlus.Models.Cad;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// F2 C4 — bounding box REAL de arcos: un arco parcial ya no infla el encuadre
/// del plano con el círculo circunscrito completo (DxfImportService.cs:269);
/// solo cuenta sus extremos y los puntos cardinales dentro del barrido.
/// </summary>
public class ArcoCadBoundingBoxTests
{
    private static ArcoCad Arco(double cx, double cy, double r, double a0, double a1) =>
        new() { Centro = new PuntoCad(cx, cy), Radio = r, AnguloInicioGrados = a0, AnguloFinGrados = a1 };

    [Fact]
    public void Arco_de_90_grados_solo_cubre_su_cuadrante()
    {
        // 0°→90° centrado en (10,10) r=5: de (15,10) a (10,15) pasando por el NE.
        var (min, max) = Arco(10, 10, 5, 0, 90).BoundingBox();
        Assert.Equal(10.0, min.X, 6);
        Assert.Equal(10.0, min.Y, 6);
        Assert.Equal(15.0, max.X, 6);
        Assert.Equal(15.0, max.Y, 6);
    }

    [Fact]
    public void Circulo_completo_cubre_todo_el_circunscrito()
    {
        var (min, max) = Arco(10, 10, 5, 0, 360).BoundingBox();
        Assert.Equal(5.0, min.X, 6);
        Assert.Equal(5.0, min.Y, 6);
        Assert.Equal(15.0, max.X, 6);
        Assert.Equal(15.0, max.Y, 6);
    }

    [Fact]
    public void Arco_que_cruza_el_cardinal_180_lo_incluye()
    {
        // 135°→225° (cruza 180°): el extremo izquierdo (cx−r) está en el barrido.
        var (min, max) = Arco(0, 0, 2, 135, 225).BoundingBox();
        Assert.Equal(-2.0, min.X, 6);                 // cardinal 180° incluido
        Assert.Equal(-1.4142135624, min.Y, 6);        // extremo a 225°
        Assert.Equal(-1.4142135624, max.X, 6);        // extremos a 135°/225°
        Assert.Equal(1.4142135624, max.Y, 6);         // extremo a 135°
    }
}
