using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del editor de columnas (Fase J.10): agregar/eliminar columnas del
/// primer nivel del edificio activo.
/// </summary>
public class ColumnasEditorViewModelTests
{
    private static Edificio UnNivel()
    {
        var ed = new Edificio();
        ed.Niveles.Add(new Nivel { Cota = 0 });
        return ed;
    }

    [Fact]
    public void Agregar_inserta_columna_correlativa_y_la_selecciona()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed);

        var c1 = vm.Agregar();
        var c2 = vm.Agregar();

        Assert.Equal(2, ed.Niveles[0].Columnas.Count);
        Assert.Equal("C-1", c1!.Nombre);
        Assert.Equal("C-2", c2!.Nombre);
        Assert.Same(c2, vm.Seleccionada);
        Assert.Same(ed.Niveles[0].Columnas, vm.Columnas);
    }

    [Fact]
    public void Eliminar_quita_la_columna_seleccionada()
    {
        var ed = UnNivel();
        var vm = new ColumnasEditorViewModel(() => ed);
        vm.Agregar();
        var c2 = vm.Agregar();

        vm.Seleccionada = c2;
        vm.Eliminar();

        Assert.Single(ed.Niveles[0].Columnas);
        Assert.Null(vm.Seleccionada);
    }

    [Fact]
    public void Sin_edificio_no_hace_nada()
    {
        var vm = new ColumnasEditorViewModel(() => null);
        Assert.Null(vm.Columnas);
        Assert.Null(vm.Agregar());
        vm.Eliminar(); // no lanza
    }
}
