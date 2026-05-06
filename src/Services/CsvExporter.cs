using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Exportador CSV plano (UTF-8 con BOM, separador ';' compatible con Excel-ES).
/// Una fila por losa: entrada + resultados parseados del .TXT cuando existan.
/// </summary>
public static class CsvExporter
{
    public static void Export(Sistema s, string path, char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        // No insertar BOM manual: UTF8Encoding(true) ya lo agrega en File.WriteAllText.
        sb.AppendLine(string.Join(sep,
            "ID","TIPO","TIPO_DESC","CARGA_ton_m2","ESPESOR_m","LX_m","LY_m","REC_m","Ly_Lx",
            "Mfx_ton_m_m","Mfy_ton_m_m","MSx_ton_m_m","MSy_ton_m_m"));
        foreach (var l in s.Losas)
        {
            sb.AppendLine(string.Join(sep,
                l.Id,
                l.Tipo,
                Quote(l.TipoDescripcion),
                F(l.Carga, inv),
                F(l.Espesor, inv),
                F(l.Lx, inv),
                F(l.Ly, inv),
                F(l.Rec, inv),
                F(l.Aspecto, inv),
                F(l.Mfx, inv),
                F(l.Mfy, inv),
                F(l.MSx, inv),
                F(l.MSy, inv)));
        }
        sb.AppendLine();
        sb.AppendLine($"Sistema:{sep}{s.Nombre}");
        sb.AppendLine($"NLOSA:{sep}{s.Losas.Count}");
        sb.AppendLine($"FC_ton_cm2:{sep}{F(s.Fc, inv)}");
        sb.AppendLine($"FY_ton_cm2:{sep}{F(s.Fy, inv)}");
        sb.AppendLine($"ADICIONALES:{sep}{s.Adicionales}");
        sb.AppendLine();
        sb.AppendLine("Bordes_X:");
        sb.AppendLine(string.Join(sep, "B-I","B-J","BALANCEO"));
        foreach (var b in s.BordesX) sb.AppendLine(string.Join(sep, b.BI, b.BJ, b.Balanceo));
        sb.AppendLine();
        sb.AppendLine("Bordes_Y:");
        sb.AppendLine(string.Join(sep, "B-I","B-J","BALANCEO"));
        foreach (var b in s.BordesY) sb.AppendLine(string.Join(sep, b.BI, b.BJ, b.Balanceo));
        sb.AppendLine();
        sb.AppendLine($"# Generado por LosasPlus. Motor de cálculo: F. Perdomo (Losas v5.00).");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string F(double? v, CultureInfo c) => v.HasValue ? v.Value.ToString("0.0000", c) : "";
    private static string F(double v, CultureInfo c) => v.ToString("0.0000", c);
    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
