using System;
using System.Threading.Tasks;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.ViewModels.Columnas;
using OxyPlot;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="ColumnaEditorViewModel"/> (Fase 5, Iteración 2):
/// instanciación, comandos con snapshot de Undo, y reactividad del diagrama
/// de interacción P-M ante cambios en la sección y en las capas de acero.
/// </summary>
public class ColumnaEditorViewModelTests
{
    /// <summary>Crea el VM sobre <paramref name="proyecto"/>; cuenta los snapshots de Undo.</summary>
    private static ColumnaEditorViewModel Crear(Proyecto proyecto, out Func<int> snapshots)
    {
        int n = 0;
        snapshots = () => n;
        return new ColumnaEditorViewModel(proyecto, () => n++);
    }

    [Fact]
    public void Instanciacion_limpia_con_proyecto_vacio()
    {
        var vm = Crear(new Proyecto(), out _);

        Assert.Null(vm.ColumnaActiva);
        Assert.NotNull(vm.ModeloDiagramaPM);
        Assert.NotNull(vm.ModeloSeccionTransversal);
    }

    [Fact]
    public void NuevaColumnaCommand_crea_activa_la_columna_y_toma_snapshot()
    {
        var vm = Crear(new Proyecto(), out var snapshots);

        vm.NuevaColumnaCommand.Execute(null);

        Assert.Single(vm.Columnas);
        Assert.NotNull(vm.ColumnaActiva);
        Assert.Equal(0.30, vm.ColumnaActiva!.Geometria.Base, precision: 6);
        Assert.Equal(0.30, vm.ColumnaActiva.Geometria.Peralte, precision: 6);
        Assert.Equal(28.0, vm.ColumnaActiva.Geometria.Fc, precision: 6);
        Assert.Equal(420.0, vm.ColumnaActiva.Acero.Fy, precision: 6);
        Assert.Equal(2, vm.ColumnaActiva.Acero.Capas.Count);
        Assert.Equal(1, snapshots());
    }

    [Fact]
    public async Task RecalcularAsync_tras_NuevaColumna_levanta_el_diagrama_y_propaga_P0()
    {
        var vm = Crear(new Proyecto(), out _);
        vm.NuevaColumnaCommand.Execute(null);

        await vm.RecalcularAsync();

        Assert.True(vm.PnMaximoNominal > 0.0);
        Assert.True(vm.TraccionPuraNominal < 0.0);
        Assert.True(vm.CantidadPuntosCurva >= 50);
        Assert.NotEmpty(vm.ModeloDiagramaPM.Series);
        Assert.NotEmpty(vm.ModeloDiagramaPM.Annotations);   // tope axial αφP0
        Assert.NotEmpty(vm.ModeloSeccionTransversal.Series); // marcadores de capas
    }

    [Fact]
    public async Task AgregarCapaCommand_repinta_el_diagramaPM_y_eleva_P0()
    {
        var vm = Crear(new Proyecto(), out _);
        vm.NuevaColumnaCommand.Execute(null);
        await vm.RecalcularAsync();
        double p0Antes = vm.PnMaximoNominal;

        vm.AgregarCapaCommand.Execute(null);
        await vm.RecalcularAsync();

        // Una capa adicional aporta más acero → P0 ≈ 0.85·fc·(Ag − Ast) + fy·Ast
        // crece linealmente con Ast porque fy > 0.85·fc.
        Assert.True(vm.PnMaximoNominal > p0Antes);
        Assert.Equal(3, vm.ColumnaActiva!.Acero.Capas.Count);
    }

    [Fact]
    public async Task EditarElAreaDeUnaCapa_AlteraLaCoordenadaMaximaDeLaCurva()
    {
        var vm = Crear(new Proyecto(), out _);
        vm.NuevaColumnaCommand.Execute(null);
        await vm.RecalcularAsync();
        double p0Antes = vm.PnMaximoNominal;

        vm.ColumnaActiva!.Acero.Capas[0].Area = 0.0010;   // mucho más acero en una capa
        await vm.RecalcularAsync();

        Assert.NotEqual(p0Antes, vm.PnMaximoNominal);
        Assert.True(vm.PnMaximoNominal > p0Antes);   // más Ast → mayor P0 nominal
    }

    [Fact]
    public void SnapshotAntesDeEditar_TomaExactamenteUnSnapshot()
    {
        var vm = Crear(new Proyecto(), out var snapshots);

        vm.SnapshotAntesDeEditar();

        Assert.Equal(1, snapshots());
    }

    [Fact]
    public void LasColumnasDelNivel_SobrevivenAlAbrirElEditorTrasRoundtripJson()
    {
        // Verifica el camino aditivo: una columna persistida en JSON se carga
        // y queda disponible para que el editor la active sin intervención.
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var columna = new Columna { Nombre = "C-Persistida" };
        columna.Geometria.Base = 0.45;
        columna.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0005 });
        proyecto.Edificios[0].Niveles[0].Columnas.Add(columna);

        var restaurado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(proyecto));
        var vm = Crear(restaurado, out _);

        Assert.NotNull(vm.ColumnaActiva);
        Assert.Equal("C-Persistida", vm.ColumnaActiva!.Nombre);
        Assert.Equal(0.45, vm.ColumnaActiva.Geometria.Base, precision: 6);
        Assert.Single(vm.ColumnaActiva.Acero.Capas);
    }
}
