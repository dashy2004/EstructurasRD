using LosasPlus.Models;
using LosasPlus.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests de pixeles de los PNG del editor de columnas (patron DiagramaPng):
/// el diagrama P-M y la seccion transversal deben dibujarse de verdad —
/// son 2 de las 4 graficas que oxy:PlotView dejaba en blanco (F1).
/// </summary>
public class ColumnasEditorPngTests
{
    private static ColumnasEditorViewModel VmConColumna()
    {
        var ed = new Edificio();
        var niv = new Nivel();
        niv.Columnas.Add(new Columna { Id = 1, Nombre = "C-1", Base = 0.40, Peralte = 0.40, Altura = 3.0 });
        ed.Niveles.Add(niv);
        var vm = new ColumnasEditorViewModel(() => ed, () => niv);
        vm.Seleccionada = niv.Columnas[0];
        return vm;
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
    public void InteraccionPng_dibuja_la_curva_PM()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.InteraccionPng);
        // Curva de diseño = #3B82F6 = rgb(59,130,246).
        Assert.True(ContieneColor(vm.InteraccionPng!, 59, 130, 246), "El PNG de interacción debe dibujar la curva φPn-φMn (azul).");
    }

    [Fact]
    public void Sin_seleccion_InteraccionPng_es_null()
    {
        var vm = VmConColumna();
        vm.Seleccionada = null;
        Assert.Null(vm.InteraccionPng);
    }

    [Fact]
    public void SeccionColumnaPng_dibuja_estribo_y_barras()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.SeccionColumnaPng);
        Assert.True(ContieneColor(vm.SeccionColumnaPng!, 139, 0, 0), "El PNG de la sección de columna debe dibujar el estribo (DarkRed).");
        Assert.True(ContieneColor(vm.SeccionColumnaPng!, 0, 0, 139), "El PNG de la sección de columna debe dibujar las barras (DarkBlue).");
    }
}
