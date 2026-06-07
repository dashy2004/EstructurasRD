using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del cableado UI del item F: al seleccionar una columna válida el VM
/// expone el armado longitudinal real (nº de barras = 2·nx+2·ny−4 y As total) y
/// construye el dibujo de la sección; sin selección, ambos quedan en null.
/// </summary>
public class ColumnasEditorArmadoTests
{
    private static (Edificio ed, Nivel nivel) UnNivel()
    {
        var ed = new Edificio();
        var nivel = new Nivel { Cota = 0 };
        ed.Niveles.Add(nivel);
        return (ed, nivel);
    }

    [Fact]
    public void Seleccionar_columna_valida_expone_armado_longitudinal_y_seccion()
    {
        var (ed, nivel) = UnNivel();
        var col = new Columna { Nombre = "C-1", Base = 0.30, Peralte = 0.40, Altura = 3.0 };
        nivel.Columnas.Add(col);
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel);

        vm.Seleccionada = col;

        // Defaults BarrasX = BarrasY = 3, barra #8 → 2·3 + 2·3 − 4 = 8 barras.
        Assert.NotNull(vm.DisenoActual);
        Assert.NotNull(vm.ArmadoLongitudinal);
        Assert.Contains("8 #8", vm.ArmadoLongitudinal);
        Assert.Contains("cm²", vm.ArmadoLongitudinal);
        Assert.NotNull(vm.ModeloSeccionColumna);
    }

    [Fact]
    public void Sin_seleccion_el_armado_y_la_seccion_quedan_en_null()
    {
        var (ed, nivel) = UnNivel();
        var col = new Columna { Nombre = "C-1", Base = 0.30, Peralte = 0.40, Altura = 3.0 };
        nivel.Columnas.Add(col);
        var vm = new ColumnasEditorViewModel(() => ed, () => nivel);

        vm.Seleccionada = col;
        Assert.NotNull(vm.ModeloSeccionColumna);   // estado válido antes de limpiar

        vm.Seleccionada = null;
        Assert.Null(vm.ArmadoLongitudinal);
        Assert.Null(vm.ModeloSeccionColumna);
    }
}
