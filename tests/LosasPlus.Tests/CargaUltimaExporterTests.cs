using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exporter del desglose de carga última (Sesión D, soporte de
/// validación Wu vs Losas.exe): CSV con resumen del sistema + tabla por losa.
/// </summary>
public class CargaUltimaExporterTests
{
    private static Muro Mur(double largo, double espesor, double altura)
        => new()
        {
            PuntoInicio = new PuntoCad(0, 0),
            PuntoFin = new PuntoCad(largo, 0),
            Espesor = espesor,
            Altura = altura,
        };

    [Fact]
    public void ToCsv_incluye_resumen_y_una_fila_por_losa_con_su_Wu()
    {
        var inv = CultureInfo.InvariantCulture;
        var cargas = CargasGlobales.SemillaPorDefecto();
        var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
        sistema.Losas.Add(new Losa { Id = 7, Lx = 4, Ly = 5, Espesor = 0.12 });
        sistema.Muros.Add(Mur(5.0, 0.15, 2.8));

        var csv = CargaUltimaExporter.ToCsv(sistema, cargas);

        // Wu esperado por el mismo pipeline (qmap repartido sobre el área total).
        var qmamp = 1.8 * 5.0 * 0.15 * 2.8;
        var qmap = CalculoEngine.ComputeQmap(qmamp, 20.0);
        var ql = CalculoEngine.ComputeQl(SistemaUso.Entrepiso, cargas);
        var qu = CalculoEngine.ComputeQu(
            CalculoEngine.ComputeQd(0.12, cargas, SistemaUso.Entrepiso, qmap), ql, cargas.Factores);

        Assert.Contains("Qmamp_ton", csv);
        Assert.Contains("LosaId", csv);                       // cabecera de la tabla
        Assert.Contains("7", csv);                            // id de la losa
        Assert.Contains(qu.ToString("0.000", inv), csv);      // Wu de la losa
    }

    [Fact]
    public void ExportXlsx_genera_un_archivo_abrible_con_la_hoja_de_carga_ultima()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
        sistema.Losas.Add(new Losa { Id = 7, Lx = 4, Ly = 5, Espesor = 0.12 });
        sistema.Muros.Add(Mur(5.0, 0.15, 2.8));

        var path = Path.Combine(Path.GetTempPath(), $"cargau_{Guid.NewGuid():N}.xlsx");
        try
        {
            CargaUltimaExporter.ExportXlsx(sistema, cargas, path);

            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            Assert.Contains(wb.Worksheets, ws => ws.Name == "Carga última");
            var ws = wb.Worksheet("Carga última");
            Assert.Equal(7, ws.Cell(8, 1).GetValue<int>());   // primera fila de datos: LosaId = 7
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static Edificio EdificioDosNiveles(int losaIdN1, int losaIdN2)
    {
        var edificio = new Edificio();
        foreach (var (cota, losaId) in new[] { (0.0, losaIdN1), (3.0, losaIdN2) })
        {
            var nivel = new Nivel { Cota = cota, Nombre = $"N{cota}" };
            var sistema = new Sistema { Uso = SistemaUso.Entrepiso };
            sistema.Losas.Add(new Losa { Id = losaId, Lx = 4, Ly = 5, Espesor = 0.12 });
            nivel.Sistemas.Add(sistema);
            edificio.Niveles.Add(nivel);
        }
        return edificio;
    }

    [Fact]
    public void ToCsv_edificio_incluye_los_sistemas_de_todos_los_niveles()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var edificio = EdificioDosNiveles(101, 202);

        var csv = CargaUltimaExporter.ToCsv(edificio, cargas);

        Assert.Contains("101", csv);   // losa del nivel 1
        Assert.Contains("202", csv);   // losa del nivel 2
    }

    [Fact]
    public void ExportXlsx_edificio_genera_una_hoja_por_sistema()
    {
        var cargas = CargasGlobales.SemillaPorDefecto();
        var edificio = EdificioDosNiveles(101, 202);

        var path = Path.Combine(Path.GetTempPath(), $"cargau_ed_{Guid.NewGuid():N}.xlsx");
        try
        {
            CargaUltimaExporter.ExportXlsx(edificio, cargas, path);

            Assert.True(File.Exists(path));
            using var wb = new XLWorkbook(path);
            Assert.Equal(2, wb.Worksheets.Count);   // un sistema por nivel
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
