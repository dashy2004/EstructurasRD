using System;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.ViewModels.Zapatas;
using LosasPlus.Zapatas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ZapataEditorViewModel"/> (Fase 6, Iteración 2):
/// instanciación, comandos con snapshot de Undo y reactividad del bar chart
/// + el croquis ante cambios en la geometría, suelo y cargas de la zapata
/// activa.
/// </summary>
public class ZapataEditorViewModelTests
{
    /// <summary>Crea el VM sobre <paramref name="proyecto"/>; cuenta los snapshots de Undo.</summary>
    private static ZapataEditorViewModel Crear(Proyecto proyecto, out Func<int> snapshots)
    {
        int n = 0;
        snapshots = () => n;
        return new ZapataEditorViewModel(proyecto, () => n++);
    }

    [Fact]
    public void Instanciacion_limpia_con_proyecto_vacio()
    {
        var vm = Crear(new Proyecto(), out _);

        Assert.Null(vm.ZapataActiva);
        Assert.NotNull(vm.ModeloPresionesTerreno);
        Assert.NotNull(vm.ModeloSeccionZapata);
        Assert.True(vm.EsEstable);   // sin zapata no hay nada que verificar
    }

    [Fact]
    public void NuevaZapataCommand_crea_activa_la_zapata_y_toma_snapshot()
    {
        var vm = Crear(new Proyecto(), out var snapshots);

        vm.NuevaZapataCommand.Execute(null);

        Assert.Single(vm.Zapatas);
        Assert.NotNull(vm.ZapataActiva);
        Assert.Equal(1.50, vm.ZapataActiva!.Dimensiones.LargoB, precision: 6);
        Assert.Equal(1.50, vm.ZapataActiva.Dimensiones.AnchoL, precision: 6);
        Assert.Equal(0.40, vm.ZapataActiva.Dimensiones.EspesorH, precision: 6);
        Assert.Equal(1.50, vm.ZapataActiva.Dimensiones.ProfundidadDesplante, precision: 6);
        Assert.Equal(200.0, vm.ZapataActiva.Suelo.PresionAdmisible, precision: 6);
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public async Task RecalcularAsync_tras_NuevaZapata_levanta_los_diagramas()
    {
        // Proyecto seedeado tiene combinaciones de servicio ("D", "D + L", etc.).
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);
        vm.NuevaZapataCommand.Execute(null);

        await vm.RecalcularAsync();

        Assert.True(vm.CombinacionesServicio.Count > 0);
        Assert.NotNull(vm.CombinacionActiva);
        Assert.NotEmpty(vm.ModeloSeccionZapata.Annotations);   // contorno + pedestal
        // Sin cargas externas, la zapata + el relleno producen presión > 0
        // por su peso propio sobre el área de la base.
        Assert.True(vm.PresionMaximaGlobal > 0.0);
    }

    [Fact]
    public async Task CargaAxialPesada_AlteraPresionMaximaGlobal()
    {
        var proyecto = ProyectoFactory.NuevoProyectoSeedeado();
        proyecto.AsegurarEstructura();
        var vm = Crear(proyecto, out _);
        vm.NuevaZapataCommand.Execute(null);
        await vm.RecalcularAsync();
        double qMaxAntes = vm.PresionMaximaGlobal;

        vm.ZapataActiva!.Cargas.Add(new CargaZapata
        {
            CodigoCaso = "D", P = 5000.0, Mx = 0.0, My = 0.0,
        });
        await vm.RecalcularAsync();

        Assert.True(vm.PresionMaximaGlobal > qMaxAntes,
            $"Se esperaba que la presión máxima aumentara; antes = {qMaxAntes:0.##}, después = {vm.PresionMaximaGlobal:0.##}.");
    }

    [Fact]
    public void SnapshotAntesDeEditar_TomaExactamenteUnSnapshot()
    {
        var vm = Crear(new Proyecto(), out var snapshots);

        vm.SnapshotAntesDeEditar();

        Assert.Equal(1, snapshots());
    }
}
