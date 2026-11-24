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
    /// Cubre las losas apoyadas en cuatro bordes (10 a 60). Las apoyadas en tres
    /// bordes (13 a 64) siguen sin mapear: no hay listado de Losas.exe con que
    /// confirmar su orientación (ver TABLAS-PERDOMO.md).
    /// </summary>
    public string SubtipoDeCodigoDL(int codigo)
        => CodigoASubtipo.TryGetValue(codigo, out var st)
            ? st
            : throw new NotSupportedException(
                $"Código de tipo {codigo} aún no mapeado a sub-tipo de tabla (faltan fixtures de validación).");

    /// <summary>
    /// Cantidad de bordes continuos de un tipo de cuatro apoyos: <c>NS</c> en los
    /// bordes horizontales (norte y sur) y <c>EW</c> en los verticales (este y
    /// oeste). Lo usa el cálculo en una dirección.
    /// </summary>
    public (int NS, int EW) BordesContinuos(int codigo)
        => ContinuosPorCodigo.TryGetValue(codigo, out var c)
            ? c
            : throw new NotSupportedException(
                $"Código de tipo {codigo} aún no mapeado a sub-tipo de tabla (faltan fixtures de validación).");

    // Verificados contra Losas.exe con el sistema de demostración de 27 losas
    // dentro del rango de la tabla (error ≤ 0.015 ton·m/m): 40, 51, 52 y 60.
    // Los tipos 10, 21, 22, 31 y 32 solo aparecen allí como losas en una
    // dirección: su orientación (qué momento de apoyo existe) está confirmada,
    // pero sus valores dentro del rango de la tabla no tienen listado de control.
    private static readonly IReadOnlyDictionary<int, string> CodigoASubtipo = new Dictionary<int, string>
    {
        [10] = "1",    // 4 apoyos simples
        [21] = "2a",   // 1 borde continuo horizontal (norte o sur)
        [22] = "2b",   // 1 borde continuo vertical (este u oeste)
        [31] = "3a",   // 2 continuos opuestos, norte y sur
        [32] = "3b",   // 2 continuos opuestos, este y oeste
        [40] = "4",    // 2 continuos adyacentes
        [51] = "5a",   // 3 continuos; el apoyo simple es horizontal
        [52] = "5b",   // 3 continuos; el apoyo simple es vertical
        [60] = "6",    // 4 continuos
    };

    private static readonly IReadOnlyDictionary<int, (int NS, int EW)> ContinuosPorCodigo =
        new Dictionary<int, (int NS, int EW)>
        {
            [10] = (0, 0), [21] = (1, 0), [22] = (0, 1), [31] = (2, 0), [32] = (0, 2),
            [40] = (1, 1), [51] = (1, 2), [52] = (2, 1), [60] = (2, 2),
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
