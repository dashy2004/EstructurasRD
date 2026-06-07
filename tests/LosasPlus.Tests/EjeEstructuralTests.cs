using System;
using LosasPlus.Models.Cad;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del eje/rejilla estructural (WS2): geometría de la recta del eje y la
/// distancia perpendicular de un punto a él — base para "cortar" secciones del 3D.
/// </summary>
public class EjeEstructuralTests
{
    [Fact]
    public void DistanciaA_es_la_perpendicular_al_eje()
    {
        // Eje vertical en x=0 (de (0,0) a (0,10)); el punto (3,5) está a 3 m.
        var eje = new EjeEstructural
        {
            Etiqueta = "A",
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(0, 10),
        };

        Assert.Equal(3.0, eje.DistanciaA(new PuntoCad(3, 5)), 6);
    }

    [Fact]
    public void DistanciaA_eje_degenerado_es_la_distancia_al_punto()
    {
        var eje = new EjeEstructural
        {
            PuntoInicio = new PuntoCad(2, 2),
            PuntoFin = new PuntoCad(2, 2),
        };

        Assert.Equal(Math.Sqrt(2), eje.DistanciaA(new PuntoCad(3, 3)), 6);
    }

    [Fact]
    public void EstaEnSeccion_es_true_dentro_de_la_tolerancia()
    {
        // Eje en x=0; un punto a 0.1 m entra con tolerancia 0.2, uno a 0.5 m no.
        var eje = new EjeEstructural
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(0, 10),
        };

        Assert.True(eje.EstaEnSeccion(new PuntoCad(0.1, 5), tolerancia: 0.2));
        Assert.False(eje.EstaEnSeccion(new PuntoCad(0.5, 5), tolerancia: 0.2));
    }
}
