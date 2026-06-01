using System;
using LosasPlus.Models;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del predimensionado de zapata (Fase J.3): A = P / q_adm y el lado de
/// la zapata cuadrada, incluyendo el encadenado con la bajada de cargas.
/// </summary>
public class PredimZapataTests
{
    [Fact]
    public void Area_es_carga_entre_presion_y_lado_es_su_raiz()
    {
        var z = PredimZapata.Cuadrada(600, 15);
        Assert.Equal(40, z.AreaRequerida, 6);
        Assert.Equal(Math.Sqrt(40), z.LadoCuadrada, 6);

        var z2 = PredimZapata.Cuadrada(100, 25);
        Assert.Equal(4, z2.AreaRequerida, 6);
        Assert.Equal(2, z2.LadoCuadrada, 6);
    }

    [Fact]
    public void Carga_o_presion_no_positivas_dan_dimensiones_nulas()
    {
        Assert.Equal(0, PredimZapata.Cuadrada(0, 15).AreaRequerida, 6);
        Assert.Equal(0, PredimZapata.Cuadrada(600, 0).AreaRequerida, 6);
        Assert.Equal(0, PredimZapata.Cuadrada(-10, 15).LadoCuadrada, 6);
    }

    [Fact]
    public void Encadena_con_la_bajada_de_cargas()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var n = new Nivel { Cota = cota };
            var s = new Sistema();
            s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 150 por nivel
            n.Sistemas.Add(s);
            ed.Niveles.Add(n);
        }

        double pBase = BajadaCargas.Acumular(ed).CargaEnBase; // 300
        var z = PredimZapata.Cuadrada(pBase, 15);

        Assert.Equal(300, pBase, 6);
        Assert.Equal(20, z.AreaRequerida, 6);
    }
}
