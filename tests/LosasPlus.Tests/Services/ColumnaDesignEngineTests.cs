using System;
using System.Linq;
using LosasPlus.Columnas;
using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="ColumnaDesignEngine"/> (Fase 5, Iteración 1):
/// verificación analítica de los puntos extremos de control del diagrama de
/// interacción P-M uniaxial 2D contra las ecuaciones cerradas de ACI 318-19,
/// más el roundtrip de persistencia de <c>Nivel.Columnas</c>.
/// </summary>
public class ColumnaDesignEngineTests
{
    /// <summary>
    /// Sección cuadrada 0.40×0.40 con refuerzo perimetral simétrico (4 capas
    /// de 250 mm² a profundidades 0.05, 0.15, 0.25 y 0.35 m).
    /// </summary>
    private static (SeccionColumna Seccion, RefuerzoColumna Acero) ColumnaCuadradaSimetrica()
    {
        var seccion = new SeccionColumna { Base = 0.40, Peralte = 0.40, Fc = 28.0 };
        var acero = new RefuerzoColumna { Fy = 420.0 };
        foreach (double d in new[] { 0.05, 0.15, 0.25, 0.35 })
            acero.Capas.Add(new CapaAcero { Profundidad = d, Area = 250e-6 });
        return (seccion, acero);
    }

    [Fact]
    public void CompresionPura_CoincideConP0Analitico()
    {
        var (seccion, acero) = ColumnaCuadradaSimetrica();
        // Ag = 160 000 mm², Ast = 4·250 = 1 000 mm².
        // P0 = 0.85·28·(160 000 − 1 000) + 420·1 000 = 4 203 800 N = 4 203.8 kN.
        const double p0Esperado = 0.85 * 28.0 * (160_000.0 - 1_000.0) + 420.0 * 1_000.0;
        const double p0EsperadoKn = p0Esperado / 1_000.0;

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        Assert.Equal(p0EsperadoKn, d.PnMaximoNominal, precision: 3);
    }

    [Fact]
    public void TraccionPura_CoincideConTnAnalitico()
    {
        var (seccion, acero) = ColumnaCuadradaSimetrica();
        // Tn = −fy·Ast = −420·1 000 = −420 000 N = −420 kN.
        const double tnEsperadoKn = -420.0 * 1_000.0 / 1_000.0;

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        Assert.Equal(tnEsperadoKn, d.TraccionPuraNominal, precision: 3);
    }

    [Fact]
    public void InversionDelMomento_PreservaLaSimetriaDeLaCurva()
    {
        var (seccion, acero) = ColumnaCuadradaSimetrica();

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        // Para una sección simétrica, por cada (M, P) con M > 0 debe existir
        // un (−M, P) en la curva nominal (mismo P, M opuesto).
        foreach (var positivo in d.CurvaNominal.Where(p => p.Momento > 1e-6))
        {
            bool tieneReflejo = d.CurvaNominal.Any(p =>
                Math.Abs(p.Momento + positivo.Momento) < 1e-6
                && Math.Abs(p.Carga - positivo.Carga) < 1e-6);
            Assert.True(tieneReflejo,
                $"Falta el reflejo de (M={positivo.Momento:0.###}, P={positivo.Carga:0.###}).");
        }
    }

    [Fact]
    public void LaCurvaTiene_AlMenos50Puntos()
    {
        var (seccion, acero) = ColumnaCuadradaSimetrica();

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        Assert.True(d.CurvaNominal.Count >= 50);
        Assert.Equal(d.CurvaNominal.Count, d.CurvaDiseno.Count);
    }

    [Fact]
    public void ElCapAxial_AplicaAlfaCero80_EnLaCabezaDeLaCurvaDeDiseno()
    {
        var (seccion, acero) = ColumnaCuadradaSimetrica();

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        double topeEsperado = 0.80 * 0.65 * d.PnMaximoNominal;
        double phiPnMaximo = d.CurvaDiseno.Max(p => p.Carga);
        Assert.True(phiPnMaximo <= topeEsperado + 1e-6,
            $"φPn máximo {phiPnMaximo:0.###} excede el tope α·φ·P0 = {topeEsperado:0.###}.");
    }

    [Fact]
    public void SeccionInvalida_DevuelveDiagramaVacio()
    {
        var seccion = new SeccionColumna { Base = 0.40, Peralte = 0.40, Fc = 28.0 };
        var acero = new RefuerzoColumna { Fy = 420.0 };   // sin capas → Ast = 0

        var d = ColumnaDesignEngine.ConstruirDiagramaInteraccion2D(seccion, acero);

        Assert.Empty(d.CurvaNominal);
        Assert.Empty(d.CurvaDiseno);
        Assert.Equal(0.0, d.PnMaximoNominal);
        Assert.Equal(0.0, d.TraccionPuraNominal);
    }

    [Fact]
    public void LasColumnasDelNivel_SobrevivenUnRoundtripDeSerializacion()
    {
        var proyecto = new Proyecto();
        proyecto.AsegurarEstructura();
        var columna = new Columna { Id = 5, Nombre = "C-RT", Altura = 2.8 };
        columna.Geometria.Base = 0.45;
        columna.Geometria.Peralte = 0.50;
        columna.Geometria.Fc = 35.0;
        columna.Acero.Fy = 420.0;
        columna.Acero.Capas.Add(new CapaAcero { Profundidad = 0.05, Area = 0.0012 });
        columna.Acero.Capas.Add(new CapaAcero { Profundidad = 0.45, Area = 0.0012 });
        proyecto.Edificios[0].Niveles[0].Columnas.Add(columna);

        var restaurado = ProyectoSerializer.FromJson(ProyectoSerializer.ToJson(proyecto));
        var c = restaurado.Edificios[0].Niveles[0].Columnas[0];

        Assert.Equal("C-RT", c.Nombre);
        Assert.Equal(2.8, c.Altura, precision: 6);
        Assert.Equal(0.45, c.Geometria.Base, precision: 6);
        Assert.Equal(0.50, c.Geometria.Peralte, precision: 6);
        Assert.Equal(35.0, c.Geometria.Fc, precision: 6);
        Assert.Equal(420.0, c.Acero.Fy, precision: 6);
        Assert.Equal(2, c.Acero.Capas.Count);
        Assert.Equal(0.05, c.Acero.Capas[0].Profundidad, precision: 6);
        Assert.Equal(0.0012, c.Acero.Capas[0].Area, precision: 6);
        Assert.Equal(0.45, c.Acero.Capas[1].Profundidad, precision: 6);
    }
}
