using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LosasPlus.Cargas;

/// <summary>Formato de un archivo de combinaciones de carga de DISEST.</summary>
public enum FormatoCombinaciones
{
    /// <summary>.DZP — programa ZAPATA. Casos sin columna SIMB; trae servicio y últimas.</summary>
    Dzp,

    /// <summary>.CEZ — programa ESFUERZOS. Casos con columna SIMB; sólo servicio.</summary>
    Cez,
}

/// <summary>Error al parsear un archivo de combinaciones <c>.DZP</c>/<c>.CEZ</c>.</summary>
public sealed class CombinacionesParseException : Exception
{
    public CombinacionesParseException(string message) : base(message) { }
}

/// <summary>
/// Parser de los archivos de combinaciones de carga de DISEST: <c>.DZP</c>
/// (programa ZAPATA) y <c>.CEZ</c> (programa ESFUERZOS). Son texto plano: las
/// líneas que empiezan con <c>$</c> son comentarios y la línea <c>FIN</c>
/// termina el archivo.
///
/// <para>
/// Estructura: una línea <c>NCC NCOMB</c>; luego NCC casos; luego un bloque de
/// NCOMB combinaciones de servicio (§3.1) y —sólo en <c>.DZP</c>— otro bloque
/// de NCOMB combinaciones últimas (§3.2).
/// </para>
///
/// <para>Lógica puramente textual, sin WPF — testeable de forma exhaustiva.</para>
/// </summary>
public static class ParserDzpCez
{
    private static readonly char[] _sep = { ' ', '\t' };

    /// <summary>
    /// Parsea el contenido de un archivo de combinaciones y devuelve un
    /// <see cref="CombinacionesProyecto"/> con sus casos y combinaciones
    /// (servicio y, si el archivo las trae, últimas).
    /// </summary>
    /// <exception cref="CombinacionesParseException">Si el formato es inválido.</exception>
    public static CombinacionesProyecto Parsear(string texto, FormatoCombinaciones formato)
    {
        if (texto is null) throw new ArgumentNullException(nameof(texto));

        // --- Pasada 1: separar líneas de datos de los comentarios; de paso,
        //     capturar la línea-comentario con los códigos de caso del .DZP. ---
        var datos = new List<string>();
        string[]? codigosComentario = null;

        foreach (var cruda in texto.Split('\n'))
        {
            var linea = cruda.Trim();
            if (linea.Length == 0) continue;
            if (linea.Equals("FIN", StringComparison.OrdinalIgnoreCase)) break;
            if (linea.StartsWith("$", StringComparison.Ordinal))
            {
                // «$ Caso  --  D  L  Lr  Ex1 …» — los códigos van tras el «--».
                if (codigosComentario is null &&
                    linea.Contains("Caso", StringComparison.OrdinalIgnoreCase))
                {
                    var t = linea.Split(_sep, StringSplitOptions.RemoveEmptyEntries);
                    int guion = Array.IndexOf(t, "--");
                    if (guion > 0 && t.Length > guion + 1)
                        codigosComentario = t.Skip(guion + 1).ToArray();
                }
                continue;
            }
            datos.Add(linea);
        }

        int idx = 0;
        var (ncc, ncomb) = LeerNccNcomb(Siguiente(datos, ref idx));

        // --- Casos de carga ---
        var casos = new List<CasoCarga>();
        for (int i = 0; i < ncc; i++)
        {
            var t = Siguiente(datos, ref idx).Split(_sep, StringSplitOptions.RemoveEmptyEntries);
            int minTokens = formato == FormatoCombinaciones.Cez ? 6 : 5;
            if (t.Length < minTokens)
                throw new CombinacionesParseException($"Caso de carga {i + 1}: formato inválido.");

            int tipo = ParsearEntero(t[^3], "TIPO");
            int fce  = ParsearEntero(t[^2], "FCE");
            int fsis = ParsearEntero(t[^1], "FSIS");

            string codigo, nombre;
            if (formato == FormatoCombinaciones.Cez)
            {
                codigo = t[1];
                nombre = string.Join(' ', t[2..^3]);
            }
            else
            {
                nombre = string.Join(' ', t[1..^3]);
                codigo = codigosComentario is not null && i < codigosComentario.Length
                    ? codigosComentario[i]
                    : "C" + t[0];
            }

            casos.Add(new CasoCarga
            {
                Codigo = codigo,
                Nombre = nombre,
                Tipo   = MapearTipo(tipo),
                Fce    = fce != 0,
                Fsis   = fsis != 0,
            });
        }

        var cp = new CombinacionesProyecto { Norma = NormaCombinaciones.Asce7_05 };
        foreach (var c in casos) cp.Casos.Add(c);

        // --- §3.1 Combinaciones de servicio ---
        foreach (var combo in LeerBloque(datos, ref idx, ncomb, casos, TipoCombinacion.Servicio))
            cp.Combinaciones.Add(combo);

        // --- §3.2 Combinaciones últimas — sólo si quedan NCOMB líneas más
        //     (el .CEZ no trae el bloque de diseño). ---
        if (idx + ncomb <= datos.Count)
            foreach (var combo in LeerBloque(datos, ref idx, ncomb, casos, TipoCombinacion.Ultima))
                cp.Combinaciones.Add(combo);

        return cp;
    }

    private static List<CombinacionCarga> LeerBloque(
        List<string> datos, ref int idx, int ncomb, List<CasoCarga> casos, TipoCombinacion tipo)
    {
        var combos = new List<CombinacionCarga>();
        for (int c = 0; c < ncomb; c++)
        {
            var t = Siguiente(datos, ref idx).Split(_sep, StringSplitOptions.RemoveEmptyEntries);
            // Cada fila es «IC GRUPO FS1 … FSn» — se requieren 2 + NCC tokens.
            if (t.Length < 2 + casos.Count)
                throw new CombinacionesParseException(
                    $"Combinación {c + 1}: menos factores ({t.Length - 2}) que casos ({casos.Count}).");

            var combo = new CombinacionCarga { Tipo = tipo };
            for (int j = 0; j < casos.Count; j++)
            {
                double f = ParsearDouble(t[2 + j], "factor");
                if (f != 0.0)
                    combo.Terminos.Add(new TerminoCombinacion(casos[j].Codigo, f));
            }
            combo.Nombre = SintetizarNombre(combo.Terminos, c + 1, tipo);
            combos.Add(combo);
        }
        return combos;
    }

    /// <summary>Construye un nombre legible de la combinación, p. ej. «1.2D + 1.6L - 0.3Ey1».</summary>
    private static string SintetizarNombre(
        IReadOnlyList<TerminoCombinacion> terminos, int numero, TipoCombinacion tipo)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < terminos.Count; i++)
        {
            var t = terminos[i];
            double abs = Math.Abs(t.Factor);
            string mag = abs == 1.0 ? "" : abs.ToString("0.###", CultureInfo.InvariantCulture);
            if (i == 0)
                sb.Append(t.Factor < 0 ? "-" : "").Append(mag).Append(t.CodigoCaso);
            else
                sb.Append(t.Factor < 0 ? " - " : " + ").Append(mag).Append(t.CodigoCaso);
        }
        return sb.Length > 0
            ? sb.ToString()
            : $"{(tipo == TipoCombinacion.Ultima ? "Última" : "Servicio")} {numero}";
    }

    private static (int ncc, int ncomb) LeerNccNcomb(string linea)
    {
        var t = linea.Split(_sep, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length < 2)
            throw new CombinacionesParseException("Falta la línea de datos generales «NCC NCOMB».");
        int ncc = ParsearEntero(t[0], "NCC");
        int ncomb = ParsearEntero(t[1], "NCOMB");
        if (ncc <= 0 || ncomb <= 0)
            throw new CombinacionesParseException("NCC y NCOMB deben ser positivos.");
        return (ncc, ncomb);
    }

    private static string Siguiente(List<string> datos, ref int idx)
    {
        if (idx >= datos.Count)
            throw new CombinacionesParseException("El archivo termina antes de lo esperado.");
        return datos[idx++];
    }

    private static int ParsearEntero(string s, string campo)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CombinacionesParseException($"Valor entero inválido en «{campo}»: «{s}».");

    private static double ParsearDouble(string s, string campo)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new CombinacionesParseException($"Valor numérico inválido en «{campo}»: «{s}».");

    private static TipoCasoCarga MapearTipo(int tipo) => tipo switch
    {
        1 => TipoCasoCarga.Muerta,
        2 => TipoCasoCarga.Viva,
        3 => TipoCasoCarga.Sismo,
        4 => TipoCasoCarga.Viento,
        5 => TipoCasoCarga.Deformacion,
        _ => throw new CombinacionesParseException($"TIPO de caso desconocido: {tipo}."),
    };
}
