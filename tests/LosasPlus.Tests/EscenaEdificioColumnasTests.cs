using System.Linq;
using LosasPlus.Models;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del dibujo de columnas reales en la escena 3D (Fase I.5): cada
/// <see cref="Columna"/> aporta un segmento vertical en su posición de planta y
/// extiende la caja envolvente.
/// </summary>
public class EscenaEdificioColumnasTests
{
    [Fact]
    public void Nivel_sin_columnas_no_agrega_segmentos_verticales()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 }); // footprint por defecto → 4 aristas de piso
        Assert.Equal(4, EscenaEdificio.Construir(ed).Segmentos.Count);
    }

    [Fact]
    public void Cada_columna_aporta_un_segmento_vertical_en_su_posicion()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Columnas.Add(new Columna { Nombre = "C-1", CoordenadaX = 10, CoordenadaY = 8, Altura = 3 });
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);
        Assert.Equal(5, esc.Segmentos.Count); // 4 piso + 1 columna

        Assert.Contains(esc.Segmentos, s =>
            s.A == new System.Numerics.Vector3(10, 0, 8) &&
            s.B == new System.Numerics.Vector3(10, 3, 8));
    }

    [Fact]
    public void Las_columnas_extienden_la_caja_envolvente()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Columnas.Add(new Columna { CoordenadaX = 10, CoordenadaY = 8, Altura = 3 });
        ed.Niveles.Add(nivel);

        var esc = EscenaEdificio.Construir(ed);
        Assert.True(esc.Max.X >= 10);
        Assert.Equal(3, esc.Max.Y, 4);
        Assert.True(esc.Max.Z >= 8);
    }
}
