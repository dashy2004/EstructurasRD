using System.IO;
using System.Linq;
using ClosedXML.Excel;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del exportador XLSX. Genera el archivo a un path temporal y luego lo
/// reabre con ClosedXML para verificar contenido por hoja, encabezados y celdas
/// puntuales.
/// </summary>
public class XlsxExporterTests
{
    private static Sistema BuildDemoSistema()
    {
        var s = new Sistema { Nombre = "Sistema Test", Fc = 0.21, Fy = 4.2, Adicionales = 1 };
        s.Losas.Add(new Losa { Id = 1, Tipo = 60, Carga = 1.5, Espesor = 0.25, Lx = 4, Ly = 6, Rec = 0.02,
                               Mfx = 1.6, Mfy = 0.4, MSx = 1.9, MSy = 1.3,
                               AsxVano = "Ø3/8\" @ 15 cm", AsyVano = "Ø3/8\" @ 15 cm" });
        s.Losas.Add(new Losa { Id = 2, Tipo = 51, Carga = 1.4, Espesor = 0.25, Lx = 5, Ly = 6, Rec = 0.02 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2, Balanceo = "S", MuI = 1.9, MuJ = 2.1, MuIJ = 2.0, D = 21.8, AsReq = 4.5, Disponer = "Ø3/8\" @ 15 cm" });
        return s;
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"losasplus_{System.Guid.NewGuid():N}.xlsx");

    [Fact]
    public void Genera_xlsx_con_hojas_basicas()
    {
        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = BuildDemoSistema() }, path);
            Assert.True(File.Exists(path));

            using var wb = new XLWorkbook(path);
            var names = wb.Worksheets.Select(w => w.Name).ToArray();
            Assert.Contains("Resumen", names);
            Assert.Contains("Losas", names);
            Assert.Contains("Apoyos", names);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hoja_Losas_tiene_encabezados_y_filas_correctas()
    {
        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = BuildDemoSistema() }, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Losas");

            Assert.Equal("ID", ws.Cell(1, 1).GetString());
            Assert.Equal("TIPO", ws.Cell(1, 2).GetString());
            Assert.Equal("Lx [m]", ws.Cell(1, 6).GetString());

            // Fila 2 = primera losa
            Assert.Equal(1, ws.Cell(2, 1).GetDouble());
            Assert.Equal(60, ws.Cell(2, 2).GetDouble());
            Assert.Equal(4.0, ws.Cell(2, 6).GetDouble());
            Assert.Equal(1.6, ws.Cell(2, 10).GetDouble());        // Mfx
            Assert.Equal("Ø3/8\" @ 15 cm", ws.Cell(2, 14).GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hoja_Apoyos_incluye_borde_X_con_momentos()
    {
        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = BuildDemoSistema() }, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Apoyos");

            Assert.Equal("X", ws.Cell(2, 1).GetString());
            Assert.Equal(1, ws.Cell(2, 2).GetDouble());
            Assert.Equal(2, ws.Cell(2, 3).GetDouble());
            Assert.Equal("S", ws.Cell(2, 4).GetString());
            Assert.Equal(1.9, ws.Cell(2, 5).GetDouble());     // MuI
            Assert.Equal(2.0, ws.Cell(2, 7).GetDouble());     // MuI-J
            Assert.Equal("Ø3/8\" @ 15 cm", ws.Cell(2, 10).GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hoja_Combinaciones_se_omite_si_archivos_no_existen()
    {
        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = BuildDemoSistema() }, path);
            using var wb = new XLWorkbook(path);
            Assert.DoesNotContain("Combinaciones", wb.Worksheets.Select(w => w.Name));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hoja_Combinaciones_se_genera_si_se_provee_archivo()
    {
        var path = TempPath();
        var dzp = Path.Combine(Path.GetTempPath(), $"losasplus_combs_{System.Guid.NewGuid():N}.DZP");
        File.WriteAllText(dzp,
            "$ DEFINICION DE LAS CARGAS\n" +
            "$  NCC  NCOMB\n" +
            "    11   86\n" +
            "$  IC   NOMBRE        TIPO  FCE  FSIS\n" +
            "    1   Carga Muerta    1    0    0\n",
            System.Text.Encoding.GetEncoding(1252));
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext
            {
                Sistema = BuildDemoSistema(),
                CombinacionesDzpPath = dzp,
            }, path);
            using var wb = new XLWorkbook(path);
            Assert.Contains("Combinaciones", wb.Worksheets.Select(w => w.Name));
            var ws = wb.Worksheet("Combinaciones");
            Assert.Contains("DEFINICION", ws.Cell(5, 1).GetString());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(dzp)) File.Delete(dzp);
        }
    }

    [Fact]
    public void Aspecto_fuera_de_rango_se_resalta_en_amarillo()
    {
        var s = BuildDemoSistema();
        s.Losas[0].Lx = 1; s.Losas[0].Ly = 5;  // Aspecto = 5, fuera

        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = s }, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Losas");
            // Columna 9 (Ly/Lx) de la fila 2 debe tener fondo amarillo
            var fill = ws.Cell(2, 9).Style.Fill.BackgroundColor;
            Assert.NotEqual(XLColor.NoColor, fill);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hoja_Resumen_lista_metadatos_del_sistema()
    {
        var s = BuildDemoSistema();
        var path = TempPath();
        try
        {
            XlsxExporter.Export(new XlsxExporter.ExportContext { Sistema = s, DLPath = "C:/test/x.DL" }, path);
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("Resumen");
            // Buscar la fila con "Sistema" y verificar que esté el nombre
            var sistemaRow = ws.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString() == "Sistema");
            Assert.NotNull(sistemaRow);
            Assert.Equal("Sistema Test", sistemaRow!.Cell(2).GetString());

            var dlRow = ws.RowsUsed().FirstOrDefault(r => r.Cell(1).GetString() == "Archivo .DL");
            Assert.NotNull(dlRow);
            Assert.Equal("C:/test/x.DL", dlRow!.Cell(2).GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
