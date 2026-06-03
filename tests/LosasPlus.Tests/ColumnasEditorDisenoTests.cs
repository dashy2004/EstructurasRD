using LosasPlus.Models;
using LosasPlus.ViewModels;
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
}
