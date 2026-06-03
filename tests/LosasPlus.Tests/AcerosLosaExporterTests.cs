using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="AcerosLosaExporter"/> (export de la pestaña Aceros, A1):
/// la tabla por franja se exporta con el mismo contenido que muestra el grid
/// (paridad con <see cref="AcerosViewModel"/>), excluye las losas sin momentos,
/// y los archivos CSV/XLSX se generan correctamente.
/// </summary>
public class AcerosLosaExporterTests
{
    private static Sistema SistemaConUnaLosaConMomentos()
    {
        var s = new Sistema { Nombre = "DEMO", Fc = 0.210, Fy = 4.200 };
        s.Losas.Add(new Losa
        {
            Id = 1, Tipo = 33, Lx = 4.0, Ly = 5.0, Espesor = 0.20, Carga = 0.8,
            Mfx = 1.0, Mfy = 0.8, MSx = 1.5, MSy = 1.2,
        });
        // Losa SIN momentos → debe quedar excluida (igual que en el grid).
        s.Losas.Add(new Losa { Id = 2, Tipo = 21, Lx = 3.0, Ly = 3.0, Espesor = 0.15 });
        return s;
    }

    [Fact]
    public void Filas_solo_de_losas_con_momentos_cuatro_por_losa()
    {
        var s = SistemaConUnaLosaConMomentos();
        var filas = AcerosLosaExporter.Filas(s).ToList();

        Assert.Equal(4, filas.Count);                     // 1 losa con momentos × 4 franjas
        Assert.All(filas, f => Assert.Equal(1, f.LosaId)); // la losa 2 (sin momentos) no aparece
        Assert.Contains(filas, f => f.Franja.Franja == "X centro");
        Assert.Contains(filas, f => f.Franja.Franja == "Y apoyo");
    }

    [Fact]
    public void Csv_tiene_encabezado_cuatro_filas_y_metadatos()
    {
        var s = SistemaConUnaLosaConMomentos();
        var csv = AcerosLosaExporter.ToCsv(s);
        var lineas = csv.Replace("\r\n", "\n").Split('\n');

        Assert.StartsWith("LOSA_ID;TIPO;FRANJA", lineas[0]);
        // 4 filas de datos (las que empiezan con el id de losa "1;").
        Assert.Equal(4, lineas.Count(l => l.StartsWith("1;")));
        Assert.Contains(lineas, l => l.StartsWith("FC_ton_cm2:"));  // convención key:;value (igual que CsvExporter)
        Assert.Contains(lineas, l => l.StartsWith("FY_ton_cm2:"));
        Assert.Contains("Disponer", csv);   // encabezado presente
        Assert.Contains("@", csv);          // formato "#n @ s cm"
    }

    [Fact]
    public void Csv_es_consistente_con_el_grid_de_AcerosViewModel()
    {
        // El export y la tabla en pantalla deben coincidir fila por fila.
        var s = SistemaConUnaLosaConMomentos();
        var vm = new AcerosViewModel(() => s);
        var filas = AcerosLosaExporter.Filas(s).ToList();

        Assert.Equal(vm.Filas.Count, filas.Count);
        for (int i = 0; i < filas.Count; i++)
        {
            Assert.Equal(vm.Filas[i].Franja, filas[i].Franja.Franja);
            Assert.Equal(vm.Filas[i].AsDisenoCm2M, filas[i].Franja.AsDisenoCm2M, 6);
            Assert.Equal(vm.Filas[i].Disponer, filas[i].Franja.Disponer);
            Assert.Equal(vm.Filas[i].Estado, AcerosLosaExporter.Estado(filas[i].Franja));
        }
    }

    [Fact]
    public void Parametros_se_propagan_al_diseno()
    {
        var s = SistemaConUnaLosaConMomentos();
        var dDefault = AcerosLosaExporter.Filas(s).First().Franja.DCm;
        var dMasRec  = AcerosLosaExporter.Filas(s, recubrimientoCm: 4.0).First().Franja.DCm;

        Assert.True(dMasRec < dDefault);  // mayor recubrimiento → menor canto útil
    }

    [Fact]
    public void ExportCsv_escribe_archivo_con_bom()
    {
        var s = SistemaConUnaLosaConMomentos();
        var path = Path.Combine(Path.GetTempPath(), $"aceros_{Guid.NewGuid():N}.csv");
        try
        {
            AcerosLosaExporter.ExportCsv(s, path);
            Assert.True(File.Exists(path));
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));  // BOM UTF-8
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ExportXlsx_genera_un_archivo_abrible_con_la_hoja_de_franjas()
    {
        var s = SistemaConUnaLosaConMomentos();
        var path = Path.Combine(Path.GetTempPath(), $"aceros_{Guid.NewGuid():N}.xlsx");
        try
        {
            AcerosLosaExporter.ExportXlsx(s, path);
            using var wb = new XLWorkbook(path);
            Assert.Contains(wb.Worksheets, ws => ws.Name == "Aceros (franjas)");
            var ws = wb.Worksheet("Aceros (franjas)");
            // 4 filas de datos a partir de la fila 5 (encabezado en la 4).
            Assert.Equal(1, ws.Cell(5, 1).GetValue<int>());  // primera fila: LOSA_ID = 1
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
