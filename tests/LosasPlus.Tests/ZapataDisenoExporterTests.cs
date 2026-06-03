using System;
using System.IO;
using System.Linq;
using LosasPlus.Calculo;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del exporter del diseño de zapata: el CSV trae el resumen (presión,
/// ratios de cortante, momento, acero, cumple) y se genera con BOM.
/// </summary>
public class ZapataDisenoExporterTests
{
    private static ZapataDisenador.DisenoZapata Diseno()
        => ZapataDisenador.DisenarZapata(
            puN: 1.0e6, bMm: 2000, lMm: 2000, c1Mm: 400, c2Mm: 400,
            dMm: 500, hMm: 600, fcMPa: 28, fyMPa: 420);

    [Fact]
    public void ToCsv_trae_el_resumen_del_diseno()
    {
        var csv = ZapataDisenoExporter.ToCsv(Diseno());
        var lineas = csv.Replace("\r\n", "\n").Split('\n');

        Assert.Contains(lineas, l => l.StartsWith("Qu_MPa:"));
        Assert.Contains(lineas, l => l.StartsWith("Punzonamiento_ratio:"));
        Assert.Contains(lineas, l => l.StartsWith("Cortante_ratio:"));
        Assert.Contains(lineas, l => l.StartsWith("Mu_kNm:"));
        Assert.Contains(lineas, l => l.StartsWith("As_mm2:"));
        Assert.Contains(lineas, l => l.StartsWith("Cumple:"));
    }

    [Fact]
    public void ExportCsv_escribe_archivo_con_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"zapata_{Guid.NewGuid():N}.csv");
        try
        {
            ZapataDisenoExporter.ExportCsv(Diseno(), path);
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 0);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
