using ClosedXML.Excel;
using LosasPlus.Models;

namespace LosasPlus.Transmision;

/// <summary>
/// Exporta el reporte de bajada de cargas de un <see cref="Edificio"/> a un
/// libro Excel (.xlsx): una hoja «Bajada de cargas» con la tabla de carga
/// acumulada por nivel (<see cref="BajadaCargas"/>) y un resumen de la carga en
/// base y el predimensionado de zapata (<see cref="PredimZapata"/>) para la
/// presión admisible dada.
///
/// <para>Tipo puro de dominio (ClosedXML) — multiplataforma y testeable.</para>
/// </summary>
public static class BajadaCargasExporter
{
    private static readonly XLColor HeaderFill = XLColor.FromArgb(0x1F, 0x29, 0x37);
    private static readonly XLColor HeaderFont = XLColor.White;

    /// <summary>Nombre de la hoja del reporte.</summary>
    public const string NombreHoja = "Bajada de cargas";

    /// <summary>
    /// Exporta la bajada de cargas del <paramref name="edificio"/> a
    /// <paramref name="xlsxPath"/>, con el predim de zapata para
    /// <paramref name="presionAdmisible"/> (t/m²).
    /// </summary>
    public static void Export(Edificio edificio, double presionAdmisible, string xlsxPath)
    {
        var bajada = BajadaCargas.Acumular(edificio);
        var zapata = PredimZapata.Cuadrada(bajada.CargaEnBase, presionAdmisible);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(NombreHoja);

        // Cabecera de la tabla.
        string[] cabeceras = { "Nivel", "Cota [m]", "Carga propia [t]", "Carga acumulada [t]" };
        for (int c = 0; c < cabeceras.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = cabeceras[c];
            cell.Style.Fill.BackgroundColor = HeaderFill;
            cell.Style.Font.FontColor = HeaderFont;
            cell.Style.Font.Bold = true;
        }

        int r = 2;
        foreach (var n in bajada.Niveles)
        {
            ws.Cell(r, 1).Value = n.Nombre;
            ws.Cell(r, 2).Value = n.Cota;
            ws.Cell(r, 3).Value = n.CargaPropia;
            ws.Cell(r, 4).Value = n.CargaAcumulada;
            r++;
        }

        // Resumen (deja una fila en blanco).
        r++;
        var resumen = new (string Etiqueta, double Valor)[]
        {
            ("Carga en base [t]", bajada.CargaEnBase),
            ("Presión admisible [t/m²]", presionAdmisible),
            ("Zapata — área [m²]", zapata.AreaRequerida),
            ("Zapata — lado cuadrada [m]", zapata.LadoCuadrada),
        };
        foreach (var (etiqueta, valor) in resumen)
        {
            ws.Cell(r, 1).Value = etiqueta;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = valor;
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(xlsxPath);
    }
}
