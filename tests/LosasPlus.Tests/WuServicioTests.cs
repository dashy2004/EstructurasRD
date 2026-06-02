using System;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la conversión carga última→servicio en el predim de zapata
/// (PredimZapata.CuadradaDesdeUltima).
/// </summary>
public class WuServicioTests
{
    [Fact]
    public void Convierte_ultima_a_servicio_con_el_factor_por_defecto()
    {
        // Wu = 60, factor 1.5 → servicio 40; área 40/15, lado = sqrt(40/15).
        var z = PredimZapata.CuadradaDesdeUltima(60, 15);
        Assert.Equal(40.0, z.CargaServicio, 6);
        Assert.Equal(40.0 / 15.0, z.AreaRequerida, 6);
        Assert.Equal(Math.Sqrt(40.0 / 15.0), z.LadoCuadrada, 6);
    }

    [Fact]
    public void Factor_unitario_o_no_positivo_usa_la_carga_tal_cual()
    {
        Assert.Equal(60.0, PredimZapata.CuadradaDesdeUltima(60, 15, 1.0).CargaServicio, 6);
        Assert.Equal(60.0, PredimZapata.CuadradaDesdeUltima(60, 15, 0).CargaServicio, 6);
    }
}
