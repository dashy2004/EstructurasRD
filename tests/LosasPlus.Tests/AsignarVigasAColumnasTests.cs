using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del descenso geométrico viga→columna (Fase J): la fuerza de cada viga
/// se reparte (mitad/mitad) a las columnas cercanas a sus extremos y se acumula
/// por columna.
/// </summary>
public class AsignarVigasAColumnasTests
{
    private static Viga Viga(double ox, double oy, double len, double ang)
    {
        var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = len });
        return v;
    }

    private static Columna Col(string nom, double x, double y) =>
        new() { Nombre = nom, CoordenadaX = x, CoordenadaY = y };

    private static Nivel NivelConVigasYColumnas(out Columna c00, out Columna c04, out Columna c40, out Columna c44)
    {
        var nivel = new Nivel { Cota = 0 };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 0 });
        s.Losas.Add(new Losa { Lx = 4, Ly = 4, Carga = 10, CoordenadaX = 0, CoordenadaY = 4 });
        nivel.Sistemas.Add(s);
        nivel.Vigas.Add(Viga(0, 4, 4, 0));   // vShared (F=80)
        nivel.Vigas.Add(Viga(0, 0, 4, 0));   // vInferior (F=40)
        nivel.Vigas.Add(Viga(0, 0, 4, 90));  // vLeft (F=40, solo paño A)

        c00 = Col("C00", 0, 0); c40 = Col("C40", 4, 0);
        c04 = Col("C04", 0, 4); c44 = Col("C44", 4, 4);
        foreach (var c in new[] { c00, c40, c04, c44 }) nivel.Columnas.Add(c);
        return nivel;
    }

    [Fact]
    public void Acumula_por_columna_y_conserva_la_carga()
    {
        var nivel = NivelConVigasYColumnas(out var c00, out var c04, out var c40, out var c44);
        var axial = RepartoGeometrico.AsignarVigasAColumnas(nivel);
        double Ax(Columna c) => axial.First(a => ReferenceEquals(a.Columna, c)).CargaAxial;

        Assert.Equal(60, Ax(c04), 3); // vShared (40) + vLeft (20)
        Assert.Equal(40, Ax(c00), 3); // vInferior (20) + vLeft (20)
        Assert.Equal(40, Ax(c44), 3); // solo vShared
        Assert.Equal(20, Ax(c40), 3); // solo vInferior
        Assert.Equal(160, axial.Sum(a => a.CargaAxial), 3); // 80 + 40 + 40
    }

    [Fact]
    public void Columna_lejana_no_recibe_carga()
    {
        var nivel = NivelConVigasYColumnas(out _, out _, out _, out _);
        var lejana = Col("Clejos", 100, 100);
        nivel.Columnas.Add(lejana);

        var axial = RepartoGeometrico.AsignarVigasAColumnas(nivel);
        Assert.DoesNotContain(axial, a => ReferenceEquals(a.Columna, lejana));
    }

    [Fact]
    public void Nivel_sin_columnas_no_asigna()
    {
        Assert.Empty(RepartoGeometrico.AsignarVigasAColumnas(new Nivel()));
    }
}
