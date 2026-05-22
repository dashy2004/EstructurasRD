using System;
using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="VigaContinuaEngine"/> (Fase 3, Iteración 1): verificación
/// analítica estricta del Método de Rigidez Directa contra resultados cerrados
/// de la teoría de vigas.
/// </summary>
public class VigaContinuaEngineTests
{
    private const double E = 2.5e7;       // kN/m²
    private const double I = 0.002;       // m⁴
    private const double EI = E * I;      // 50 000 kN·m²

    /// <summary>Combinación con un único término — un caso mayorado por un factor.</summary>
    private static CombinacionesProyecto Combinacion(string nombre,
        params (string caso, double factor)[] terminos)
    {
        var cp = new CombinacionesProyecto();
        var combo = new CombinacionCarga { Nombre = nombre };
        foreach (var (caso, factor) in terminos)
            combo[caso] = factor;
        cp.Combinaciones.Add(combo);
        return cp;
    }

    private static CombinacionesProyecto UnaCombinacion(string caso, double factor)
        => Combinacion("combo", (caso, factor));

    /// <summary>Punto del diagrama en la abscisa <paramref name="x"/>.</summary>
    private static PuntoDiagrama PuntoEn(ResultadoCombinacion r, double x)
        => r.Diagrama.First(p => Math.Abs(p.X - x) < 1e-6);

    /// <summary>Viga de dos tramos iguales sobre tres apoyos fijos, con UDL en ambos tramos.</summary>
    private static Viga VigaDosTramosIguales(double longitudTramo, double w)
    {
        var viga = new Viga { Nombre = "Dos tramos" };
        for (int i = 0; i < 2; i++)
        {
            var t = new TramoViga { Longitud = longitudTramo, ModuloElasticidad = E, Inercia = I };
            t.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, w, "D"));
            viga.Tramos.Add(t);
        }
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(longitudTramo, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(2 * longitudTramo, TipoApoyo.Fijo));
        return viga;
    }

    [Fact]
    public void VigaSimple_CargaUniforme_MomentoYDeflexionDeCentro()
    {
        const double w = 24.0, L = 6.0;
        var viga = new Viga { Nombre = "Simplemente apoyada" };
        var tramo = new TramoViga { Longitud = L, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, w, "D"));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(L, TipoApoyo.Fijo));

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        Assert.False(r.EsInestable);
        var combo = r.Combinaciones[0];
        Assert.Equal(w * L * L / 8.0, PuntoEn(combo, L / 2).Momento, precision: 6);
        Assert.Equal(5.0 * w * L * L * L * L / (384.0 * EI),
                     PuntoEn(combo, L / 2).Deflexion, precision: 6);
    }

    [Fact]
    public void VigaSimple_CargaPuntualCentral_MomentoYDeflexionDeCentro()
    {
        const double P = 100.0, L = 6.0;
        var viga = new Viga { Nombre = "Simplemente apoyada" };
        var tramo = new TramoViga { Longitud = L, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Puntual, P, "D", posicion: L / 2));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(L, TipoApoyo.Fijo));

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        var combo = r.Combinaciones[0];
        Assert.Equal(P * L / 4.0, PuntoEn(combo, L / 2).Momento, precision: 6);
        Assert.Equal(P * L * L * L / (48.0 * EI), PuntoEn(combo, L / 2).Deflexion, precision: 6);
    }

    [Fact]
    public void Voladizo_CargaEnPunta_MomentoEmpotramientoYDeflexionPunta()
    {
        const double P = 30.0, L = 4.0;
        var viga = new Viga { Nombre = "Voladizo" };
        var tramo = new TramoViga { Longitud = L, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Puntual, P, "D", posicion: L));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Empotrado));   // extremo libre en X=L

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        Assert.False(r.EsInestable);
        var combo = r.Combinaciones[0];
        Assert.Equal(-P * L, PuntoEn(combo, 0.0).Momento, precision: 6);
        Assert.Equal(P * L * L * L / (3.0 * EI), PuntoEn(combo, L).Deflexion, precision: 6);
    }

    [Fact]
    public void DosTramosIguales_CargaUniforme_MomentoApoyoCentral_wL2_8()
    {
        const double w = 24.0, L = 6.0;
        var r = VigaContinuaEngine.Resolver(VigaDosTramosIguales(L, w), UnaCombinacion("D", 1.0));

        Assert.False(r.EsInestable);
        var combo = r.Combinaciones[0];
        // Momento (hogging) en el apoyo central: −wL²/8.
        Assert.Equal(-w * L * L / 8.0, PuntoEn(combo, L).Momento, precision: 6);
        // Reacciones clásicas: 3wL/8, 10wL/8, 3wL/8.
        Assert.Equal(3.0 * w * L / 8.0, combo.Reacciones[0].Valor, precision: 6);
        Assert.Equal(10.0 * w * L / 8.0, combo.Reacciones[1].Valor, precision: 6);
        Assert.Equal(3.0 * w * L / 8.0, combo.Reacciones[2].Valor, precision: 6);
    }

    [Fact]
    public void Combinacion_MayoraVariosCasos_AlSumarLosFactores()
    {
        const double L = 6.0, wMuerta = 10.0, wViva = 20.0;
        var viga = new Viga { Nombre = "Simplemente apoyada" };
        var tramo = new TramoViga { Longitud = L, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, wMuerta, "D"));
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, wViva, "L"));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(L, TipoApoyo.Fijo));

        var r = VigaContinuaEngine.Resolver(viga,
            Combinacion("1.2D + 1.6L", ("D", 1.2), ("L", 1.6)));

        // w efectiva = 1.2·10 + 1.6·20 = 44 kN/m → M centro = 44·L²/8.
        double wEfectiva = 1.2 * wMuerta + 1.6 * wViva;
        Assert.Equal(wEfectiva * L * L / 8.0, PuntoEn(r.Combinaciones[0], L / 2).Momento, precision: 6);
    }

    [Fact]
    public void Equilibrio_SumaDeReaccionesIgualaLaCargaAplicada()
    {
        const double w = 24.0, L = 6.0;
        var r = VigaContinuaEngine.Resolver(VigaDosTramosIguales(L, w), UnaCombinacion("D", 1.0));

        double sumaReacciones = r.Combinaciones[0].Reacciones.Sum(re => re.Valor);
        Assert.Equal(w * 2.0 * L, sumaReacciones, precision: 6);
    }

    [Fact]
    public void ApoyoElastico_ConRigidezMuyAlta_SeComportaComoApoyoFijo()
    {
        const double w = 24.0, L = 6.0;
        var viga = new Viga { Nombre = "Apoyo elástico" };
        var tramo = new TramoViga { Longitud = L, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, w, "D"));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(L, TipoApoyo.Elastico) { RigidezResorte = 1e10 });

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        Assert.False(r.EsInestable);
        var combo = r.Combinaciones[0];
        // Un resorte casi rígido recupera el apoyo fijo: M centro ≈ wL²/8, R ≈ wL/2.
        Assert.Equal(w * L * L / 8.0, PuntoEn(combo, L / 2).Momento, precision: 3);
        Assert.Equal(w * L / 2.0, combo.Reacciones[1].Valor, precision: 3);
    }

    [Fact]
    public void ApoyosInsuficientes_DevuelveResultadoInestableSinLanzarExcepcion()
    {
        var viga = new Viga { Nombre = "Mecanismo" };
        viga.Tramos.Add(new TramoViga { Longitud = 6.0, ModuloElasticidad = E, Inercia = I });
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));   // un solo apoyo → mecanismo

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        Assert.True(r.EsInestable);
        Assert.False(string.IsNullOrEmpty(r.Mensaje));
        Assert.Empty(r.Combinaciones);
    }

    [Fact]
    public void Voladizo_Intermedio_MomentoContinuoEnElApoyo()
    {
        const double P = 15.0, voladizo = 2.0;
        var viga = new Viga { Nombre = "Con voladizo" };
        var tramo = new TramoViga { Longitud = 8.0, ModuloElasticidad = E, Inercia = I };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Puntual, P, "D", posicion: 8.0));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(6.0, TipoApoyo.Fijo));   // voladizo [6, 8]

        var r = VigaContinuaEngine.Resolver(viga, UnaCombinacion("D", 1.0));

        Assert.False(r.EsInestable);
        var enApoyo = r.Combinaciones[0].Diagrama
            .Where(p => Math.Abs(p.X - 6.0) < 1e-6).ToList();
        Assert.Equal(2, enApoyo.Count);   // nodo compartido por dos elementos
        Assert.Equal(enApoyo[0].Momento, enApoyo[1].Momento, precision: 6);   // M continuo
        Assert.Equal(-P * voladizo, enApoyo[0].Momento, precision: 6);
    }
}
