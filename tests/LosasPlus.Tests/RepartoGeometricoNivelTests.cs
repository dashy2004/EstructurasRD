using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la agregación por viga del reparto geométrico de un nivel (Fase
/// J.16): una viga compartida por dos paños suma las cargas de ambos bordes.
/// </summary>
public class RepartoGeometricoNivelTests
{
    private static Viga Viga(string nom, double ox, double oy, double len, double ang)
    {
        var v = new Viga { Nombre = nom, OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = len });
        return v;
    }

    [Fact]
    public void Una_viga_compartida_suma_los_bordes_de_ambos_panos()
    {
        // Dos paños 4×4 apilados en Y (q=10): cada borde w=10, F=40.
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);

        var vCompartida = Viga("V-shared", 0, 4, 4, 0); // borde y=4 (top de A, bottom de B)
        var vInferior = Viga("V-bottom", 0, 0, 4, 0);   // borde y=0 (solo A)
        nivel.Vigas.Add(vCompartida);
        nivel.Vigas.Add(vInferior);

        var agg = RepartoGeometrico.AsignarNivel(nivel);

        var compartida = agg.First(c => ReferenceEquals(c.Viga, vCompartida));
        Assert.Equal(20, compartida.CargaLineal, 3);
        Assert.Equal(80, compartida.FuerzaTotal, 3);

        var inferior = agg.First(c => ReferenceEquals(c.Viga, vInferior));
        Assert.Equal(10, inferior.CargaLineal, 3);
        Assert.Equal(40, inferior.FuerzaTotal, 3);
    }

    [Fact]
    public void Nivel_vacio_no_asigna()
    {
        Assert.Empty(RepartoGeometrico.AsignarNivel(new Nivel()));
    }
}
