using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Parser determinístico de la salida .TXT del programa Losas.exe.
///
/// Estructura del .TXT (Ver. 5.20, validada con fixtures de Losas v5.00):
/// <list type="number">
///   <item>Encabezado (sistema, archivo, fecha, programa, versión, archivo de datos).</item>
///   <item>"CÁLCULO DE MOMENTOS" → tabla por losa: ID TIPO CARGA H Lx Ly Mfx Mfy -Msx -Msy.</item>
///   <item>"CALCULO DE ARMADURAS" → cabecera con reglamento, f'c, fy.</item>
///   <item>"ARMADURAS SEGÚN \"X\"" → tabla: LOSA d Mu As Disponer As(provisto).</item>
///   <item>"ARMADURAS SEGÚN \"Y\"" → idem.</item>
///   <item>"ARMADURAS SEGÚN \"X\" SOBRE LOS APOYOS" → tabla: I-J MuI MuJ MuI-J d As AsI AsJ DAs Disponer.</item>
///   <item>"ARMADURAS SEGÚN \"Y\" SOBRE LOS APOYOS" → idem.</item>
/// </list>
/// El parser identifica cada sección por su encabezado (regex tolerante a acentos
/// y mayúsculas) y aplica una expresión regular específica a cada fila de datos.
/// Los plugins pueden reemplazarlo vía hook 'parse-txt' (no hay reemplazo dinámico
/// del singleton, pero un plugin puede llamar a <see cref="ParseRaw"/> y devolver
/// un <see cref="ParsedOutput"/> propio).
/// </summary>
public static class TxtParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- Section markers ----------
    private static readonly Regex RxSecMomentos = new(@"C[AÁ]LCULO\s+DE\s+MOMENTOS", RegexOptions.IgnoreCase);
    private static readonly Regex RxSecArmaduras = new(@"C[AÁ]LCULO\s+DE\s+ARMADURAS", RegexOptions.IgnoreCase);
    private static readonly Regex RxSecArmX = new(@"^\s*ARMADURAS\s+SEG[UÚ]N\s+""?X""?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex RxSecArmY = new(@"^\s*ARMADURAS\s+SEG[UÚ]N\s+""?Y""?\s*$", RegexOptions.IgnoreCase);
    private static readonly Regex RxSecApoyoX = new(@"ARMADURAS\s+SEG[UÚ]N\s+""?X""?\s+SOBRE\s+LOS\s+APOYOS", RegexOptions.IgnoreCase);
    private static readonly Regex RxSecApoyoY = new(@"ARMADURAS\s+SEG[UÚ]N\s+""?Y""?\s+SOBRE\s+LOS\s+APOYOS", RegexOptions.IgnoreCase);

    // ---------- Header metadata ----------
    private static readonly Regex RxHdrSistema = new(@"SISTEMA\s+DE\s+LOSAS\s*:\s*(.+)", RegexOptions.IgnoreCase);
    private static readonly Regex RxHdrArchivo = new(@"Archivo\s+de\s+datos\s*:\s*(.+)", RegexOptions.IgnoreCase);
    private static readonly Regex RxHdrVersion = new(@"Ver\.\s*([\d.]+)", RegexOptions.IgnoreCase);
    private static readonly Regex RxHdrFecha = new(@"\b([A-Z][a-z]{2}\s+\d{1,2},\s+\d{4})\b");
    private static readonly Regex RxHdrReglamento = new(@"reglamento\s+([A-Z0-9\-\s]+)", RegexOptions.IgnoreCase);
    private static readonly Regex RxHdrFc = new(@"f'c\s*:\s*(.+)", RegexOptions.IgnoreCase);
    private static readonly Regex RxHdrFy = new(@"\bfy\s*:\s*(.+)", RegexOptions.IgnoreCase);

    // ---------- Row patterns ----------
    // Momentos: ID TIPO CARGA H Lx Ly Mfx Mfy -Msx -Msy  (10 columnas)
    private static readonly Regex RxRowMomento = new(
        @"^\s*(\d+)\s+(\d+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)" +
        @"\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*$");

    // Armaduras (vano, X o Y): LOSA d Mu As Disponer As(prov)
    // Disponer es texto libre (puede contener Ø, comillas, espacios, @, números).
    private static readonly Regex RxRowArmadura = new(
        @"^\s*(\d+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+(.+?)\s+([\d.]+)\s*$");

    // Apoyos: II-JJ  MuI  MuJ  MuI-J  d  As  AsI  AsJ  DAs  Disponer
    private static readonly Regex RxRowApoyo = new(
        @"^\s*(\d+)\s*-\s*(\d+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)" +
        @"\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+(.+?)\s*$");

    // ---------- Public API ----------

    /// <summary>Parsea texto en memoria y retorna un <see cref="ParsedOutput"/>.</summary>
    public static ParsedOutput Parse(string txt, IEnumerable<Losa>? known = null)
    {
        var output = ParseRaw(txt);

        // Si nos pasan losas conocidas, sembramos el diccionario para preservar IDs aún sin datos.
        if (known != null)
        {
            var byId = output.PorLosa.ToDictionary(l => l.Id);
            foreach (var l in known)
                if (!byId.ContainsKey(l.Id))
                    output.PorLosa.Add(new LosaResult { Id = l.Id });
            output.PorLosa = output.PorLosa.OrderBy(l => l.Id).ToList();
        }
        return output;
    }

    /// <summary>Parsea texto desde un archivo, asumiendo encoding cp1252 (default del motor).</summary>
    public static ParsedOutput ParseFile(string path)
    {
        var text = File.ReadAllText(path, Encoding.GetEncoding(1252));
        return ParseRaw(text);
    }

    /// <summary>Inyecta los resultados parseados en el modelo (Mfx, Mfy, MSx, MSy y disposiciones).</summary>
    public static void Apply(ParsedOutput parsed, IEnumerable<Losa> losas)
    {
        var byId = parsed.PorLosa.ToDictionary(r => r.Id);
        foreach (var l in losas)
        {
            if (!byId.TryGetValue(l.Id, out var r)) continue;
            l.Mfx = r.Mfx;
            l.Mfy = r.Mfy;
            l.MSx = r.MSx;
            l.MSy = r.MSy;
            if (!string.IsNullOrEmpty(r.DisponerX)) l.AsxVano = r.DisponerX;
            if (!string.IsNullOrEmpty(r.DisponerY)) l.AsyVano = r.DisponerY;
        }
    }

    /// <summary>
    /// Inyecta los resultados de apoyos sobre soportes en los <see cref="BordeAdic"/>
    /// del sistema (BordesX/BordesY). El match es por (BI, BJ) sin importar el orden.
    /// </summary>
    public static void ApplyApoyos(ParsedOutput parsed, IEnumerable<BordeAdic> bordesX, IEnumerable<BordeAdic> bordesY)
    {
        ApplyOneDirection(parsed.Apoyos.Where(a => a.Direccion == "X"), bordesX);
        ApplyOneDirection(parsed.Apoyos.Where(a => a.Direccion == "Y"), bordesY);
    }

    private static void ApplyOneDirection(IEnumerable<ApoyoResult> apoyos, IEnumerable<BordeAdic> bordes)
    {
        var bordesList = bordes.ToList();
        foreach (var a in apoyos)
        {
            // Coincidencia por par no-ordenado (B-I,B-J) ≡ (B-J,B-I)
            var b = bordesList.FirstOrDefault(x =>
                (x.BI == a.BordeI && x.BJ == a.BordeJ) ||
                (x.BI == a.BordeJ && x.BJ == a.BordeI));
            if (b is null) continue;
            b.MuI = a.MuI;
            b.MuJ = a.MuJ;
            b.MuIJ = a.MuIJ;
            b.D = a.D;
            b.AsReq = a.As;
            b.Disponer = a.Disponer;
        }
    }

    // ---------- Core ----------

    public static ParsedOutput ParseRaw(string txt)
    {
        var output = new ParsedOutput { Raw = txt ?? "" };
        if (string.IsNullOrWhiteSpace(txt)) return output;

        var lines = (txt ?? "").Replace("\r\n", "\n").Split('\n');

        // Two passes: 1) extract metadata + locate sections; 2) parse each section.
        var sections = LocateSections(lines);
        ExtractMetadata(lines, output);

        // Momentos
        if (sections.TryGetValue(SectionKind.Momentos, out var rngM))
        {
            for (int i = rngM.start; i < rngM.end; i++)
            {
                var m = RxRowMomento.Match(lines[i]);
                if (!m.Success) continue;
                var lr = GetOrAdd(output, ParseInt(m.Groups[1]));
                lr.Tipo = ParseInt(m.Groups[2]);
                lr.Carga = ParseDouble(m.Groups[3]);
                lr.H = ParseDouble(m.Groups[4]);
                lr.Lx = ParseDouble(m.Groups[5]);
                lr.Ly = ParseDouble(m.Groups[6]);
                lr.Mfx = ParseDouble(m.Groups[7]);
                lr.Mfy = ParseDouble(m.Groups[8]);
                lr.MSx = ParseDouble(m.Groups[9]);
                lr.MSy = ParseDouble(m.Groups[10]);
            }
        }

        // Armaduras X (vano)
        if (sections.TryGetValue(SectionKind.ArmaduraX, out var rngAx))
            ParseArmadurasInto(lines, rngAx, output, isX: true);

        // Armaduras Y (vano)
        if (sections.TryGetValue(SectionKind.ArmaduraY, out var rngAy))
            ParseArmadurasInto(lines, rngAy, output, isX: false);

        // Apoyos X
        if (sections.TryGetValue(SectionKind.ApoyoX, out var rngPx))
            ParseApoyosInto(lines, rngPx, output, "X");

        // Apoyos Y
        if (sections.TryGetValue(SectionKind.ApoyoY, out var rngPy))
            ParseApoyosInto(lines, rngPy, output, "Y");

        output.PorLosa = output.PorLosa.OrderBy(l => l.Id).ToList();
        output.Apoyos = output.Apoyos
            .OrderBy(a => a.Direccion).ThenBy(a => a.BordeI).ThenBy(a => a.BordeJ).ToList();
        return output;
    }

    private static void ParseArmadurasInto(string[] lines, (int start, int end) rng, ParsedOutput output, bool isX)
    {
        for (int i = rng.start; i < rng.end; i++)
        {
            var m = RxRowArmadura.Match(lines[i]);
            if (!m.Success) continue;
            var lr = GetOrAdd(output, ParseInt(m.Groups[1]));
            var d = ParseDouble(m.Groups[2]);
            var mu = ParseDouble(m.Groups[3]);
            var asReq = ParseDouble(m.Groups[4]);
            var disp = m.Groups[5].Value.Trim();
            var asProv = ParseDouble(m.Groups[6]);
            if (isX)
            {
                lr.Dx = d; lr.Mux = mu; lr.AsxReq = asReq; lr.DisponerX = disp; lr.AsxProv = asProv;
            }
            else
            {
                lr.Dy = d; lr.Muy = mu; lr.AsyReq = asReq; lr.DisponerY = disp; lr.AsyProv = asProv;
            }
        }
    }

    private static void ParseApoyosInto(string[] lines, (int start, int end) rng, ParsedOutput output, string direccion)
    {
        for (int i = rng.start; i < rng.end; i++)
        {
            var m = RxRowApoyo.Match(lines[i]);
            if (!m.Success) continue;
            output.Apoyos.Add(new ApoyoResult
            {
                Direccion = direccion,
                BordeI = ParseInt(m.Groups[1]),
                BordeJ = ParseInt(m.Groups[2]),
                MuI = ParseDouble(m.Groups[3]),
                MuJ = ParseDouble(m.Groups[4]),
                MuIJ = ParseDouble(m.Groups[5]),
                D = ParseDouble(m.Groups[6]),
                As = ParseDouble(m.Groups[7]),
                AsI = ParseDouble(m.Groups[8]),
                AsJ = ParseDouble(m.Groups[9]),
                DAs = ParseDouble(m.Groups[10]),
                Disponer = m.Groups[11].Value.Trim(),
            });
        }
    }

    private enum SectionKind { Momentos, ArmaduraX, ArmaduraY, ApoyoX, ApoyoY }

    /// <summary>Devuelve el rango [start, end) de líneas para cada sección detectada.</summary>
    private static Dictionary<SectionKind, (int start, int end)> LocateSections(string[] lines)
    {
        // Apoyo* contiene "X SOBRE LOS APOYOS" / "Y SOBRE LOS APOYOS" — chequear primero
        // que ARMADURAS SEGÚN "X" para no clasificar el apoyo como sección de vano.
        var marks = new List<(int line, SectionKind kind)>();
        for (int i = 0; i < lines.Length; i++)
        {
            var s = lines[i].Trim();
            if (RxSecApoyoX.IsMatch(s)) marks.Add((i, SectionKind.ApoyoX));
            else if (RxSecApoyoY.IsMatch(s)) marks.Add((i, SectionKind.ApoyoY));
            else if (RxSecArmX.IsMatch(s)) marks.Add((i, SectionKind.ArmaduraX));
            else if (RxSecArmY.IsMatch(s)) marks.Add((i, SectionKind.ArmaduraY));
            else if (RxSecMomentos.IsMatch(s)) marks.Add((i, SectionKind.Momentos));
        }

        var result = new Dictionary<SectionKind, (int start, int end)>();
        for (int i = 0; i < marks.Count; i++)
        {
            int start = marks[i].line + 1;
            int end = (i + 1 < marks.Count) ? marks[i + 1].line : lines.Length;
            // No sobreescribir si la sección ya fue detectada (se queda con la primera ocurrencia).
            if (!result.ContainsKey(marks[i].kind))
                result[marks[i].kind] = (start, end);
        }
        return result;
    }

    private static void ExtractMetadata(string[] lines, ParsedOutput output)
    {
        foreach (var raw in lines)
        {
            var s = raw.Trim();
            if (s.Length == 0) continue;
            Match m;
            if (output.Sistema is null && (m = RxHdrSistema.Match(s)).Success) output.Sistema = m.Groups[1].Value.Trim();
            if (output.Archivo is null && (m = RxHdrArchivo.Match(s)).Success) output.Archivo = m.Groups[1].Value.Trim();
            if (output.Version is null && (m = RxHdrVersion.Match(s)).Success) output.Version = m.Groups[1].Value.Trim();
            if (output.Fecha is null && (m = RxHdrFecha.Match(s)).Success) output.Fecha = m.Groups[1].Value.Trim();
            if (output.Reglamento is null && (m = RxHdrReglamento.Match(s)).Success) output.Reglamento = m.Groups[1].Value.Trim();
            if (output.FcRaw is null && (m = RxHdrFc.Match(s)).Success) output.FcRaw = m.Groups[1].Value.Trim();
            if (output.FyRaw is null && (m = RxHdrFy.Match(s)).Success) output.FyRaw = m.Groups[1].Value.Trim();
        }
    }

    private static LosaResult GetOrAdd(ParsedOutput output, int id)
    {
        var existing = output.PorLosa.FirstOrDefault(l => l.Id == id);
        if (existing != null) return existing;
        var lr = new LosaResult { Id = id };
        output.PorLosa.Add(lr);
        return lr;
    }

    private static int ParseInt(Group g) => int.Parse(g.Value, NumberStyles.Integer, Inv);
    private static double ParseDouble(Group g) => double.Parse(g.Value, NumberStyles.Float, Inv);
}

public sealed class ParsedOutput
{
    public string Raw { get; set; } = "";

    // Encabezado
    public string? Sistema { get; set; }
    public string? Archivo { get; set; }
    public string? Fecha { get; set; }
    public string? Version { get; set; }
    public string? Reglamento { get; set; }
    public string? FcRaw { get; set; }
    public string? FyRaw { get; set; }

    public List<LosaResult> PorLosa { get; set; } = new();
    public List<ApoyoResult> Apoyos { get; set; } = new();
}

public sealed class LosaResult
{
    public int Id { get; set; }

    // Cálculo de momentos
    public int? Tipo { get; set; }
    public double? Carga { get; set; }
    public double? H { get; set; }
    public double? Lx { get; set; }
    public double? Ly { get; set; }
    public double? Mfx { get; set; }
    public double? Mfy { get; set; }
    public double? MSx { get; set; }
    public double? MSy { get; set; }

    // Armaduras según X (vano)
    public double? Dx { get; set; }
    public double? Mux { get; set; }
    public double? AsxReq { get; set; }
    public string? DisponerX { get; set; }
    public double? AsxProv { get; set; }

    // Armaduras según Y (vano)
    public double? Dy { get; set; }
    public double? Muy { get; set; }
    public double? AsyReq { get; set; }
    public string? DisponerY { get; set; }
    public double? AsyProv { get; set; }
}

public sealed class ApoyoResult
{
    public string Direccion { get; set; } = "X";  // "X" o "Y"
    public int BordeI { get; set; }
    public int BordeJ { get; set; }
    public double? MuI { get; set; }
    public double? MuJ { get; set; }
    public double? MuIJ { get; set; }
    public double? D { get; set; }
    public double? As { get; set; }
    public double? AsI { get; set; }
    public double? AsJ { get; set; }
    public double? DAs { get; set; }
    public string? Disponer { get; set; }
}
