using System.Text.RegularExpressions;

namespace LosasPlus.Services;

/// <summary>
/// Clasifica cada línea del .TXT (salida del motor Losas.exe) según su rol visual.
/// Lógica pura: sin dependencias de WPF para que pueda testearse con xUnit.
/// </summary>
public static class TxtLineClassifier
{
    public enum LineKind
    {
        Empty,
        Comment,        // .DL style: línea que empieza con '$'
        SectionHeader,  // todo en mayúsculas — títulos como "CÁLCULO DE MOMENTOS"
        Separator,      // `---` o `===` (líneas de guiones/iguales)
        ColumnHeader,   // tabla: línea con tokens como "LOSA TIPO Mfx Mfy"
        Units,          // línea de unidades: "ton/m²   cm     m      m"
        DataRow,        // tabla de datos numéricos
        Title,          // título principal "DISEÑO DE SISTEMAS DE LOSAS"
        Meta,           // "Archivo de datos:", "F. Perdomo", "Apr 29, 2026"
        Plain
    }

    private static readonly Regex RxComment = new(@"^\s*\$");
    private static readonly Regex RxSeparator = new(@"^\s*[-=_]{3,}\s*$");
    private static readonly Regex RxAllCapsHeader = new(
        @"^\s*[A-ZÁÉÍÓÚÑÜ][A-ZÁÉÍÓÚÑÜ0-9\s\.\""'\-\(\)/]+\s*$");
    private static readonly Regex RxStartsWithLeadingNumber = new(@"^\s+[\-]?\d");
    // Case-sensitive a propósito: las etiquetas de columna en el .TXT del motor usan
    // cases específicas (LOSA TIPO CARGA H en mayúsculas, Lx Ly Mfx Mfy en mixto).
    // Si fuera IgnoreCase, "AS " dentro de "ARMADURAS " activaría el match indeseado.
    private static readonly Regex RxColumnHeader = new(
        @"\b(LOSA|TIPO|CARGA|Lx|Ly|Mfx|Mfy|MSx|MSy|MuI|MuJ|MuI-J|BORDE|DISPONER|REC|AsI|AsJ|DAs)\b");
    // Línea de unidades: aparecen "ton/m²", "cm", "[-]", "ton.m/m", "ton/cm2", "kg/cm".
    private static readonly Regex RxUnitsLine = new(
        @"(ton/m²|ton/m2|cm²|cm/m|kg/cm²|kg/cm2|ton\.m/m|\[\s*-\s*\]|\[\s*ton)");
    private static readonly Regex RxMeta = new(
        @"\b(Archivo\s+de\s+datos|F\.\s*Perdomo|Ver\.|Hormigón|Acero|reglamento|f'c\s*:|\bfy\s*:|SISTEMA\s+DE\s+LOSAS\s*:)",
        RegexOptions.IgnoreCase);

    public static LineKind Classify(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return LineKind.Empty;

        if (RxComment.IsMatch(line)) return LineKind.Comment;
        if (RxSeparator.IsMatch(line)) return LineKind.Separator;

        var trimmed = line.Trim();

        // Filas de datos: empiezan con espacios + número entero (id de losa) o con par "II- JJ"
        if (RxStartsWithLeadingNumber.IsMatch(line) && CountDigits(trimmed) >= 2)
            return LineKind.DataRow;

        // Línea de unidades
        if (RxUnitsLine.IsMatch(line) && !RxColumnHeader.IsMatch(line)) return LineKind.Units;

        // Encabezado de columnas: contiene labels conocidos pero NO empieza con número
        if (RxColumnHeader.IsMatch(line) && !RxStartsWithLeadingNumber.IsMatch(line))
            return LineKind.ColumnHeader;

        // Título principal (línea con muchas mayúsculas grandes)
        if (RxAllCapsHeader.IsMatch(trimmed) && trimmed.Length > 10 && PercentUpper(trimmed) > 0.6)
            return LineKind.SectionHeader;

        // Metadata (líneas con info clave)
        if (RxMeta.IsMatch(line)) return LineKind.Meta;

        return LineKind.Plain;
    }

    private static int CountDigits(string s)
    {
        int n = 0;
        foreach (var c in s) if (char.IsDigit(c)) n++;
        return n;
    }

    private static double PercentUpper(string s)
    {
        int letters = 0, upper = 0;
        foreach (var c in s)
        {
            if (char.IsLetter(c))
            {
                letters++;
                if (char.IsUpper(c)) upper++;
            }
        }
        return letters == 0 ? 0 : (double)upper / letters;
    }
}
