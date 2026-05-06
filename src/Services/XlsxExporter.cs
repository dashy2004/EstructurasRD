using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Exporta un <see cref="Sistema"/> a un archivo Excel (.xlsx) con varias hojas:
/// <list type="bullet">
///   <item><b>Resumen</b>: metadatos del sistema + parámetros materiales.</item>
///   <item><b>Losas</b>: tabla con todas las losas (entrada + resultados parseados).</item>
///   <item><b>Apoyos</b>: tabla de bordes I-J en X e Y con sus momentos.</item>
///   <item><b>Esquema</b>: imagen del Canvas si se provee PNG bytes.</item>
///   <item><b>Combinaciones</b>: contenido de Combinaciones.DZP / .CEZ si se provee.</item>
/// </list>
/// </summary>
public static class XlsxExporter
{
    public sealed class ExportContext
    {
        public Sistema Sistema { get; set; } = new();
        public byte[]? EsquemaPng { get; set; }
        public string? CombinacionesDzpPath { get; set; }
        public string? CombinacionesCezPath { get; set; }
        public string? TxtSalidaPath { get; set; }
        public string? TxtSalidaContenido { get; set; }
        public string? DLPath { get; set; }
    }

    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XLColor HeaderFont = XLColor.White;
    private static readonly XLColor WarnFill = XLColor.FromArgb(0xFF, 0xF3, 0xC4);

    public static void Export(ExportContext ctx, string xlsxPath)
    {
        if (string.IsNullOrWhiteSpace(xlsxPath)) throw new ArgumentException("Ruta de salida vacía.", nameof(xlsxPath));

        using var wb = new XLWorkbook();
        AddResumen(wb, ctx);
        AddLosas(wb, ctx.Sistema);
        AddApoyos(wb, ctx.Sistema);
        AddEspejoTxt(wb, ctx);
        if (ctx.EsquemaPng is { Length: > 0 }) AddEsquema(wb, ctx.EsquemaPng);
        AddCombinaciones(wb, ctx.CombinacionesDzpPath, ctx.CombinacionesCezPath);

        // Asegurar que existe la carpeta de destino
        var dir = Path.GetDirectoryName(Path.GetFullPath(xlsxPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        wb.SaveAs(xlsxPath);
    }

    private static void AddResumen(XLWorkbook wb, ExportContext ctx)
    {
        var ws = wb.Worksheets.Add("Resumen");
        var s = ctx.Sistema;
        var rows = new List<(string k, string v)>
        {
            ("Sistema",          s.Nombre),
            ("f'c [ton/cm²]",    s.Fc.ToString("0.000", CultureInfo.InvariantCulture)),
            ("fy  [ton/cm²]",    s.Fy.ToString("0.000", CultureInfo.InvariantCulture)),
            ("ADICIONALES",      s.Adicionales.ToString(CultureInfo.InvariantCulture)),
            ("Cantidad de losas", s.Losas.Count.ToString(CultureInfo.InvariantCulture)),
            ("Bordes adic. en X", s.BordesX.Count.ToString(CultureInfo.InvariantCulture)),
            ("Bordes adic. en Y", s.BordesY.Count.ToString(CultureInfo.InvariantCulture)),
            ("Archivo .DL",       ctx.DLPath ?? "(no guardado)"),
            ("Archivo .TXT",      ctx.TxtSalidaPath ?? "(no importado)"),
            ("",                  ""),
            ("Generado por",      "LosasPlus"),
            ("Motor de cálculo",  "Francisco Eludino Perdomo · Losas v5.x · método Pieper-Martens"),
            ("Fecha export",      DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
        };

        ws.Cell("A1").Value = "RESUMEN DEL SISTEMA";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Range("A1:B1").Merge();

        for (int i = 0; i < rows.Count; i++)
        {
            ws.Cell(i + 3, 1).Value = rows[i].k;
            ws.Cell(i + 3, 1).Style.Font.Bold = true;
            ws.Cell(i + 3, 2).Value = rows[i].v;
        }
        ws.Column(1).Width = 26;
        ws.Column(2).Width = 70;
    }

    private static void AddLosas(XLWorkbook wb, Sistema s)
    {
        var ws = wb.Worksheets.Add("Losas");
        string[] headers =
        {
            "ID", "TIPO", "Descripción tipo", "Carga [t/m²]", "H [m]", "Lx [m]", "Ly [m]", "Rec [m]", "Ly/Lx",
            "Mfx", "Mfy", "MSx", "MSy", "Disponer X (vano)", "Disponer Y (vano)",
        };
        for (int i = 0; i < headers.Length; i++)
            HeaderCell(ws.Cell(1, i + 1), headers[i]);

        int row = 2;
        foreach (var l in s.Losas.OrderBy(x => x.Id))
        {
            int c = 1;
            ws.Cell(row, c++).Value = l.Id;
            ws.Cell(row, c++).Value = l.Tipo;
            ws.Cell(row, c++).Value = TipoLosa.Catalogo.TryGetValue(l.Tipo, out var t) ? t.Descripcion : "(no estándar)";
            ws.Cell(row, c++).Value = l.Carga;
            ws.Cell(row, c++).Value = l.Espesor;
            ws.Cell(row, c++).Value = l.Lx;
            ws.Cell(row, c++).Value = l.Ly;
            ws.Cell(row, c++).Value = l.Rec;
            ws.Cell(row, c++).Value = l.Aspecto;
            if (l.Mfx.HasValue) ws.Cell(row, c).Value = l.Mfx.Value; c++;
            if (l.Mfy.HasValue) ws.Cell(row, c).Value = l.Mfy.Value; c++;
            if (l.MSx.HasValue) ws.Cell(row, c).Value = l.MSx.Value; c++;
            if (l.MSy.HasValue) ws.Cell(row, c).Value = l.MSy.Value; c++;
            ws.Cell(row, c++).Value = l.AsxVano ?? "";
            ws.Cell(row, c++).Value = l.AsyVano ?? "";

            // Resaltar aspecto fuera de rango
            if (l.AspectoFueraDeRango)
                ws.Cell(row, 9).Style.Fill.BackgroundColor = WarnFill;

            row++;
        }

        // Formato numérico
        ws.Range(2, 4, row - 1, 4).Style.NumberFormat.Format = "0.000";    // Carga
        ws.Range(2, 5, row - 1, 8).Style.NumberFormat.Format = "0.000";    // H, Lx, Ly, Rec
        ws.Range(2, 9, row - 1, 9).Style.NumberFormat.Format = "0.00";     // Aspecto
        ws.Range(2, 10, row - 1, 13).Style.NumberFormat.Format = "0.000";  // Momentos

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();
    }

    private static void AddApoyos(XLWorkbook wb, Sistema s)
    {
        var ws = wb.Worksheets.Add("Apoyos");
        string[] headers =
        {
            "Dirección", "B-I", "B-J", "Balanceo",
            "MuI", "MuJ", "MuI-J", "d [cm]", "As [cm²/m]", "Disponer",
        };
        for (int i = 0; i < headers.Length; i++)
            HeaderCell(ws.Cell(1, i + 1), headers[i]);

        int row = 2;
        void DumpBordes(IEnumerable<BordeAdic> bordes, string dir)
        {
            foreach (var b in bordes.OrderBy(x => x.BI).ThenBy(x => x.BJ))
            {
                int c = 1;
                ws.Cell(row, c++).Value = dir;
                ws.Cell(row, c++).Value = b.BI;
                ws.Cell(row, c++).Value = b.BJ;
                ws.Cell(row, c++).Value = b.Balanceo;
                if (b.MuI.HasValue)   ws.Cell(row, c).Value = b.MuI.Value;   c++;
                if (b.MuJ.HasValue)   ws.Cell(row, c).Value = b.MuJ.Value;   c++;
                if (b.MuIJ.HasValue)  ws.Cell(row, c).Value = b.MuIJ.Value;  c++;
                if (b.D.HasValue)     ws.Cell(row, c).Value = b.D.Value;     c++;
                if (b.AsReq.HasValue) ws.Cell(row, c).Value = b.AsReq.Value; c++;
                ws.Cell(row, c++).Value = b.Disponer ?? "";
                row++;
            }
        }
        DumpBordes(s.BordesX, "X");
        DumpBordes(s.BordesY, "Y");

        if (row > 2)
        {
            ws.Range(2, 5, row - 1, 7).Style.NumberFormat.Format = "0.000";
            ws.Range(2, 8, row - 1, 8).Style.NumberFormat.Format = "0.0";
            ws.Range(2, 9, row - 1, 9).Style.NumberFormat.Format = "0.000";
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        ws.RangeUsed()?.SetAutoFilter();
    }

    /// <summary>
    /// Hoja "Espejo .TXT" — reproduce línea por línea el contenido del .TXT del motor
    /// preservando formato/columnas para que se vea idéntico en Excel. Útil para edición/revisión
    /// externa sin perder la apariencia del output original.
    /// </summary>
    private static void AddEspejoTxt(XLWorkbook wb, ExportContext ctx)
    {
        // Resolver el contenido: del context, o leyendo el archivo si se pasó la ruta
        string? content = ctx.TxtSalidaContenido;
        if (string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(ctx.TxtSalidaPath) && File.Exists(ctx.TxtSalidaPath))
        {
            try { content = File.ReadAllText(ctx.TxtSalidaPath, Encoding.GetEncoding(1252)); }
            catch { /* opcional, seguimos sin hoja */ }
        }
        if (string.IsNullOrEmpty(content)) return;

        var ws = wb.Worksheets.Add("Espejo .TXT");
        ws.Cell("A1").Value = "Salida del motor (espejo del .TXT generado por Losas.exe)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 12;
        ws.Range("A1:B1").Merge();

        // Cada línea del .TXT se vuelca tal cual en una sola columna en monoespaciado.
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var cell = ws.Cell(i + 3, 1);
            cell.Value = lines[i];
            cell.Style.Font.FontName = "Consolas";
            cell.Style.Font.FontSize = 10;
            cell.Style.Alignment.WrapText = false;

            // Colores por tipo de línea para legibilidad (similar al HighlightedTxtView)
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("$"))
                cell.Style.Font.FontColor = XLColor.FromArgb(0x88, 0x88, 0x88);
            else if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"^\s*[-=_]{3,}\s*$"))
                cell.Style.Font.FontColor = XLColor.FromArgb(0xAA, 0xAA, 0xAA);
            else if (System.Text.RegularExpressions.Regex.IsMatch(lines[i].Trim(),
                     @"^[A-ZÁÉÍÓÚÑ][A-ZÁÉÍÓÚÑ0-9\s\.\""'\-\(\)/]+$") && lines[i].Trim().Length > 10)
            {
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.FromArgb(0x00, 0x65, 0x91);  // Engineering Blue
            }
        }

        ws.Column(1).Width = 110;
        ws.SheetView.FreezeRows(2);
    }

    private static void AddEsquema(XLWorkbook wb, byte[] pngBytes)
    {
        var ws = wb.Worksheets.Add("Esquema");
        ws.Cell("A1").Value = "Esquema 2D del sistema (snapshot del Canvas)";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 13;

        using var ms = new MemoryStream(pngBytes);
        var pic = ws.AddPicture(ms, "esquema").MoveTo(ws.Cell("A3"));
        // Limitar tamaño visible (manteniendo aspecto)
        const int maxW = 1000;
        if (pic.Width > maxW)
        {
            double scale = (double)maxW / pic.Width;
            pic.Scale(scale);
        }
        ws.Column(1).Width = 80;
    }

    private static void AddCombinaciones(XLWorkbook wb, string? dzpPath, string? cezPath)
    {
        if (!File.Exists(dzpPath ?? "") && !File.Exists(cezPath ?? "")) return;
        var ws = wb.Worksheets.Add("Combinaciones");
        ws.Cell("A1").Value = "FACTORES DE COMBINACIÓN DE CARGAS";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Range("A1:F1").Merge();

        int row = 3;
        if (File.Exists(dzpPath))
        {
            ws.Cell(row, 1).Value = $"FUENTE: {Path.GetFileName(dzpPath)} (programa ZAPATA)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 6).Merge();
            row += 2;
            row = DumpCombinacionesText(ws, dzpPath!, row);
            row += 2;
        }
        if (File.Exists(cezPath))
        {
            ws.Cell(row, 1).Value = $"FUENTE: {Path.GetFileName(cezPath)} (programa ESFUERZOS)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Range(row, 1, row, 6).Merge();
            row += 2;
            row = DumpCombinacionesText(ws, cezPath!, row);
        }
        ws.Column(1).Width = 110;
        ws.Style.Font.FontName = "Consolas";
        ws.Style.Font.FontSize = 10;
    }

    /// <summary>
    /// Vuelca el contenido de un archivo de combinaciones línea-por-línea en la columna A,
    /// preservando el formato. Las líneas que empiezan con '$' (comentarios) van en gris.
    /// </summary>
    private static int DumpCombinacionesText(IXLWorksheet ws, string path, int startRow)
    {
        var lines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            ws.Cell(startRow + i, 1).Value = line;
            if (line.TrimStart().StartsWith("$"))
                ws.Cell(startRow + i, 1).Style.Font.FontColor = XLColor.FromArgb(0x88, 0x88, 0x88);
        }
        return startRow + lines.Length;
    }

    private static void HeaderCell(IXLCell cell, string text)
    {
        cell.Value = text;
        cell.Style.Fill.BackgroundColor = HeaderFill;
        cell.Style.Font.FontColor = HeaderFont;
        cell.Style.Font.Bold = true;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }
}
