using System;
using System.IO;
using System.Linq;
using LosasPlus.Importers;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del <see cref="CargasGlobalesXlsxImporter"/> contra el archivo real
/// del ingeniero (<c>cargas_estructurales_demo.xlsx</c>) copiado a fixtures.
/// </summary>
public class CargasGlobalesXlsxImporterTests
{
    private const string XlsxReal = "fixtures/cargas_estructurales_demo.xlsx";

    // =================================================================
    // Smoke / archivo real del ingeniero
    // =================================================================

    [Fact]
    public void Fixture_existe()
    {
        Assert.True(File.Exists(XlsxReal),
            $"Fixture xlsx no copiado al output del test: {Path.GetFullPath(XlsxReal)}");
    }

    [Fact]
    public void Importar_devuelve_un_CargasGlobales_no_nulo()
    {
        var imp = new CargasGlobalesXlsxImporter();
        var c = imp.Importar(XlsxReal);
        Assert.NotNull(c);
    }

    // =================================================================
    // Pesos propios entrepiso
    // =================================================================

    [Fact]
    public void Importar_lee_los_3_pesos_propios_entrepiso()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        Assert.Equal(3, c.PesosPropiosEntrepiso.Items.Count);
    }

    [Fact]
    public void Importar_pesos_propios_entrepiso_son_Mosaicos_Mortero_Panete()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        var nombres = c.PesosPropiosEntrepiso.Items.Select(p => p.Nombre).ToList();

        Assert.Contains("Mosaicos", nombres);
        Assert.Contains("Mortero",  nombres);
        // El xls usa "Pañete" con eñe — el importer debe preservar el nombre tal cual.
        Assert.Contains(nombres, n => n.StartsWith("Pañete", StringComparison.Ordinal)
                                  || n.StartsWith("Panete", StringComparison.Ordinal));
    }

    [Fact]
    public void Importar_pesos_propios_entrepiso_total_matchea_xls()
    {
        // xls tiene Mosaicos 0.069 + Mortero 0.072 + Pañete 0.040 = 0.181 t/m².
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        Assert.Equal(0.181, c.PesosPropiosEntrepiso.Total, precision: 3);
    }

    [Fact]
    public void Importar_Mosaicos_tiene_espesor_0_03_y_densidad_2_3()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        var mosaicos = c.PesosPropiosEntrepiso.Items.Single(p => p.Nombre == "Mosaicos");

        Assert.Equal(0.03, mosaicos.Espesor,  precision: 3);
        Assert.Equal(2.30, mosaicos.Densidad, precision: 2);
        Assert.Equal(0.069, mosaicos.W,       precision: 3);
    }

    [Fact]
    public void Importar_Mortero_tiene_espesor_0_04_y_densidad_1_8()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        var mortero = c.PesosPropiosEntrepiso.Items.Single(p => p.Nombre == "Mortero");

        Assert.Equal(0.04, mortero.Espesor,  precision: 3);
        Assert.Equal(1.80, mortero.Densidad, precision: 2);
        Assert.Equal(0.072, mortero.W,       precision: 3);
    }

    // =================================================================
    // Pesos propios techo
    // =================================================================

    [Fact]
    public void Importar_lee_pesos_propios_techo()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        // El xls tiene 3 items en techo (Fino/Impermeabilizante/Pañete o similar).
        // Si la posicion 66-68 esta vacia, falla — esto es la confirmacion.
        Assert.NotEmpty(c.PesosPropiosTecho.Items);
        Assert.True(c.PesosPropiosTecho.Total > 0,
            "Total de pesos propios techo debe ser > 0 si las filas 66-68 tienen datos.");
    }

    // =================================================================
    // Tabla de carga muerta por espesor
    // =================================================================

    [Fact]
    public void Importar_carga_muerta_tiene_15_filas()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        Assert.Equal(15, c.CargaMuertaPorEspesor.Filas.Count);
    }

    [Fact]
    public void Importar_carga_muerta_filas_cubren_h_0_06_a_0_20()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        var hs = c.CargaMuertaPorEspesor.Filas.Select(f => Math.Round(f.H, 2)).OrderBy(h => h).ToList();

        Assert.Equal(0.06, hs.First(), precision: 2);
        Assert.Equal(0.20, hs.Last(),  precision: 2);
    }

    [Fact]
    public void Importar_densidad_default_es_2_4()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        var densidades = c.CargaMuertaPorEspesor.Filas.Select(f => f.Densidad).Distinct().ToList();
        Assert.Single(densidades);
        Assert.Equal(2.4, densidades[0], precision: 2);
    }

    // =================================================================
    // Edge cases
    // =================================================================

    [Fact]
    public void Importar_lanza_si_archivo_no_existe()
    {
        var imp = new CargasGlobalesXlsxImporter();
        Assert.Throws<FileNotFoundException>(() => imp.Importar("fixtures/no-existe.xlsx"));
    }

    [Fact]
    public void Importar_lanza_si_path_es_null()
    {
        var imp = new CargasGlobalesXlsxImporter();
        Assert.Throws<ArgumentNullException>(() => imp.Importar(null!));
    }

    // =================================================================
    // Compatibilidad con CalculoEngine: lo importado debe seguir computando
    // =================================================================

    [Fact]
    public void Cargas_importadas_son_compatibles_con_LookupQd()
    {
        var c = new CargasGlobalesXlsxImporter().Importar(XlsxReal);

        // Para h_eq=0.12 entrepiso: 0.288 (W propio) + 0.181 (pesos propios) = 0.469.
        var qd = c.CargaMuertaPorEspesor.LookupQd(hEq: 0.12, c.PesosPropiosEntrepiso.Total);
        Assert.Equal(0.469, qd, precision: 3);
    }

    [Fact]
    public void Cargas_importadas_pueden_reemplazar_la_semilla_default()
    {
        // El use case real: un Proyecto arranca con SemillaPorDefecto (via
        // ProyectoFactory), el usuario hace click en "Importar de .xls" y
        // la semilla se reemplaza con los valores del xls del ingeniero.
        // Probamos que el reemplazo es total.
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        var totalSemilla = p.Cargas.PesosPropiosEntrepiso.Total;

        var importadas = new CargasGlobalesXlsxImporter().Importar(XlsxReal);
        p.Cargas = importadas;

        Assert.Same(importadas, p.Cargas);
        Assert.Equal(0.181, p.Cargas.PesosPropiosEntrepiso.Total, precision: 3);
        // Sanity: la semilla y la importada coinciden por casualidad para este xls
        // (el ingeniero usa los mismos valores que pusimos en SemillaPorDefecto).
        Assert.Equal(totalSemilla, p.Cargas.PesosPropiosEntrepiso.Total, precision: 3);
    }
}
