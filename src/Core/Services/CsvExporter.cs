using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Exportador CSV de <b>resultados</b> producidos por el motor (Losas.exe + LosasPlus.Calculo).
/// UTF-8 con BOM, separador ';' compatible con Excel-ES.
/// <para>
/// Una fila por losa con todas las columnas que el ingeniero quiere comparar contra
/// su Excel de referencia ("ESPESOR EQUIVALENTE.xlsx"): geometría, momentos del
/// motor (Mfx, Mfy, MSx, MSy), acero parseado del .TXT (AsxVano, AsyVano),
/// outputs del CalculoEngine (HCalc, HEq, Qd, Ql, Qu, αx, αy, αm, estado),
/// cómputos métricos (V_concreto, V_bovedilla, N bovedillas) y acero distribuido
/// configurado por losa (AsxCalc, AsyCalc).
/// </para>
/// Para bordes adicionales se exportan las columnas del motor: MuI, MuJ, MuI-J,
/// d, As/m, Disponer — también ya parseadas del .TXT.
/// </summary>
public static class CsvExporter
{
    public static void Export(Sistema s, string path, char sep = ';')
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        // ----- Tabla principal: una fila por losa con TODOS los resultados -----
        sb.AppendLine(string.Join(sep,
            "ID","TIPO","TIPO_DESC","CARGA_ton_m2","ESPESOR_m","LX_m","LY_m","REC_m","Ly_Lx","COND",
            "Mfx","Mfy","MSx","MSy","AsxVano","AsyVano",
            "HCalc_m","HEq_m","Qd","Ql","Qu",
            "AlphaX","AlphaY","AlphaM","EstadoAlphaFm",
            "VTotal_m3","VBovedilla_m3","VConcreto_m3","CantBovedillas",
            "Asx_cm2","Asy_cm2"));
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
                l.Cond,
                F(l.Mfx, inv),
                F(l.Mfy, inv),
                F(l.MSx, inv),
                F(l.MSy, inv),
                Quote(l.AsxVano ?? ""),
                Quote(l.AsyVano ?? ""),
                F(l.HCalc, inv),
                F(l.HEq, inv),
                F(l.Qd, inv),
                F(l.Ql, inv),
                F(l.Qu, inv),
                F(l.AlphaX, inv),
                F(l.AlphaY, inv),
                F(l.AlphaM, inv),
                Quote(l.EstadoAlphaFm ?? ""),
                F(l.VTotal, inv),
                F(l.VBovedilla, inv),
                F(l.VConcreto, inv),
                l.CantBovedillasTotal.HasValue ? l.CantBovedillasTotal.Value.ToString(inv) : "",
                F(l.AsxCalc, inv),
                F(l.AsyCalc, inv)));
        }
        sb.AppendLine();

        // ----- Metadatos del sistema -----
        sb.AppendLine($"Sistema:{sep}{s.Nombre}");
        sb.AppendLine($"NLOSA:{sep}{s.Losas.Count}");
        sb.AppendLine($"FC_ton_cm2:{sep}{F(s.Fc, inv)}");
        sb.AppendLine($"FY_ton_cm2:{sep}{F(s.Fy, inv)}");
        sb.AppendLine($"ADICIONALES:{sep}{s.Adicionales}");
        sb.AppendLine();

        // ----- Bordes adicionales: input + resultados del motor -----
        sb.AppendLine("Bordes_X:");
        sb.AppendLine(string.Join(sep, "B-I","B-J","BALANCEO","MuI","MuJ","MuIJ","d_cm","AsReq","Disponer"));
        foreach (var b in s.BordesX)
            sb.AppendLine(string.Join(sep, b.BI, b.BJ, b.Balanceo,
                F(b.MuI, inv), F(b.MuJ, inv), F(b.MuIJ, inv), F(b.D, inv), F(b.AsReq, inv),
                Quote(b.Disponer ?? "")));
        sb.AppendLine();
        sb.AppendLine("Bordes_Y:");
        sb.AppendLine(string.Join(sep, "B-I","B-J","BALANCEO","MuI","MuJ","MuIJ","d_cm","AsReq","Disponer"));
        foreach (var b in s.BordesY)
            sb.AppendLine(string.Join(sep, b.BI, b.BJ, b.Balanceo,
                F(b.MuI, inv), F(b.MuJ, inv), F(b.MuIJ, inv), F(b.D, inv), F(b.AsReq, inv),
                Quote(b.Disponer ?? "")));
        sb.AppendLine();
        sb.AppendLine($"# Generado por LosasPlus. Motor: F. Perdomo (Losas v5.x) + LosasPlus.Calculo (ACI 318).");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string F(double? v, CultureInfo c) => v.HasValue ? v.Value.ToString("0.0000", c) : "";
    private static string F(double v, CultureInfo c) => v.ToString("0.0000", c);
    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}
