// Plugin: genera un .xlsx personalizado paralelo al export oficial.
// Hook: 'custom-export' — disparado por LosasPlus tras el export estándar a XLSX o CSV.
// Usa ClosedXML (reference inyectada por el host).

using ClosedXML.Excel;
using System.IO;

if (Hook == "custom-export" && Sistema != null)
{
    // Sólo genero la versión "minimal" si el host me pasó un OutputXlsxPath
    // (es decir, el usuario eligió Exportar XLSX, no CSV).
    if (string.IsNullOrEmpty(OutputXlsxPath)) return null;

    var dir = Path.GetDirectoryName(OutputXlsxPath);
    var name = Path.GetFileNameWithoutExtension(OutputXlsxPath);
    var sidecar = Path.Combine(dir ?? "", $"{name}_minimal.xlsx");

    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Resumen mínimo");

    ws.Cell("A1").Value = $"Sistema: {Sistema.Nombre}";
    ws.Cell("A1").Style.Font.Bold = true;
    ws.Cell("A1").Style.Font.FontSize = 14;
    ws.Range("A1:F1").Merge();

    string[] headers = { "ID", "TIPO", "Lx", "Ly", "Mfx", "Mfy" };
    for (int i = 0; i < headers.Length; i++)
    {
        var c = ws.Cell(3, i + 1);
        c.Value = headers[i];
        c.Style.Font.Bold = true;
        c.Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    int row = 4;
    foreach (var l in Sistema.Losas)
    {
        ws.Cell(row, 1).Value = l.Id;
        ws.Cell(row, 2).Value = l.Tipo;
        ws.Cell(row, 3).Value = l.Lx;
        ws.Cell(row, 4).Value = l.Ly;
        if (l.Mfx.HasValue) ws.Cell(row, 5).Value = l.Mfx.Value;
        if (l.Mfy.HasValue) ws.Cell(row, 6).Value = l.Mfy.Value;
        row++;
    }
    ws.Columns().AdjustToContents();
    wb.SaveAs(sidecar);

    Log($"excel-export.csx: generó {Path.GetFileName(sidecar)} junto al .xlsx oficial");
}

return null;
