using System;
using System.IO;
using System.Linq;
using LosasPlus.Calculo.PieperMartens;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.PieperMartens;

/// <summary>
/// Valida el motor nativo contra el listado real de <c>Losas.exe</c> del sistema
/// de demostración de 27 losas, que trae los tipos 10, 21, 22, 31, 32, 40, 51,
/// 52 y 60, y losas en una dirección (ε = Ly/Lx fuera de 0.50–2.00).
/// Lo esperado se lee del <c>.TXT</c>; el sistema, del <c>.DL</c>.
/// </summary>
public class SistemaDemo27LosasTests
{
    /// <summary>Los factores tabulados tienen 2 decimales; el error observado máximo es 0.015 ton·m/m.</summary>
    private const double Tolerancia = 0.02;

    private static string Fixture(string nombre)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", nombre);
        Assert.True(File.Exists(path), $"Fixture no encontrada: {path}");
        return path;
    }

    private static readonly Sistema Sistema = DLFileService.Read(Fixture("sistema_demo_27_losas.DL"));
    private static readonly ParsedOutput Esperado = TxtParser.ParseFile(Fixture("sistema_demo_27_losas.TXT"));
    private static readonly SalidaPerdomo Salida = SistemaPieperMartensCalculator.Crear().Calcular(Sistema);

    [Fact]
    public void Las_27_losas_se_calculan_sin_dejar_ninguna_fuera()
    {
        Assert.Equal(27, Salida.Momentos.Count);
        Assert.Empty(Salida.LosasNoParseadas);
    }

    [Fact]
    public void Los_momentos_de_todas_las_losas_reproducen_el_listado_de_Losas_exe()
    {
        foreach (var e in Esperado.PorLosa)
        {
            var m = Salida.Momentos.Single(x => x.LosaId == e.Id);
            Assert.True(Math.Abs(m.Mfx - e.Mfx!.Value) <= Tolerancia, $"Losa {e.Id} (tipo {e.Tipo}) Mfx: {m.Mfx:F3} vs {e.Mfx:F3}");
            Assert.True(Math.Abs(m.Mfy - e.Mfy!.Value) <= Tolerancia, $"Losa {e.Id} (tipo {e.Tipo}) Mfy: {m.Mfy:F3} vs {e.Mfy:F3}");
            Assert.True(Math.Abs(m.NMSx - e.MSx!.Value) <= Tolerancia, $"Losa {e.Id} (tipo {e.Tipo}) Msx: {m.NMSx:F3} vs {e.MSx:F3}");
            Assert.True(Math.Abs(m.NMSy - e.MSy!.Value) <= Tolerancia, $"Losa {e.Id} (tipo {e.Tipo}) Msy: {m.NMSy:F3} vs {e.MSy:F3}");
        }
    }

    [Fact]
    public void Los_momentos_de_borde_balanceados_reproducen_el_listado_de_Losas_exe()
    {
        Assert.Equal(22, Esperado.Apoyos.Count);
        foreach (var e in Esperado.Apoyos)
        {
            var lista = e.Direccion == "X" ? Salida.ArmadurasXApoyos : Salida.ArmadurasYApoyos;
            var a = lista.Single(x => x.BordeI == e.BordeI && x.BordeJ == e.BordeJ);
            Assert.True(Math.Abs(a.MuIJ - e.MuIJ!.Value) <= Tolerancia,
                $"Borde {e.Direccion} {e.BordeI}-{e.BordeJ}: {a.MuIJ:F3} vs {e.MuIJ:F3}");
        }
    }

    [Theory]
    [InlineData(10, 2.0, 4.1, 0)]   // apoyos simples: q·L²/8
    [InlineData(22, 2.0, 4.1, 1)]   // un extremo continuo: q·L²/10.24 en vano, q·L²/8 en apoyo
    [InlineData(32, 2.0, 4.1, 2)]   // dos extremos continuos: q·L²/12 en vano y en apoyo
    public void Una_losa_en_una_direccion_usa_la_luz_corta_y_la_continuidad_de_sus_extremos(
        int tipo, double lx, double ly, int extremosContinuos)
    {
        var calc = new MomentosCalculator(TablaPieperMartens.Cargar());
        var losa = new Losa { Id = 1, Tipo = tipo, Carga = 1.0, Espesor = 0.15, Lx = lx, Ly = ly, Rec = 0.025 };
        double ql2 = 1.0 * lx * lx;
        double[] vano = { 8.0, 10.24, 12.0 };
        double[] apoyo = { 0.0, 1.0 / 8.0, 1.0 / 12.0 };

        var m = calc.Calcular(losa);

        Assert.Equal(ql2 / vano[extremosContinuos], m.Mfx, 3);
        Assert.Equal(ql2 * apoyo[extremosContinuos], m.Msx, 3);
        Assert.Equal(0.0, m.Mfy, 3);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(51)]
    [InlineData(52)]
    [InlineData(60)]
    public void Una_direccion_con_borde_continuo_en_ambos_sentidos_no_esta_verificada_y_se_rechaza(int tipo)
    {
        var calc = new MomentosCalculator(TablaPieperMartens.Cargar());
        var losa = new Losa { Id = 1, Tipo = tipo, Carga = 1.0, Espesor = 0.15, Lx = 2.0, Ly = 4.5, Rec = 0.025 };

        var ex = Assert.Throws<NotSupportedException>(() => calc.Calcular(losa));

        Assert.Contains("una dirección", ex.Message);
    }

    [Fact]
    public void Una_proporcion_que_cae_en_el_limite_por_redondeo_se_calcula_con_la_tabla()
    {
        // 2.6 / 1.3 da 2.0000000000000004 en doble precisión: sigue siendo ε = 2.00.
        var calc = new MomentosCalculator(TablaPieperMartens.Cargar());
        var losa = new Losa { Id = 1, Tipo = 60, Carga = 1.0, Espesor = 0.15, Lx = 1.3, Ly = 2.6, Rec = 0.025 };

        var m = calc.Calcular(losa);

        Assert.True(m.Mfx > 0 && m.Mfy > 0);
    }

    [Fact]
    public void Una_losa_con_luz_nula_no_aborta_el_sistema_y_queda_reportada()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 60, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 5.0, Rec = 0.025 });
        s.Losas.Add(new Losa { Id = 2, Tipo = 60, Carga = 1.0, Espesor = 0.20, Lx = 0.0, Ly = 5.0, Rec = 0.025 });

        var salida = SistemaPieperMartensCalculator.Crear().Calcular(s);

        Assert.Single(salida.Momentos);
        Assert.Equal(new[] { 2 }, salida.LosasNoParseadas);
    }

    [Fact]
    public void Una_losa_que_no_se_pudo_calcular_no_conserva_momentos_de_un_calculo_anterior()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 60, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 5.0, Rec = 0.025 });
        var sinTabla = new Losa { Id = 2, Tipo = 13, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 5.0, Rec = 0.025,
                                  Mfx = 9.9, Mfy = 9.9, MSx = 9.9, MSy = 9.9 };
        s.Losas.Add(sinTabla);

        SistemaPieperMartensCalculator.Crear().CalcularYAplicar(s);

        Assert.Null(sinTabla.Mfx);
        Assert.Null(sinTabla.Mfy);
        Assert.Null(sinTabla.MSx);
        Assert.Null(sinTabla.MSy);
    }

    [Fact]
    public void Un_tipo_sin_tabla_verificada_no_aborta_el_sistema_y_queda_reportado()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 60, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 5.0, Rec = 0.025 });
        s.Losas.Add(new Losa { Id = 2, Tipo = 13, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 5.0, Rec = 0.025 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S" });

        var salida = SistemaPieperMartensCalculator.Crear().Calcular(s);

        Assert.Single(salida.Momentos);
        Assert.Equal(new[] { 2 }, salida.LosasNoParseadas);
        Assert.Empty(salida.ArmadurasXApoyos);
    }
}
