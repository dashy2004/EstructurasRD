using System.Linq;
using System.Numerics;
using LosasPlus.Models;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del dibujo de zapatas en la escena 3D. Una columna se dibuja como caja
/// extruida (12 aristas, K.6); con zapata aporta además el recuadro (4 aristas)
/// de la huella de la zapata en la base.
/// </summary>
public class EscenaEdificioZapatasTests
{
    private static Edificio ConColumna(Zapata? zapata)
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        nivel.Columnas.Add(new Columna { CoordenadaX = 10, CoordenadaY = 8, Altura = 3, Zapata = zapata });
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Columna_sin_zapata_no_dibuja_recuadro()
    {
        // 4 aristas de piso + 12 aristas de la caja de columna (sin recuadro de zapata).
        Assert.Equal(16, EscenaEdificio.Construir(ConColumna(null)).Segmentos.Count);
    }

    [Fact]
    public void Columna_con_zapata_agrega_recuadro_de_cuatro_aristas()
    {
        var ed = ConColumna(new Zapata { Ancho = 2, Largo = 3 });
        var esc = EscenaEdificio.Construir(ed);

        // 4 piso + 12 caja de columna + 4 zapata.
        Assert.Equal(20, esc.Segmentos.Count);

        // Borde inferior de la huella: de (9,0,6.5) a (11,0,6.5).
        Assert.Contains(esc.Segmentos, s =>
            s.A == new Vector3(9, 0, 6.5f) && s.B == new Vector3(11, 0, 6.5f));
    }
}
