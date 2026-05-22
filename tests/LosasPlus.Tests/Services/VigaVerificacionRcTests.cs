using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Rc;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="RcDesignEngine.VerificarVigaCompleta"/> (Fase 4,
/// Iteración 3): la verificación demanda/capacidad a flexión a lo largo de una
/// viga, contrastada con la distribución de momentos cerrada de la teoría de
/// vigas continuas.
/// </summary>
public class VigaVerificacionRcTests
{
    private const double E = 2.5e7;   // kN/m²
    private const double I = 0.002;   // m⁴

    /// <summary>Combinación de un único caso «D» mayorado por <paramref name="factor"/>.</summary>
    private static CombinacionesProyecto UnaCombinacion(string caso, double factor)
    {
        var cp = new CombinacionesProyecto();
        var combo = new CombinacionCarga { Nombre = "U" };
        combo[caso] = factor;
        cp.Combinaciones.Add(combo);
        return cp;
    }

    /// <summary>Fija en el tramo una sección RC conocida y un refuerzo simétrico.</summary>
    private static void ConfigurarSeccion(TramoViga tramo, double areaAcero)
    {
        tramo.Seccion.Base = 0.30;
        tramo.Seccion.Peralte = 0.60;
        tramo.Seccion.Recubrimiento = 0.06;
        tramo.Seccion.Fc = 28.0;
        tramo.Seccion.Fy = 420.0;
        tramo.Refuerzo.AreaAceroBot = areaAcero;
        tramo.Refuerzo.AreaAceroTop = areaAcero;
    }

    /// <summary>Viga de dos tramos iguales sobre tres apoyos fijos, con UDL «D».</summary>
    private static Viga VigaDosTramosRc(double largoTramo, double w, double areaAcero)
    {
        var viga = new Viga { Nombre = "Viga RC dos tramos" };
        for (int i = 0; i < 2; i++)
        {
            var t = new TramoViga { Longitud = largoTramo, ModuloElasticidad = E, Inercia = I };
            t.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, w, "D"));
            ConfigurarSeccion(t, areaAcero);
            viga.Tramos.Add(t);
        }
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(largoTramo, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(2.0 * largoTramo, TipoApoyo.Fijo));
        return viga;
    }

    /// <summary>Viga simplemente apoyada de un tramo, con UDL «D».</summary>
    private static Viga VigaUnTramoRc(double largo, double w, double areaAcero)
    {
        var viga = new Viga { Nombre = "Viga RC un tramo" };
        var t = new TramoViga { Longitud = largo, ModuloElasticidad = E, Inercia = I };
        t.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, w, "D"));
        ConfigurarSeccion(t, areaAcero);
        viga.Tramos.Add(t);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(largo, TipoApoyo.Fijo));
        return viga;
    }

    /// <summary>Resuelve la viga, exige que sea estable y verifica su envolvente.</summary>
    private static VerificacionViga Verificar(Viga viga, CombinacionesProyecto combos)
    {
        var resultado = VigaContinuaEngine.Resolver(viga, combos);
        Assert.False(resultado.EsInestable);
        Assert.NotNull(resultado.Envolvente);
        return RcDesignEngine.VerificarVigaCompleta(viga, resultado.Envolvente!);
    }

    [Fact]
    public void DosTramos_CargaPesadaYAceroModesto_ElRatioMaximoCaeEnElApoyoCentral()
    {
        const double L = 6.0, w = 60.0;
        // φMn ≈ 118 kN·m frente a un momento de apoyo wL²/8 = 270 kN·m.
        var viga = VigaDosTramosRc(L, w, areaAcero: 0.0006);

        var v = Verificar(viga, UnaCombinacion("D", 1.0));

        Assert.False(v.Conforme);
        Assert.True(v.RatioMaximo > 1.0);

        var critico = v.Puntos.OrderByDescending(p => p.RatioDemandaCapacidad).First();
        Assert.Equal(L, critico.X, precision: 6);       // la estación crítica es el apoyo central
        Assert.True(critico.MomentoUltimo < 0.0);       // allí gobierna el momento negativo
    }

    [Fact]
    public void DosTramos_ConAceroAmplio_LaVigaEsConforme()
    {
        const double L = 6.0, w = 60.0;
        // φMn ≈ 364 kN·m supera con holgura el momento de apoyo de 270 kN·m.
        var viga = VigaDosTramosRc(L, w, areaAcero: 0.0020);

        var v = Verificar(viga, UnaCombinacion("D", 1.0));

        Assert.True(v.Conforme);
        Assert.True(v.RatioMaximo <= 1.0);
        Assert.NotEmpty(v.Puntos);
        Assert.All(v.Puntos, p => Assert.True(p.RatioDemandaCapacidad <= 1.0));
    }

    [Fact]
    public void TramoSinAcero_ConDemanda_ReportaRatioInfinitoYNoConforme()
    {
        var viga = VigaUnTramoRc(largo: 6.0, w: 40.0, areaAcero: 0.0);

        var v = Verificar(viga, UnaCombinacion("D", 1.0));

        Assert.False(v.Conforme);
        Assert.True(double.IsPositiveInfinity(v.RatioMaximo));
        Assert.Contains(v.Puntos, p => double.IsPositiveInfinity(p.RatioDemandaCapacidad));
    }

    [Fact]
    public void VigaSimple_CargaUniforme_ElRatioMaximoCaeEnElCentroDelVano()
    {
        const double L = 6.0, w = 40.0;
        var viga = VigaUnTramoRc(L, w, areaAcero: 0.0020);

        var v = Verificar(viga, UnaCombinacion("D", 1.0));

        var critico = v.Puntos.OrderByDescending(p => p.RatioDemandaCapacidad).First();
        Assert.Equal(L / 2.0, critico.X, precision: 6);  // la estación crítica es el centro del vano
        Assert.True(critico.MomentoUltimo > 0.0);        // allí gobierna el momento positivo
        Assert.True(v.RatioMaximo > 0.0);
    }
}
