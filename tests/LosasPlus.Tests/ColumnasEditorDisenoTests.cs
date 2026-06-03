using System.Linq;
using LosasPlus.Models;
using LosasPlus.ViewModels;
using OxyPlot;
using OxyPlot.Series;
using Xunit;

namespace LosasPlus.Tests.ViewModels;

/// <summary>
/// Tests del diseño a flexo-compresión expuesto por <see cref="ColumnasEditorViewModel"/>:
/// <c>DisenoActual</c> se computa para la columna seleccionada con los inputs del VM,
/// y reacciona a cambios de material/armado.
/// </summary>
public class ColumnasEditorDisenoTests
{
    private static ColumnasEditorViewModel VmConColumna()
    {
        var ed = new Edificio();
        var niv = new Nivel();
        niv.Columnas.Add(new Columna { Id = 1, Nombre = "C-1", Base = 0.40, Peralte = 0.40, Altura = 3.0 });
        ed.Niveles.Add(niv);
        var vm = new ColumnasEditorViewModel(() => ed);
        vm.Seleccionada = niv.Columnas[0];
        return vm;
    }

    [Fact]
    public void DisenoActual_se_calcula_para_la_columna_seleccionada()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.DisenoActual);
        Assert.True(vm.DisenoActual!.RhoG > 0);
        Assert.NotEmpty(vm.DisenoActual.Diagrama);
    }

    [Fact]
    public void Sin_seleccion_DisenoActual_es_null()
    {
        var vm = VmConColumna();
        vm.Seleccionada = null;
        Assert.Null(vm.DisenoActual);
    }

    [Fact]
    public void Cambiar_fc_recalcula_el_diseno()
    {
        var vm = VmConColumna();
        double poAntes = vm.DisenoActual!.PoN;
        vm.FcMPa = 35.0;                       // más f'c → más Po
        Assert.True(vm.DisenoActual!.PoN > poAntes);
    }

    [Fact]
    public void Armado_invalido_no_rompe_y_deja_null()
    {
        var vm = VmConColumna();
        vm.BarrasX = 1;                        // < 2 → inválido para LayoutPerimetral
        Assert.Null(vm.DisenoActual);
    }

    [Fact]
    public void ModeloInteraccion_tiene_curva_tope_y_punto_de_demanda()
    {
        var vm = VmConColumna();
        var plot = vm.ModeloInteraccion;
        Assert.NotNull(plot);
        Assert.Equal(3, plot!.Series.Count);                          // curva + tope + demanda
        var curva = plot.Series.OfType<LineSeries>().First();
        Assert.Equal(vm.DisenoActual!.Diagrama.Count, curva.Points.Count);
        Assert.Single(plot.Series.OfType<ScatterSeries>());          // 1 punto de demanda
    }

    [Fact]
    public void Punto_demanda_verde_cuando_cumple()
    {
        var vm = VmConColumna();
        vm.PuKN = 1000; vm.MuKNm = 50;                                // dentro del diagrama
        Assert.True(vm.DisenoActual!.Chequeo.Cumple);
        var scatter = vm.ModeloInteraccion!.Series.OfType<ScatterSeries>().Single();
        Assert.Equal(OxyColor.Parse("#10B981"), scatter.MarkerFill);  // verde
    }

    [Fact]
    public void Punto_demanda_rojo_cuando_no_cumple()
    {
        var vm = VmConColumna();
        vm.MuKNm = 1000;                                              // momento excesivo → fuera
        Assert.False(vm.DisenoActual!.Chequeo.Cumple);
        var scatter = vm.ModeloInteraccion!.Series.OfType<ScatterSeries>().Single();
        Assert.Equal(OxyColor.Parse("#EF4444"), scatter.MarkerFill);  // rojo
    }

    [Fact]
    public void ModeloInteraccion_se_anula_al_deseleccionar()
    {
        var vm = VmConColumna();
        Assert.NotNull(vm.ModeloInteraccion);
        vm.Seleccionada = null;
        Assert.Null(vm.ModeloInteraccion);                           // no debe quedar el plot viejo
    }
}
