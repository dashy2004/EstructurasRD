using System;
using System.IO;
using System.Linq;
using LosasPlus.Calculo;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="ColumnaDisenoExporter"/>: el CSV trae el resumen del
/// diseño y la tabla del diagrama de interacción, y el archivo se genera con BOM.
/// </summary>
public class ColumnaDisenoExporterTests
{
    private static ColumnaDisenador.DisenoColumna Diseno()
    {
        var bars = ColumnaDisenador.LayoutPerimetral(400, 400, 40, 3, 3, 8);
        var s = new ColumnaSeccion(400, 400, 28, 420, bars);
        return ColumnaDisenador.DisenarColumna(s, 8, 1_000_000, 50e6);
    }

    [Fact]
    public void Csv_trae_resumen_y_tabla_del_diagrama()
    {
        var d = Diseno();
        var csv = ColumnaDisenoExporter.ToCsv(d);
        var lineas = csv.Replace("\r\n", "\n").Split('\n');

        Assert.Contains(lineas, l => l.StartsWith("RhoG:"));
        Assert.Contains(lineas, l => l.StartsWith("Po_kN:"));
        Assert.Contains(lineas, l => l.StartsWith("PhiPnMax_kN:"));
        Assert.Contains(lineas, l => l.StartsWith("Estribo:"));
        Assert.Contains(lineas, l => l.StartsWith("Demanda_ratio:"));
        // cabecera de la tabla del diagrama + una fila por punto del diagrama.
        Assert.Contains("c_mm;Pn_kN;Mn_kNm;phi;phiPn_kN;phiMn_kNm", csv);
        int filasDiagrama = lineas.Count(l => l.Length > 0 && char.IsDigit(l[0]) && l.Contains(';') && !l.Contains(':'));
        Assert.Equal(d.Diagrama.Count, filasDiagrama);
    }

    [Fact]
    public void ExportCsv_escribe_archivo_con_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"columna_{Guid.NewGuid():N}.csv");
        try
        {
            ColumnaDisenoExporter.ExportCsv(Diseno(), path);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));   // BOM UTF-8
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
