using System;
using System.Linq;
using LosasPlus.Services;
using LosasPlus.Vigas;
using LosasPlus.ViewModels.Rc;
using OxyPlot;
using OxyPlot.Annotations;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="RcSectionDesignViewModel"/> (Fase 4, Iteración 2): el
/// recálculo reactivo de la capacidad de la sección, el puente de Undo y la
/// regeneración del diagrama del bloque de Whitney.
/// </summary>
public class RcSectionDesignViewModelTests
{
    /// <summary>Crea el VM; <paramref name="snapshots"/> cuenta los snapshots de Undo.</summary>
    private static RcSectionDesignViewModel Crear(out Func<int> snapshots)
    {
        int n = 0;
        snapshots = () => n;
        return new RcSectionDesignViewModel(() => n++);
    }

    /// <summary>Un tramo con una sección RC y refuerzo conocidos.</summary>
    private static TramoViga TramoConSeccion()
    {
        var tramo = new TramoViga();
        tramo.Seccion.Base = 0.30;
        tramo.Seccion.Peralte = 0.60;
        tramo.Seccion.Recubrimiento = 0.06;
        tramo.Seccion.Fc = 28.0;
        tramo.Seccion.Fy = 420.0;
        tramo.Refuerzo.AreaAceroBot = 0.0020;
        tramo.Refuerzo.AreaAceroTop = 0.0008;
        return tramo;
    }

    /// <summary>Profundidad del eje neutro dibujada en el diagrama (anotación horizontal).</summary>
    private static double EjeNeutroY(PlotModel modelo)
        => modelo.Annotations.OfType<LineAnnotation>()
                 .First(a => a.Type == LineAnnotationType.Horizontal).Y;

    [Fact]
    public void CambiarAreaAceroBot_ModificaPhi_YMueveElEjeNeutroDelDiagrama()
    {
        var vm = Crear(out _);
        vm.Retarget(TramoConSeccion());
        double phiAntes = vm.FactorPhi;
        double cAntes = EjeNeutroY(vm.ModeloSeccion);

        vm.AreaAceroBot = 0.0060;   // mucho más acero

        Assert.True(vm.FactorPhi < phiAntes);                      // menos dúctil → φ menor
        Assert.True(EjeNeutroY(vm.ModeloSeccion) > cAntes);        // eje neutro más profundo
    }

    [Fact]
    public void EditarUnParametro_TomaExactamenteUnSnapshotDeUndo()
    {
        var vm = Crear(out var snapshots);
        vm.Retarget(TramoConSeccion());

        vm.Base = 0.35;

        Assert.Equal(1, snapshots());
    }

    [Fact]
    public void UnSetConElMismoValor_NoTomaNingunSnapshot()
    {
        var vm = Crear(out var snapshots);
        vm.Retarget(TramoConSeccion());

        vm.Base = 0.30;   // idéntico al valor actual

        Assert.Equal(0, snapshots());
    }

    [Fact]
    public void ElToggleMomentoPositivo_IntercambiaElAceroSuperiorEInferior()
    {
        var vm = Crear(out _);
        vm.Retarget(TramoConSeccion());   // AceroBot 0.0020 > AceroTop 0.0008

        double mnPositivo = vm.MomentoNominal;   // momento positivo → acero inferior
        vm.MomentoPositivo = false;              // momento negativo → acero superior
        double mnNegativo = vm.MomentoNominal;

        Assert.True(mnPositivo > mnNegativo);
    }

    [Fact]
    public void RetargetNull_DejaElDiagramaLimpioYNoLanzaExcepcion()
    {
        var vm = Crear(out _);
        vm.Retarget(TramoConSeccion());
        Assert.NotEmpty(vm.ModeloSeccion.Annotations);

        vm.Retarget(null);

        Assert.False(vm.HayTramo);
        Assert.Empty(vm.ModeloSeccion.Annotations);
        Assert.Empty(vm.ModeloSeccion.Series);
    }

    [Fact]
    public void TrasRetarget_LosResultadosReflejanLaSeccionDelTramo()
    {
        var vm = Crear(out _);
        var tramo = TramoConSeccion();

        vm.Retarget(tramo);

        var esperado = RcDesignEngine.CalcularCapacidad(
            tramo.Seccion, tramo.Refuerzo, momentoPositivo: true);

        Assert.True(vm.HayTramo);
        Assert.Equal(esperado.MomentoNominal, vm.MomentoNominal, precision: 6);
        Assert.Equal(esperado.FactorPhi, vm.FactorPhi, precision: 6);
        Assert.Equal(esperado.EjeNeutro, vm.EjeNeutro, precision: 6);
    }
}
