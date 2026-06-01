using System.Linq;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la extensión del modelo de dominio para Memoria Plus.
///
/// Cubre tres ejes:
/// <list type="number">
///   <item><b>Backward compatibility</b>: una <see cref="Losa"/> creada con el
///         constructor de toda la vida sigue siendo válida (todos los nuevos
///         campos tienen defaults seguros).</item>
///   <item><b>Defaults sensatos</b>: K=28, h_piso=2.8 m, mampostería en 0,
///         <see cref="CargasGlobales"/> sembradas con la tabla del .xlsx.</item>
///   <item><b>Propiedades calculadas que no requieren engine</b>: Cond, Ln,
///         Ratio, Area se calculan directamente desde Lx/Ly.</item>
/// </list>
/// </summary>
public class ModeloMemoriaPlusTests
{
    // =================================================================
    // BACKWARD COMPATIBILITY
    // =================================================================

    [Fact]
    public void Losa_existente_sigue_valida_con_los_nuevos_campos_en_default()
    {
        // Construcción "clásica" que usaba LosasPlus.App antes de la extensión.
        var losa = new Losa
        {
            Id = 1, Tipo = 10,
            Carga = 2.0, Espesor = 0.12, Lx = 4.0, Ly = 4.0, Rec = 0.02
        };

        Assert.True(losa.EsValida);
        Assert.Equal(28, losa.K);
        Assert.Equal(2.8, losa.HPisoTecho);
        Assert.Equal(0,   losa.MampN);
        Assert.Equal(0,   losa.MampO);
        Assert.Equal(0,   losa.MampP);
        Assert.Null(losa.Bw);
        Assert.Null(losa.HBloque);
        Assert.Null(losa.HUsarOverride);
        Assert.False(losa.CarryQuToCarga);

        // Outputs computados arrancan en null hasta que el engine corra.
        Assert.Null(losa.HCalc);
        Assert.Null(losa.HEq);
        Assert.Null(losa.Qmamp);
        Assert.Null(losa.Qmap);
        Assert.Null(losa.Qd);
        Assert.Null(losa.Ql);
        Assert.Null(losa.Qu);

        Assert.False(losa.EspesorInsuficiente);
        Assert.False(losa.LnExcedeRango);
    }

    [Fact]
    public void Sistema_default_es_entrepiso_sin_salida_perdomo()
    {
        var s = new Sistema { Nombre = "E1" };

        Assert.Equal(SistemaUso.Entrepiso, s.Uso);
        Assert.Equal(0.0, s.CotaMetros);
        Assert.Null(s.SalidaPerdomo);
        Assert.False(s.TieneSalidaPerdomo);
    }

    [Fact]
    public void ProyectoFactory_NuevoProyectoSeedeado_carga_los_defaults()
    {
        // Tras commit 18 (persistencia), el constructor `new Proyecto()` no
        // seedea Cargas — evita que System.Text.Json en modo Populate
        // duplique las 15 filas al deserializar. El seedeo vive en el factory
        // que la UI llama cuando hace "Nuevo proyecto".
        var p = ProyectoFactory.NuevoProyectoSeedeado();

        Assert.NotNull(p.Cargas);
        Assert.NotNull(p.Cargas.CargaMuertaPorEspesor);
        Assert.NotEmpty(p.Cargas.CargaMuertaPorEspesor.Filas);
        Assert.Equal(15, p.Cargas.CargaMuertaPorEspesor.Filas.Count);  // h=0.06..0.20 paso 0.01

        Assert.Equal(280,  p.FcKgCm2);
        Assert.Equal(4200, p.FyKgCm2);
        Assert.Empty(p.Normas);
    }

    [Fact]
    public void Proyecto_default_arranca_con_Cargas_vacias()
    {
        // El constructor por defecto produce un Proyecto "limpio" para
        // deserialización JSON sin duplicar collections pre-seedeadas.
        var p = new Proyecto();
        Assert.NotNull(p.Cargas);
        Assert.Empty(p.Cargas.CargaMuertaPorEspesor.Filas);
        Assert.Empty(p.Cargas.PesosPropiosEntrepiso.Items);
        Assert.Empty(p.Cargas.PesosPropiosTecho.Items);
    }

    // =================================================================
    // PROPIEDADES CALCULADAS QUE NO REQUIEREN ENGINE
    // =================================================================

    [Theory]
    [InlineData(4.0, 4.0,  "2D")]   // ratio 1.0
    [InlineData(4.0, 6.0,  "2D")]   // ratio 1.5
    [InlineData(4.0, 8.0,  "2D")]   // ratio 2.0 (limite)
    [InlineData(4.0, 8.5,  "1D")]   // ratio 2.125 (>2)
    [InlineData(2.0, 6.0,  "1D")]
    [InlineData(8.0, 4.0,  "2D")]   // simétrico — orden no importa
    public void Cond_se_decide_por_relacion_max_min(double lx, double ly, string esperado)
    {
        var l = new Losa { Lx = lx, Ly = ly, Espesor = 0.12 };
        Assert.Equal(esperado, l.Cond);
    }

    [Theory]
    [InlineData(4.0, 6.0, 6.0)]    // 2D → max
    [InlineData(6.0, 4.0, 6.0)]
    [InlineData(2.0, 6.0, 2.0)]    // 1D (ratio 3) → min
    [InlineData(6.0, 2.0, 2.0)]
    public void Ln_es_max_para_2D_y_min_para_1D(double lx, double ly, double esperadoLn)
    {
        var l = new Losa { Lx = lx, Ly = ly };
        Assert.Equal(esperadoLn, l.Ln, precision: 4);
    }

    [Fact]
    public void Area_es_lx_por_ly()
    {
        var l = new Losa { Lx = 6.45, Ly = 5.4 };
        Assert.Equal(34.83, l.Area, precision: 4);
    }

    [Fact]
    public void Ratio_es_max_sobre_min()
    {
        var l = new Losa { Lx = 4.5, Ly = 6.2 };
        Assert.Equal(6.2 / 4.5, l.Ratio, precision: 4);
    }

    // =================================================================
    // K FACTOR
    // =================================================================

    [Fact]
    public void K_default_es_28_ambos_extremos_continuos()
    {
        var l = new Losa();
        Assert.Equal(28, l.K);
        Assert.Equal(28, Losa.FactorK.AmbosExtremosContinuos);
    }

    [Fact]
    public void K_valores_validos_son_los_4_de_ACI()
    {
        var validos = Losa.FactorK.ValoresValidos;
        Assert.Equal(4, validos.Length);
        Assert.Contains(20, validos);  // simplemente apoyada
        Assert.Contains(24, validos);  // un extremo continuo
        Assert.Contains(28, validos);  // ambos extremos continuos
        Assert.Contains(10, validos);  // voladizo
    }

    // =================================================================
    // CARGAS GLOBALES (semilla del .xlsx)
    // =================================================================

    [Fact]
    public void CargasGlobales_semilla_tiene_pesos_propios_de_entrepiso_y_techo()
    {
        var c = CargasGlobales.SemillaPorDefecto();

        Assert.Equal(3, c.PesosPropiosEntrepiso.Items.Count);
        var nombresEntrepiso = c.PesosPropiosEntrepiso.Items.Select(p => p.Nombre).ToList();
        Assert.Contains("Mosaicos", nombresEntrepiso);
        Assert.Contains("Mortero",  nombresEntrepiso);
        Assert.Contains("Pañete",   nombresEntrepiso);

        Assert.Equal(3, c.PesosPropiosTecho.Items.Count);
        var nombresTecho = c.PesosPropiosTecho.Items.Select(p => p.Nombre).ToList();
        Assert.Contains("Pañete", nombresTecho);
    }

    [Fact]
    public void CargasGlobales_pesos_propios_entrepiso_dan_total_correcto()
    {
        // Valores de la hoja Cargas filas 26-28: Mosaicos 0.069 + Mortero 0.072 + Pañete 0.040 = 0.181 ton/m²
        var c = CargasGlobales.SemillaPorDefecto();
        Assert.Equal(0.181, c.PesosPropiosEntrepiso.Total, precision: 3);
    }

    [Fact]
    public void CargasGlobales_factores_default_son_ACI_318_05()
    {
        var c = CargasGlobales.SemillaPorDefecto();
        Assert.Equal(1.2, c.Factores.FactorD);
        Assert.Equal(1.6, c.Factores.FactorL);
        Assert.Equal("ACI 318-05", c.Factores.Norma);
    }

    [Theory]
    [InlineData(1.0, 0.5, 1.0 * 1.2 + 0.5 * 1.6)]   // 1.20 + 0.80 = 2.00
    [InlineData(0.5, 0.2, 0.5 * 1.2 + 0.2 * 1.6)]   // 0.60 + 0.32 = 0.92
    public void FactoresCombinacion_combinar_aplica_qu_ACI(double qd, double ql, double esperado)
    {
        var f = new FactoresCombinacion();
        Assert.Equal(esperado, f.Combinar(qd, ql), precision: 4);
    }

    [Fact]
    public void CargasVivas_default_son_R001_y_ACI()
    {
        var v = new CargasVivasPorUso();
        Assert.Equal(0.20, v.Entrepiso);
        Assert.Equal(0.40, v.BalconesEscalera);
        Assert.Equal(0.10, v.TechoPlano);
    }

    [Theory]
    [InlineData(SistemaUso.Entrepiso, 0.20)]
    [InlineData(SistemaUso.Balcon,    0.40)]
    [InlineData(SistemaUso.Techo,     0.10)]
    [InlineData(SistemaUso.Otro,      0.20)]   // fallback a Entrepiso para usos no estándar
    public void CargasVivas_resuelve_por_uso(SistemaUso uso, double esperado)
    {
        var v = new CargasVivasPorUso();
        Assert.Equal(esperado, v.Para(uso));
    }

    [Fact]
    public void TablaCargaMuerta_lookup_devuelve_fila_anterior_si_h_no_es_exacto()
    {
        // VLOOKUP TRUE: para h_eq=0.157 → debe devolver la fila h=0.15.
        var tabla = new TablaCargaMuertaPorEspesor();
        tabla.RegenerarFilas(densidad: 2.4);
        // Cada fila tiene WPropio = h*2.4 y Qd=0; el lookup le suma los pesos propios.
        var qd = tabla.LookupQd(hEq: 0.157, pesosPropiosTotal: 0.181);
        // Esperado = WPropio(h=0.15) + 0.181 = 0.36 + 0.181 = 0.541
        Assert.Equal(0.541, qd, precision: 3);
    }

    // =================================================================
    // SALIDA PERDOMO (esqueleto)
    // =================================================================

    [Fact]
    public void SalidaPerdomo_arranca_con_listas_vacias()
    {
        var s = new SalidaPerdomo();
        Assert.Empty(s.Momentos);
        Assert.Empty(s.ArmadurasXCentro);
        Assert.Empty(s.ArmadurasYCentro);
        Assert.Empty(s.ArmadurasXApoyos);
        Assert.Empty(s.ArmadurasYApoyos);
        Assert.Empty(s.LosasNoParseadas);
    }

    // =================================================================
    // VALIDACIONES SOFT (warnings)
    // =================================================================

    [Fact]
    public void EspesorInsuficiente_es_true_cuando_espesor_menor_que_hcalc()
    {
        var l = new Losa { Lx = 4, Ly = 4, Espesor = 0.10, HCalc = 0.13 };
        Assert.True(l.EspesorInsuficiente);

        l.Espesor = 0.13;
        Assert.False(l.EspesorInsuficiente);

        l.Espesor = 0.15;
        Assert.False(l.EspesorInsuficiente);
    }

    [Theory]
    // Casos 2D (ratio ≤ 2): Ln = max(Lx, Ly).
    [InlineData(7.0, 4.0,  false)]   // 2D, Ln = 7   → false
    [InlineData(8.0, 4.0,  false)]   // 2D limite, Ln = 8   → false (no es estricto >)
    [InlineData(8.5, 5.0,  true)]    // 2D, Ln = 8.5 → true
    [InlineData(10.0, 6.0, true)]    // 2D, Ln = 10  → true
    // Caso 1D (ratio > 2): Ln = min(Lx, Ly) — no dispara aunque Lx sea grande.
    [InlineData(9.0, 4.0,  false)]   // 1D, Ln = 4
    public void LnExcedeRango_se_dispara_cuando_Ln_supera_8_metros(double lx, double ly, bool esperado)
    {
        var l = new Losa { Lx = lx, Ly = ly };
        Assert.Equal(esperado, l.LnExcedeRango);
    }
}
