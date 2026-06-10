using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="TopologiaPlanta"/> — la clave de invalidación del caché
/// de layout del lienzo CAD (UI1.1, auditoría del caché): el hash cambia con
/// TODO input del <see cref="LayoutSolver"/> (posición, ancla, dimensiones,
/// bordes) y con nada más.
/// </summary>
public class TopologiaPlantaTests
{
    private static Sistema Base()
    {
        var s = new Sistema();
        s.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 3, CoordenadaX = 1, CoordenadaY = 2, Anclada = true });
        s.Losas.Add(new Losa { Id = 2, Lx = 3, Ly = 3 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });
        return s;
    }

    [Fact]
    public void Hash_cambia_si_una_losa_se_mueve_en_planta()
    {
        var s = Base();
        int antes = TopologiaPlanta.Hash(s);

        s.Losas[0].CoordenadaX = 9.0;   // p.ej. drag en Planta 2D

        Assert.NotEqual(antes, TopologiaPlanta.Hash(s));
    }

    [Fact]
    public void Hash_cambia_si_cambia_el_ancla()
    {
        // Anclada-en-(0,0) ≠ libre-en-(0,0): el solver las trata distinto.
        var s = Base();
        int antes = TopologiaPlanta.Hash(s);

        s.Losas[1].Anclada = true;

        Assert.NotEqual(antes, TopologiaPlanta.Hash(s));
    }

    [Fact]
    public void Hash_cambia_si_cambian_dimensiones_o_bordes()
    {
        var s = Base();
        int antes = TopologiaPlanta.Hash(s);

        s.Losas[1].Lx = 5.0;
        int conLx = TopologiaPlanta.Hash(s);
        Assert.NotEqual(antes, conLx);

        s.BordesY.Add(new BordeAdic { BI = 1, BJ = 2 });
        Assert.NotEqual(conLx, TopologiaPlanta.Hash(s));
    }

    [Fact]
    public void Hash_estable_ante_cambios_sin_efecto_en_la_posicion()
    {
        var s = Base();
        int antes = TopologiaPlanta.Hash(s);

        s.Losas[0].Carga = 9.99;
        s.Losas[0].Espesor = 0.25;
        s.Losas[0].Tipo = 21;

        Assert.Equal(antes, TopologiaPlanta.Hash(s));
    }
}
