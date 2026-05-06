using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Lectura/escritura del archivo .DL conforme al formato documentado en Losas.hlp:
/// <list type="bullet">
///   <item>Líneas de comentario inician con <c>$</c>.</item>
///   <item>El archivo puede contener UNO o VARIOS sistemas. Cada sistema empieza con
///         una línea de NOMBRE (texto libre, p.ej. "Sistema No 1") y luego el bloque
///         numérico: NLOSA FC FY ADIC → NLOSA filas (ID TIPO CARGA H Lx Ly REC) →
///         NX → NX filas (B-I B-J BAL) → NY → NY filas.</item>
/// </list>
/// El writer produce el archivo con anchos de columna idénticos al modelo de F. Perdomo
/// para máxima compatibilidad con el motor original (encoding cp1252, line endings CRLF).
/// </summary>
public static class DLFileService
{
    private static readonly NumberFormatInfo Inv = CultureInfo.InvariantCulture.NumberFormat;

    // ---------- Read ----------

    /// <summary>Lee todos los sistemas del archivo .DL (uno o varios).</summary>
    public static List<Sistema> ReadAll(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("No se encontró el archivo .DL", path);

        var rawLines = File.ReadAllLines(path, Encoding.GetEncoding(1252));
        var liveLines = rawLines
            .Select(StripInlineComment)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (liveLines.Count == 0)
            throw new FormatException("Archivo .DL vacío o sólo con comentarios.");

        var sistemas = new List<Sistema>();
        int lineIdx = 0;
        while (lineIdx < liveLines.Count)
        {
            var (sistema, consumed) = ReadOneSistema(liveLines, lineIdx);
            if (sistema is null) break;
            sistemas.Add(sistema);
            lineIdx += consumed;
        }
        return sistemas;
    }

    /// <summary>Lee el primer sistema del .DL (compatibilidad hacia atrás con archivos de un solo sistema).</summary>
    public static Sistema Read(string path)
    {
        var all = ReadAll(path);
        if (all.Count == 0) throw new FormatException("El archivo .DL no contiene ningún sistema.");
        return all[0];
    }

    /// <summary>Lee un único sistema desde una posición de líneas dada y devuelve cuántas líneas consumió.</summary>
    private static (Sistema? sistema, int consumed) ReadOneSistema(List<string> lines, int startIdx)
    {
        if (startIdx >= lines.Count) return (null, 0);

        var sistema = new Sistema { Nombre = lines[startIdx].Trim() };

        int lineIdx = startIdx + 1;
        var queue = new Queue<string>();

        string Next(string label)
        {
            while (queue.Count == 0)
            {
                if (lineIdx >= lines.Count)
                    throw new FormatException($".DL incompleto, falta: {label}");
                foreach (var t in lines[lineIdx].Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                    queue.Enqueue(t);
                lineIdx++;
            }
            return queue.Dequeue();
        }

        // NLOSA FC FY ADIC
        int nlosa = ParseInt(Next("NLOSA"));
        sistema.Fc = ParseDouble(Next("FC"));
        sistema.Fy = ParseDouble(Next("FY"));
        sistema.Adicionales = ParseInt(Next("ADICIONALES"));

        // Losas
        for (int i = 0; i < nlosa; i++)
        {
            sistema.Losas.Add(new Losa
            {
                Id = ParseInt(Next($"ID losa #{i + 1}")),
                Tipo = ParseInt(Next($"TIPO losa #{i + 1}")),
                Carga = ParseDouble(Next($"CARGA losa #{i + 1}")),
                Espesor = ParseDouble(Next($"ESPESOR losa #{i + 1}")),
                Lx = ParseDouble(Next($"LX losa #{i + 1}")),
                Ly = ParseDouble(Next($"LY losa #{i + 1}")),
                Rec = ParseDouble(Next($"REC losa #{i + 1}"))
            });
        }

        int nx = ParseInt(Next("Cantidad de bordes adic. según X"));
        for (int i = 0; i < nx; i++)
        {
            sistema.BordesX.Add(new BordeAdic
            {
                BI = ParseInt(Next($"BI X #{i + 1}")),
                BJ = ParseInt(Next($"BJ X #{i + 1}")),
                Balanceo = Next($"BAL X #{i + 1}")
            });
        }

        int ny = ParseInt(Next("Cantidad de bordes adic. según Y"));
        for (int i = 0; i < ny; i++)
        {
            sistema.BordesY.Add(new BordeAdic
            {
                BI = ParseInt(Next($"BI Y #{i + 1}")),
                BJ = ParseInt(Next($"BJ Y #{i + 1}")),
                Balanceo = Next($"BAL Y #{i + 1}")
            });
        }

        // Cuántas líneas consumimos: lineIdx se movió hasta la primera línea NO consumida
        // o EOF. Calculamos en términos de offset desde startIdx.
        // Si quedaron tokens sin consumir en queue, significa que la última línea tenía
        // datos extra — los descartamos para que el siguiente sistema empiece limpio.
        return (sistema, lineIdx - startIdx);
    }

    // ---------- Write ----------

    /// <summary>
    /// Genera el contenido del .DL para varios sistemas. Cada sistema reproduce el
    /// formato byte-compatible verificado contra el modelo del motor original.
    /// </summary>
    public static string WriteAll(IEnumerable<Sistema> sistemas)
    {
        var sb = new StringBuilder();
        AppendCRLF(sb, "$  ARCHIVO DE DATOS DEL PROGRAMA LOSA");

        bool first = true;
        foreach (var s in sistemas)
        {
            if (!first)
            {
                AppendCRLF(sb, "$");
                AppendCRLF(sb, "$  --- siguiente sistema ---");
            }
            AppendOneSistema(sb, s);
            first = false;
        }

        return sb.ToString();
    }

    public static string Write(Sistema s) => WriteAll(new[] { s });

    private static void AppendOneSistema(StringBuilder sb, Sistema s)
    {
        AppendCRLF(sb, "$");
        AppendCRLF(sb, s.Nombre);
        AppendCRLF(sb, "$");

        AppendCRLF(sb, "$   NLOSA       FC        FY     ADICIONALES");
        AppendCRLF(sb, "$    [-]    [ton/cm2]  [ton/cm2]     [-]");
        AppendCRLF(sb, string.Format(Inv,
            "{0,7}{1,12:0.000}{2,11:0.000}{3,9}",
            s.Losas.Count, s.Fc, s.Fy, s.Adicionales));
        AppendCRLF(sb, "$");

        AppendCRLF(sb, "$    ID    TIPO    CARGA   ESPESOR     LX       LY       REC");
        AppendCRLF(sb, "$    [-]    [-]  [ton/m2]    [m]       [m]      [m]      [m]");
        foreach (var l in s.Losas)
        {
            AppendCRLF(sb, string.Format(Inv,
                "{0,7}{1,7}{2,10:0.000}{3,9:0.000}{4,10:0.000}{5,9:0.000}{6,9:0.000}",
                l.Id, l.Tipo, l.Carga, l.Espesor, l.Lx, l.Ly, l.Rec));
        }
        AppendCRLF(sb, "$");

        AppendCRLF(sb, "$   ADIC. SEGUN X");
        AppendCRLF(sb, string.Format(Inv, "{0,10}", s.BordesX.Count));
        if (s.BordesX.Count > 0)
        {
            AppendCRLF(sb, "$");
            AppendCRLF(sb, "$      B-I        B-J      BALANCEO");
            foreach (var b in s.BordesX)
                AppendCRLF(sb, string.Format(Inv, "{0,10}{1,10}{2,10}",
                    b.BI, b.BJ, NormalizeBal(b.Balanceo)));
        }
        AppendCRLF(sb, "$");

        AppendCRLF(sb, "$   ADIC. SEGUN Y");
        AppendCRLF(sb, string.Format(Inv, "{0,10}", s.BordesY.Count));
        if (s.BordesY.Count > 0)
        {
            AppendCRLF(sb, "$");
            AppendCRLF(sb, "$      B-I        B-J      BALANCEO");
            foreach (var b in s.BordesY)
                AppendCRLF(sb, string.Format(Inv, "{0,10}{1,10}{2,10}",
                    b.BI, b.BJ, NormalizeBal(b.Balanceo)));
        }
    }

    public static void Save(Sistema s, string path)
        => File.WriteAllText(path, Write(s), Encoding.GetEncoding(1252));

    public static void SaveAll(IEnumerable<Sistema> sistemas, string path)
        => File.WriteAllText(path, WriteAll(sistemas), Encoding.GetEncoding(1252));

    // ---------- Helpers ----------

    private static void AppendCRLF(StringBuilder sb, string line)
    {
        sb.Append(line);
        sb.Append('\r');
        sb.Append('\n');
    }

    private static string StripInlineComment(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith("$")) return "";
        int hash = raw.IndexOf('$');
        return hash >= 0 ? raw[..hash] : raw;
    }

    private static string NormalizeBal(string? b)
    {
        var v = (b ?? "S").Trim().ToUpperInvariant();
        return v == "N" ? "N" : "S";
    }

    private static int ParseInt(string s) => int.Parse(s, NumberStyles.Integer, Inv);
    private static double ParseDouble(string s) => double.Parse(s, NumberStyles.Float, Inv);
}
