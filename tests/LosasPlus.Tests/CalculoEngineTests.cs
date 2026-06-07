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
    public void HEq_vigueta_y_bloque_aplica_modelo_paramétrico_T_section()
    {
        // Cross-section T: Bw=0.10, HBloque=0.20, B=0.50, hCapeta=0.05.
        // Por la fórmula del T-section: h_eq ≈ 0.1806 m.
        var l = new Losa { Lx = 4, Ly = 4, Bw = 0.10, HBloque = 0.20 };
        var h = CalculoEngine.ComputeHEq(l, hUsar: 0.25, fyKgCm2: 4200);
        Assert.InRange(h, 0.179, 0.182);
    }

    [Fact]
    public void HEq_vigueta_caída_a_capeta_por_default_si_hUsar_no_excede_hBloque()
    {
        // hUsar=0.15 < hBloque=0.20 → hCapeta default 0.05 (no negativo).
        var l = new Losa { Lx = 4, Ly = 4, Bw = 0.10, HBloque = 0.20 };
        var h = CalculoEngine.ComputeHEq(l, hUsar: 0.15, fyKgCm2: 4200);
        // h_eq calculado con hCapeta=0.05 da el mismo valor que el test anterior.
        Assert.InRange(h, 0.179, 0.182);
    }

    [Fact]
    public void HEq_vigueta_bloque_es_menor_que_altura_total_pero_mayor_que_capeta()
    {
        // Sanity: h_eq cae entre la capeta sola (0.05) y la altura total
        // (h_bloque + h_capeta = 0.25). Modelo razonable para losa nervada.
        var l = new Losa { Lx = 4, Ly = 4, Bw = 0.12, HBloque = 0.20 };
        var h = CalculoEngine.ComputeHEq(l, hUsar: 0.25, fyKgCm2: 4200);
        Assert.True(h > 0.05);
        Assert.True(h < 0.25);
    }

    [Fact]
    public void HEq_aumenta_con_nervio_mas_ancho()
    {
        var lDelgado = new Losa { Lx = 4, Ly = 4, Bw = 0.08, HBloque = 0.20 };
        var lGrueso  = new Losa { Lx = 4, Ly = 4, Bw = 0.15, HBloque = 0.20 };
        var hDelg = CalculoEngine.ComputeHEq(lDelgado, hUsar: 0.25, fyKgCm2: 4200);
        var hGrue = CalculoEngine.ComputeHEq(lGrueso,  hUsar: 0.25, fyKgCm2: 4200);
        Assert.True(hGrue > hDelg, $"Esperado {hGrue} > {hDelg}: más nervio = más rigidez = más h_eq");
    }

    [Fact]
    public void HEq_aumenta_con_bloque_mas_alto()
    {
        var lCorto = new Losa { Lx = 4, Ly = 4, Bw = 0.10, HBloque = 0.15 };
        var lLargo = new Losa { Lx = 4, Ly = 4, Bw = 0.10, HBloque = 0.25 };
        var hC = CalculoEngine.ComputeHEq(lCorto, hUsar: 0.20, fyKgCm2: 4200);
        var hL = CalculoEngine.ComputeHEq(lLargo, hUsar: 0.30, fyKgCm2: 4200);
        Assert.True(hL > hC, $"Esperado {hL} > {hC}: bloque más alto = más altura útil del T");
    }

    [Fact]
    public void ComputeHEqViguetaBloque_rechaza_bw_que_excede_panel()
    {
        // bw >= bPanel rompe la geometría (nervio ocuparía todo el panel).
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalculoEngine.ComputeHEqViguetaBloque(bw: 0.60, hBloque: 0.20, hCapeta: 0.05, bPanel: 0.50));
    }

    [Theory]
    [InlineData(0,    0.20, 0.05, 0.50)]  // bw=0 inválido
    [InlineData(0.10, 0,    0.05, 0.50)]  // hBloque=0 inválido
    [InlineData(0.10, 0.20, 0,    0.50)]  // hCapeta=0 inválido
    [InlineData(0.10, 0.20, 0.05, 0)]     // bPanel=0 inválido
    public void ComputeHEqViguetaBloque_rechaza_dimensiones_no_positivas(double bw, double hBloque, double hCapeta, double bPanel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CalculoEngine.ComputeHEqViguetaBloque(bw, hBloque, hCapeta, bPanel));
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
        // Usa el factory porque CalculoEngine necesita Cargas.SemillaPorDefecto
        // para hacer el LookupQd en la tabla h↔qd (15 filas) y los pesos propios.
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre  = "Test Neapolis IV";
        p.FyKgCm2 = 4200;
        var s = new Sistema { Nombre = "Earlette", Uso = SistemaUso.Entrepiso };
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
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre  = "Multinivel";
        p.FyKgCm2 = 4200;
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

    [Fact]
    public void RecalcularProyecto_recalcula_sistemas_de_niveles_2_y_superiores()
    {
        // Proyecto pluriNIVEL genuino: 2 plantas, cada una con su propio Sistema.
        // Nivel 1 = Edificios[0].Niveles[0] (= la fachada p.Sistemas).
        // Nivel 2 = Edificios[0].Niveles[1] (NO alcanzable por p.Sistemas).
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.Nombre  = "Pluri 2 niveles";
        p.FyKgCm2 = 4200;
        p.AsegurarEstructura();

        // Nivel 1 (entrepiso) vía la fachada.
        var n1 = new Sistema { Nombre = "N1-E1", Uso = SistemaUso.Entrepiso };
        n1.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        p.Sistemas.Add(n1);

        // Nivel 2 (techo) como SEGUNDA planta real del edificio.
        var nivel2 = new Nivel { Nombre = "Nivel 2", Uso = SistemaUso.Techo, Cota = 2.80 };
        var n2 = new Sistema { Nombre = "N2-Techo", Uso = SistemaUso.Techo };
        n2.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 4 });
        nivel2.Sistemas.Add(n2);
        p.Edificios[0].Niveles.Add(nivel2);

        CalculoEngine.RecalcularProyecto(p);

        // Nivel 1 recalculado (entrepiso → ql = 0.20).
        Assert.NotNull(n1.Losas[0].Qu);
        Assert.Equal(0.20, n1.Losas[0].Ql);

        // Nivel 2 recalculado (techo → ql = 0.10). Antes de B2c quedaba en null
        // porque RecalcularProyecto solo recorría p.Sistemas (= Nivel 1).
        Assert.NotNull(n2.Losas[0].Qu);
        Assert.Equal(0.10, n2.Losas[0].Ql);
    }

    // =================================================================
    // ESPESOR EQUIVALENTE — αfm (ACI 9.5.3.3)
    // Ref: ESPESOR EQUIVALENTE.xlsx, columnas T..AA
    // =================================================================

    [Fact]
    public void InerciaRectangular_b30_h70_es_857500_cm4()
    {
        // I = 30 · 70³ / 12 = 30 · 343000 / 12 = 857500 cm⁴
        var i = CalculoEngine.ComputeInerciaRectangular(30, 70);
        Assert.Equal(857500.0, i, precision: 1);
    }

    [Fact]
    public void InerciaLosa_replica_modelo_xls()
    {
        // L_perp = 4 m, J = 0.12 m.
        // ancho_cm = L_perp·100/2 = 200 cm.
        // peralte_cm = J·100 = 12 cm → peralte³ = 1728 cm³.
        // I = ancho · peralte³ / 12 = 200 · 1728 / 12 = 28800 cm⁴.
        //
        // Nota: la fórmula del Excel "J³·10⁶ / 12" es equivalente porque
        // J está en m: J³ = 0.001728, J³·10⁶ = 1728 cm³ (cambio de unidad).
        var i = CalculoEngine.ComputeInerciaLosa(4.0, 0.12);
        Assert.Equal(28_800.0, i, precision: 1);
    }

    [Fact]
    public void Alpha_se_calcula_como_iViga_sobre_iLosa()
    {
        // Iviga 30·70³/12 = 857500, Ilosa arbitrario 100000 → α = 8.575
        var a = CalculoEngine.ComputeAlpha(857500, 100_000);
        Assert.Equal(8.575, a, precision: 4);
    }

    [Fact]
    public void Alpha_lanza_si_iLosa_es_cero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculoEngine.ComputeAlpha(100, 0));
    }

    [Fact]
    public void AlphaFm_de_losa_4x4_h012_es_OK()
    {
        // Losa 4x4 m, J = 0.12 m, viga 30x70 cm.
        // Iviga = 857500
        // Ilosa_x = (4·100/2)·(12)³·10⁶/12 = 200·1.728e9/12 = 2.88e10 ... espera, J = 0.12 → J·100 = 12 cm
        // peralte³ = 12³ = 1728 cm³ → 1.728e3 cm³ (no 1.728e9). Mi código multiplica J*100 y eleva al cubo.
        // (0.12·100)³ = 12³ = 1728 (cm³)
        // Ilosa = 200 · 1728 / 12 = 28800 cm⁴ → α = 857500/28800 ≈ 29.78
        // αx = αy = ~29.78 → αm ≈ 29.78 > 2 → OK
        var losa  = new Losa { Lx = 4, Ly = 4 };
        var viga  = new VigaTipo { BaseCm = 30, AlturaCm = 70 };
        var afm   = CalculoEngine.ComputeAlphaFm(losa, viga, 0.12);
        Assert.Equal(29.7743, afm.AlphaX, precision: 2);
        Assert.Equal(29.7743, afm.AlphaY, precision: 2);
        Assert.Equal(29.7743, afm.AlphaM, precision: 2);
        Assert.Equal("OK", afm.Estado);
    }

    [Fact]
    public void AlphaFm_status_CHK_cuando_alphaM_no_supera_2()
    {
        // Losa muy grande con viga muy chica: αm < 2.
        var losa = new Losa { Lx = 10, Ly = 10 };
        var viga = new VigaTipo { BaseCm = 10, AlturaCm = 20 };  // I = 10·8000/12 = 6666.7
        // Ilosa = (10·100/2)·20³/12 = 500·8000/12 = 333333 → α ≈ 0.02 → CHK
        var afm = CalculoEngine.ComputeAlphaFm(losa, viga, 0.20);
        Assert.True(afm.AlphaM < 2.0);
        Assert.Equal("CHK", afm.Estado);
    }

    // =================================================================
    // ESPESOR EQUIVALENTE — Volúmenes y cantidades de bovedilla
    // =================================================================

    [Fact]
    public void Volumenes_losa_2D_4x4_con_bovedilla_default()
    {
        // Bovedilla 0.15/0.50/0.50/0.15 → módulo S+B = 0.65 m
        // M = floor(4/0.65) = 6, N = floor(4/0.65) = 6 → total = 36
        // V bovedilla individual = 0.50·0.50·0.15 = 0.0375 m³
        // V_bov = 36 · 0.0375 = 1.35 m³
        // V_total = 0.30 · 4 · 4 = 4.80 m³
        // V_concreto = 4.80 - 1.35 = 3.45 m³
        var losa = new Losa { Lx = 4, Ly = 4 };
        var bov  = new Bovedilla();
        var v    = CalculoEngine.ComputeVolumenes(losa, bov, 0.30);
        Assert.Equal(6, v.CantBovedillasX);
        Assert.Equal(6, v.CantBovedillasY);
        Assert.Equal(36, v.Total);
        Assert.Equal(1.35, v.VBovedilla, precision: 4);
        Assert.Equal(4.80, v.VTotal,     precision: 4);
        Assert.Equal(3.45, v.VConcreto,  precision: 4);
    }

    [Fact]
    public void Volumenes_losa_1D_cuenta_nervios_en_direccion_corta()
    {
        // Losa 1D 2.0 × 6.0 (ratio = 3, claramente 1D). Cond = "1D".
        // Luz nervios = min = 2.0 m → M = floor(2/0.65) = 3
        // Luz perpendicular = 6.0 m → N = floor(6/0.50) = 12 (bovedillas por nervio)
        var losa = new Losa { Lx = 2, Ly = 6 };
        Assert.Equal("1D", losa.Cond);
        var v = CalculoEngine.ComputeVolumenes(losa, new Bovedilla(), 0.25);
        Assert.Equal(3, v.CantBovedillasX);
        Assert.Equal(12, v.CantBovedillasY);
        Assert.Equal(36, v.Total);
    }

    [Fact]
    public void Volumenes_VConcreto_nunca_es_negativo()
    {
        // Si las bovedillas pesan más que el volumen total (caso degenerado por
        // espesor muy chico), el código debe entregar 0 sin lanzar.
        var losa = new Losa { Lx = 4, Ly = 4 };
        var bov  = new Bovedilla { S = 0.10, B = 0.10, L = 0.10, H = 0.10 };
        var v = CalculoEngine.ComputeVolumenes(losa, bov, 0.01);  // h muy chico
        Assert.True(v.VConcreto >= 0);
    }

    // =================================================================
    // ACERO POR BARRAS — As total
    // =================================================================

    [Fact]
    public void AreasBarras_lookup_funciona_para_los_seis_diametros()
    {
        Assert.Equal(0.71, AreasBarras.Para(3));
        Assert.Equal(1.27, AreasBarras.Para(4));
        Assert.Equal(1.99, AreasBarras.Para(5));
        Assert.Equal(2.85, AreasBarras.Para(6));
        Assert.Equal(3.88, AreasBarras.Para(7));
        Assert.Equal(5.07, AreasBarras.Para(8));
        Assert.Equal(0.00, AreasBarras.Para(99));  // fuera de rango
    }

    [Fact]
    public void AsTotal_replica_formula_del_xls()
    {
        // Excel: D = 5.07·#1" + 2.85·#3/4" + 1.27·#1/2" + 0.71·#3/8"
        // Ejemplo: 2 barras #8 + 0 #6 + 4 #4 + 3 #3 = 2·5.07 + 4·1.27 + 3·0.71 = 10.14 + 5.08 + 2.13 = 17.35
        var r = new RefuerzoBarras { N3 = 3, N4 = 4, N8 = 2 };
        Assert.Equal(17.35, CalculoEngine.ComputeAsTotal(r), precision: 4);
    }

    [Fact]
    public void AsTotal_vacio_es_cero()
    {
        Assert.Equal(0.0, CalculoEngine.ComputeAsTotal(new RefuerzoBarras()));
    }

    // =================================================================
    // EMPALMES / BARRAS ADICIONALES
    // =================================================================

    [Fact]
    public void AsAdicional_replica_formula_del_xls()
    {
        // Excel B281=7.6 cm², C281=2.0, D281=4.0 → As_adic = 7.6 - 1.0 - 2.0 = 4.6
        Assert.Equal(4.6, CalculoEngine.ComputeAsAdicional(7.6, 2.0, 4.0), precision: 4);
    }

    [Fact]
    public void AsAdicional_clampa_a_cero_si_empalmes_cubren_demanda()
    {
        // Si las dos mitades ya superan el As_req, no hay adicional faltante.
        Assert.Equal(0.0, CalculoEngine.ComputeAsAdicional(5.0, 6.0, 6.0));
    }

    [Fact]
    public void SeparacionBarrasAdicionales_es_areaBarra_sobre_Asadic()
    {
        // As_adic = 4.6 cm², #3/8" (0.71) → s = 0.71/4.6 ≈ 0.1543
        Assert.Equal(0.71 / 4.6, CalculoEngine.ComputeSeparacionBarrasAdicionales(4.6, 3), precision: 6);
        // #1/2" (1.27) → s = 1.27/4.6 ≈ 0.2761
        Assert.Equal(1.27 / 4.6, CalculoEngine.ComputeSeparacionBarrasAdicionales(4.6, 4), precision: 6);
    }

    [Fact]
    public void SeparacionBarrasAdicionales_es_infinito_si_no_hay_demanda()
    {
        Assert.Equal(double.PositiveInfinity, CalculoEngine.ComputeSeparacionBarrasAdicionales(0.0, 3));
    }

    [Fact]
    public void SeparacionBarrasAdicionales_lanza_para_diametro_invalido()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculoEngine.ComputeSeparacionBarrasAdicionales(4.6, 99));
    }

    // =================================================================
    // PIPELINE — α, volúmenes y As también se rellenan en RecalcularLosa
    // =================================================================

    [Fact]
    public void RecalcularLosa_rellena_alphaFm_y_volumenes_y_As()
    {
        var (p, s) = BuildContextoEntrepiso();
        // Viga tipo default 30×70, bovedilla default 15/50/50/15.
        var l = new Losa { Id = 1, Lx = 4, Ly = 4 };
        l.RefuerzoX.N4 = 5;
        l.RefuerzoY.N4 = 5;
        s.Losas.Add(l);

        CalculoEngine.RecalcularLosa(l, s, p);

        Assert.NotNull(l.AlphaX);
        Assert.NotNull(l.AlphaY);
        Assert.NotNull(l.AlphaM);
        Assert.NotNull(l.EstadoAlphaFm);
        Assert.True(l.AlphaM!.Value > 0);

        Assert.NotNull(l.VBovedilla);
        Assert.NotNull(l.VTotal);
        Assert.NotNull(l.VConcreto);
        Assert.True(l.VConcreto!.Value >= 0);

        Assert.Equal(5 * 1.27, l.AsxCalc!.Value, precision: 4);
        Assert.Equal(5 * 1.27, l.AsyCalc!.Value, precision: 4);
    }
}
