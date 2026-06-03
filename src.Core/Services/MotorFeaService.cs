using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Puente (B6) entre LosasPlus (C#) y el motor FEA nativo (Python, paquete
/// <c>motor-fea</c>). <b>Aditivo</b>: ofrece diseñar una losa por elementos
/// finitos como alternativa al flujo <c>Losas.exe</c>, sin reemplazarlo.
///
/// <para>El motor se invoca como un proceso externo (igual patrón que
/// <c>Losas.exe</c>): se le pasa un JSON de parámetros por stdin
/// (<c>motor-fea --disenar-losa -</c>) y devuelve un JSON de resultados por
/// stdout. La construcción de parámetros y el parseo del resultado son
/// <b>puros y testeables</b>; sólo <see cref="DisenarLosaAsync"/> toca el proceso.</para>
///
/// <para><b>Unidades.</b> LosasPlus usa ton·cm; el motor usa SI. Las conversiones
/// (f'c, q, E) viven acá, en la frontera.</para>
/// </summary>
public static class MotorFeaService
{
    /// <summary>1 ton/cm² en MPa.</summary>
    public const double TonCm2_a_MPa = 98.0665;

    /// <summary>1 ton/m² en N/m².</summary>
    public const double TonM2_a_NM2 = 9806.65;

    /// <summary>Densidad de malla por defecto (elementos por lado).</summary>
    public const int MallaDefault = 8;

    /// <summary>
    /// Construye el diccionario de parámetros del motor (JSON-able) a partir de una
    /// <see cref="Losa"/> y su <see cref="Sistema"/>. Convierte ton·cm → SI.
    /// </summary>
    public static Dictionary<string, object> ConstruirParametros(
        Losa losa, Sistema sistema, int nx = MallaDefault, int ny = MallaDefault, string borde = "simple")
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));

        double fcMPa = sistema.Fc * TonCm2_a_MPa;             // 0.21 ton/cm² → ~20.6 MPa
        double fyMPa = sistema.Fy * TonCm2_a_MPa;             // 4.2 ton/cm² → ~412 MPa
        double eMPa = 4700.0 * Math.Sqrt(fcMPa);              // ACI 318-19 §19.2.2.1: Ec = 4700√f'c
        double qNM2 = losa.Carga * TonM2_a_NM2;               // ton/m² → N/m²
        double recMm = (losa.Rec > 0 ? losa.Rec : 0.025) * 1000.0;  // m → mm

        return new Dictionary<string, object>
        {
            ["a"] = losa.Lx,
            ["b"] = losa.Ly,
            ["nx"] = nx,
            ["ny"] = ny,
            ["E"] = eMPa * 1.0e6,                              // MPa → Pa
            ["nu"] = 0.2,
            ["t"] = losa.Espesor,
            ["q"] = qNM2,
            ["fc"] = fcMPa,
            ["fy"] = fyMPa,
            ["recubrimiento"] = recMm,
            ["borde"] = borde,
        };
    }

    /// <summary>Serializa los parámetros a JSON.</summary>
    public static string ParametrosAJson(Dictionary<string, object> parametros)
        => JsonSerializer.Serialize(parametros, new JsonSerializerOptions { WriteIndented = false });

    /// <summary>Parsea el JSON de resultados del motor a un <see cref="ResultadoMotorLosa"/>.</summary>
    public static ResultadoMotorLosa ParsearResultado(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new ResultadoMotorLosa
        {
            WCentral = r.GetProperty("w_central").GetDouble(),
            MxMax = r.GetProperty("mx_max").GetDouble(),
            MyMax = r.GetProperty("my_max").GetDouble(),
            MApoyoMax = r.TryGetProperty("m_apoyo_max", out var ma) ? ma.GetDouble() : 0.0,
            FranjaX = LeerFranja(r.GetProperty("franja_x")),
            FranjaY = LeerFranja(r.GetProperty("franja_y")),
            FranjaApoyo = r.TryGetProperty("franja_apoyo", out var fa) ? LeerFranja(fa) : null,
        };
    }

    private static FranjaMotor LeerFranja(JsonElement f) => new()
    {
        AsRequerido = f.GetProperty("as_requerido").GetDouble(),
        AsDiseno = f.GetProperty("as_diseno").GetDouble(),
        Disponer = f.GetProperty("disponer").GetString() ?? "",
        Cumple = f.GetProperty("cumple").GetBoolean(),
    };

    /// <summary>
    /// Invoca el motor (<paramref name="comando"/>, p. ej. <c>motor-fea</c> o
    /// <c>python3 -m motor_fea.api.cli</c>) para diseñar la losa. Devuelve el
    /// resultado parseado, o lanza con un mensaje claro si el motor no está
    /// disponible (no rompe el flujo <c>Losas.exe</c>).
    /// </summary>
    public static async Task<ResultadoMotorLosa> DisenarLosaAsync(
        Losa losa, Sistema sistema, string comando = "motor-fea", int nx = MallaDefault, int ny = MallaDefault)
    {
        string json = ParametrosAJson(ConstruirParametros(losa, sistema, nx, ny));

        var partes = comando.Split(' ', 2);
        var psi = new ProcessStartInfo
        {
            FileName = partes[0],
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Arguments = partes.Length > 1 ? partes[1] + " --disenar-losa -" : "--disenar-losa -";

        Process proc;
        try { proc = Process.Start(psi)!; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"No se pudo lanzar el motor FEA ('{comando}'). Verificá que esté instalado. Detalle: {ex.Message}", ex);
        }

        await proc.StandardInput.WriteAsync(json);
        proc.StandardInput.Close();
        string salida = await proc.StandardOutput.ReadToEndAsync();
        string err = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(salida))
            throw new InvalidOperationException($"El motor FEA falló (exit {proc.ExitCode}): {err}");

        return ParsearResultado(salida);
    }

    /// <summary>1 N·m en ton·m (1 ton-fuerza = 9806.65 N).</summary>
    public const double NM_a_TonM = 1.0 / TonM2_a_NM2;

    /// <summary>
    /// Aplica los momentos del motor a la <see cref="Losa"/>: puebla Mfx/Mfy (vano)
    /// y MSx/MSy (apoyo) convirtiendo N·m/m → ton·m/m, para que el pipeline de Aceros
    /// los use igual que los momentos del <c>.TXT</c> de <c>Losas.exe</c>. Ruta
    /// <b>aditiva</b> (el motor reemplaza la fuente de momentos, no el pipeline).
    /// </summary>
    public static void AplicarMomentos(Losa losa, ResultadoMotorLosa r)
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        if (r is null) throw new ArgumentNullException(nameof(r));
        losa.Mfx = r.MxMax * NM_a_TonM;          // vano X
        losa.Mfy = r.MyMax * NM_a_TonM;          // vano Y
        losa.MSx = r.MApoyoMax * NM_a_TonM;      // apoyo (el motor da un único m_apoyo_max)
        losa.MSy = r.MApoyoMax * NM_a_TonM;
    }

    /// <summary>
    /// Calcula los momentos de TODAS las losas del sistema con el motor (alternativa
    /// aditiva a <c>Losas.exe</c>) y los aplica. No toca el flujo <c>Losas.exe</c>.
    /// </summary>
    public static async Task CalcularSistemaConMotorAsync(
        Sistema sistema, string comando = "motor-fea", int nx = MallaDefault, int ny = MallaDefault)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        foreach (var losa in sistema.Losas)
        {
            var r = await DisenarLosaAsync(losa, sistema, comando, nx, ny);
            AplicarMomentos(losa, r);
        }
    }
}

/// <summary>Resultado del diseño de losa por el motor FEA (unidades SI / mm²·m).</summary>
public sealed class ResultadoMotorLosa
{
    public double WCentral { get; set; }
    public double MxMax { get; set; }
    public double MyMax { get; set; }
    public double MApoyoMax { get; set; }
    public FranjaMotor FranjaX { get; set; } = new();
    public FranjaMotor FranjaY { get; set; } = new();
    public FranjaMotor? FranjaApoyo { get; set; }
}

/// <summary>Diseño de acero de una franja devuelto por el motor.</summary>
public sealed class FranjaMotor
{
    public double AsRequerido { get; set; }
    public double AsDiseno { get; set; }
    public string Disponer { get; set; } = "";
    public bool Cumple { get; set; }
}
