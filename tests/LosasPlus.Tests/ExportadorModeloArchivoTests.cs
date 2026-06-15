using System.IO;
using System.Text.Json;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

public class ExportadorModeloArchivoTests
{
    // 2 columnas (0,0) y (5,0) (Altura 3) + 1 viga (0,0)->(5,0):
    //   nodos: (0,0,0),(0,0,3),(5,0,0),(5,0,3) = 4 (la viga reusa 2 bases)
    //   barras: 2 columnas + 1 viga = 3
    private static Edificio PorticoMinimo()
    {
        var nivel = new Nivel { Cota = 0.0 };
        nivel.Sistemas.Add(new Sistema { Fc = 0.210, Fy = 4.200 });
        nivel.Columnas.Add(new Columna { CoordenadaX = 0, CoordenadaY = 0, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        nivel.Columnas.Add(new Columna { CoordenadaX = 5, CoordenadaY = 0, Base = 0.30, Peralte = 0.30, Altura = 3.0, Zapata = new Zapata() });
        var v = new Viga { OrigenX = 0, OrigenY = 0, AnguloGrados = 0 };
        v.Tramos.Add(new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50 });
        nivel.Vigas.Add(v);
        var ed = new Edificio();
        ed.Niveles.Add(nivel);
        return ed;
    }

    [Fact]
    public void Exporta_a_archivo_y_devuelve_resumen()
    {
        string ruta = Path.Combine(Path.GetTempPath(), $"modelo_motor_{Path.GetRandomFileName()}.json");
        try
        {
            var resumen = ExportadorModeloArchivo.Exportar(PorticoMinimo(), ruta);

            Assert.True(File.Exists(ruta));
            Assert.Equal(4, resumen.Nodos);
            Assert.Equal(3, resumen.Barras);

            string json = File.ReadAllText(ruta);
            using var doc = JsonDocument.Parse(json);
            Assert.True(doc.RootElement.TryGetProperty("nodos", out _));
        }
        finally { if (File.Exists(ruta)) File.Delete(ruta); }
    }
}
