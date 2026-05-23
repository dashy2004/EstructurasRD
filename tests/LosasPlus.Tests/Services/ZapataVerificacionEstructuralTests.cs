using LosasPlus.Cargas;
using LosasPlus.Services;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="ZapataDesignEngine.VerificarEstructuraZapata"/>
/// (Fase 6, Iteración 3): verificación analítica de las tres fallas
/// estructurales ELU (flexión, cortante unidimensional, punzonamiento
/// biaxial) según ACI 318-19.
/// </summary>
public class ZapataVerificacionEstructuralTests
{
    private const double FcMaterial = 28.0;   // MPa
    private const double FyMaterial = 420.0;  // MPa

    /// <summary>Zapata de control 2.0×2.0×0.50 m, columna 0.30×0.30.</summary>
    private static ZapataAislada ZapataDeControl(double H = 0.50)
    {
        var z = new ZapataAislada { Id = 1, Nombre = "Z-ELU" };
        z.Dimensiones.LargoB = 2.0;
        z.Dimensiones.AnchoL = 2.0;
        z.Dimensiones.EspesorH = H;
        z.Dimensiones.ProfundidadDesplante = 1.50;
        z.Dimensiones.PeralteColumna = 0.30;
        z.Dimensiones.AnchoColumna = 0.30;
        z.Suelo.PresionAdmisible = 200.0;
        z.Suelo.PesoEspecificoSuelo = 18.0;
        return z;
    }

    /// <summary>Construye un proyecto con una sola combinación de tipo `Ultima`.</summary>
    private static CombinacionesProyecto UnaCombinacionUltima(string caso, double factor)
    {
        var cp = new CombinacionesProyecto();
        var combo = new CombinacionCarga { Nombre = "U", Tipo = TipoCombinacion.Ultima };
        combo[caso] = factor;
        cp.Combinaciones.Add(combo);
        return cp;
    }

    [Fact]
    public void ZapataAdecuada_TodosLosRatiosMenoresQueUno()
    {
        var z = ZapataDeControl(H: 0.50);
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 200.0 });
        var combos = UnaCombinacionUltima("D", 1.2);

        var r = ZapataDesignEngine.VerificarEstructuraZapata(z, combos, FcMaterial, FyMaterial);

        Assert.True(r.EstructuraConforme,
            $"Se esperaba EstructuraConforme=true; ratios = F:{r.RatioFlexion:0.##}, V:{r.RatioCortante:0.##}, P:{r.RatioPunzonamiento:0.##}.");
        Assert.True(r.RatioFlexion < 1.0);
        Assert.True(r.RatioCortante < 1.0);
        Assert.True(r.RatioPunzonamiento < 1.0);
    }

    [Fact]
    public void ZapataDelgadaConCargaSeveraP_FallaPorPunzonamientoYCortante()
    {
        // Zapata delgada (H = 0.18 → d ≈ 0.105 m) con carga muy severa.
        var z = ZapataDeControl(H: 0.18);
        z.Dimensiones.LargoB = 1.5;
        z.Dimensiones.AnchoL = 1.5;
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 4000.0 });
        var combos = UnaCombinacionUltima("D", 1.2);

        var r = ZapataDesignEngine.VerificarEstructuraZapata(z, combos, FcMaterial, FyMaterial);

        Assert.False(r.EstructuraConforme);
        Assert.True(r.RatioPunzonamiento > 1.0,
            $"Se esperaba ratio de punzonamiento > 1; valor real = {r.RatioPunzonamiento:0.###}.");
        Assert.True(r.RatioCortante > 1.0,
            $"Se esperaba ratio de cortante > 1; valor real = {r.RatioCortante:0.###}.");
    }

    [Fact]
    public void SoloCombinacionesUltima_SonProcesadas()
    {
        var z = ZapataDeControl();
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 500.0 });
        var cp = new CombinacionesProyecto();
        var servicio = new CombinacionCarga { Nombre = "D", Tipo = TipoCombinacion.Servicio };
        servicio["D"] = 1.0;
        cp.Combinaciones.Add(servicio);
        var ultima = new CombinacionCarga { Nombre = "1.4D", Tipo = TipoCombinacion.Ultima };
        ultima["D"] = 1.4;
        cp.Combinaciones.Add(ultima);

        var r = ZapataDesignEngine.VerificarEstructuraZapata(z, cp, FcMaterial, FyMaterial);

        // La combinación de servicio se filtra; sólo "1.4D" genera resultados.
        Assert.Equal("1.4D", r.NombreCombinacionCritica);
    }

    [Fact]
    public void PerimetroCriticoB0_EsConcentricoAlPedestal()
    {
        var z = ZapataDeControl();   // H=0.50 → d=0.425
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 500.0 });
        var combos = UnaCombinacionUltima("D", 1.2);

        var r = ZapataDesignEngine.VerificarEstructuraZapata(z, combos, FcMaterial, FyMaterial);

        // Perímetro = (b_col + d) × (l_col + d) = (0.30 + 0.425) × (0.30 + 0.425).
        const double esperado = 0.30 + 0.425;
        Assert.Equal(esperado, r.LadoPerimetroX, precision: 6);
        Assert.Equal(esperado, r.LadoPerimetroY, precision: 6);
    }
}
