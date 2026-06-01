using System.Linq;
using LosasPlus.Models;
using LosasPlus.Persistence;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la posición en planta de la losa (Fase J.13): valor por defecto y
/// round-trip de serialización (aditivo).
/// </summary>
public class LosaCoordenadasTests
{
    [Fact]
    public void Losa_nueva_tiene_posicion_en_el_origen()
    {
        var losa = new Losa();
        Assert.Equal(0, losa.CoordenadaX, 9);
        Assert.Equal(0, losa.CoordenadaY, 9);
    }

    [Fact]
    public void La_posicion_en_planta_sobrevive_al_round_trip()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        var s = new Sistema();
        s.Losas.Add(new Losa { Id = 1, Lx = 4, Ly = 5, CoordenadaX = 2.5, CoordenadaY = 3 });
        p.Edificios[0].Niveles[0].Sistemas.Add(s);

        var recargado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(p));
        var losa = recargado.Edificios[0].Niveles[0].Sistemas.Last().Losas[0];

        Assert.Equal(2.5, losa.CoordenadaX, 9);
        Assert.Equal(3, losa.CoordenadaY, 9);
        Assert.Equal(4, losa.Lx, 9);
        Assert.Equal(5, losa.Ly, 9);
    }
}
