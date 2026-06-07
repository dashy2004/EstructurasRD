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

    [Fact]
    public void Losas_filtra_por_el_centro_del_pano()
    {
        // Eje horizontal en y=0; el centro del paño = (X+Lx/2, Y+Ly/2).
        var eje = new EjeEstructural
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(10, 0),
        };
        var losas = new List<Losa>
        {
            new Losa { Id = 1, CoordenadaX = 0, CoordenadaY = -2, Lx = 4, Ly = 4 }, // centro (2,0) → en eje
            new Losa { Id = 2, CoordenadaX = 4, CoordenadaY = -2, Lx = 4, Ly = 4 }, // centro (6,0) → en eje
            new Losa { Id = 3, CoordenadaX = 0, CoordenadaY = 5,  Lx = 4, Ly = 4 }, // centro (2,7) → lejos
        };

        var enEje = SeccionPorEje.Losas(eje, losas, tolerancia: 0.1).ToList();

        Assert.Equal(2, enEje.Count);
        Assert.DoesNotContain(enEje, l => l.Id == 3);
    }
}
