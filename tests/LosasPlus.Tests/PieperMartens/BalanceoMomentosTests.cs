using System.Collections.Generic;
using LosasPlus.Calculo.PieperMartens;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// Fase 2 (Balanceo) del motor nativo Pieper-Martens. Valores esperados = bloque
/// "ARMADURAS … SOBRE LOS APOYOS" de <c>tests/fixtures/restaurante_2.TXT</c>
/// (Losas.exe, F. Perdomo).
/// </summary>
public class BalanceoMomentosTests
{
    // Momentos de empotramiento (Msx, Msy) de cada losa de RESTAURANTE 2.
    // (Mfx/Mfy no intervienen en el balanceo de apoyos.)
    private static readonly IReadOnlyDictionary<int, MomentosLosa> Ms = new Dictionary<int, MomentosLosa>
    {
        [1] = new(0, 0, 1.987, 2.108),
        [2] = new(0, 0, 1.987, 2.108),
        [3] = new(0, 0, 1.859, 2.096),
        [4] = new(0, 0, 1.859, 2.096),
        [5] = new(0, 0, 0.000, 0.518),
    };

    private static readonly BalanceoMomentos Bal = new();

    [Theory]
    //          I  J  dir                 modo   MuI    MuJ    MuI-J
    [InlineData(1, 2, DireccionApoyo.X, "S", 1.987, 1.987, 1.987)] // X 1-2
    [InlineData(2, 4, DireccionApoyo.X, "S", 1.987, 1.859, 1.923)] // X 2-4
    [InlineData(1, 3, DireccionApoyo.Y, "S", 2.108, 2.096, 2.102)] // Y 1-3
    [InlineData(2, 4, DireccionApoyo.Y, "S", 2.108, 2.096, 2.102)] // Y 2-4
    [InlineData(5, 2, DireccionApoyo.Y, "N", 0.518, 2.108, 0.518)] // Y 5-2 (vuelo)
    [InlineData(5, 1, DireccionApoyo.Y, "N", 0.518, 2.108, 0.518)] // Y 5-1 (vuelo)
    public void Balanceo_coincide_con_LosasExe(
        int i, int j, DireccionApoyo dir, string modo,
        double muI, double muJ, double muIJ)
    {
        var r = Bal.Balancear(i, j, dir, modo, Ms);

        Assert.Equal(muI, r.MuI, 0.01);
        Assert.Equal(muJ, r.MuJ, 0.01);
        Assert.Equal(muIJ, r.MuIJ, 0.01);
    }
}
