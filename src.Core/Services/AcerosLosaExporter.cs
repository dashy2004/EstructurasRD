using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using LosasPlus.Calculo;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el <b>diseño de acero a flexión por franja</b> (pestaña "Aceros", A1):
/// para cada <see cref="Losa"/> con momentos importados del <c>.TXT</c>, corre
/// <see cref="AcerosLosaDesigner.DisenarLosa"/> y vuelca las 4 franjas
/// (X/Y centro y apoyo) como una tabla plana — el equivalente exportable del
/// <c>DataGrid</c> de la pestaña Aceros.
///
/// <para>Complementa a <see cref="CsvExporter"/>/<see cref="XlsxExporter"/>, que
/// exportan la tabla a nivel de <b>losa</b> (geometría + momentos + αfm). Acá la
/// granularidad es la <b>franja</b> (As req/mín/diseño + "Disponer" por barra).</para>
///
/// <para>Funciones puras sin dependencia de Avalonia — testeables headless. El
/// método <see cref="ToCsv"/> devuelve el texto sin tocar disco, para aserción
/// directa en tests.</para>
/// </summary>
public static class AcerosLosaExporter
{
    /// <summary>Una fila aplanada del diseño: identifica losa+franja y el diseño de esa franja.</summary>
    public readonly record struct Fila(int LosaId, int Tipo, DisenoAceroFranja Franja);

    /// <summary>
    /// Enumera las filas de diseño (una por losa×franja) de las losas del sistema
    /// que tienen momentos importados. Mismo criterio y orden que el VM de Aceros.
    /// </summary>
    public static IEnumerable<Fila> Filas(
        Sistema sistema,
        double recubrimientoCm = AcerosLosaDesigner.RecubrimientoDefaultCm,
        int barraSupuesta = 4)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        foreach (var losa in sistema.Losas.Where(TieneMomentos))
        {
            var ml = new MomentoLosa(
                losa.Id, losa.Tipo, losa.Carga, losa.Espesor, losa.Lx, losa.Ly,
                losa.Mfx ?? 0, losa.Mfy ?? 0, losa.MSx ?? 0, losa.MSy ?? 0);

            var d = AcerosLosaDesigner.DisenarLosa(
                ml, sistema.Fc, sistema.Fy, recubrimientoCm, barraSupuesta);

            yield return new Fila(losa.Id, losa.Tipo, d.XCentro);
            yield return new Fila(losa.Id, losa.Tipo, d.YCentro);
            yield return new Fila(losa.Id, losa.Tipo, d.XApoyo);
            yield return new Fila(losa.Id, losa.Tipo, d.YApoyo);
        }
    }

    private static bool TieneMomentos(Losa l)
        => l.Mfx.HasValue || l.Mfy.HasValue || l.MSx.HasValue || l.MSy.HasValue;

    private static readonly string[] Encabezados =
    {
        "LOSA_ID", "TIPO", "FRANJA", "Mu_tonM", "d_cm",
        "As_req_cm2m", "As_min_cm2m", "As_diseno_cm2m",
        "Barra", "Espaciamiento_cm", "As_prov_cm2m", "Disponer", "Gobierna", "ESTADO",
    };

    /// <summary>Estado textual de la franja (espeja <c>DisenoAceroFila.Estado</c> del VM).</summary>
    public static string Estado(DisenoAceroFranja f)
        => f.SeccionInsuficiente ? "SECCIÓN INSUF."
         : (!f.SeccionInsuficiente && f.CumpleEspaciamiento ? "OK" : "REVISAR");

    /// <summary>Qué armadura gobierna: "mínimo" (temperatura) o "flexión".</summary>
    private static string Gobierna(DisenoAceroFranja f)
        => f.SeccionInsuficiente ? "" : (f.GobiernaMinimo ? "mínimo" : "flexión");

    /// <summary>
    /// Diseño de acero por franja como CSV (UTF-8/BOM, separador ';' compatible
    /// con Excel-ES). Determinista (sin timestamp) para testeo directo.
    /// </summary>
    public static string ToCsv(
        Sistema sistema,
        double recubrimientoCm = AcerosLosaDesigner.RecubrimientoDefaultCm,
        int barraSupuesta = 4,
        char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.AppendLine(string.Join(sep, Encabezados));
        foreach (var fila in Filas(sistema, recubrimientoCm, barraSupuesta))
        {
            var f = fila.Franja;
            sb.AppendLine(string.Join(sep,
                fila.LosaId,
                fila.Tipo,
                Quote(f.Franja),
                F(f.MuTonM, inv),
                F(f.DCm, inv),
                F(f.AsRequeridoCm2M, inv),
                F(f.AsMinimoCm2M, inv),
                F(f.AsDisenoCm2M, inv),
                f.SeccionInsuficiente ? "" : ("#" + f.NumeroBarra.ToString(inv)),
                F(f.EspaciamientoCm, inv),
                F(f.AsProvistaCm2M, inv),
                Quote(f.Disponer),
                Quote(Gobierna(f)),
                Quote(Estado(f))));
        }
        sb.AppendLine();
        sb.AppendLine($"Sistema:{sep}{Quote(sistema.Nombre)}");
        sb.AppendLine($"FC_ton_cm2:{sep}{F(sistema.Fc, inv)}");
        sb.AppendLine($"FY_ton_cm2:{sep}{F(sistema.Fy, inv)}");
        sb.AppendLine($"Recubrimiento_cm:{sep}{F(recubrimientoCm, inv)}");
        sb.AppendLine($"Barra_supuesta:{sep}#{barraSupuesta.ToString(inv)}");
        sb.AppendLine($"# Diseño de acero a flexión por franja (ACI 318-19). Motor: LosasPlus.Calculo / AcerosLosaDesigner.");
        return sb.ToString();
    }

    /// <summary>Escribe el CSV del diseño por franja a <paramref name="path"/>.</summary>
    public static void ExportCsv(
        Sistema sistema, string path,
        double recubrimientoCm = AcerosLosaDesigner.RecubrimientoDefaultCm, int barraSupuesta = 4)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(sistema, recubrimientoCm, barraSupuesta), new UTF8Encoding(true));
    }

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XLColor OkFill = XLColor.FromArgb(0xE4, 0xF8, 0xE4);
    private static readonly XLColor WarnFill = XLColor.FromArgb(0xFC, 0xE4, 0xE4);

    /// <summary>
    /// Exporta el diseño por franja a un <c>.xlsx</c> de una hoja, con shading
    /// verde/rojo por ESTADO. Mismo estilo visual que <see cref="XlsxExporter"/>.
    /// </summary>
    public static void ExportXlsx(
        Sistema sistema, string path,
        double recubrimientoCm = AcerosLosaDesigner.RecubrimientoDefaultCm, int barraSupuesta = 4)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Aceros (franjas)");

        ws.Cell("A1").Value = "DISEÑO DE ACERO A FLEXIÓN POR FRANJA — ACI 318-19";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 13;
        ws.Range(1, 1, 1, Encabezados.Length).Merge();
        ws.Cell("A2").Value = string.Create(CultureInfo.InvariantCulture,
            $"Sistema: {sistema.Nombre}  ·  f'c={sistema.Fc:0.000} ton/cm²  ·  fy={sistema.Fy:0.000} ton/cm²  ·  rec={recubrimientoCm:0.#} cm  ·  barra supuesta #{barraSupuesta}");
        ws.Range(2, 1, 2, Encabezados.Length).Merge();

        for (int i = 0; i < Encabezados.Length; i++)
        {
            var cell = ws.Cell(4, i + 1);
            cell.Value = Encabezados[i];
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 5;
        foreach (var fila in Filas(sistema, recubrimientoCm, barraSupuesta))
        {
            var f = fila.Franja;
            int c = 1;
            ws.Cell(row, c++).Value = fila.LosaId;
            ws.Cell(row, c++).Value = fila.Tipo;
            ws.Cell(row, c++).Value = f.Franja;
            ws.Cell(row, c++).Value = f.MuTonM;
            ws.Cell(row, c++).Value = f.DCm;
            if (!double.IsNaN(f.AsRequeridoCm2M)) ws.Cell(row, c).Value = f.AsRequeridoCm2M; c++;
            ws.Cell(row, c++).Value = f.AsMinimoCm2M;
            if (!double.IsNaN(f.AsDisenoCm2M)) ws.Cell(row, c).Value = f.AsDisenoCm2M; c++;
            ws.Cell(row, c++).Value = f.SeccionInsuficiente ? "" : $"#{f.NumeroBarra}";
            if (!double.IsNaN(f.EspaciamientoCm)) ws.Cell(row, c).Value = f.EspaciamientoCm; c++;
            if (!double.IsNaN(f.AsProvistaCm2M)) ws.Cell(row, c).Value = f.AsProvistaCm2M; c++;
            ws.Cell(row, c++).Value = f.Disponer;
            ws.Cell(row, c++).Value = Gobierna(f);
            var estado = Estado(f);
            ws.Cell(row, c).Value = estado;
            ws.Cell(row, c).Style.Fill.BackgroundColor = estado == "OK" ? OkFill : WarnFill;
            row++;
        }

        ws.Range(5, 4, Math.Max(5, row - 1), 8).Style.NumberFormat.Format = "0.000";
        ws.Range(5, 10, Math.Max(5, row - 1), 11).Style.NumberFormat.Format = "0.0";
        ws.SheetView.FreezeRows(4);
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        wb.SaveAs(path);
    }

    private static string F(double v, CultureInfo c) => double.IsNaN(v) ? "" : v.ToString("0.0000", c);
    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
