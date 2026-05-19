using System;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="MotorCotas"/> — el cálculo de cotas dinámicas (las
/// distancias dₙ relativas a las losas vecinas) de la FASE A v1.1.
/// </summary>
public class MotorCotasTests
{
    [Fact]
    public void Sin_vecinas_no_genera_cotas()
    {
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new RectM[0]);
        Assert.Empty(cotas);
    }

    [Fact]
    public void Una_vecina_sin_relacion_no_genera_cotas()
    {
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new[] { new RectM(20, 20, 3, 3) });
        Assert.Empty(cotas);
    }

    [Fact]
    public void Una_vecina_con_desfase_parcial_genera_cotas_con_el_valor_del_desfase()
    {
        // La activa y la vecina comparten cara en X; la vecina está corrida 1 m en Y.
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new[] { new RectM(4, 1, 3, 3) });
        // Dos desfases (superior e inferior) de 1 m → dos cotas verticales.
        Assert.Equal(2, cotas.Count);
        Assert.All(cotas, c => Assert.Equal(EjeCota.Vertical, c.Eje));
        Assert.All(cotas, c => Assert.Equal(1.0, c.Valor, precision: 6));
    }

    [Fact]
    public void Una_vecina_con_holgura_genera_una_cota_de_separacion()
    {
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new[] { new RectM(4.5, 0, 3, 3) });
        var cota = Assert.Single(cotas);
        Assert.Equal(EjeCota.Horizontal, cota.Eje);
        Assert.Equal(0.5, cota.Valor, precision: 6);
    }

    [Fact]
    public void El_valor_de_la_cota_es_la_distancia_entre_inicio_y_fin()
    {
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new[] { new RectM(4, 1, 3, 3) });
        Assert.NotEmpty(cotas);
        Assert.All(cotas, c => Assert.Equal(Math.Abs(c.Fin - c.Inicio), c.Valor, precision: 6));
    }

    [Fact]
    public void Las_cotas_paralelas_se_escalonan()
    {
        // Genera dos cotas verticales (desfase superior + inferior).
        var cotas = MotorCotas.Calcular(new RectM(0, 0, 4, 3), new[] { new RectM(4, 1, 3, 3) });
        Assert.Equal(2, cotas.Count);
        Assert.NotEqual(cotas[0].CoordenadaLinea, cotas[1].CoordenadaLinea);
    }
}
