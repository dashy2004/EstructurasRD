using System.Collections.Generic;
using LosasPlus.Calculo;
using Xunit;

namespace LosasPlus.Tests.Calculo;

/// <summary>
/// Tests del <see cref="ColumnaDisenador"/> (diseño de columnas a flexo-compresión,
/// ACI 318-19, SI). Ciclo 1: sección, áreas y cuantía.
/// </summary>
public class ColumnaDisenadorTests
{
    /// <summary>Sección 400×400 mm, f'c 28 MPa, fy 420 MPa, 8 barras Ø20 (314 mm² c/u).</summary>
    private static ColumnaSeccion Seccion400x400_8Ø20()
    {
        const double a = 314.159;            // mm² de una Ø20
        double d = 400.0 / 2 - 40;           // 160 mm: borde de barra desde el centro (rec 40)
        var barras = new List<BarraLong>
        {
            new(-d, -d, a), new(0, -d, a), new(d, -d, a),
            new(-d,  0, a),                  new(d,  0, a),
            new(-d,  d, a), new(0,  d, a), new(d,  d, a),
        };
        return new ColumnaSeccion(400, 400, 28, 420, barras);
    }

    [Theory]
    [InlineData(21.0, 0.85)]   // f'c ≤ 28 → 0.85
    [InlineData(28.0, 0.85)]   // límite → 0.85
    [InlineData(35.0, 0.80)]   // 28+7 → 0.85-0.05
    [InlineData(42.0, 0.75)]   // 28+14 → 0.85-0.10
    [InlineData(56.0, 0.65)]   // 28+28 → 0.65 (piso)
    [InlineData(70.0, 0.65)]   // clamp al piso
    public void Beta1_sigue_ACI_318_19(double fc, double esperado)
        => Assert.Equal(esperado, ColumnaDisenador.Beta1(fc), 3);

    [Fact]
    public void Seccion_calcula_Ag_Ast_y_cuantia()
    {
        var s = Seccion400x400_8Ø20();
        Assert.Equal(160_000.0, s.Ag, 1);            // 400×400
        Assert.Equal(8 * 314.159, s.Ast, 1);         // 8 Ø20
        Assert.Equal(s.Ast / s.Ag, s.RhoG, 6);       // ρg = Ast/Ag (~0.0157)
    }

    [Fact]
    public void CumpleCuantia_dentro_de_1_a_8_por_ciento()
    {
        var s = Seccion400x400_8Ø20();
        Assert.True(ColumnaDisenador.CumpleCuantia(s));    // ρg ≈ 1.57% → OK
    }

    [Fact]
    public void CumpleCuantia_falla_si_muy_poco_acero()
    {
        // 400×400 con 2 Ø10 (78.5 mm² c/u) → ρg ≈ 0.1% < 1%.
        var barras = new List<BarraLong> { new(-160, 0, 78.54), new(160, 0, 78.54) };
        var s = new ColumnaSeccion(400, 400, 28, 420, barras);
        Assert.False(ColumnaDisenador.CumpleCuantia(s));
    }

    [Fact]
    public void CentroidePlastico_simetrico_es_el_centro()
    {
        var s = Seccion400x400_8Ø20();
        var (x, y) = ColumnaDisenador.CentroidePlastico(s);
        Assert.Equal(0.0, x, 3);
        Assert.Equal(0.0, y, 3);
    }

    [Fact]
    public void CentroidePlastico_se_corre_hacia_el_lado_con_mas_acero()
    {
        // Más acero arriba (y > 0) → el centroide plástico se corre hacia arriba.
        var barras = new List<BarraLong>
        {
            new(0,  160, 1000), new(120, 160, 1000),  // mucho acero arriba
            new(0, -160,  100),                        // poco abajo
        };
        var s = new ColumnaSeccion(400, 400, 28, 420, barras);
        var (_, y) = ColumnaDisenador.CentroidePlastico(s);
        Assert.True(y > 0, $"esperaba y>0, fue {y}");
    }

    [Fact]
    public void Po_squash_load_ACI_22_4_2_2()
    {
        var s = Seccion400x400_8Ø20();
        double ag = 400.0 * 400, ast = 8 * 314.159;
        double esperado = 0.85 * 28 * (ag - ast) + 420 * ast;   // ≈ 4.80 MN
        Assert.Equal(esperado, ColumnaDisenador.Po(s), 1);
        Assert.True(ColumnaDisenador.Po(s) > 4.7e6 && ColumnaDisenador.Po(s) < 4.9e6);
    }

    [Fact]
    public void PhiPnMax_es_052_de_Po_para_estribos()
    {
        var s = Seccion400x400_8Ø20();
        Assert.Equal(0.65 * 0.80 * ColumnaDisenador.Po(s), ColumnaDisenador.PhiPnMax(s), 1);
    }

    [Theory]
    [InlineData(0.0010, 0.650)]   // εt < εy → compresión controlada
    [InlineData(0.0021, 0.650)]   // εt = εy → piso
    [InlineData(0.0036, 0.775)]   // transición: 0.65 + 0.25·(0.0015/0.003)
    [InlineData(0.0060, 0.900)]   // εt ≥ εy+0.003 → tracción controlada
    public void PhiPorDeformacion_sigue_Tabla_21_2_2(double et, double esperado)
        => Assert.Equal(esperado, ColumnaDisenador.PhiPorDeformacion(et, 420), 3);

    [Fact]
    public void PuntoInteraccion_a_c200_valor_calculado_a_mano()
    {
        // 400×400, f'c 28, fy 420, 8Ø20 (3 arriba d=40, 2 medio d=200, 3 abajo d=360).
        // c=200 → a=170, Cc=1 618 400 N; capas en fluencia ±420.
        var p = ColumnaDisenador.PuntoInteraccion(Seccion400x400_8Ø20(), 200.0);
        Assert.True(Math.Abs(p.Pn - 1_595_969.0) < 500, $"Pn={p.Pn}");
        Assert.True(Math.Abs(p.Mn - 309_195_968.0) < 5e5, $"Mn={p.Mn}");
        Assert.Equal(0.0024, p.Et, 5);
        Assert.Equal(0.675, p.Phi, 3);
    }

    [Fact]
    public void PuntoInteraccion_en_c_balanceada_da_et_igual_a_ey()
    {
        var s = Seccion400x400_8Ø20();
        double dMax = 360.0;                                   // capa extrema en tracción
        double cb = ColumnaDisenador.ProfundidadBalanceada(dMax, 420);
        var p = ColumnaDisenador.PuntoInteraccion(s, cb);
        Assert.Equal(420.0 / 200_000.0, p.Et, 5);             // εt ≈ εy
    }

    [Fact]
    public void Pn_crece_con_c_monotono()
    {
        var s = Seccion400x400_8Ø20();
        Assert.True(ColumnaDisenador.PuntoInteraccion(s, 300).Pn
                  > ColumnaDisenador.PuntoInteraccion(s, 150).Pn);
    }

    [Fact]
    public void DiagramaInteraccion_barre_de_traccion_a_compresion()
    {
        var s = Seccion400x400_8Ø20();
        var d = ColumnaDisenador.DiagramaInteraccion(s, 30);
        Assert.Equal(30, d.Count);
        Assert.True(d[0].Pn < d[^1].Pn);       // monótono: más c → más axial
        Assert.True(d[0].Et > 0);              // arranca en tracción (c chico)
        Assert.True(d[^1].Et < d[0].Et);       // termina en compresión (εt menor)
    }

    [Theory]
    [InlineData(3, 9.53)]
    [InlineData(8, 25.40)]
    [InlineData(11, 35.81)]
    public void DiametroBarraMm_ASTM_A615(int n, double mm)
        => Assert.Equal(mm, ColumnaDisenador.DiametroBarraMm(n), 2);

    [Fact]
    public void EstriboColumna_usa_3_para_long_hasta_10_y_4_desde_11()
    {
        var s = new ColumnaSeccion(400, 400, 28, 420, new List<BarraLong>());
        Assert.Equal(3, ColumnaDisenador.EstriboColumna(s, 8).Numero);   // long #8 ≤ #10 → #3
        Assert.Equal(4, ColumnaDisenador.EstriboColumna(s, 11).Numero);  // long #11 ≥ #11 → #4
    }

    [Fact]
    public void EstriboColumna_separacion_es_el_minimo_de_los_tres_criterios()
    {
        var s = new ColumnaSeccion(400, 400, 28, 420, new List<BarraLong>());
        // long #5 (db 15.88): 16·15.88=254.1 gobierna sobre 48·9.53=457 y minDim=400.
        Assert.Equal(254.08, ColumnaDisenador.EstriboColumna(s, 5).SeparacionMm, 1);
        // long #8 (db 25.40): 16·25.40=406.4 > minDim=400 → gobierna la dimensión.
        Assert.Equal(400.0, ColumnaDisenador.EstriboColumna(s, 8).SeparacionMm, 1);
    }

    [Fact]
    public void ChequearDemanda_punto_interior_cumple()
    {
        // Pu≈1000 kN, Mu≈50 kN·m: bien dentro del diagrama del 400×400 8Ø20.
        var r = ColumnaDisenador.ChequearDemanda(Seccion400x400_8Ø20(), 1_000_000, 50e6);
        Assert.True(r.Cumple);
        Assert.True(r.Ratio < 1.0);
        Assert.True(r.PhiMnCapacidadNmm > 0);
    }

    [Fact]
    public void ChequearDemanda_momento_excesivo_no_cumple()
    {
        // Mismo axial, Mu≈400 kN·m: supera la capacidad a momento → ratio > 1.
        var r = ColumnaDisenador.ChequearDemanda(Seccion400x400_8Ø20(), 1_000_000, 400e6);
        Assert.False(r.Cumple);
        Assert.True(r.Ratio > 1.0);
    }

    [Fact]
    public void ChequearDemanda_sobrecarga_axial_no_cumple()
    {
        // Pu = 3000 kN > φPn,max (~2498 kN) → no cumple aunque Mu sea chico.
        var r = ColumnaDisenador.ChequearDemanda(Seccion400x400_8Ø20(), 3_000_000, 10e6);
        Assert.False(r.Cumple);
        Assert.True(3_000_000 > r.PhiPnMaxN);
    }
}
