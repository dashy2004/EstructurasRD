using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del servicio puro de geometría de bordes de continuidad (UI1.8).
/// </summary>
public class BordesPlantaServiceTests
{
    private static Losa L(int id, double x, double y, double lx = 4, double ly = 3, int tipo = 0)
        => new Losa { Id = id, CoordenadaX = x, CoordenadaY = y, Lx = lx, Ly = ly, Tipo = tipo };

    [Fact]
    public void EjeInferido_lado_a_lado_horizontal_es_X()
    {
        var a = L(1, 0, 0);
        var b = L(2, 4, 0);                       // a la derecha de a
        Assert.Equal(EjeBorde.X, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void EjeInferido_apiladas_vertical_es_Y()
    {
        var a = L(1, 0, 0);
        var b = L(2, 0, 3);                        // encima/debajo de a
        Assert.Equal(EjeBorde.Y, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void EjeInferido_empate_resuelve_a_X()
    {
        var a = L(1, 0, 0, lx: 2, ly: 2);
        var b = L(2, 2, 2, lx: 2, ly: 2);         // |Δx| == |Δy|
        Assert.Equal(EjeBorde.X, BordesPlantaService.EjeInferido(a, b));
    }

    [Fact]
    public void SegmentoCompartido_contacto_pleno_horizontal_devuelve_cara_vertical_eje_X()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 4, 0, lx: 4, ly: 3);         // b pegada a la derecha de a
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.X, seg!.Value.Eje);
        Assert.Equal(4, seg.Value.X0, 3);          // cara en x = 4
        Assert.Equal(4, seg.Value.X1, 3);
        Assert.Equal(0, seg.Value.Y0, 3);          // solape y = [0,3]
        Assert.Equal(3, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_contacto_pleno_vertical_devuelve_cara_horizontal_eje_Y()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 0, 3, lx: 4, ly: 3);         // b pegada encima de a
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.Y, seg!.Value.Eje);
        Assert.Equal(3, seg.Value.Y0, 3);          // cara en y = 3
        Assert.Equal(3, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_contacto_parcial_recorta_el_solape()
    {
        var a = L(1, 0, 0, lx: 4, ly: 4);
        var b = L(2, 4, 2, lx: 4, ly: 4);         // desfase vertical: solape y = [2,4]
        var seg = BordesPlantaService.SegmentoCompartido(a, b);
        Assert.NotNull(seg);
        Assert.Equal(EjeBorde.X, seg!.Value.Eje);
        Assert.Equal(2, seg.Value.Y0, 3);
        Assert.Equal(4, seg.Value.Y1, 3);
    }

    [Fact]
    public void SegmentoCompartido_con_holgura_mayor_que_tol_devuelve_null()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 5, 0, lx: 4, ly: 3);         // 1 m de hueco
        Assert.Null(BordesPlantaService.SegmentoCompartido(a, b));
    }

    [Fact]
    public void SegmentoCompartido_disjuntas_devuelve_null()
    {
        var a = L(1, 0, 0, lx: 4, ly: 3);
        var b = L(2, 20, 20, lx: 4, ly: 3);
        Assert.Null(BordesPlantaService.SegmentoCompartido(a, b));
    }
}
