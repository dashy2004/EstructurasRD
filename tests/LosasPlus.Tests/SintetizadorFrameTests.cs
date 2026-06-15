using System.Linq;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class SintetizadorFrameTests
{
    // Pórtico 1 vano: cuadrado 5×5, 4 columnas (Altura 3) en un nivel Cota 0,
    // 4 vigas que cierran el anillo a esa cota (origen→extremo coinciden con bases de columna).
    private static Edificio PorticoUnVano()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });

        (double x, double y)[] esq = { (0, 0), (5, 0), (5, 5), (0, 5) };
        foreach (var (x, y) in esq)
            nivel.Columnas.Add(new Columna { CoordenadaX = x, CoordenadaY = y, Base = 0.30, Peralte = 0.30, Altura = 3.0 });

        (double ox, double oy, double ang, double len)[] vigas =
        {
            (0, 0,   0, 5), (5, 0,  90, 5), (5, 5, 180, 5), (0, 5, 270, 5),
        };
        foreach (var (ox, oy, ang, len) in vigas)
        {
            var v = new Viga { OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
            v.Tramos.Add(new TramoViga { Longitud = len, Base = 0.30, Peralte = 0.50 });
            nivel.Vigas.Add(v);
        }

        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Sintetiza_portico_1vano_con_nodos_deduplicados()
    {
        var (nodos, elementos) = SintetizadorFrame.Sintetizar(PorticoUnVano());

        Assert.Equal(8, nodos.Count);            // 4 bases (z=0) + 4 topes (z=3); vigas reusan las bases
        Assert.Equal(8, elementos.Count);        // 4 columnas + 4 vigas
        Assert.All(elementos, e => Assert.NotEqual(e.NodoI, e.NodoJ));
        Assert.Equal(4, elementos.Count(e => e.EsColumna));
        Assert.Equal(4, elementos.Count(e => !e.EsColumna));
        Assert.All(elementos, e => Assert.Equal(0.210, e.Fc, 9));
    }
}
