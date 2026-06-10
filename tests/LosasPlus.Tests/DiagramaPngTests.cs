using System.Linq;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Rendering;
using LosasPlus.Vigas;
using LosasPlus.ViewModels.Vigas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Prueba que el render a PNG (<see cref="DiagramaPng"/>) dibuja efectivamente
/// las series de cortante y deflexión — las mismas que el control oxy:PlotView
/// deja en blanco por su bug de render. Si estos píxeles están, el workaround B
/// (mostrar el PNG en un &lt;Image&gt;) hace visibles los diagramas.
/// </summary>
public class DiagramaPngTests
{
    private static Viga VigaResoluble()
    {
        var viga = new Viga { Id = 1, Nombre = "V-1" };
        var tramo = new TramoViga { Longitud = 6.0, ModuloElasticidad = 2.5e7, Inercia = 0.002 };
        tramo.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 20.0, "D"));
        viga.Tramos.Add(tramo);
        viga.Apoyos.Add(new ApoyoViga(0.0, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(6.0, TipoApoyo.Fijo));
        return viga;
    }

    /// <summary>¿Hay algún píxel cercano (±tol por canal) al color RGB dado?</summary>
    private static bool ContieneColor(byte[] png, int r, int g, int b, int tol = 24)
    {
        using var img = Image.Load<Rgba32>(png);
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                var p = img[x, y];
                if (System.Math.Abs(p.R - r) <= tol &&
                    System.Math.Abs(p.G - g) <= tol &&
                    System.Math.Abs(p.B - b) <= tol)
                    return true;
            }
        return false;
    }

    [Fact]
    public async Task Render_dibuja_cortante_y_deflexion()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        proyecto.Edificios[0].Niveles[0].Vigas.Add(VigaResoluble());
        int n = 0;
        var vm = new VigaEditorViewModel(proyecto, () => n++, () => proyecto.Edificios[0].Niveles[0]);
        await vm.RecalcularAsync();

        var pngEsfuerzos = DiagramaPng.Render(vm.ModeloEsfuerzos, 1000, 380);
        var pngDeflexion = DiagramaPng.Render(vm.ModeloDeflexion, 1000, 380);

        Assert.NotNull(pngEsfuerzos);
        Assert.NotNull(pngDeflexion);
        // Cortante = rgb(14,125,184); deflexión = rgb(46,125,50).
        Assert.True(ContieneColor(pngEsfuerzos!, 14, 125, 184), "El PNG de esfuerzos debe dibujar la curva de cortante (azul).");
        Assert.True(ContieneColor(pngDeflexion!, 46, 125, 50), "El PNG de deflexión debe dibujar la curva δ (verde).");
    }
}
