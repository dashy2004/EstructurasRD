using LosasPlus.Models;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la bajada de cargas por niveles (Fase J.2): acumulación de la carga
/// gravitatoria descendiendo por cota hasta la base.
/// </summary>
public class BajadaCargasTests
{
    private static Nivel Niv(double cota, double lx, double ly, double q)
    {
        var n = new Nivel { Cota = cota, Nombre = $"Nivel {cota}" };
        var s = new Sistema();
        s.Losas.Add(new Losa { Lx = lx, Ly = ly, Carga = q });
        n.Sistemas.Add(s);
        return n;
    }

    [Fact]
    public void Edificio_nulo_o_sin_niveles_da_resultado_vacio()
    {
        Assert.Equal(0, BajadaCargas.Acumular(null).CargaEnBase, 6);
        Assert.Empty(BajadaCargas.Acumular(new Edificio()).Niveles);
    }

    [Fact]
    public void Acumula_de_arriba_hacia_abajo_por_cota()
    {
        var ed = new Edificio();
        ed.Niveles.Add(Niv(0, 2, 5, 30)); // 300, añadido primero (desordenado)
        ed.Niveles.Add(Niv(6, 2, 5, 10)); // 100
        ed.Niveles.Add(Niv(3, 4, 5, 10)); // 200

        var bj = BajadaCargas.Acumular(ed);

        // Orden: cota 6 (tope) → 3 → 0 (base).
        Assert.Equal(6, bj.Niveles[0].Cota, 6);
        Assert.Equal(0, bj.Niveles[2].Cota, 6);

        Assert.Equal(100, bj.Niveles[0].CargaAcumulada, 6);
        Assert.Equal(300, bj.Niveles[1].CargaAcumulada, 6);
        Assert.Equal(600, bj.Niveles[2].CargaAcumulada, 6);
        Assert.Equal(600, bj.CargaEnBase, 6);
    }

    [Fact]
    public void PesoLosas_suma_todas_las_losas_de_todos_los_sistemas()
    {
        var nivel = new Nivel { Cota = 0 };
        var s1 = new Sistema(); s1.Losas.Add(new Losa { Lx = 2, Ly = 5, Carga = 10 }); // 100
        var s2 = new Sistema(); s2.Losas.Add(new Losa { Lx = 1, Ly = 5, Carga = 10 }); // 50
        nivel.Sistemas.Add(s1);
        nivel.Sistemas.Add(s2);

        Assert.Equal(150, BajadaCargas.PesoLosas(nivel), 6);
    }
}
