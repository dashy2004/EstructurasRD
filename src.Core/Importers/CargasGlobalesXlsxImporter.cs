using System;
using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using LosasPlus.Models;

namespace LosasPlus.Importers;

/// <summary>
/// Importa una <see cref="CargasGlobales"/> desde un libro Excel con la
/// estructura de <c>ARCHIVO_ESTRUCTURAL_2025.xlsx</c> del ingeniero
/// (hoja <c>Cargas</c>).
///
/// <para>
/// Posiciones de celdas (1-based, fila/columna):
/// </para>
/// <list type="bullet">
///   <item><b>Entrepiso pesos propios</b>: filas 26..28, col 3=nombre, 4=espesor, 5=densidad.</item>
///   <item><b>Techo pesos propios</b>: filas 66..68, mismas columnas.</item>
///   <item><b>Carga muerta por espesor</b>: filas 9..23, col 4=h (m), col 5=densidad (t/m³).</item>
/// </list>
///
/// <para>
/// El importer es <b>resiliente</b>: si el archivo no contiene la hoja
/// <c>Cargas</c>, lanza con un mensaje claro. Si las celdas individuales
/// están vacías o son inválidas, deja los valores por defecto (densidad 2.4)
/// para esa fila, en vez de fallar la importación entera.
/// </para>
///
/// <para>
/// <b>NO se importa</b> de la hoja: cargas vivas (Entrepiso/Balcones/Techo)
/// ni factores de combinación. Su semántica en la hoja del ingeniero está
/// menos estandarizada (mezcla de etiquetas y valores en celdas variables).
/// El usuario debe revisarlos en la UI tras importar.
/// </para>
/// </summary>
public sealed class CargasGlobalesXlsxImporter
{
    /// <summary>
    /// Lee <paramref name="xlsxPath"/> y devuelve un nuevo
    /// <see cref="CargasGlobales"/>. No modifica el archivo de origen.
    /// </summary>
    public CargasGlobales Importar(string xlsxPath)
    {
        if (xlsxPath is null) throw new ArgumentNullException(nameof(xlsxPath));
        if (!File.Exists(xlsxPath))
            throw new FileNotFoundException($"Archivo .xlsx no encontrado: {xlsxPath}", xlsxPath);

        using var wb = new XLWorkbook(xlsxPath);
        if (!wb.TryGetWorksheet("Cargas", out var sheet))
            throw new InvalidOperationException(
                "El libro no contiene la hoja 'Cargas' esperada por el importer.");

        var c = new CargasGlobales();

        // Pesos propios entrepiso (3 items: Mosaicos, Mortero, Pañete por convencion).
        LeerPesosPropios(sheet, c.PesosPropiosEntrepiso, filaInicio: 26, filaFin: 28);

        // Pesos propios techo (Fino, Impermeabilizante, Pañete por convencion).
        LeerPesosPropios(sheet, c.PesosPropiosTecho, filaInicio: 66, filaFin: 68);

        // Tabla h ↔ densidad por espesor (15 filas h=0.06..0.20).
        c.CargaMuertaPorEspesor.Filas.Clear();
        for (int r = 9; r <= 23; r++)
        {
            var h = LeerDouble(sheet.Cell(r, 4));
            var d = LeerDouble(sheet.Cell(r, 5));
            if (h <= 0) continue;  // fila inválida — saltar

            c.CargaMuertaPorEspesor.Filas.Add(new FilaCargaMuerta
            {
                H = h,
                Densidad = d > 0 ? d : 2.4,
            });
        }

        return c;
    }

    /// <summary>
    /// Lee una sub-tabla de pesos propios (3 filas típicamente) y la agrega a
    /// <paramref name="destino"/>. Reemplaza completamente cualquier item
    /// previo en la colección.
    /// </summary>
    private static void LeerPesosPropios(
        IXLWorksheet sheet, PesosPropiosUso destino, int filaInicio, int filaFin)
    {
        destino.Items.Clear();
        for (int r = filaInicio; r <= filaFin; r++)
        {
            var nombre = (sheet.Cell(r, 3).GetString() ?? "").Trim();
            var espesor = LeerDouble(sheet.Cell(r, 4));
            var densidad = LeerDouble(sheet.Cell(r, 5));

            // Si el espesor es 0 o el nombre vacío, asumimos que la fila no
            // existe en este xls — saltamos en vez de meter un item ruido.
            if (string.IsNullOrWhiteSpace(nombre) || espesor <= 0) continue;

            destino.Items.Add(new PesoPropio
            {
                Nombre   = nombre,
                Espesor  = espesor,
                Densidad = densidad > 0 ? densidad : 2.4,
            });
        }
    }

    /// <summary>
    /// Lee una celda como <see cref="double"/>. Devuelve 0 si la celda está
    /// vacía o contiene texto no numérico (resiliente a celdas con notas).
    /// </summary>
    private static double LeerDouble(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        try
        {
            // ClosedXML normaliza al locale del workbook; forzar Invariant para parsing.
            if (cell.TryGetValue<double>(out var v)) return v;
            var s = cell.GetString();
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }
        catch
        {
            return 0;
        }
    }
}
