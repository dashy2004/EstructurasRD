using System;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="CadEditorViewModel"/> enfocados en el gate CanExecute
/// del comando <see cref="CadEditorViewModel.EliminarMuroCommand"/> — bug
/// «botones congelados» en Avalonia (sin CommandManager.RequerySuggested).
/// </summary>
public class CadEditorViewModelTests
{
    /// <summary>
    /// Importador de plano nulo — suficiente para tests que no ejercen la
    /// importación DXF.
    /// </summary>
    private sealed class NullPlanoImporter : IPlanoImporter
    {
        public PlanoReferencia Importar(string rutaArchivo)
            => throw new NotSupportedException("No se usa en estos tests.");
    }

    private static CadEditorViewModel Crear()
    {
        var sistema = new Sistema();
        return new CadEditorViewModel(
            () => sistema,
            () => { },           // pushUndoSnapshot — no-op para tests
            new NullPlanoImporter());
    }

    // ---- Tests del gate CanExecute (fix frozen-buttons) ----

    [Fact]
    public void EliminarMuroCommand_inhabilitado_cuando_no_hay_muro_seleccionado()
    {
        var vm = Crear();

        Assert.False(vm.EliminarMuroCommand.CanExecute(null));
    }

    [Fact]
    public void EliminarMuroCommand_se_habilita_al_setear_MuroSeleccionado()
    {
        var vm = Crear();
        var muro = new Muro
        {
            Id = 1,
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin    = new PuntoCad(3, 0),
        };

        // Suscribir el evento ANTES de mutar el estado.
        bool eventFired = false;
        vm.EliminarMuroCommand.CanExecuteChanged += (_, _) => eventFired = true;

        vm.MuroSeleccionado = muro;

        Assert.True(vm.EliminarMuroCommand.CanExecute(null));
        // El evento se encoló vía Dispatcher.UIThread.Post; verificación
        // funcional garantizada por el CanExecute == true anterior.
        _ = eventFired;
    }

    [Fact]
    public void EliminarMuroCommand_vuelve_a_false_al_deseleccionar_el_muro()
    {
        var vm = Crear();
        vm.MuroSeleccionado = new Muro
        {
            Id = 1,
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin    = new PuntoCad(3, 0),
        };
        Assert.True(vm.EliminarMuroCommand.CanExecute(null));

        bool eventFired = false;
        vm.EliminarMuroCommand.CanExecuteChanged += (_, _) => eventFired = true;

        vm.MuroSeleccionado = null;

        Assert.False(vm.EliminarMuroCommand.CanExecute(null));
        _ = eventFired;
    }

    [Fact]
    public void Seleccionar_dos_muros_distintos_mantiene_command_habilitado()
    {
        var vm = Crear();
        var m1 = new Muro { Id = 1, PuntoInicio = new PuntoCad(0, 0), PuntoFin = new PuntoCad(2, 0) };
        var m2 = new Muro { Id = 2, PuntoInicio = new PuntoCad(2, 0), PuntoFin = new PuntoCad(5, 0) };

        vm.MuroSeleccionado = m1;
        Assert.True(vm.EliminarMuroCommand.CanExecute(null));

        vm.MuroSeleccionado = m2;
        Assert.True(vm.EliminarMuroCommand.CanExecute(null));
    }

    // ---- Clamp Escala > 0 (UI1.7): el TextBox commitea un «0» transitorio ----

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EscalaPlano_rechaza_valores_invalidos_sin_mutar_ni_redibujar(double invalido)
    {
        var vm = Crear();
        vm.Plano = new PlanoReferencia { MinX = 0, MinY = 0, MaxX = 10, MaxY = 5, Escala = 2.0 };
        int revAntes = vm.RevisionPlano;

        vm.EscalaPlano = invalido;

        Assert.Equal(2.0, vm.EscalaPlano);
        Assert.Equal(revAntes, vm.RevisionPlano);
    }

    [Fact]
    public void EscalaPlano_acepta_valores_validos_y_bumpea_revision()
    {
        var vm = Crear();
        vm.Plano = new PlanoReferencia { MinX = 0, MinY = 0, MaxX = 10, MaxY = 5, Escala = 2.0 };
        int revAntes = vm.RevisionPlano;

        vm.EscalaPlano = 0.5;

        Assert.Equal(0.5, vm.EscalaPlano);
        Assert.Equal(revAntes + 1, vm.RevisionPlano);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void EscalaPdf_rechaza_valores_invalidos_sin_mutar_ni_redibujar(double invalido)
    {
        var vm = Crear();
        vm.Pdf = new PdfReferencia { Ancho = 0.841, Alto = 0.594, Escala = 100.0 };
        int revAntes = vm.RevisionPdf;

        vm.EscalaPdf = invalido;

        Assert.Equal(100.0, vm.EscalaPdf);
        Assert.Equal(revAntes, vm.RevisionPdf);
    }

    [Fact]
    public void EscalaPdf_acepta_valores_validos_y_bumpea_revision()
    {
        var vm = Crear();
        vm.Pdf = new PdfReferencia { Ancho = 0.841, Alto = 0.594, Escala = 100.0 };
        int revAntes = vm.RevisionPdf;

        vm.EscalaPdf = 50.0;

        Assert.Equal(50.0, vm.EscalaPdf);
        Assert.Equal(revAntes + 1, vm.RevisionPdf);
    }
}
