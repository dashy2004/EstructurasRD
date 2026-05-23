using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="ZapataDesignEngine"/> (Fase 6, Iteración 1):
/// verificación analítica de la distribución lineal de presiones de contacto
/// en una zapata aislada bajo flexocompresión biaxial, contrastada con las
/// ecuaciones cerradas <c>q = P/A ± Mx/Wx ± My/Wy</c>.
/// </summary>
public class ZapataDesignEngineTests
{
    private const double PesoEspecificoConcreto = 24.0;   // kN/m³, igual al motor

    /// <summary>
    /// Zapata de control 2.0×2.0×0.50 m, Df=1.5 m, columna 0.30×0.30,
    /// terreno q_adm=200 kN/m², γ_suelo=18 kN/m³.
    /// </summary>
    private static ZapataAislada ZapataDeControl()
    {
        var z = new ZapataAislada { Id = 1, Nombre = "Z-1" };
        z.Dimensiones.LargoB = 2.0;
        z.Dimensiones.AnchoL = 2.0;
        z.Dimensiones.EspesorH = 0.50;
        z.Dimensiones.ProfundidadDesplante = 1.50;
        z.Dimensiones.PeralteColumna = 0.30;
        z.Dimensiones.AnchoColumna = 0.30;
        z.Suelo.PresionAdmisible = 200.0;
        z.Suelo.PesoEspecificoSuelo = 18.0;
        return z;
    }

    /// <summary>Carga permanente esperada de <see cref="ZapataDeControl"/>.</summary>
    private static double PpermanenteDeControl()
    {
        const double B = 2.0, L = 2.0, H = 0.50, Df = 1.50;
        const double gSuelo = 18.0, aCol = 0.30 * 0.30;
        double pesoZapata = PesoEspecificoConcreto * B * L * H;
        double pesoTerreno = gSuelo * (Df - H) * (B * L - aCol);
        return pesoZapata + pesoTerreno;
    }

    /// <summary>Construye un <c>CombinacionesProyecto</c> con una sola combo de servicio.</summary>
    private static CombinacionesProyecto UnaCombinacionServicio(string caso, double factor)
    {
        var cp = new CombinacionesProyecto();
        var combo = new CombinacionCarga { Nombre = "S", Tipo = TipoCombinacion.Servicio };
        combo[caso] = factor;
        cp.Combinaciones.Add(combo);
        return cp;
    }

    [Fact]
    public void CargaConcentricaPura_ProducePresionUniformeIgualA_PtSobreA()
    {
        var z = ZapataDeControl();
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 1000.0, Mx = 0.0, My = 0.0 });
        var combos = UnaCombinacionServicio("D", 1.0);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, combos);

        Assert.Single(r.PorCombinacion);
        var p = r.PorCombinacion[0];
        double qEsperado = (PpermanenteDeControl() + 1000.0) / (2.0 * 2.0);
        Assert.Equal(qEsperado, p.Q1, precision: 3);
        Assert.Equal(qEsperado, p.Q2, precision: 3);
        Assert.Equal(qEsperado, p.Q3, precision: 3);
        Assert.Equal(qEsperado, p.Q4, precision: 3);
        Assert.False(p.HayDespegue);
    }

    [Fact]
    public void FlexionUniaxialX_GeneraPresionesLinealesEnLaDireccionY()
    {
        var z = ZapataDeControl();
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 1000.0, Mx = 80.0, My = 0.0 });
        var combos = UnaCombinacionServicio("D", 1.0);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, combos);

        var p = r.PorCombinacion[0];
        // Los lados +y (q1, q2) son iguales; los lados −y (q3, q4) son iguales.
        Assert.Equal(p.Q1, p.Q2, precision: 6);
        Assert.Equal(p.Q3, p.Q4, precision: 6);
        // Mx positivo comprime el lado +y → q1 > q3.
        Assert.True(p.Q1 > p.Q3);
    }

    [Fact]
    public void FlexionBiaxial_GeneraMaximoEnUnaEsquinaYMinimoEnLaOpuesta()
    {
        var z = ZapataDeControl();
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 1000.0, Mx = 80.0, My = 80.0 });
        var combos = UnaCombinacionServicio("D", 1.0);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, combos);

        var p = r.PorCombinacion[0];
        // q1 es la esquina (+B/2,+L/2) — máxima con Mx+ y My+.
        Assert.Equal(p.Maxima, p.Q1, precision: 6);
        // q3 es la esquina (−B/2,−L/2) — mínima.
        Assert.Equal(p.Minima, p.Q3, precision: 6);
        // q2 y q4 son intermedios e iguales por simetría (Mx = My).
        Assert.Equal(p.Q2, p.Q4, precision: 6);
        Assert.True(p.Q2 < p.Q1 && p.Q2 > p.Q3);
    }

    [Fact]
    public void PresionAdmisibleExcedida_SeMarcaLaBanderaYInvalidaEsEstable()
    {
        var z = ZapataDeControl();
        // P enorme para sobrepasar q_adm = 200 kN/m² (q ≈ 25 000 kN/m²).
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 100_000.0 });
        var combos = UnaCombinacionServicio("D", 1.0);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, combos);

        Assert.True(r.PorCombinacion[0].ExcedePresionAdmisible);
        Assert.False(r.EsEstable);
    }

    [Fact]
    public void Despegue_CalculaPorcentajeDeAreaEnTraccion()
    {
        var z = ZapataDeControl();
        // Mx grande para que q_min < 0 en los lados −y.
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 500.0, Mx = 600.0, My = 0.0 });
        var combos = UnaCombinacionServicio("D", 1.0);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, combos);

        var p = r.PorCombinacion[0];
        Assert.True(p.HayDespegue);
        Assert.True(p.Minima < 0.0);
        Assert.True(p.PorcentajeAreaEnTraccion > 0.0 && p.PorcentajeAreaEnTraccion < 1.0);
    }

    [Fact]
    public void SoloCombinacionesDeServicio_SonProcesadas()
    {
        var z = ZapataDeControl();
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 500.0 });
        var cp = new CombinacionesProyecto();
        var servicio = new CombinacionCarga { Nombre = "D + L", Tipo = TipoCombinacion.Servicio };
        servicio["D"] = 1.0;
        cp.Combinaciones.Add(servicio);
        var ultima = new CombinacionCarga { Nombre = "1.4D", Tipo = TipoCombinacion.Ultima };
        ultima["D"] = 1.4;
        cp.Combinaciones.Add(ultima);

        var r = ZapataDesignEngine.AnalizarPresionesDeContacto(z, cp);

        Assert.Single(r.PorCombinacion);
        Assert.Equal("D + L", r.PorCombinacion[0].NombreCombinacion);
    }

    [Fact]
    public void LasZapatasDelNivel_SobrevivenUnRoundtripDeSerializacion()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var z = new ZapataAislada { Id = 7, Nombre = "Z-RT" };
        z.Dimensiones.LargoB = 1.80;
        z.Dimensiones.AnchoL = 2.20;
        z.Dimensiones.EspesorH = 0.40;
        z.Dimensiones.ProfundidadDesplante = 1.80;
        z.Suelo.PresionAdmisible = 180.0;
        z.Suelo.PesoEspecificoSuelo = 17.5;
        z.Cargas.Add(new CargaZapata { CodigoCaso = "D", P = 600.0, Mx = 40.0, My = 20.0 });
        z.Cargas.Add(new CargaZapata { CodigoCaso = "L", P = 200.0, Mx = 0.0, My = 10.0 });
        proyecto.Edificios[0].Niveles[0].Zapatas.Add(z);

        var restaurado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(proyecto));
        var zr = restaurado.Edificios[0].Niveles[0].Zapatas[0];

        Assert.Equal("Z-RT", zr.Nombre);
        Assert.Equal(1.80, zr.Dimensiones.LargoB, precision: 6);
        Assert.Equal(2.20, zr.Dimensiones.AnchoL, precision: 6);
        Assert.Equal(0.40, zr.Dimensiones.EspesorH, precision: 6);
        Assert.Equal(1.80, zr.Dimensiones.ProfundidadDesplante, precision: 6);
        Assert.Equal(180.0, zr.Suelo.PresionAdmisible, precision: 6);
        Assert.Equal(17.5, zr.Suelo.PesoEspecificoSuelo, precision: 6);
        Assert.Equal(2, zr.Cargas.Count);
        Assert.Equal("D", zr.Cargas[0].CodigoCaso);
        Assert.Equal(600.0, zr.Cargas[0].P, precision: 6);
        Assert.Equal(40.0, zr.Cargas[0].Mx, precision: 6);
        Assert.Equal(20.0, zr.Cargas[0].My, precision: 6);
        Assert.Equal("L", zr.Cargas[1].CodigoCaso);
        Assert.Equal(10.0, zr.Cargas[1].My, precision: 6);
    }
}
