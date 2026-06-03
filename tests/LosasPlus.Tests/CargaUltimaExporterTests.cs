using System.Globalization;
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
}
