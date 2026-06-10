using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del predimensionado geométrico de zapatas (Fase J.19): dimensiona la
/// zapata de cada columna con su carga axial REAL del descenso viga→columna.
/// </summary>
public class PredimensionarGeometricoTests
{
    private static Viga Viga(double ox, double oy, double len, double ang)
    {
        var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = len });
        return v;
    }

    private static Columna Col(string nom, double x, double y) =>
        new() { Nombre = nom, CoordenadaX = x, CoordenadaY = y };

    [Fact]
    public void Dimensiona_cada_zapata_con_la_axial_real()
    {
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        nivel.Vigas.Add(Viga(0, 4, 4, 0));   // F80
        nivel.Vigas.Add(Viga(0, 0, 4, 0));   // F40
        nivel.Vigas.Add(Viga(0, 0, 4, 90));  // F40
        var c04 = Col("C04", 0, 4);          // axial real = 60
        foreach (var c in new[] { Col("C00", 0, 0), Col("C40", 4, 0), c04, Col("C44", 4, 4) })
            nivel.Columnas.Add(c);

        // Default Wu→servicio (factor 1.5): Wu=60 → servicio 40 → área 40/15.
        var res = DescensoColumnas.PredimensionarGeometrico(nivel, 15);

        Assert.Equal(4, res.Count);
        var r04 = res.First(r => ReferenceEquals(r.Columna, c04));
        Assert.Equal(60, r04.CargaAxial, 3);  // axial reportada = Wu
        Assert.Equal(System.Math.Sqrt(40.0 / 15.0), r04.LadoZapata, 3);
        Assert.NotNull(c04.Zapata);
        Assert.Equal(40.0 / 15.0, c04.Zapata!.AreaContacto, 3);
        Assert.Equal("Z-C04", c04.Zapata.Nombre);

        // factor 1.0 (sin conversión) → Wu directo: 60/15 = 4 m² → lado 2.
        var sinConv = DescensoColumnas.PredimensionarGeometrico(nivel, 15, 1.0);
        Assert.Equal(2, sinConv.First(r => ReferenceEquals(r.Columna, c04)).LadoZapata, 3);
    }

    [Fact]
    public void Sin_vigas_no_dimensiona()
    {
        var nivel = new Nivel();
        nivel.Columnas.Add(Col("X", 0, 0));
        Assert.Empty(DescensoColumnas.PredimensionarGeometrico(nivel, 15));
    }

    [Fact]
    public void PuDemandaGeometricoKN_da_la_axial_tributaria_de_la_columna_en_kN()
    {
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        nivel.Vigas.Add(Viga(0, 4, 4, 0));
        nivel.Vigas.Add(Viga(0, 0, 4, 0));
        nivel.Vigas.Add(Viga(0, 0, 4, 90));
        var c04 = Col("C04", 0, 4);
        var lejana = Col("CX", 20, 20);   // ninguna viga apoya cerca
        foreach (var c in new[] { Col("C00", 0, 0), Col("C40", 4, 0), c04, Col("C44", 4, 4), lejana })
            nivel.Columnas.Add(c);

        Assert.Equal(60 * DescensoColumnas.KN_por_Ton,
                     DescensoColumnas.PuDemandaGeometricoKN(nivel, c04), 3);
        Assert.Equal(0.0, DescensoColumnas.PuDemandaGeometricoKN(nivel, lejana), 6);
    }
}
