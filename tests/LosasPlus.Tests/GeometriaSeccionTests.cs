using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class GeometriaSeccionTests
{
    [Fact]
    public void Rectangular_calcula_A_Iy_Iz_J_de_una_seccion_0_30x0_50()
    {
        var p = GeometriaSeccion.Rectangular(0.30, 0.50); // b=0.30, h=0.50

        Assert.Equal(0.15, p.Area, 9);
        Assert.Equal(0.003125, p.InerciaZ, 9);  // b·h³/12
        Assert.Equal(0.001125, p.InerciaY, 9);  // h·b³/12
        Assert.Equal(0.002817, p.ConstanteTorsion, 6);
    }

    [Fact]
    public void Rectangular_seccion_cuadrada_0_30_coincide_con_el_ejemplo_del_motor()
    {
        var p = GeometriaSeccion.Rectangular(0.30, 0.30);
        Assert.Equal(0.09, p.Area, 9);
        Assert.Equal(0.000675, p.InerciaZ, 9);
        Assert.Equal(0.000675, p.InerciaY, 9);
        Assert.Equal(0.001141, p.ConstanteTorsion, 6);
    }
}
