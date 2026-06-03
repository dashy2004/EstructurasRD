using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del descenso de carga a columnas y zapatas (Fase J.7): reparto
/// equitativo + predimensionado de zapata por columna, y la cadena con la
/// bajada de cargas.
/// </summary>
public class DescensoColumnasTests
{
    private static List<Columna> CuatroColumnas() => new()
    {
        new Columna { Nombre = "C-1" }, new Columna { Nombre = "C-2" },
        new Columna { Nombre = "C-3" }, new Columna { Nombre = "C-4" },
    };

    [Fact]
    public void Reparte_equitativo_y_predimensiona_cada_zapata()
    {
        var cols = CuatroColumnas();
        var res = DescensoColumnas.RepartirEquitativo(cols, 600, 15); // 150 c/u → área 10 → lado √10

        Assert.Equal(4, res.Count);
        Assert.All(res, r => Assert.Equal(150, r.CargaAxial, 6));
        Assert.All(res, r => Assert.Equal(Math.Sqrt(10), r.LadoZapata, 6));
        Assert.All(cols, c =>
        {
            Assert.NotNull(c.Zapata);
            Assert.Equal(10, c.Zapata!.AreaContacto, 6);
        });
        Assert.Equal("Z-C-1", cols[0].Zapata!.Nombre);
    }

    [Fact]
    public void Actualiza_una_zapata_existente_sin_duplicar()
    {
        var col = new Columna { Nombre = "C-X", Zapata = new Zapata { Nombre = "previa", Ancho = 9, Largo = 9 } };
        DescensoColumnas.RepartirEquitativo(new[] { col }, 100, 25); // 4 m² → lado 2

        Assert.Equal("previa", col.Zapata!.Nombre);
        Assert.Equal(2, col.Zapata.Ancho, 6);
        Assert.Equal(2, col.Zapata.Largo, 6);
    }

    [Fact]
    public void Sin_columnas_o_carga_no_positiva_no_hace_nada()
    {
        Assert.Empty(DescensoColumnas.RepartirEquitativo(new List<Columna>(), 600, 15));
        Assert.Empty(DescensoColumnas.RepartirEquitativo(CuatroColumnas(), 0, 15));
    }

    [Fact]
    public void Encadena_con_la_bajada_de_cargas()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var nivel = new Nivel { Cota = cota };
            var s = new Sistema();
            s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 150 por nivel
            nivel.Sistemas.Add(s);
            ed.Niveles.Add(nivel);
        }

        double pBase = BajadaCargas.Acumular(ed).CargaEnBase; // 300
        var res = DescensoColumnas.RepartirEquitativo(CuatroColumnas(), pBase, 15); // 75 c/u

        Assert.Equal(300, pBase, 6);
        Assert.All(res, r => Assert.Equal(75, r.CargaAxial, 6));
    }

    [Fact]
    public void PuDemandaKN_convierte_el_axial_descendido_de_ton_a_kN()
    {
        // Cierra el lazo losa→bajada→columna: el axial descendido (ton) se entrega
        // como Pu en kN, la unidad que el chequeo P-M de la columna (PuKN) espera.
        var carga = new CargaColumna(new Columna { Nombre = "C-1" }, CargaAxial: 10.0, LadoZapata: 0.5);

        var puKN = DescensoColumnas.PuDemandaKN(carga);

        Assert.Equal(98.0665, puKN, 4);   // 10 tonf × 9.80665
    }
}
