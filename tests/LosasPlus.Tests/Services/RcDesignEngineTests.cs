using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Rc;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="RcDesignEngine"/> (Fase 4, Iteración 1): verificación
/// analítica estricta del bloque equivalente de Whitney (ACI 318-19) contra las
/// ecuaciones cerradas de equilibrio de la sección.
/// </summary>
public class RcDesignEngineTests
{
    private const double Es = 200_000.0;   // MPa

    [Fact]
    public void SeccionSubReforzada_ControlDeTraccion_MomentoYFactores()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.60, Recubrimiento = 0.06, Fc = 28.0, Fy = 420.0,
        };
        const double areaAcero = 0.0020;   // m²

        var r = RcDesignEngine.CalcularCapacidad(seccion, areaAcero);

        // Ecuaciones del bloque de Whitney en N-mm-MPa.
        double b = 300.0, d = 540.0, asMm2 = 2000.0, fc = 28.0, fy = 420.0;
        double aEsp = asMm2 * fy / (0.85 * fc * b);              // β1 = 0.85
        double cEsp = aEsp / 0.85;
        double etEsp = 0.003 * (d - cEsp) / cEsp;
        double mnEsp = asMm2 * fy * (d - aEsp / 2.0) / 1e6;      // kN·m

        Assert.Equal(0.85, r.Beta1, precision: 6);
        Assert.True(r.AceroFluye);
        Assert.Equal(ClasificacionSeccion.TensionControlada, r.Clasificacion);
        Assert.Equal(0.90, r.FactorPhi, precision: 6);
        Assert.Equal(aEsp / 1000.0, r.BloqueCompresion, precision: 6);
        Assert.Equal(cEsp / 1000.0, r.EjeNeutro, precision: 6);
        Assert.Equal(etEsp, r.DeformacionNetaTraccion, precision: 6);
        Assert.Equal(mnEsp, r.MomentoNominal, precision: 6);
        Assert.Equal(0.90 * mnEsp, r.MomentoDiseno, precision: 6);
    }

    [Fact]
    public void Beta1_SigueLaEscaleraDeAci318()
    {
        const double area = 0.0015;
        Assert.Equal(0.85, RcDesignEngine.CalcularCapacidad(new SeccionRC { Fc = 28.0 }, area).Beta1, precision: 6);
        Assert.Equal(0.80, RcDesignEngine.CalcularCapacidad(new SeccionRC { Fc = 35.0 }, area).Beta1, precision: 6);
        Assert.Equal(0.65, RcDesignEngine.CalcularCapacidad(new SeccionRC { Fc = 60.0 }, area).Beta1, precision: 6);
    }

    [Fact]
    public void SeccionEnTransicion_InterpolaLinealmenteElFactorPhi()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.50, Recubrimiento = 0.05, Fc = 28.0, Fy = 420.0,
        };

        var r = RcDesignEngine.CalcularCapacidad(seccion, 0.0030);

        Assert.Equal(ClasificacionSeccion.Transicion, r.Clasificacion);
        Assert.True(r.FactorPhi > 0.65 && r.FactorPhi < 0.90);

        double ey = 420.0 / Es;
        double phiEsp = 0.65 + 0.25 * (r.DeformacionNetaTraccion - ey) / 0.003;
        Assert.Equal(phiEsp, r.FactorPhi, precision: 6);
    }

    [Fact]
    public void SeccionPocoReforzada_NoCumpleLaCuantiaMinima()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.60, Recubrimiento = 0.06, Fc = 28.0, Fy = 420.0,
        };

        var r = RcDesignEngine.CalcularCapacidad(seccion, 0.0003);

        Assert.False(r.CumpleCuantiaMinima);
        Assert.True(r.Cuantia < r.CuantiaMinima);
        Assert.NotEmpty(r.Advertencias);
    }

    [Fact]
    public void SeccionSobreReforzada_ElAceroNoFluye_RamaCuadratica()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.50, Recubrimiento = 0.05, Fc = 28.0, Fy = 420.0,
        };

        var r = RcDesignEngine.CalcularCapacidad(seccion, 0.0050);

        Assert.False(r.AceroFluye);
        Assert.False(r.CumpleLimiteDeformacion);
        Assert.True(r.DeformacionNetaTraccion < 420.0 / Es);
    }

    [Fact]
    public void Equilibrio_LaCompresionDelBloqueIgualaLaTraccion()
    {
        var seccion = new SeccionRC
        {
            Base = 0.35, Peralte = 0.65, Recubrimiento = 0.06, Fc = 30.0, Fy = 420.0,
        };

        var r = RcDesignEngine.CalcularCapacidad(seccion, 0.0025);

        // C = 0.85·f'c·a·b  (kN: f'c en MPa, a y b en m → MN·1000).
        double compresion = 0.85 * seccion.Fc * r.BloqueCompresion * seccion.Base * 1000.0;
        // T = Mn / jd.
        double traccion = r.MomentoNominal / r.BrazoPalanca;
        Assert.Equal(compresion, traccion, precision: 3);
    }

    [Fact]
    public void SeccionBalanceada_LaDeformacionNetaIgualaLaFluencia()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.60, Recubrimiento = 0.06, Fc = 28.0, Fy = 420.0,
        };
        const double b = 0.30, d = 0.54, fc = 28.0, fy = 420.0, beta1 = 0.85, ecu = 0.003;
        double ey = fy / Es;
        double rhoBalanceado = 0.85 * beta1 * (fc / fy) * (ecu / (ecu + ey));
        double areaBalanceada = rhoBalanceado * b * d;   // m²

        var r = RcDesignEngine.CalcularCapacidad(seccion, areaBalanceada);

        Assert.Equal(ey, r.DeformacionNetaTraccion, precision: 6);
    }

    [Fact]
    public void SobrecargaConRefuerzo_EligeElAceroSegunElSignoDelMomento()
    {
        var seccion = new SeccionRC
        {
            Base = 0.30, Peralte = 0.60, Recubrimiento = 0.06, Fc = 28.0, Fy = 420.0,
        };
        var refuerzo = new RefuerzoLongitudinal { AreaAceroBot = 0.0025, AreaAceroTop = 0.0010 };

        var positivo = RcDesignEngine.CalcularCapacidad(seccion, refuerzo, momentoPositivo: true);
        var negativo = RcDesignEngine.CalcularCapacidad(seccion, refuerzo, momentoPositivo: false);

        Assert.Equal(RcDesignEngine.CalcularCapacidad(seccion, 0.0025).MomentoNominal,
                     positivo.MomentoNominal, precision: 6);
        Assert.Equal(RcDesignEngine.CalcularCapacidad(seccion, 0.0010).MomentoNominal,
                     negativo.MomentoNominal, precision: 6);
        Assert.True(positivo.MomentoNominal > negativo.MomentoNominal);
    }

    [Fact]
    public void SeccionYRefuerzoDelTramo_SobrevivenUnRoundtripDeSerializacion()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var viga = new Viga { Nombre = "V-RC" };
        var tramo = new TramoViga { Longitud = 6.0 };
        tramo.Seccion.Base = 0.35;
        tramo.Seccion.Peralte = 0.65;
        tramo.Seccion.Fc = 35.0;
        tramo.Refuerzo.AreaAceroBot = 0.0024;
        tramo.Refuerzo.AreaAceroTop = 0.0012;
        viga.Tramos.Add(tramo);
        proyecto.Edificios[0].Niveles[0].Vigas.Add(viga);

        var restaurado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(proyecto));
        var t = restaurado.Edificios[0].Niveles[0].Vigas[0].Tramos[0];

        Assert.Equal(0.35, t.Seccion.Base, precision: 6);
        Assert.Equal(0.65, t.Seccion.Peralte, precision: 6);
        Assert.Equal(35.0, t.Seccion.Fc, precision: 6);
        Assert.Equal(0.0024, t.Refuerzo.AreaAceroBot, precision: 6);
        Assert.Equal(0.0012, t.Refuerzo.AreaAceroTop, precision: 6);
    }
}
