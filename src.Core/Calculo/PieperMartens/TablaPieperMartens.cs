using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LosasPlus.Calculo.PieperMartens;

/// <summary>
/// Factores de momento de las tablas de Pieper-Martens (F. Perdomo) para un
/// sub-tipo de borde y una relación de aspecto ε = Ly/Lx ya interpolada.
/// Convención: Mfx = q·Lx²/Fx, Mfy = q·Ly²/Fy, Msx = q·Lx²/Sx, Msy = q·Ly²/Sy.
/// <see cref="Sx"/>/<see cref="Sy"/> nulos ⇒ ese borde no aporta momento de
/// empotramiento (apoyo simple o libre) ⇒ Ms = 0 en esa dirección.
/// </summary>
public readonly record struct FactoresMomento(double Fx, double Fy, double? Sx, double? Sy);

/// <summary>
/// Tablas de coeficientes del método de Pieper-Martens según F. Perdomo
/// (Losas v5.21), embebidas en <c>TablasPerdomo.json</c>. Provee interpolación
/// lineal en ε = Ly/Lx (rango 0.50–2.00) y el mapeo del código de tipo del .DL
/// al sub-tipo de tabla.
/// </summary>
public sealed class TablaPieperMartens
{
    private const string RecursoJson = "LosasPlus.Calculo.PieperMartens.TablasPerdomo.json";

    private readonly IReadOnlyDictionary<string, Subtipo> _tipos;

    private TablaPieperMartens(IReadOnlyDictionary<string, Subtipo> tipos) => _tipos = tipos;

    /// <summary>Columnas tabuladas de un sub-tipo. <c>Sx</c>/<c>Sy</c> nulos donde el PDF pone "--".</summary>
    private sealed record Subtipo(double[] Eps, double[] Fx, double[] Fy, double?[] Sx, double?[] Sy);

    /// <summary>Carga las tablas desde el recurso JSON embebido en LosasPlus.Core.</summary>
    public static TablaPieperMartens Cargar()
    {
        var asm = typeof(TablaPieperMartens).Assembly;
        using var s = asm.GetManifestResourceStream(RecursoJson)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {RecursoJson}");
        using var doc = JsonDocument.Parse(s);

        var tipos = new Dictionary<string, Subtipo>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.GetProperty("tipos").EnumerateObject())
        {
            var t = p.Value;
            tipos[p.Name] = new Subtipo(
                LeerDoubles(t, "eps"), LeerDoubles(t, "Fx"), LeerDoubles(t, "Fy"),
                LeerNullables(t, "Sx"), LeerNullables(t, "Sy"));
        }
        return new TablaPieperMartens(tipos);
    }

    /// <summary>Factores (Fx,Fy,Sx,Sy) para un sub-tipo, interpolados linealmente en ε.</summary>
    public FactoresMomento Factores(string subtipo, double eps)
    {
        if (!_tipos.TryGetValue(subtipo, out var t))
            throw new ArgumentException($"Sub-tipo Pieper-Martens desconocido: '{subtipo}'.", nameof(subtipo));

        return new FactoresMomento(
            Interpolar(t.Eps, t.Fx, eps),
            Interpolar(t.Eps, t.Fy, eps),
            InterpolarNullable(t.Eps, t.Sx, eps),
            InterpolarNullable(t.Eps, t.Sy, eps));
    }

    /// <summary>
    /// Mapea el código de tipo del .DL (p. ej. 40) al sub-tipo de tabla (p. ej. "4").
    /// Convención (spec F3 §3.3): d1 = nº de TABLA del PDF (1–6); d2 = 0 bloque
    /// único (tablas simétricas 1/4/6), 1/2 = orientación a/b (tablas 2/3/5),
    /// 3/4 = tabla (d1+6) orientación a/b (losas apoyadas en TRES bordes, un
    /// borde libre — tablas 7–12). Sólo el 40 está verificado numéricamente
    /// contra Losas.exe (RESTAURANTE 2); el resto se confirmará con fixtures
    /// del usuario en Windows (ver TABLAS-PERDOMO.md §3). Los voladizos 71/72
    /// NO pasan por aquí (MomentosCalculator.EsVoladizo los resuelve antes).
    /// </summary>
    public string SubtipoDeCodigoDL(int codigo)
        => CodigoASubtipo.TryGetValue(codigo, out var st)
            ? st
            : throw new NotSupportedException(
                $"Código de tipo {codigo} fuera del catálogo .DL: sin sub-tipo de tabla Pieper-Martens.");

    private static readonly IReadOnlyDictionary<int, string> CodigoASubtipo = new Dictionary<int, string>
    {
        // ---- 4 bordes apoyados (tablas 1–6) ----
        [10] = "1",    // T1  4 apoyos simples (bloque único)
        [21] = "2a",   // T2a 1 empotrado horizontal (N/S) → Sy
        [22] = "2b",   // T2b 1 empotrado vertical (E/W) → Sx
        [31] = "3a",   // T3a 2 opuestos N,S
        [32] = "3b",   // T3b 2 opuestos E,W
        [40] = "4",    // T4  2 adyacentes empotrados — VERIFICADO vs Losas.exe
        [51] = "5a",   // T5a 3 empotrados, apoyo horizontal
        [52] = "5b",   // T5b 3 empotrados, apoyo vertical
        [60] = "6",    // T6  perimetral (bloque único)
        // ---- 3 bordes apoyados + 1 libre (tablas 7–12): (d1)(3|4) → (d1+6) a|b ----
        [13] = "7a",  [14] = "7b",
        [23] = "8a",  [24] = "8b",
        [33] = "9a",  [34] = "9b",
        [43] = "10a", [44] = "10b",
        [53] = "11a", [54] = "11b",
        [63] = "12a", [64] = "12b",
    };

    // ---- helpers ---------------------------------------------------------

    private static double[] LeerDoubles(JsonElement t, string clave)
    {
        var arr = t.GetProperty(clave);
        var r = new double[arr.GetArrayLength()];
        int i = 0;
        foreach (var e in arr.EnumerateArray()) r[i++] = e.GetDouble();
        return r;
    }

    private static double?[] LeerNullables(JsonElement t, string clave)
    {
        var arr = t.GetProperty(clave);
        var r = new double?[arr.GetArrayLength()];
        int i = 0;
        foreach (var e in arr.EnumerateArray())
            r[i++] = e.ValueKind == JsonValueKind.Null ? null : e.GetDouble();
        return r;
    }

    /// <summary>Interpolación lineal en x; clamp en los extremos del rango tabulado.</summary>
    private static double Interpolar(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];
        for (int i = 1; i < xs.Length; i++)
        {
            if (x <= xs[i])
            {
                double f = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                return ys[i - 1] + f * (ys[i] - ys[i - 1]);
            }
        }
        return ys[^1];
    }

    /// <summary>
    /// Igual que <see cref="Interpolar"/> pero la columna puede ser nula. En las
    /// tablas un coeficiente S es nulo en TODO el rango o en ninguno (un borde
    /// está empotrado para esa dirección o no lo está), así que basta mirar el
    /// primer valor.
    /// </summary>
    private static double? InterpolarNullable(double[] xs, double?[] ys, double x)
    {
        if (ys[0] is null) return null;
        var v = new double[ys.Length];
        for (int i = 0; i < ys.Length; i++) v[i] = ys[i] ?? 0.0;
        return Interpolar(xs, v, x);
    }
}
