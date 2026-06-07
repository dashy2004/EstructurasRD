using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using LosasPlus.Calculo;
using LosasPlus.Models;
using LosasPlus.Transmision;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el <b>desglose de carga última directa</b> (Sesión D) de un sistema a
/// CSV: un bloque de resumen (mampostería de muros, qmap repartido, carga viva)
/// seguido de una tabla por losa con su carga muerta total y su Wu (LRFD). Sirve
/// para <b>validar Wu vs Losas.exe</b> comparando fila a fila. Unidades en t/m²
/// (Qmamp en ton). UTF-8/BOM, separador ';'.
///
/// <para>Espeja a <see cref="ColumnaDisenoExporter"/>/<see cref="AcerosLosaExporter"/>:
/// <see cref="ToCsv"/> es puro y determinista (sin timestamp) para aserción directa
/// en tests. Reproduce la misma convención que
/// <see cref="CargaUltimaCalculator.AplicarCargaUltima(Sistema, CargasGlobales)"/>:
/// mampostería repartida sobre el área total (qmap común) y peso propio por el
/// espesor de cada losa.</para>
/// </summary>
public static class CargaUltimaExporter
{
    /// <summary>Desglose de carga última de un sistema a CSV (texto, sin tocar disco).</summary>
    public static string ToCsv(Sistema sistema, CargasGlobales cargas, char sep = ';')
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        double qmamp = CargaUltimaCalculator.PesoMamposteria(sistema);
        double areaTotal = sistema.Losas.Sum(l => l.Lx * l.Ly);
        double qmap = CalculoEngine.ComputeQmap(qmamp, areaTotal);
        double ql = CalculoEngine.ComputeQl(sistema.Uso, cargas);

        sb.AppendLine("# CARGA ÚLTIMA DIRECTA — Wu (LRFD 1.2D + 1.6L)");
        sb.AppendLine($"Uso:{sep}{sistema.Uso}");
        sb.AppendLine(string.Join(sep, "Qmamp_ton", "Qmap_tm2", "Ql_tm2"));
        sb.AppendLine(string.Join(sep,
            qmamp.ToString("0.000", inv),
            qmap.ToString("0.000", inv),
            ql.ToString("0.000", inv)));
        sb.AppendLine();

        sb.AppendLine(string.Join(sep, "LosaId", "Lx_m", "Ly_m", "Espesor_m", "Qd_tm2", "Qu_tm2"));
        foreach (var losa in sistema.Losas)
        {
            double qd = CalculoEngine.ComputeQd(losa.Espesor, cargas, sistema.Uso, qmap);
            double qu = CalculoEngine.ComputeQu(qd, ql, cargas.Factores);
            sb.AppendLine(string.Join(sep,
                losa.Id.ToString(inv),
                losa.Lx.ToString("0.00", inv),
                losa.Ly.ToString("0.00", inv),
                losa.Espesor.ToString("0.000", inv),
                qd.ToString("0.000", inv),
                qu.ToString("0.000", inv)));
        }

        return sb.ToString();
    }

    /// <summary>Escribe el CSV del desglose a <paramref name="path"/> (UTF-8 con BOM).</summary>
    public static void ExportCsv(Sistema sistema, CargasGlobales cargas, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(sistema, cargas), new UTF8Encoding(true));
    }

    /// <summary>
    /// Desglose de carga última de <b>todo un edificio</b> a CSV: concatena el
    /// bloque de cada sistema (recorriendo niveles → sistemas) precedido por una
    /// cabecera que identifica el nivel y el sistema.
    /// </summary>
    public static string ToCsv(Edificio edificio, CargasGlobales cargas, char sep = ';')
    {
        if (edificio is null) throw new ArgumentNullException(nameof(edificio));
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));

        var sb = new StringBuilder();
        foreach (var nivel in edificio.Niveles)
            foreach (var sistema in nivel.Sistemas)
            {
                sb.AppendLine($"## NIVEL:{sep}{nivel.Nombre}{sep}SISTEMA:{sep}{sistema.Nombre}");
                sb.Append(ToCsv(sistema, cargas, sep));
                sb.AppendLine();
            }
        return sb.ToString();
    }

    /// <summary>Escribe el CSV del desglose de todo el edificio a <paramref name="path"/> (UTF-8 con BOM).</summary>
    public static void ExportCsv(Edificio edificio, CargasGlobales cargas, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(edificio, cargas), new UTF8Encoding(true));
    }

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x29, 0x37);

    /// <summary>
    /// Exporta el mismo desglose a <b>XLSX</b> (hoja «Carga última»): bloque de
    /// resumen + tabla por losa con su Wu. Mismo estilo visual que
    /// <see cref="AcerosLosaExporter"/> (encabezados oscuros, columnas autoajustadas).
    /// </summary>
    public static void ExportXlsx(Sistema sistema, CargasGlobales cargas, string path)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));

        using var wb = new XLWorkbook();
        EscribirHoja(wb.Worksheets.Add("Carga última"), sistema, cargas);
        GuardarLibro(wb, path);
    }

    /// <summary>
    /// Exporta el desglose de <b>todo el edificio</b> a XLSX: una hoja por sistema
    /// (recorriendo niveles → sistemas), cada una con el mismo formato que la
    /// sobrecarga por sistema.
    /// </summary>
    public static void ExportXlsx(Edificio edificio, CargasGlobales cargas, string path)
    {
        if (edificio is null) throw new ArgumentNullException(nameof(edificio));
        if (cargas is null) throw new ArgumentNullException(nameof(cargas));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));

        using var wb = new XLWorkbook();
        int k = 1;
        foreach (var nivel in edificio.Niveles)
            foreach (var sistema in nivel.Sistemas)
                EscribirHoja(wb.Worksheets.Add(NombreHoja(k++, sistema)), sistema, cargas);
        GuardarLibro(wb, path);
    }

    private static void EscribirHoja(IXLWorksheet ws, Sistema sistema, CargasGlobales cargas)
    {
        var inv = CultureInfo.InvariantCulture;
        double qmamp = CargaUltimaCalculator.PesoMamposteria(sistema);
        double areaTotal = sistema.Losas.Sum(l => l.Lx * l.Ly);
        double qmap = CalculoEngine.ComputeQmap(qmamp, areaTotal);
        double ql = CalculoEngine.ComputeQl(sistema.Uso, cargas);

        ws.Cell("A1").Value = "CARGA ÚLTIMA DIRECTA — Wu (LRFD 1.2D + 1.6L)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, 6).Merge();
        ws.Cell("A2").Value = string.Create(inv, $"Sistema: {sistema.Nombre}  ·  Uso: {sistema.Uso}");
        ws.Range(2, 1, 2, 6).Merge();

        // Resumen (fila 4 encabezados, fila 5 valores).
        string[] resumen = { "Qmamp_ton", "Qmap_tm2", "Ql_tm2" };
        for (int i = 0; i < resumen.Length; i++)
            EstiloEncabezado(ws.Cell(4, i + 1), resumen[i]);
        ws.Cell(5, 1).Value = qmamp;
        ws.Cell(5, 2).Value = qmap;
        ws.Cell(5, 3).Value = ql;
        ws.Range(5, 1, 5, 3).Style.NumberFormat.Format = "0.000";

        // Tabla por losa (fila 7 encabezados, datos desde fila 8).
        string[] headers = { "LosaId", "Lx_m", "Ly_m", "Espesor_m", "Qd_tm2", "Qu_tm2" };
        for (int i = 0; i < headers.Length; i++)
            EstiloEncabezado(ws.Cell(7, i + 1), headers[i]);

        int row = 8;
        foreach (var losa in sistema.Losas)
        {
            double qd = CalculoEngine.ComputeQd(losa.Espesor, cargas, sistema.Uso, qmap);
            double qu = CalculoEngine.ComputeQu(qd, ql, cargas.Factores);
            ws.Cell(row, 1).Value = losa.Id;
            ws.Cell(row, 2).Value = losa.Lx;
            ws.Cell(row, 3).Value = losa.Ly;
            ws.Cell(row, 4).Value = losa.Espesor;
            ws.Cell(row, 5).Value = qd;
            ws.Cell(row, 6).Value = qu;
            row++;
        }
        ws.Range(8, 2, Math.Max(8, row - 1), 6).Style.NumberFormat.Format = "0.000";
        ws.SheetView.FreezeRows(7);
        ws.Columns().AdjustToContents();
    }

    private static void GuardarLibro(XLWorkbook wb, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        wb.SaveAs(path);
    }

    /// <summary>
    /// Nombre de hoja único y válido para el sistema <paramref name="k"/>-ésimo:
    /// prefijo numérico (garantiza unicidad) + nombre del sistema, saneado de los
    /// caracteres que Excel prohíbe y truncado a 31 caracteres.
    /// </summary>
    private static string NombreHoja(int k, Sistema sistema)
    {
        var nombre = string.IsNullOrWhiteSpace(sistema.Nombre) ? $"Sistema {k}" : sistema.Nombre;
        var bruto = $"{k}-{nombre}";
        foreach (var c in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            bruto = bruto.Replace(c, '_');
        return bruto.Length <= 31 ? bruto : bruto.Substring(0, 31);
    }

    private static void EstiloEncabezado(IXLCell cell, string texto)
    {
        cell.Value = texto;
        cell.Style.Fill.BackgroundColor = HeaderFill;
        cell.Style.Font.FontColor = XLColor.White;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }
}
