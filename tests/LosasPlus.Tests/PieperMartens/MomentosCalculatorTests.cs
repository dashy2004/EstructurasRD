using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// Fase 1 (Momentos) del motor nativo Pieper-Martens. Los valores esperados son
/// los de <c>Losas.exe</c> (F. Perdomo) para el sistema RESTAURANTE 2, ver
/// <c>tests/fixtures/restaurante_2.TXT</c> y el spec
/// <c>docs/superpowers/specs/2026-06-03-motor-pieper-martens-nativo-design.md</c> §5.
///
/// Tolerancia 0.01 ton·m/m: los coeficientes S de la tabla están publicados a 2
/// decimales, lo que introduce ~0.007 de diferencia en Msx al interpolar.
/// </summary>
public class MomentosCalculatorTests
{
    private static readonly MomentosCalculator Calc = new(TablaPieperMartens.Cargar());

    private static Losa Losa(int tipo, double w, double lx, double ly) =>
        new() { Tipo = tipo, Carga = w, Lx = lx, Ly = ly, Espesor = 0.20, Rec = 0.025 };

    [Theory]
    //         tipo    w      Lx     Ly      Mfx     Mfy     Msx     Msy
    [InlineData(40, 0.720, 6.850, 6.650, 1.280, 1.358, 1.987, 2.108)] // losas 1 y 2
    [InlineData(40, 0.720, 6.850, 6.450, 1.199, 1.352, 1.859, 2.096)] // losas 3 y 4
    public void Momentos_Tipo40_coinciden_con_LosasExe(
        int tipo, double w, double lx, double ly,
        double mfx, double mfy, double msx, double msy)
    {
        var m = Calc.Calcular(Losa(tipo, w, lx, ly));

        Assert.Equal(mfx, m.Mfx, 0.01);
        Assert.Equal(mfy, m.Mfy, 0.01);
        Assert.Equal(msx, m.Msx, 0.01);
        Assert.Equal(msy, m.Msy, 0.01);
    }

    /// <summary>
    /// Losa 5 de RESTAURANTE 2: Tipo 71 (voladizo/franja en una dirección,
    /// 13.65×1.20). No sale de las tablas two-way: vuela como ménsula sobre Ly,
    /// Msy = q·Ly²/2 = 0.518; el resto de momentos es 0.
    /// </summary>
    [Fact]
    public void Momentos_Tipo71_voladizo_solo_Msy()
    {
        var m = Calc.Calcular(Losa(71, 0.720, 13.650, 1.200));

        Assert.Equal(0.000, m.Mfx, 0.01);
        Assert.Equal(0.000, m.Mfy, 0.01);
        Assert.Equal(0.000, m.Msx, 0.01);
        Assert.Equal(0.518, m.Msy, 0.01);
    }
}
