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
}
