using System.Collections.Generic;
using LosasPlus.Grillas;
using Xunit;

namespace LosasPlus.Tests.Topologia;

/// <summary>
/// Tests del motor de snap puro <see cref="GridSnapEngine"/>
/// (Módulo 2 Parte B Fase 3D-II). Verifica la lógica matemática de
/// magnetización del cursor sin depender de WPF, HelixToolkit ni
/// hilo UI — 100% dominio.
/// </summary>
public class GridSnapEngineTests
{
    [Fact]
    public void CalcularSnap_DentroDelRadio_MagnetizaAInterseccionExacta()
    {
        // Grilla default 3×3 a 6 m → intersecciones en (0,0), (0,6), (0,12),
        // (6,0), (6,6), (6,12), (12,0), (12,6), (12,12).
        // Click en (0.3, 5.85) → cercano (0, 6). Distancia = √(0.09+0.0225) ≈ 0.335 m < 0.80.
        var grilla = GrillaEstructural.Default();

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(0.3, 5.85, grilla);

        Assert.True(snapped);
        Assert.Equal(0.0, x);
        Assert.Equal(6.0, y);
    }

    [Fact]
    public void CalcularSnap_FueraDelRadio_RetornaPosicionLibre()
    {
        // Click lejos de cualquier intersección: (2.5, 3.5) — la más
        // cercana es (0,6) a √(6.25+6.25) = 3.54 m, muy lejos del radio 0.80.
        var grilla = GrillaEstructural.Default();

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(2.5, 3.5, grilla);

        Assert.False(snapped);
        Assert.Equal(2.5, x);
        Assert.Equal(3.5, y);
    }

    [Fact]
    public void CalcularSnap_PuntoExactamenteEnInterseccion_SnapInmediato()
    {
        var grilla = GrillaEstructural.Default();

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(6.0, 6.0, grilla);

        Assert.True(snapped);
        Assert.Equal(6.0, x);
        Assert.Equal(6.0, y);
    }

    [Fact]
    public void CalcularSnap_GrillaVacia_DevuelvePosicionLibreCon_SnappedFalse()
    {
        var grillaVacia = new GrillaEstructural(
            new List<EjeNombrado>(),
            new List<EjeNombrado>());

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(3.7, 9.2, grillaVacia);

        Assert.False(snapped);
        Assert.Equal(3.7, x);
        Assert.Equal(9.2, y);
    }

    [Fact]
    public void CalcularSnap_JustoEnElLimiteDeTolerancia_SnapPositivo()
    {
        // Distancia exactamente igual al radio → la spec dice "menor o
        // igual" ⇒ snap. Punto (0.80, 0.0) cercano a (0,0), distancia 0.80.
        var grilla = GrillaEstructural.Default();

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(
            0.80, 0.0, grilla, radioToleranciaM: 0.80);

        Assert.True(snapped);
        Assert.Equal(0.0, x);
        Assert.Equal(0.0, y);
    }

    [Fact]
    public void CalcularSnap_DiagonalCerca_PrefiereInterseccionMasCercana()
    {
        // Click en (5.7, 5.7) — está casi exactamente en (6,6), distancia
        // √(0.09+0.09) ≈ 0.424 m. Cercano debe ser (6, 6) NO (0, 0)
        // (que está a √(64.98+64.98) ≈ 8.06 m).
        var grilla = GrillaEstructural.Default();

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(5.7, 5.7, grilla);

        Assert.True(snapped);
        Assert.Equal(6.0, x);
        Assert.Equal(6.0, y);
    }

    [Fact]
    public void CalcularSnap_GrillaSoloConUnEjeEnCadaDireccion_SnapAlPuntoUnico()
    {
        var grillaUnica = new GrillaEstructural(
            new List<EjeNombrado> { new("X", 10.0) },
            new List<EjeNombrado> { new("Y", 20.0) });

        var (x, y, snapped) = GridSnapEngine.CalcularSnapAGrilla(10.2, 20.5, grillaUnica);

        Assert.True(snapped);   // distancia √(0.04+0.25) ≈ 0.54 < 0.80
        Assert.Equal(10.0, x);
        Assert.Equal(20.0, y);
    }

    [Fact]
    public void CalcularSnap_RadioCero_NuncaActivaMagnetizacion()
    {
        // Edge case: radio 0 → ningún punto puede activar snap salvo si
        // cae exactamente sobre una intersección. Verifica que sí lo hace
        // en el caso degenerado.
        var grilla = GrillaEstructural.Default();

        var (xLejos, yLejos, snappedLejos) = GridSnapEngine.CalcularSnapAGrilla(
            0.01, 0.01, grilla, radioToleranciaM: 0.0);
        var (xExacto, yExacto, snappedExacto) = GridSnapEngine.CalcularSnapAGrilla(
            0.0, 0.0, grilla, radioToleranciaM: 0.0);

        Assert.False(snappedLejos);
        Assert.Equal(0.01, xLejos);
        Assert.Equal(0.01, yLejos);

        Assert.True(snappedExacto);   // distancia 0 ≤ 0
        Assert.Equal(0.0, xExacto);
        Assert.Equal(0.0, yExacto);
    }
}
