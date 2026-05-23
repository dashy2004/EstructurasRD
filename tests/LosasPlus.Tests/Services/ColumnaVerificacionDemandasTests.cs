using LosasPlus.Columnas;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="ColumnaDesignEngine.VerificarPuntoDemanda"/> (Fase 5,
/// Iteración 3): el cálculo del ratio radial Demanda/Capacidad por
/// intersección rayo-segmento contra la frontera <c>CurvaDiseno</c> de un
/// diagrama de interacción P-M, contrastado con los puntos extremos de
/// control de la teoría (origen, interior, frontera y exterior).
/// </summary>
public class ColumnaVerificacionDemandasTests
{
    /// <summary>
    /// Diagrama de control de una columna cuadrada simétrica
    /// (0.40×0.40, fc=28 MPa, fy=420 MPa, 4 capas de 250 mm² en d=0.05/0.15/
    /// 0.25/0.35) — patrón clonado de <c>ColumnaDesignEngineTests</c>.
    /// </summary>
    private static DiagramaInteraccion2D DiagramaCuadradaSimetrica()
    {
        var seccion = new SeccionColumna { Base = 0.40, Peralte = 0.40, Fc = 28.0 };
        var acero = new RefuerzoColumna { Fy = 420.0 };
        foreach (double d in new[] { 0.05, 0.15, 0.25, 0.35 })
            acero.Capas.Add(new CapaAcero { Profundidad = d, Area = 250e-6 });
        return ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);
    }

    [Fact]
    public void PuntoEnElOrigen_RatioEsCero()
    {
        var d = DiagramaCuadradaSimetrica();

        var r = ColumnaDesignEngine.VerificarPuntoDemanda(d, mu: 0.0, pu: 0.0);

        Assert.Equal(0.0, r.RatioEficiencia);
        Assert.True(r.EsSeguro);
    }

    [Fact]
    public void PuntoInteriorObvio_RatioMenorQueUno()
    {
        var d = DiagramaCuadradaSimetrica();
        // P0 ≈ 4 204 kN, αφP0 ≈ 2 186 kN. El punto (50, 1 000) cae claramente
        // dentro de la frontera de diseño cerrada.
        var r = ColumnaDesignEngine.VerificarPuntoDemanda(d, mu: 50.0, pu: 1_000.0);

        Assert.True(r.EsSeguro);
        Assert.True(r.RatioEficiencia > 0.0 && r.RatioEficiencia < 1.0,
            $"Se esperaba 0 < ratio < 1, pero ratio = {r.RatioEficiencia:0.######}.");
    }

    [Fact]
    public void PuntoExternoMasivo_RatioMayorQueUno()
    {
        var d = DiagramaCuadradaSimetrica();
        // Escalar el punto interior por 10× lo coloca holgadamente fuera del
        // polígono cerrado.
        var r = ColumnaDesignEngine.VerificarPuntoDemanda(d, mu: 500.0, pu: 10_000.0);

        Assert.False(r.EsSeguro);
        Assert.True(r.RatioEficiencia > 1.0,
            $"Se esperaba ratio > 1, pero ratio = {r.RatioEficiencia:0.######}.");
    }

    [Fact]
    public void PuntoSobreLaFrontera_RatioAproximadoAUno()
    {
        var d = DiagramaCuadradaSimetrica();
        // Un punto interior de la curva de diseño (medio del ramal positivo,
        // lejos de la ancla P0 y de la ancla Tn). Si la demanda coincide con
        // un punto del polígono, el rayo lo intercepta exactamente allí y el
        // ratio = 1.
        var sobreFrontera = d.CurvaDiseno[26];

        var r = ColumnaDesignEngine.VerificarPuntoDemanda(
            d, mu: sobreFrontera.Momento, pu: sobreFrontera.Carga);

        Assert.Equal(1.0, r.RatioEficiencia, precision: 6);
    }

    [Fact]
    public void DemandaConCargaAxialNula_NoLanzaDivisionPorCero()
    {
        var d = DiagramaCuadradaSimetrica();
        // Mu puro (Pu = 0): el rayo es horizontal sobre el eje +M. La
        // intersección con el ramal de M ≥ 0 a P = 0 es finita; el algoritmo
        // no debe lanzar ni devolver NaN.
        var r = ColumnaDesignEngine.VerificarPuntoDemanda(d, mu: 50.0, pu: 0.0);

        Assert.True(double.IsFinite(r.RatioEficiencia),
            $"Ratio no finito: {r.RatioEficiencia}.");
        Assert.True(r.RatioEficiencia > 0.0);
    }

    [Fact]
    public void DiagramaVacio_DevuelveSinCapacidad()
    {
        var r = ColumnaDesignEngine.VerificarPuntoDemanda(
            DiagramaInteraccion2D.Vacio, mu: 100.0, pu: 100.0);

        Assert.True(double.IsPositiveInfinity(r.RatioEficiencia));
        Assert.False(r.EsSeguro);
    }
}
