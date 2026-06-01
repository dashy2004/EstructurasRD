using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Models;
using LosasPlus.Transmision;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exportador XLSX de la bajada de cargas (Fase J.8): genera el libro
/// a un path temporal y lo reabre para verificar la tabla por nivel y el
/// resumen de carga en base + zapata.
/// </summary>
public class BajadaCargasExporterTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"bajada_{Guid.NewGuid():N}.xlsx");

    private static Edificio EdificioDosNiveles()
    {
        var ed = new Edificio();
        foreach (var cota in new[] { 0.0, 3.0 })
        {
            var nivel = new Nivel { Cota = cota };
            var s = new Sistema();
            s.Losas.Add(new Losa { Lx = 10, Ly = 10, Carga = 1.5 }); // 150 por nivel
            nivel.Sistemas.Add(s);
            ed.Niveles.Add(nivel);
        }
        return ed;
    }

    [Fact]
    public void Exporta_tabla_por_nivel_y_resumen_de_zapata()
    {
        var path = TempPath();
        try
        {
            BajadaCargasExporter.Export(EdificioDosNiveles(), 15, path);
            Assert.True(File.Exists(path));

            using var wb = new XLWorkbook(path);
            Assert.Contains(wb.Worksheets, w => w.Name == BajadaCargasExporter.NombreHoja);
            var ws = wb.Worksheet(BajadaCargasExporter.NombreHoja);

            Assert.Equal("Nivel", ws.Cell(1, 1).GetString());

            // Tope (cota 3) y base (cota 0).
            Assert.Equal(3, ws.Cell(2, 2).GetDouble(), 4);
            Assert.Equal(150, ws.Cell(2, 4).GetDouble(), 4);
            Assert.Equal(0, ws.Cell(3, 2).GetDouble(), 4);
            Assert.Equal(300, ws.Cell(3, 4).GetDouble(), 4);

            // Resumen (fila 5 en adelante, tras una fila en blanco).
            Assert.Equal(300, ws.Cell(5, 2).GetDouble(), 4);              // carga en base
            Assert.Equal(15, ws.Cell(6, 2).GetDouble(), 4);               // q_adm
            Assert.Equal(20, ws.Cell(7, 2).GetDouble(), 4);               // área zapata
            Assert.Equal(Math.Sqrt(20), ws.Cell(8, 2).GetDouble(), 4);    // lado zapata
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
