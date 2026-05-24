using LosasPlus.Grillas;
using Xunit;

namespace LosasPlus.Tests.Topologia;

/// <summary>
/// Tests del dominio puro <see cref="GrillaEstructural"/> +
/// <see cref="EjeNombrado"/> (Módulo 2 Parte A Fase 3D-II).
/// </summary>
public class GrillaEstructuralTests
{
    [Fact]
    public void Default_TieneTresEjesXYTresEjesY_AEspaciamientoSeisMetros()
    {
        var g = GrillaEstructural.Default();

        Assert.Equal(3, g.EjesX.Count);
        Assert.Equal(3, g.EjesY.Count);

        // EjesX: A=0, B=6, C=12.
        Assert.Equal("A", g.EjesX[0].Nombre); Assert.Equal( 0.0, g.EjesX[0].PosicionMetros);
        Assert.Equal("B", g.EjesX[1].Nombre); Assert.Equal( 6.0, g.EjesX[1].PosicionMetros);
        Assert.Equal("C", g.EjesX[2].Nombre); Assert.Equal(12.0, g.EjesX[2].PosicionMetros);

        // EjesY: 1=0, 2=6, 3=12.
        Assert.Equal("1", g.EjesY[0].Nombre); Assert.Equal( 0.0, g.EjesY[0].PosicionMetros);
        Assert.Equal("2", g.EjesY[1].Nombre); Assert.Equal( 6.0, g.EjesY[1].PosicionMetros);
        Assert.Equal("3", g.EjesY[2].Nombre); Assert.Equal(12.0, g.EjesY[2].PosicionMetros);
    }

    [Fact]
    public void EstaVacia_GrillaSinEjes_DevuelveTrue()
    {
        var g = new GrillaEstructural(
            new System.Collections.Generic.List<EjeNombrado>(),
            new System.Collections.Generic.List<EjeNombrado>());

        Assert.True(g.EstaVacia);
    }

    [Fact]
    public void EstaVacia_GrillaDefault_DevuelveFalse()
    {
        var g = GrillaEstructural.Default();

        Assert.False(g.EstaVacia);
    }

    [Fact]
    public void EjeNombrado_IgualdadPorValor_ReglaRecordStruct()
    {
        var a1 = new EjeNombrado("A", 0.0);
        var a2 = new EjeNombrado("A", 0.0);
        var b  = new EjeNombrado("B", 6.0);

        // record struct → igualdad estructural por valor (no por referencia).
        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.True(a1 == a2);
    }
}
