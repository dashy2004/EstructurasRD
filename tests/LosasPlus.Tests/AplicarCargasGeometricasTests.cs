using System.Linq;
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Transmision;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la aplicación de cargas geométricas a las vigas (Fase J.17):
/// distribuye la carga agregada como CargaElemento, es idempotente, y la viga
/// resultante resuelve con el motor (lazo losa→viga→análisis).
/// </summary>
public class AplicarCargasGeometricasTests
{
    private static Viga Viga(string nom, double ox, double oy, double len, double ang)
    {
        var v = new Viga { Nombre = nom, OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = len });
        return v;
    }

    private static (Nivel nivel, Viga compartida) NivelDosPanos()
    {
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        var compartida = Viga("V-shared", 0, 4, 4, 0); // borde y=4, w=20
        nivel.Vigas.Add(compartida);
        nivel.Vigas.Add(Viga("V-bottom", 0, 0, 4, 0));  // borde y=0, w=10
        return (nivel, compartida);
    }

    [Fact]
    public void Aplica_la_carga_agregada_como_distribuida_y_es_idempotente()
    {
        var (nivel, compartida) = NivelDosPanos();

        Assert.Equal(2, RepartoGeometrico.AplicarCargasGeometricas(nivel, "D"));

        bool EsD(CargaElemento c) => c.Tipo == TipoCargaElemento.Distribuida && c.CodigoCaso == "D";
        var cargas = compartida.Tramos[0].Cargas.Where(EsD).ToList();
        Assert.Single(cargas);
        Assert.Equal(20, cargas[0].Magnitud, 3);

        RepartoGeometrico.AplicarCargasGeometricas(nivel, "D"); // re-aplicar
        Assert.Single(compartida.Tramos[0].Cargas.Where(EsD));  // no duplica
    }

    [Fact]
    public void La_viga_cargada_resuelve_y_las_reacciones_suman_la_carga_total()
    {
        var (nivel, compartida) = NivelDosPanos();
        RepartoGeometrico.AplicarCargasGeometricas(nivel, "D");

        compartida.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Fijo));
        compartida.Apoyos.Add(new ApoyoViga(4, TipoApoyo.Fijo));

        var cp = new CombinacionesProyecto();
        cp.Casos.Add(new CasoCarga { Codigo = "D", Nombre = "Muerta", Tipo = TipoCasoCarga.Muerta });
        var combo = new CombinacionCarga { Nombre = "D", Tipo = TipoCombinacion.Ultima };
        combo["D"] = 1.0;
        cp.Combinaciones.Add(combo);

        var res = VigaContinuaEngine.Resolver(compartida, cp);

        Assert.False(res.EsInestable);
        double sumaReacciones = res.Combinaciones[0].Reacciones.Sum(r => r.Valor);
        Assert.Equal(80, sumaReacciones, 1); // w·L = 20·4
    }
}
