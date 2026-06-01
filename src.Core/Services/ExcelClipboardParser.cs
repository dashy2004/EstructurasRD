using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Parsea texto pegado desde Excel/Calc/etc. en formato TSV, CSV o separado por ';'.
/// Detecta columnas <c>Lx</c>, <c>Ly</c>, <c>Espesor</c>/<c>H</c>, <c>Carga</c>/<c>Wu</c>,
/// opcionalmente <c>Tipo</c> y <c>Rec</c>. Si la primera fila no parece header
/// (contiene sólo números), asume el orden [Lx, Ly, Espesor, Carga, (Tipo), (Rec)].
/// Acepta coma decimal española y la convierte a punto.
/// </summary>
public static class ExcelClipboardParser
{
    public sealed class ParseResult
    {
        public List<Losa> Losas { get; } = new();
        public List<string> Warnings { get; } = new();
        public int LineasIgnoradas { get; set; }
    }

    public sealed class Defaults
    {
        public int Tipo { get; set; } = 10;
        public double EspesorM { get; set; } = 0.12;
        public double RecM { get; set; } = 0.02;
        public double CargaTonM2 { get; set; } = 0.40;
        public int IdInicial { get; set; } = 1;
        /// <summary>Si true, asume que H y Rec vienen en CENTÍMETROS (típico en Excel) y los convierte a metros.</summary>
        public bool EspesorEnCm { get; set; } = false;
    }

    private static readonly char[] _separators = { '\t', ';', ',' };
    private static readonly NumberFormatInfo _inv = CultureInfo.InvariantCulture.NumberFormat;

    public static ParseResult Parse(string text, Defaults? def = null)
    {
        def ??= new Defaults();
        var result = new ParseResult();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var rawLines = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();
        // Quitar líneas vacías al final
        while (rawLines.Count > 0 && string.IsNullOrWhiteSpace(rawLines[^1])) rawLines.RemoveAt(rawLines.Count - 1);
        if (rawLines.Count == 0) return result;

        // Detectar separador automáticamente: el que aparezca más en la primera línea con datos
        char sep = DetectSeparator(rawLines[0]);
        var lines = rawLines.Select(l => Split(l, sep).Select(s => s.Trim()).ToArray()).ToList();

        // Detectar header: si la primera fila tiene tokens que NO parsean como número, probablemente es header
        bool hasHeader = lines[0].Any(tok => !TryParseDouble(tok, out _));

        var colMap = new Dictionary<string, int>();
        if (hasHeader)
        {
            for (int i = 0; i < lines[0].Length; i++)
            {
                var key = Normalize(lines[0][i]);
                if      (key is "lx" or "lcorta" or "lc")                colMap["lx"] = i;
                else if (key is "ly" or "llarga" or "ll")                colMap["ly"] = i;
                else if (key is "h" or "espesor" or "esp" or "t" or "e") colMap["h"] = i;
                else if (key.StartsWith("carga") || key is "w" or "wu")  colMap["w"] = i;
                else if (key.StartsWith("tipo"))                          colMap["tipo"] = i;
                else if (key.StartsWith("rec"))                           colMap["rec"] = i;
                else if (key is "id" or "n" or "no" or "numero")          colMap["id"] = i;
            }
            lines.RemoveAt(0);
            if (!colMap.ContainsKey("lx") || !colMap.ContainsKey("ly"))
                result.Warnings.Add("No se detectaron columnas Lx/Ly en el header. Usá los nombres 'Lx' y 'Ly'.");
        }
        else
        {
            // Sin header: orden posicional
            colMap["lx"] = 0; colMap["ly"] = 1;
            if (lines[0].Length >= 3) colMap["h"] = 2;
            if (lines[0].Length >= 4) colMap["w"] = 3;
            if (lines[0].Length >= 5) colMap["tipo"] = 4;
            if (lines[0].Length >= 6) colMap["rec"] = 5;
        }

        int nextId = def.IdInicial;
        int rowNum = hasHeader ? 2 : 1;
        foreach (var parts in lines)
        {
            if (parts.All(string.IsNullOrWhiteSpace)) { rowNum++; continue; }

            double GetDouble(string key, double fallback)
            {
                if (!colMap.TryGetValue(key, out var i) || i >= parts.Length) return fallback;
                if (string.IsNullOrWhiteSpace(parts[i])) return fallback;
                return TryParseDouble(parts[i], out var v) ? v : fallback;
            }
            int GetInt(string key, int fallback)
            {
                if (!colMap.TryGetValue(key, out var i) || i >= parts.Length) return fallback;
                if (string.IsNullOrWhiteSpace(parts[i])) return fallback;
                return int.TryParse(parts[i].Trim(), NumberStyles.Integer, _inv, out var v) ? v : fallback;
            }

            var lx = GetDouble("lx", 0);
            var ly = GetDouble("ly", 0);
            if (lx <= 0 || ly <= 0)
            {
                result.Warnings.Add($"Fila {rowNum} ignorada: Lx={lx}, Ly={ly} (deben ser > 0).");
                result.LineasIgnoradas++;
                rowNum++;
                continue;
            }

            var hRaw = GetDouble("h", def.EspesorM);
            var recRaw = GetDouble("rec", def.RecM);
            // Si los headers contienen "cm" o el usuario marcó cm en defaults, convertir
            if (def.EspesorEnCm)
            {
                hRaw /= 100.0;
                recRaw /= 100.0;
            }

            result.Losas.Add(new Losa
            {
                Id = GetInt("id", nextId++),
                Tipo = GetInt("tipo", def.Tipo),
                Carga = GetDouble("w", def.CargaTonM2),
                Espesor = hRaw,
                Lx = lx,
                Ly = ly,
                Rec = recRaw,
            });
            rowNum++;
        }

        // Reemitir IDs si hay duplicados
        var ids = result.Losas.Select(l => l.Id).ToList();
        if (ids.Count != ids.Distinct().Count())
        {
            for (int i = 0; i < result.Losas.Count; i++) result.Losas[i].Id = i + def.IdInicial;
            result.Warnings.Add("Había IDs duplicados — se renumeraron secuencialmente.");
        }

        return result;
    }

    private static char DetectSeparator(string line)
    {
        // Más frecuente entre TAB / ; / , (en ese orden de preferencia)
        int tabs = line.Count(c => c == '\t');
        int semi = line.Count(c => c == ';');
        int comm = line.Count(c => c == ',');
        if (tabs >= semi && tabs >= comm) return '\t';
        if (semi >= comm) return ';';
        return ',';
    }

    private static IEnumerable<string> Split(string line, char sep)
        => line.Split(sep, StringSplitOptions.None);

    private static bool TryParseDouble(string s, out double v)
    {
        s = s.Trim();
        // Coma decimal (es-ES, es-AR) → punto
        s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, _inv, out v);
    }

    private static string Normalize(string s)
    {
        s = s.Trim().ToLowerInvariant();
        // Quitar tildes simples
        var map = new Dictionary<char, char> { ['á'] = 'a', ['é'] = 'e', ['í'] = 'i', ['ó'] = 'o', ['ú'] = 'u', ['ñ'] = 'n' };
        var sb = new System.Text.StringBuilder();
        foreach (var c in s) sb.Append(map.TryGetValue(c, out var n) ? n : c);
        // Quitar paréntesis con unidades: "lx (m)" → "lx"
        var t = sb.ToString();
        var paren = t.IndexOf('(');
        if (paren > 0) t = t[..paren].Trim();
        var bracket = t.IndexOf('[');
        if (bracket > 0) t = t[..bracket].Trim();
        return t;
    }
}
