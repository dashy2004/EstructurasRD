using LosasPlus.Calculo;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="CalculoEngine"/> validando cada función pura por separado
/// y el pipeline orquestador completo, contra valores extraídos de la hoja
/// <c>Carga EARLLETTE</c> del libro <c>cargas_estructurales_demo.xlsx</c>.
///
/// <para>
/// Goldens documentados (proyecto Neapolis IV, Ing. Oliver Guillén Rosa, CODIA 18139):
/// </para>
/// <list type="table">
///   <listheader><term>Losa</term><term>Lx</term><term>Ly</term><term>h</term><term>MampO</term><term>Qmamp</term><term>Qmap</term><term>Qd</term><term>Ql</term><term>Qu</term></listheader>
///   <item><term>1</term><term>6.45</term><term>5.40</term><term>0.15</term><term>1.78</term><term>1.27359</term><term>0.10</term><term>0.641</term><term>0.20</term><term>1.0892</term></item>
///   <item><term>2</term><term>4.90</term><term>4.45</term><term>0.12</term><term>7.67</term><term>5.55001</term><term>0.25453</term><term>0.72353</term><term>0.20</term><term>1.18824</term></item>
///   <item><term>3</term><term>4.90</term><term>4.40</term><term>0.12</term><term>0.00</term><term>0.00000</term><term>0.00000</term><term>0.469</term><term>0.20</term><term>0.8828</term></item>
/// </list>
/// </summary>
public class CalculoEngineTests
{
    private const double TolDefault = 0.001;
    private const double TolAjustada = 0.005;  // para valores derivados de redondeo de h

    // =================================================================
    // h_calc — fórmulas 1D y 2D
    // =================================================================

    [Theory]
    [InlineData(4.0, 28, 0.142857)]  // ambos extremos continuos
    [InlineData(5.6, 28, 0.20)]
    [InlineData(4.0, 20, 0.20)]      // simplemente apoyada
    [InlineData(4.0, 24, 0.166667)]
    [InlineData(2.0, 10, 0.20)]      // voladizo
    public void HCalc1D_es_Ln_dividido_por_K(double ln, int k, double esperado)
    {
        var h = CalculoEngine.ComputeHCalc1D(ln, k);
        Assert.Equal(esperado, h, precision: 4);
    }

    [Theory]
    // Losa 1 del xls: Ln=6.45, Fy=4200, ratio=6.45/5.4=1.1944... → h_calc=0.15176
    [InlineData(6.45, 4200, 1.194444444, 0.15176)]
    // Losa 2 del xls: Ln=4.9, Fy=4200, ratio=4.9/4.45=1.10112 → h_calc=0.11740
    [InlineData(4.9,  4200, 1.101123595, 0.11740)]
    // Losa 3 del xls: Ln=4.9, Fy=4200, ratio=4.9/4.4=1.11364 → h_calc=0.11712
    [InlineData(4.9,  4200, 1.113636363, 0.11712)]
    public void HCalc2D_replica_xls(double ln, double fy, double ratio, double esperado)
    {
        var h = CalculoEngine.ComputeHCalc2D(ln, fy, ratio);
        Assert.Equal(esperado, h, precision: 4);
    }

    [Theory]
    [InlineData(0.151,  0.15)]  // ROUND(0.151, 2) = 0.15 → MAX(0.12, 0.15) = 0.15
    [InlineData(0.117,  0.12)]  // ROUND(0.117, 2) = 0.12 → MAX(0.12, 0.12) = 0.12
    [InlineData(0.085,  0.12)]  // ROUND(0.085, 2) = 0.09 → MAX(0.12, 0.09) = 0.12 (clamp)
    [InlineData(0.234,  0.23)]
    public void HUsar_es_max_de_012_y_round_2(double hCalc, double esperado)
    {
        Assert.Equal(esperado, CalculoEngine.ComputeHUsar(hCalc), precision: 4);
    }

    [Fact]
    public void HCalc_dispatcha_segun_Cond()
    {
        // Losa 2D
        var l2d = new Losa { Lx = 5, Ly = 4, K = 28 };
        var h2d = CalculoEngine.ComputeHCalc(l2d, fyKgCm2: 4200);
        Assert.Equal(CalculoEngine.ComputeHCalc2D(5, 4200, 5.0/4.0), h2d, precision: 6);

        // Losa 1D (ratio > 2)
        var l1d = new Losa { Lx = 9, Ly = 4, K = 28 };
        var h1d = CalculoEngine.ComputeHCalc(l1d, fyKgCm2: 4200);
        Assert.Equal(CalculoEngine.ComputeHCalc1D(4, 28), h1d, precision: 6);
    }

    // =================================================================
    // h_eq — placeholder por ahora (h_eq = h_usar)
    // =================================================================

    [Fact]
    public void HEq_default_es_h_usar_para_losa_maciza()
    {
        var l = new Losa { Lx = 4, Ly = 4 };
        Assert.Equal(0.15, CalculoEngine.ComputeHEq(l, hUsar: 0.15, fyKgCm2: 4200));
    }

    [Fact]
    public void HEq_devuelve_h_usar_aunque_haya_bw_y_HBloque_temporariamente()
    {
        // TODO: cuando se implementen las formulas αfm, este test cambiara para
        // exigir un h_eq distinto a h_usar.
        var l = new Losa { Lx = 4, Ly = 4, Bw = 0.10, HBloque = 0.20 };
        Assert.Equal(0.15, CalculoEngine.ComputeHEq(l, hUsar: 0.15, fyKgCm2: 4200));
    }

    // =================================================================
    // Qmamp — peso de mampostería
    // =================================================================

    [Theory]
    // Losa 1 del xls: h_piso=2.8, h=0.15, MampO=1.78 → 1.8*(2.8-0.15)*0.15*1.78 = 1.27359
    [InlineData(2.8, 0.15, 0, 1.78, 0, 1.27359)]
    // Losa 2 del xls: h_piso=2.8, h=0.12, MampO=7.67 → 1.8*(2.8-0.12)*0.15*7.67 = 5.550012
    [InlineData(2.8, 0.12, 0, 7.67, 0, 5.550012)]
    // Sin mampostería → 0
    [InlineData(2.8, 0.12, 0, 0,    0, 0)]
    // Losa de techo (h_piso = h_losa) → 0 (no hay altura para mampostería)
    [InlineData(0.15, 0.15, 1, 1, 1, 0)]
    // Mampostería de 0.20 m: 1.8*(2.8-0.12)*0.2*5 = 4.824
    [InlineData(2.8, 0.12, 5, 0,    0, 4.824)]
    public void Qmamp_replica_formula_xls(double hPiso, double hLosa, double n, double o, double p, double esperado)
    {
        var q = CalculoEngine.ComputeQmamp(hPiso, hLosa, n, o, p);
        Assert.Equal(esperado, q, precision: 4);
    }

    // =================================================================
    // Qmap — carga distribuida
    // =================================================================

    [Theory]
    // Losa 1: Qmamp=1.27359, Area=34.83 → 1.27359/34.83 = 0.0366 < 0.10 → clamp a 0.10
    [InlineData(1.27359, 34.83, 0.10)]
    // Losa 2: Qmamp=5.550012, Area=21.805 → 5.550012/21.805 = 0.25453
    [InlineData(5.550012, 21.805, 0.25453)]
    // Sin mampostería: Qmap=0 (no clamp)
    [InlineData(0,        21.805, 0)]
    // Mampostería pesada: 10/20 = 0.50
    [InlineData(10,       20,     0.50)]
    public void Qmap_aplica_clamp_minimo_0_10(double qmamp, double area, double esperado)
    {
        var q = CalculoEngine.ComputeQmap(qmamp, area);
        Assert.Equal(esperado, q, precision: 4);
    }

    // =================================================================
    // Qd — lookup en tabla de carga muerta + Qmap
    // =================================================================

    [Fact]
    public void Qd_para_h_012_entrepiso_es_0_469()
    {
        var c = CargasGlobales.SemillaPorDefecto();
        // h_eq=0.12, sin mampostería → lookup(0.12, entrepiso) = 0.288 + 0.181 = 0.469
        var qd = CalculoEngine.ComputeQd(hEq: 0.12, c, SistemaUso.Entrepiso, qmap: 0);
        Assert.Equal(0.469, qd, precision: 3);
    }

    [Fact]
    public void Qd_para_h_015_entrepiso_es_0_541()
    {
        var c = CargasGlobales.SemillaPorDefecto();
        var qd = CalculoEngine.ComputeQd(hEq: 0.15, c, SistemaUso.Entrepiso, qmap: 0);
        Assert.Equal(0.541, qd, precision: 3);
    }

    [Fact]
    public void Qd_suma_Qmap_al_lookup_de_la_tabla()
    {
        var c = CargasGlobales.SemillaPorDefecto();
        // h=0.15, Qmap=0.10 → 0.541 + 0.10 = 0.641 (Losa 1 del xls)
        var qd = CalculoEngine.ComputeQd(hEq: 0.15, c, SistemaUso.Entrepiso, qmap: 0.10);
        Assert.Equal(0.641, qd, precision: 3);
    }

    // =================================================================
    // Ql — carga viva por uso
    // =================================================================

    [Theory]
    [InlineData(SistemaUso.Entrepiso, 0.20)]
    [InlineData(SistemaUso.Balcon,    0.40)]
    [InlineData(SistemaUso.Techo,     0.10)]
    public void Ql_se_resuelve_por_uso(SistemaUso uso, double esperado)
    {
        var ql = CalculoEngine.ComputeQl(uso, CargasGlobales.SemillaPorDefecto());
        Assert.Equal(esperado, ql);
    }

    // =================================================================
    // Qu — combinación
    // =================================================================

    [Theory]
    // Losa 1: Qd=0.641, Ql=0.20 → 1.2*0.641 + 1.6*0.20 = 0.7692 + 0.32 = 1.0892
    [InlineData(0.641,   0.20, 1.0892)]
    // Losa 2: Qd=0.72353, Ql=0.20 → 1.2*0.72353 + 1.6*0.20 = 0.86824 + 0.32 = 1.18824
    [InlineData(0.72353, 0.20, 1.18824)]
    // Losa 3: Qd=0.469, Ql=0.20 → 1.2*0.469 + 1.6*0.20 = 0.5628 + 0.32 = 0.8828
    [InlineData(0.469,   0.20, 0.8828)]
    public void Qu_aplica_combinacion_ACI_318_05(double qd, double ql, double esperado)
    {
        var qu = CalculoEngine.ComputeQu(qd, ql, new FactoresCombinacion());
        Assert.Equal(esperado, qu, precision: 4);
    }

    // =================================================================
    // PIPELINE COMPLETO — goldens del xls (3 losas reales)
    // =================================================================

    /// <summary>
    /// Construye el contexto de proyecto+sistema entrepiso típico para los tests
    /// del pipeline (mismo Fy, mismas cargas globales que el .xlsx).
    /// </summary>
    private static (Proyecto, Sistema) BuildContextoEntrepiso()
    {
        var p = new Proyecto { Nombre = "Test Neapolis IV", FyKgCm2 = 4200 };
        var s = new Sistema  { Nombre = "Earlette",         Uso = SistemaUso.Entrepiso };
        p.Sistemas.Add(s);
        return (p, s);
    }

    [Fact]
    public void Pipeline_Losa1_Neapolis_replica_xls()
    {
        var (p, s) = BuildContextoEntrepiso();
        var l = new Losa { Id = 1, Lx = 6.45, Ly = 5.40, MampO = 1.78 };
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.Equal("2D",     l.Cond);
        Assert.Equal(6.45,     l.Ln,           precision: 4);
        Assert.Equal(0.15176,  l.HCalc!.Value, precision: 4);
        Assert.Equal(0.15,     l.Espesor,      precision: 4);
        Assert.Equal(0.15,     l.HEq!.Value,   precision: 4);
        Assert.Equal(34.83,    l.Area,         precision: 4);
        Assert.Equal(1.27359,  l.Qmamp!.Value, precision: 4);
        Assert.Equal(0.10,     l.Qmap!.Value,  precision: 4);  // clamped
        Assert.Equal(0.641,    l.Qd!.Value,    precision: 3);
        Assert.Equal(0.20,     l.Ql!.Value,    precision: 4);
        Assert.Equal(1.0892,   l.Qu!.Value,    precision: 4);
    }

    [Fact]
    public void Pipeline_Losa2_Neapolis_replica_xls()
    {
        var (p, s) = BuildContextoEntrepiso();
        var l = new Losa { Id = 2, Lx = 4.90, Ly = 4.45, MampO = 7.67 };
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.Equal("2D",       l.Cond);
        Assert.Equal(4.9,        l.Ln,           precision: 4);
        Assert.Equal(0.117403,   l.HCalc!.Value, precision: 4);
        Assert.Equal(0.12,       l.Espesor,      precision: 4);
        Assert.Equal(21.805,     l.Area,         precision: 4);
        Assert.Equal(5.550012,   l.Qmamp!.Value, precision: 4);
        Assert.Equal(0.254529,   l.Qmap!.Value,  precision: 4);
        Assert.Equal(0.723529,   l.Qd!.Value,    precision: 4);
        Assert.Equal(0.20,       l.Ql!.Value,    precision: 4);
        Assert.Equal(1.188235,   l.Qu!.Value,    precision: 4);
    }

    [Fact]
    public void Pipeline_Losa3_Neapolis_sin_mamposteria_replica_xls()
    {
        var (p, s) = BuildContextoEntrepiso();
        var l = new Losa { Id = 3, Lx = 4.90, Ly = 4.40 };
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.Equal(0.0,    l.Qmamp!.Value);
        Assert.Equal(0.0,    l.Qmap!.Value);
        Assert.Equal(0.469,  l.Qd!.Value, precision: 3);
        Assert.Equal(0.20,   l.Ql!.Value);
        Assert.Equal(0.8828, l.Qu!.Value, precision: 4);
    }

    // =================================================================
    // SYNC A LOSA.CARGA (qu sincronizado al motor solo si CarryQuToCarga=true)
    // =================================================================

    [Fact]
    public void Pipeline_no_pisa_Carga_si_CarryQuToCarga_es_false_default()
    {
        var (p, s) = BuildContextoEntrepiso();
        var l = new Losa { Id = 1, Lx = 4, Ly = 4, Carga = 99.99 };  // valor manual
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.Equal(99.99, l.Carga);  // sin tocar
        Assert.NotNull(l.Qu);
    }

    [Fact]
    public void Pipeline_sincroniza_Qu_a_Carga_si_CarryQuToCarga_es_true()
    {
        var (p, s) = BuildContextoEntrepiso();
        var l = new Losa { Id = 1, Lx = 4, Ly = 4, Carga = 99.99, CarryQuToCarga = true };
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.NotEqual(99.99, l.Carga);
        Assert.Equal(l.Qu!.Value, l.Carga, precision: 4);
    }

    // =================================================================
    // OVERRIDE DE H_USAR
    // =================================================================

    [Fact]
    public void Pipeline_respeta_HUsarOverride()
    {
        var (p, s) = BuildContextoEntrepiso();
        // Lx=Ly=4 → h_calc ≈ 0.094 → ROUND=0.09 → MAX(0.12)=0.12 (default)
        // Override forzado a 0.20.
        var l = new Losa { Id = 1, Lx = 4, Ly = 4, HUsarOverride = 0.20 };
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.Equal(0.20, l.Espesor, precision: 4);
        Assert.Equal(0.20, l.HEq!.Value, precision: 4);
        // Qd debe corresponder a h=0.20 (no a 0.12).
        // Qd_lookup(0.20) = 0.20*2.4 + 0.181 = 0.48 + 0.181 = 0.661
        Assert.Equal(0.661, l.Qd!.Value, precision: 3);
    }

    // =================================================================
    // PIPELINE EN CASCADA (proyecto / sistema)
    // =================================================================

    [Fact]
    public void RecalcularSistema_corre_engine_sobre_todas_las_losas()
    {
        var (p, s) = BuildContextoEntrepiso();
        s.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        s.Losas.Add(new Losa { Id = 2, Lx = 5, Ly = 4 });
        s.Losas.Add(new Losa { Id = 3, Lx = 6, Ly = 5 });

        CalculoEngine.RecalcularSistema(s, p);

        foreach (var l in s.Losas)
        {
            Assert.NotNull(l.HCalc);
            Assert.NotNull(l.Qd);
            Assert.NotNull(l.Qu);
        }
    }

    [Fact]
    public void RecalcularProyecto_corre_engine_sobre_todos_los_sistemas()
    {
        var p = new Proyecto { Nombre = "Multinivel", FyKgCm2 = 4200 };
        var e1 = new Sistema { Nombre = "E1",    Uso = SistemaUso.Entrepiso };
        var t  = new Sistema { Nombre = "Techo", Uso = SistemaUso.Techo };
        e1.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        t.Losas.Add(new Losa  { Id = 2, Lx = 4, Ly = 4 });
        p.Sistemas.Add(e1);
        p.Sistemas.Add(t);

        CalculoEngine.RecalcularProyecto(p);

        // Entrepiso: ql = 0.20
        Assert.Equal(0.20, e1.Losas[0].Ql);
        // Techo: ql = 0.10 (¡diferente!)
        Assert.Equal(0.10, t.Losas[0].Ql);
    }
}
