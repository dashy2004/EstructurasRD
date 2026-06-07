using System;
using System.Globalization;
using System.IO;
using System.Text;
using LosasPlus.Calculo;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el <b>diseño de una columna</b> (ACI 318-19) a CSV: un bloque de
/// resumen (cuantía, Po, φPn,max, estribo, chequeo de demanda) seguido de la
/// tabla de puntos del <b>diagrama de interacción</b> P-M. Unidades amigables
/// (kN, kN·m) sobre el cálculo interno en SI (N, mm). UTF-8/BOM, separador ';'.
///
/// <para>Espeja a <see cref="AcerosLosaExporter"/>: <see cref="ToCsv"/> es puro y
/// determinista (sin timestamp) para aserción directa en tests.</para>
/// </summary>
public static class ColumnaDisenoExporter
{
    /// <summary>Diseño de columna a CSV (texto, sin tocar disco).</summary>
    public static string ToCsv(ColumnaDisenador.DisenoColumna d, char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# DISEÑO DE COLUMNA — ACI 318-19");
        sb.AppendLine($"RhoG:{sep}{d.RhoG.ToString("0.0000", inv)}");
        sb.AppendLine($"CumpleCuantia:{sep}{d.CumpleCuantia}");
        sb.AppendLine($"Po_kN:{sep}{(d.PoN / 1000.0).ToString("0.0", inv)}");
        sb.AppendLine($"PhiPnMax_kN:{sep}{(d.PhiPnMaxN / 1000.0).ToString("0.0", inv)}");
        sb.AppendLine($"Estribo:{sep}#{d.Estribo.Numero} @ {d.Estribo.SeparacionMm.ToString("0.0", inv)} mm");
        sb.AppendLine($"Demanda_ratio:{sep}{d.Chequeo.Ratio.ToString("0.000", inv)}");
        sb.AppendLine($"Demanda_cumple:{sep}{d.Chequeo.Cumple}");
        sb.AppendLine();
        sb.AppendLine("# DIAGRAMA DE INTERACCIÓN P-M");
        sb.AppendLine(string.Join(sep, "c_mm", "Pn_kN", "Mn_kNm", "phi", "phiPn_kN", "phiMn_kNm"));
        foreach (var p in d.Diagrama)
            sb.AppendLine(string.Join(sep,
                p.C.ToString("0.0", inv),
                (p.Pn / 1000.0).ToString("0.0", inv),
                (p.Mn / 1e6).ToString("0.000", inv),
                p.Phi.ToString("0.000", inv),
                (p.PhiPn / 1000.0).ToString("0.0", inv),
                (p.PhiMn / 1e6).ToString("0.000", inv)));
        return sb.ToString();
    }

    /// <summary>Escribe el CSV del diseño a <paramref name="path"/> (UTF-8 con BOM).</summary>
    public static void ExportCsv(ColumnaDisenador.DisenoColumna d, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(d), new UTF8Encoding(true));
    }

    /// <summary>
    /// Diseño de columna a CSV <b>incluyendo</b> el resumen de esbeltez/δ
    /// (ACI 318-19 §6.6.4): el bloque base seguido de un bloque «# ESBELTEZ».
    /// </summary>
    public static string ToCsv(
        ColumnaDisenador.DisenoColumna d, ColumnaDisenador.ResumenEsbeltezColumna esbeltez, char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(ToCsv(d, sep));
        sb.AppendLine();
        sb.AppendLine("# ESBELTEZ (ACI 318-19 §6.6.4)");
        sb.AppendLine($"r_mm:{sep}{esbeltez.RMm.ToString("0.0", inv)}");
        sb.AppendLine($"kLu_sobre_r:{sep}{esbeltez.KLuSobreR.ToString("0.00", inv)}");
        sb.AppendLine($"Limite:{sep}{esbeltez.Limite.ToString("0.00", inv)}");
        sb.AppendLine($"EsEsbelta:{sep}{esbeltez.EsEsbelta}");
        sb.AppendLine($"Pc_kN:{sep}{(esbeltez.PcN / 1000.0).ToString("0.0", inv)}");
        sb.AppendLine($"delta:{sep}{esbeltez.Delta.ToString("0.000", inv)}");
        return sb.ToString();
    }

    /// <summary>Escribe el CSV del diseño + esbeltez a <paramref name="path"/> (UTF-8 con BOM).</summary>
    public static void ExportCsv(
        ColumnaDisenador.DisenoColumna d, ColumnaDisenador.ResumenEsbeltezColumna esbeltez, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(d, esbeltez), new UTF8Encoding(true));
    }
}
