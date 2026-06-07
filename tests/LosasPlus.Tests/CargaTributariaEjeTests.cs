using System.Collections.Generic;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de <see cref="GeneradorVigas.CargaLinealTributariaDelEje"/> (item G): la
/// carga lineal uniforme equivalente (kN/m) sobre un eje a partir de la carga Wu
/// de las losas que cruza (mitad del paño a esta viga).
/// </summary>
public class CargaTributariaEjeTests
{
    private static EjeEstructural EjeHorizontal() => new()
    {
        PuntoInicio = new PuntoCad(0, 0),
        PuntoFin = new PuntoCad(10, 0),
    };

    [Fact]
    public void Suma_la_mitad_del_Wu_de_las_losas_del_eje_y_lo_reparte_en_kN_por_m()
    {
        var eje = EjeHorizontal();   // largo 10 m
        var losas = new List<Losa>
        {
            new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 1.0 }, // centro (2,0) en eje
            new Losa { Id = 2, CoordenadaX = 4, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 1.0 }, // centro (6,0) en eje
            new Losa { Id = 3, CoordenadaX = 0, CoordenadaY = 5,  Lx = 4, Ly = 4, Carga = 1.0 }, // lejos → excluida
        };

        double w = GeneradorVigas.CargaLinealTributariaDelEje(eje, losas, tolerancia: 0.1);

        // Σ (Wu·Lx·Ly·0.5) = (1·16·0.5) + (1·16·0.5) = 16 ton; ×9.80665 / 10 m = 15.6906 kN/m
        Assert.Equal(16.0 * GeneradorVigas.TonF_a_KN / 10.0, w, 6);
        Assert.Equal(15.6906, w, 3);
    }

    [Fact]
    public void Sin_losas_en_el_eje_es_cero()
    {
        var eje = EjeHorizontal();
        var losas = new List<Losa>
        {
            new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = 50, Lx = 4, Ly = 4, Carga = 1.0 }, // centro lejos
        };
        Assert.Equal(0.0, GeneradorVigas.CargaLinealTributariaDelEje(eje, losas, 0.1), 9);
    }

    [Fact]
    public void Ignora_losas_sin_carga_o_geometria_invalida()
    {
        var eje = EjeHorizontal();
        var losas = new List<Losa>
        {
            new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 0.0 }, // Wu=0 → ignora
            new Losa { Id = 2, CoordenadaX = 4, CoordenadaY = -2, Lx = 4, Ly = 4, Carga = 2.0 }, // aporta
        };
        // Solo la losa 2: 2·16·0.5 = 16 ton → mismo valor que el caso base.
        Assert.Equal(16.0 * GeneradorVigas.TonF_a_KN / 10.0, GeneradorVigas.CargaLinealTributariaDelEje(eje, losas, 0.1), 6);
    }

    [Fact]
    public void Eje_degenerado_o_nulo_es_cero()
    {
        var degenerado = new EjeEstructural { PuntoInicio = new PuntoCad(3, 3), PuntoFin = new PuntoCad(3, 3) };
        var losas = new List<Losa> { new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = 0, Lx = 4, Ly = 4, Carga = 1.0 } };
        Assert.Equal(0.0, GeneradorVigas.CargaLinealTributariaDelEje(degenerado, losas, 1.0), 9);
        Assert.Equal(0.0, GeneradorVigas.CargaLinealTributariaDelEje(EjeHorizontal(), null!, 0.1), 9);
    }
}
