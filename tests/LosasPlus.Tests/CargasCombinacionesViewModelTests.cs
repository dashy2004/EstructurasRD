using System;
using LosasPlus.Cargas;
using LosasPlus.Models;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="CargasCombinacionesViewModel"/> (Fase 2, Iteración 2):
/// los comandos mutan las colecciones del proyecto y disparan un snapshot de
/// Undo antes de cada mutación.
/// </summary>
public class CargasCombinacionesViewModelTests
{
    /// <summary>Crea el VM sobre un proyecto limpio; <paramref name="snapshots"/> cuenta los snapshots de Undo.</summary>
    private static CargasCombinacionesViewModel Crear(out Proyecto proyecto, out Func<int> snapshots)
    {
        int n = 0;
        proyecto = new Proyecto();
        snapshots = () => n;
        return new CargasCombinacionesViewModel(proyecto, () => n++);
    }

    [Fact]
    public void AgregarCaso_agrega_selecciona_y_toma_snapshot()
    {
        var vm = Crear(out var proyecto, out var snapshots);

        vm.AgregarCasoCommand.Execute(null);

        Assert.Single(proyecto.Combinaciones.Casos);
        Assert.Same(proyecto.Combinaciones.Casos[0], vm.CasoSeleccionado);
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public void EliminarCaso_quita_el_seleccionado_y_toma_snapshot()
    {
        var vm = Crear(out var proyecto, out var snapshots);
        vm.AgregarCasoCommand.Execute(null);    // 1er snapshot

        vm.EliminarCasoCommand.Execute(null);   // 2do snapshot

        Assert.Empty(proyecto.Combinaciones.Casos);
        Assert.Null(vm.CasoSeleccionado);
        Assert.Equal(2, snapshots());
    }

    [Fact]
    public void EliminarCasoCommand_no_ejecutable_sin_seleccion()
    {
        var vm = Crear(out _, out _);

        Assert.False(vm.EliminarCasoCommand.CanExecute(null));
        vm.AgregarCasoCommand.Execute(null);
        Assert.True(vm.EliminarCasoCommand.CanExecute(null));
    }

    [Fact]
    public void AgregarCombinacion_agrega_selecciona_y_toma_snapshot()
    {
        var vm = Crear(out var proyecto, out var snapshots);

        vm.AgregarCombinacionCommand.Execute(null);

        Assert.Single(proyecto.Combinaciones.Combinaciones);
        Assert.Same(proyecto.Combinaciones.Combinaciones[0], vm.CombinacionSeleccionada);
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public void AgregarTermino_agrega_a_la_combinacion_seleccionada()
    {
        var vm = Crear(out _, out var snapshots);
        vm.AgregarCombinacionCommand.Execute(null);   // 1er snapshot + selecciona la combinación

        vm.AgregarTerminoCommand.Execute(null);       // 2do snapshot

        Assert.Single(vm.CombinacionSeleccionada!.Terminos);
        Assert.Same(vm.CombinacionSeleccionada.Terminos[0], vm.TerminoSeleccionado);
        Assert.Equal(2, snapshots());
    }

    [Fact]
    public void AgregarTermino_no_ejecutable_sin_combinacion_seleccionada()
    {
        var vm = Crear(out _, out _);
        Assert.False(vm.AgregarTerminoCommand.CanExecute(null));
    }

    [Fact]
    public void Norma_setter_cambia_el_valor_y_toma_un_solo_snapshot()
    {
        var vm = Crear(out var proyecto, out var snapshots);

        vm.Norma = NormaCombinaciones.R027;

        Assert.Equal(NormaCombinaciones.R027, proyecto.Combinaciones.Norma);
        Assert.Equal(1, snapshots());

        // Reasignar el mismo valor no debe tomar otro snapshot.
        vm.Norma = NormaCombinaciones.R027;
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public void NotificarRestauracion_reinicia_la_seleccion()
    {
        var vm = Crear(out _, out _);
        vm.AgregarCasoCommand.Execute(null);
        vm.AgregarCombinacionCommand.Execute(null);
        Assert.NotNull(vm.CasoSeleccionado);
        Assert.NotNull(vm.CombinacionSeleccionada);

        vm.NotificarRestauracion();

        Assert.Null(vm.CasoSeleccionado);
        Assert.Null(vm.CombinacionSeleccionada);
    }
}
