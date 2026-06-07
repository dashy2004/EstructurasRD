using System;
using System.Linq;
using LosasPlus.Calculo;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Paso 4 (ejes): el generador crea la rejilla A,B,C / 1,2,3 a partir de las
/// columnas, alimentando <c>Edificio.Ejes</c> que <c>PlantaCanvas</c> ya dibuja.
/// </summary>
public class GeneradorEjesTests
{
    private static Columna Col(double x, double y) => new() { CoordenadaX = x, CoordenadaY = y };

    [Fact]
    public void DesdeColumnas_grid_2x2_da_2_verticales_y_2_horizontales()
    {
        var cols = new[] { Col(0, 0), Col(5, 0), Col(0, 4), Col(5, 4) };

        var ejes = GeneradorEjes.DesdeColumnas(cols);

        Assert.Equal(4, ejes.Count);
        Assert.Equal(new[] { "1", "2", "A", "B" },
            ejes.Select(e => e.Etiqueta).OrderBy(s => s).ToArray());

        var ejeA = ejes.Single(e => e.Etiqueta == "A");      // vertical en X=0
        Assert.Equal(0, ejeA.PuntoInicio.X, 3);
        Assert.Equal(0, ejeA.PuntoFin.X, 3);
        Assert.True(ejeA.PuntoFin.Y > ejeA.PuntoInicio.Y);   // recorre en Y
    }

    [Fact]
    public void DesdeColumnas_agrupa_columnas_cercanas_en_un_solo_eje()
    {
        // 0.0 y 0.1 caen en la misma línea (tol 0.25); 5.0 es otra.
        var cols = new[] { Col(0.0, 0), Col(0.1, 0), Col(5.0, 0) };

        var ejes = GeneradorEjes.DesdeColumnas(cols, toleranciaM: 0.25);

        var verticales = ejes.Where(e => Math.Abs(e.PuntoInicio.X - e.PuntoFin.X) < 1e-6).ToList();
        Assert.Equal(2, verticales.Count);
    }

    [Fact]
    public void DesdeColumnas_sin_columnas_no_genera_ejes()
        => Assert.Empty(GeneradorEjes.DesdeColumnas(Array.Empty<Columna>()));
}
