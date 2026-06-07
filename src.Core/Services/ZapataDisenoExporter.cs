using System;
using System.Globalization;
using System.IO;
using System.Text;
using LosasPlus.Calculo;

namespace LosasPlus.Services;

/// <summary>
/// Exporta el <b>diseño de una zapata aislada</b> (ACI 318-19) a CSV: un bloque
/// de resumen con la presión de contacto, los ratios de punzonamiento y cortante
/// unidireccional, el momento, el acero requerido y si cumple. Espeja a
/// <see cref="ColumnaDisenoExporter"/>: <see cref="ToCsv"/> es puro y determinista
/// (sin timestamp). UTF-8/BOM, separador ';'.
/// </summary>
public static class ZapataDisenoExporter
{
    /// <summary>Diseño de zapata a CSV (texto, sin tocar disco).</summary>
    public static string ToCsv(ZapataDisenador.DisenoZapata d, char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# DISEÑO DE ZAPATA AISLADA — ACI 318-19");
        sb.AppendLine($"Qu_MPa:{sep}{d.QuMPa.ToString("0.000", inv)}");
        sb.AppendLine($"Punzonamiento_ratio:{sep}{d.Punzonamiento.Ratio.ToString("0.000", inv)}");
        sb.AppendLine($"Punzonamiento_cumple:{sep}{d.Punzonamiento.Cumple}");
        sb.AppendLine($"Cortante_ratio:{sep}{d.Cortante.Ratio.ToString("0.000", inv)}");
        sb.AppendLine($"Cortante_cumple:{sep}{d.Cortante.Cumple}");
        sb.AppendLine($"Mu_kNm:{sep}{(d.MuNmm / 1.0e6).ToString("0.0", inv)}");
        sb.AppendLine($"As_mm2:{sep}{d.Acero.AsMm2.ToString("0.0", inv)}");
        sb.AppendLine($"As_min_mm2:{sep}{d.Acero.AsMinMm2.ToString("0.0", inv)}");
        sb.AppendLine($"Seccion_insuficiente:{sep}{d.Acero.SeccionInsuficiente}");
        sb.AppendLine($"Cumple:{sep}{d.Cumple}");
        return sb.ToString();
    }

    /// <summary>Escribe el CSV del diseño a <paramref name="path"/> (UTF-8 con BOM).</summary>
    public static void ExportCsv(ZapataDisenador.DisenoZapata d, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Ruta de salida vacía.", nameof(path));
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToCsv(d), new UTF8Encoding(true));
    }
}
