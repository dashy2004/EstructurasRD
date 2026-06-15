using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MapeadorLosaMotorTests
{
    private static (Sistema sis, Losa losa) Demo()
    {
        var sis = new Sistema { Fc = 0.210, Fy = 4.200 }; // ton/cm²
        var losa = new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 };
        sis.Losas.Add(losa);
        return (sis, losa);
    }

    [Fact]
    public void Mapea_geometria_y_malla()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "simple");
        Assert.Equal(5.0, p.A, 6);
        Assert.Equal(5.0, p.B, 6);
        Assert.Equal(0.20, p.T, 6);
        Assert.Equal(8, p.Nx);
        Assert.Equal(8, p.Ny);
        Assert.Equal(0.2, p.Nu, 6);
        Assert.Equal("simple", p.Borde);
    }

    [Fact]
    public void Convierte_unidades_app_a_SI()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "empotrado");
        Assert.Equal(25.0, p.Recubrimiento, 3);          // 0.025 m → mm
        Assert.Equal(9806.65, p.Q, 2);                   // 1.0 ton/m² → N/m²
        Assert.Equal(20.594, p.Fc, 3);                   // 0.210 ton/cm² → MPa
        Assert.Equal(411.879, p.Fy, 3);                  // 4.200 ton/cm² → MPa
        Assert.Equal("empotrado", p.Borde);
    }

    [Fact]
    public void Deriva_E_por_ACI_en_Pa()
    {
        var (sis, losa) = Demo();
        var p = MapeadorLosaMotor.Map(losa, sis, "simple");
        // E = 4700·√(20.594) · 1e6 ≈ 2.1329e10 Pa
        Assert.True(System.Math.Abs(p.E - 2.1329e10) < 1e7, $"E fuera de tolerancia: {p.E}");
    }
}
