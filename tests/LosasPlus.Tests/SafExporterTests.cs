using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Cargas;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exportador SAF (Structural Analysis Format). Genera el libro a un
/// path temporal y lo reabre con ClosedXML para verificar las hojas por objeto,
/// la normalización de materiales/secciones, la deduplicación de nodos y el
/// signo de las cargas.
/// </summary>
public class SafExporterTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"saf_{Guid.NewGuid():N}.xlsx");

    /// <summary>
    /// Viga continua de dos tramos idénticos (5 m, 0.30×0.50, mismo E) sobre
    /// tres apoyos, con una carga distribuida (D) en el tramo 1 y una puntual
    /// (L) en el tramo 2.
    /// </summary>
    private static Viga BuildDemoViga()
    {
        var viga = new Viga { Id = 1, Nombre = "V-1" };

        var t1 = new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50, ModuloElasticidad = 2.5e7 };
        t1.Cargas.Add(new CargaElemento(TipoCargaElemento.Distribuida, 20.0, "D"));
        var t2 = new TramoViga { Longitud = 5, Base = 0.30, Peralte = 0.50, ModuloElasticidad = 2.5e7 };
        t2.Cargas.Add(new CargaElemento(TipoCargaElemento.Puntual, 12.0, "L", posicion: 2.5));
        viga.Tramos.Add(t1);
        viga.Tramos.Add(t2);

        viga.Apoyos.Add(new ApoyoViga(0, TipoApoyo.Empotrado));
        viga.Apoyos.Add(new ApoyoViga(5, TipoApoyo.Fijo));
        viga.Apoyos.Add(new ApoyoViga(10, TipoApoyo.Fijo));
        return viga;
    }

    private static CombinacionesProyecto BuildDemoCargas()
    {
        var cp = new CombinacionesProyecto();
        cp.Casos.Add(new CasoCarga { Codigo = "D", Nombre = "Carga Muerta", Tipo = TipoCasoCarga.Muerta });
        cp.Casos.Add(new CasoCarga { Codigo = "L", Nombre = "Carga Viva", Tipo = TipoCasoCarga.Viva });

        var ult = new CombinacionCarga { Nombre = "1.2D + 1.6L", Tipo = TipoCombinacion.Ultima };
        ult["D"] = 1.2; ult["L"] = 1.6;
        cp.Combinaciones.Add(ult);
        return cp;
    }

    [Fact]
    public void Genera_libro_con_hojas_SAF()
    {
        var path = TempPath();
        try
        {
            SafExporter.Export(new[] { BuildDemoViga() }, BuildDemoCargas(), path, "Proyecto Test");
            Assert.True(File.Exists(path));

            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToArray();
            Assert.Contains("StructuralPointConnection", names);
            Assert.Contains("StructuralCurveMember", names);
            Assert.Contains("StructuralPointSupport", names);
            Assert.Contains("StructuralMaterial", names);
            Assert.Contains("StructuralCrossSection", names);
            Assert.Contains("StructuralLoadCase", names);
            Assert.Contains("StructuralLoadCombination", names);
            Assert.Contains("StructuralCurveMemberLoad", names);
            Assert.Contains("StructuralPointLoadOnBeam", names);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Normaliza_seccion_y_material_y_nodos()
    {
        var path = TempPath();
        try
        {
            SafExporter.Export(new[] { BuildDemoViga() }, BuildDemoCargas(), path, "Proyecto Test");
            using var wb = new XLWorkbook(path);

            // Dos tramos con la misma sección/material → un único registro de cada.
            Assert.Equal(1, wb.Worksheet("StructuralCrossSection").RowsUsed().Count() - 1);
            Assert.Equal(1, wb.Worksheet("StructuralMaterial").RowsUsed().Count() - 1);
            Assert.Equal("R300x500", wb.Worksheet("StructuralCrossSection").Cell(2, 1).GetString());

            // Nodos deduplicados: fronteras 0,5,10 (los apoyos coinciden) → 3 nodos.
            var nodos = wb.Worksheet("StructuralPointConnection");
            Assert.Equal(3, nodos.RowsUsed().Count() - 1);
            Assert.Equal(5.0, nodos.Cell(3, 2).GetDouble(), 6);

            // Dos tramos → dos miembros.
            Assert.Equal(2, wb.Worksheet("StructuralCurveMember").RowsUsed().Count() - 1);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Cargas_descendentes_son_negativas_en_Z()
    {
        var path = TempPath();
        try
        {
            SafExporter.Export(new[] { BuildDemoViga() }, BuildDemoCargas(), path, "Proyecto Test");
            using var wb = new XLWorkbook(path);

            // Distribuida: Value 1 (col 6) = -20 (gravedad −Z).
            var dist = wb.Worksheet("StructuralCurveMemberLoad");
            Assert.Equal(-20.0, dist.Cell(2, 6).GetDouble(), 6);

            // Puntual: Value (col 5) = -12.
            var punt = wb.Worksheet("StructuralPointLoadOnBeam");
            Assert.Equal(-12.0, punt.Cell(2, 5).GetDouble(), 6);
            Assert.Equal(2.5, punt.Cell(2, 6).GetDouble(), 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Combinacion_se_exporta_como_ULS_con_coeficientes()
    {
        var path = TempPath();
        try
        {
            SafExporter.Export(new[] { BuildDemoViga() }, BuildDemoCargas(), path, "Proyecto Test");
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("StructuralLoadCombination");

            Assert.Equal("1.2D + 1.6L", ws.Cell(2, 1).GetString());
            Assert.Equal("ULS", ws.Cell(2, 2).GetString());
            Assert.Equal("D;L", ws.Cell(2, 3).GetString());
            Assert.Equal("1.2;1.6", ws.Cell(2, 4).GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
