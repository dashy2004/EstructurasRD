using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del selector de secciones por eje (WS2-colección): qué elementos quedan
/// "cortados" por un <see cref="EjeEstructural"/> según su centro.
/// </summary>
public class SeccionPorEjeTests
{
    [Fact]
    public void Columnas_filtra_las_que_caen_en_la_seccion_del_eje()
    {
        // Eje vertical en x=0; columnas a 0.1 m entran con tol 0.2, la de 1.0 m no.
        var eje = new EjeEstructural
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(0, 10),
        };
        var columnas = new List<Columna>
        {
            new Columna { Nombre = "C1", CoordenadaX = 0.1, CoordenadaY = 2 },
            new Columna { Nombre = "C2", CoordenadaX = 0.1, CoordenadaY = 5 },
            new Columna { Nombre = "C3", CoordenadaX = 1.0, CoordenadaY = 5 },
        };

        var enEje = SeccionPorEje.Columnas(eje, columnas, tolerancia: 0.2).ToList();

        Assert.Equal(2, enEje.Count);
        Assert.DoesNotContain(enEje, c => c.Nombre == "C3");
    }
}
