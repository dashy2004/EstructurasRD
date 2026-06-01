using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del reparto geométrico losa→viga (Fase J.15): asignación de la carga
/// de cada borde del paño a la viga colineal y solapada.
/// </summary>
public class RepartoGeometricoTests
{
    private static Viga Viga(double ox, double oy, double len, double ang)
    {
        var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = len });
        return v;
    }

    // Losa 4×6 en (0,0), q=10 → corto(len4): w=10, F=40 ; largo(len6): w=80/6, F=80.
    private static Losa Losa46() => new() { Lx = 4, Ly = 6, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 };

    [Fact]
    public void Asigna_el_borde_inferior_a_la_viga_colineal()
    {
        var asign = RepartoGeometrico.AsignarLosaAVigas(Losa46(), new[] { Viga(0, 0, 4, 0) });
        Assert.Single(asign);
        Assert.Equal(10, asign[0].CargaLineal, 3);
        Assert.Equal(40, asign[0].FuerzaTotal, 3);
    }

    [Fact]
    public void Asigna_bordes_distintos_a_sus_vigas()
    {
        var vBottom = Viga(0, 0, 4, 0);
        var vLeft = Viga(0, 0, 6, 90);
        var asign = RepartoGeometrico.AsignarLosaAVigas(Losa46(), new[] { vBottom, vLeft });

        Assert.Equal(2, asign.Count);
        var izq = asign.First(a => ReferenceEquals(a.Viga, vLeft));
        Assert.Equal(80.0 / 6.0, izq.CargaLineal, 3);
        Assert.Equal(80, izq.FuerzaTotal, 3);
    }

    [Fact]
    public void No_asigna_vigas_no_colineales_ni_sin_solape()
    {
        Assert.Empty(RepartoGeometrico.AsignarLosaAVigas(Losa46(), new[] { Viga(0, 100, 4, 0) }));   // lejana
        Assert.Empty(RepartoGeometrico.AsignarLosaAVigas(Losa46(), new[] { Viga(10, 0, 4, 0) }));    // colineal sin solape
    }

    [Fact]
    public void Losa_invalida_no_asigna()
    {
        Assert.Empty(RepartoGeometrico.AsignarLosaAVigas(
            new Losa { Lx = 0, Ly = 6, Carga = 10 }, new[] { Viga(0, 0, 4, 0) }));
    }
}
