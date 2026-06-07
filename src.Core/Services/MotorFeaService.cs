using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LosasPlus.Calculo;
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

    /// <summary>
    /// Mapea el <paramref name="tipo"/> de losa (caso Pieper-Martens, ver
    /// <see cref="TipoLosa"/>) al parámetro <c>borde</c> que entiende el motor FEM.
    /// El motor sólo soporta dos condiciones de contorno
    /// (<c>"simple"</c> = simplemente apoyada, <c>"empotrado"</c> = contorno
    /// empotrado), por lo que el mapeo es necesariamente grueso:
    /// <list type="bullet">
    ///   <item>Si el tipo tiene <b>al menos un</b> borde continuo
    ///   (<see cref="BorderKind.Empotrado"/>) ⇒ <c>"empotrado"</c> — el motor
    ///   modela el contorno empotrado y recupera el momento negativo de apoyo,
    ///   capturando (aproximadamente) la continuidad que el tipo codifica.</item>
    ///   <item>Si <b>ningún</b> borde es continuo (tipos puramente apoyados / con
    ///   vuelos, p. ej. 10/13/14) ⇒ <c>"simple"</c>.</item>
    /// </list>
    /// Antes de este cambio toda losa se modelaba como simplemente apoyada,
    /// ignorando la continuidad del tipo. Si el código no está en el catálogo se
    /// asume <c>"simple"</c> (degradación segura).
    /// </summary>
    public static string BordeDesdeTipo(int tipo)
    {
        if (TipoLosa.Catalogo.TryGetValue(TipoLosa.NormalizarCodigo(tipo), out var t)
            && t.BordesContinuos > 0)
            return "empotrado";
        return "simple";
    }

    // Regex para normalizar tokens NaN/Infinity/-Infinity del motor Python a null
    // antes de parsear. Solo reemplaza ocurrencias fuera de strings JSON (tras :,[, ).
    private static readonly Regex _reNaN =
        new(@"(?<=[:\[,\s])(-?Infinity|NaN)(?=[\s,\}\]])", RegexOptions.Compiled);

    /// <summary>
    /// Normaliza tokens <c>NaN</c>/<c>Infinity</c>/<c>-Infinity</c> emitidos por el
    /// motor Python a <c>null</c>, de modo que el parser JSON estándar los acepte.
    /// No modifica valores dentro de cadenas de texto.
    /// </summary>
    internal static string NormalizarNaN(string json) => _reNaN.Replace(json, "null");

    private static readonly JsonSerializerOptions _opcionesMotor = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>Parsea el JSON de resultados del motor a un <see cref="ResultadoMotorLosa"/>.
    /// Soporta tokens <c>NaN</c>/<c>Infinity</c> que el motor emite cuando una
    /// sección es insuficiente; en ese caso la franja queda marcada con
    /// <see cref="FranjaMotor.SeccionInsuficiente"/> = <see langword="true"/> sin
    /// lanzar excepción.</summary>
    public static ResultadoMotorLosa ParsearResultado(string json)
    {
        // Pre-normalizar: NaN/Infinity → null para que el parser JSON estándar no falle.
        string jsonNormalizado = NormalizarNaN(json);

        var dto = JsonSerializer.Deserialize<ResultadoMotorDto>(jsonNormalizado, _opcionesMotor)
            ?? throw new InvalidOperationException("El motor devolvió JSON vacío o nulo.");

        return new ResultadoMotorLosa
        {
            WCentral  = dto.WCentral  ?? double.NaN,
            MxMax     = dto.MxMax     ?? double.NaN,
            MyMax     = dto.MyMax     ?? double.NaN,
            MApoyoMax = dto.MApoyoMax ?? 0.0,
            FranjaX   = MapearFranja(dto.FranjaX),
            FranjaY   = MapearFranja(dto.FranjaY),
            FranjaApoyo = dto.FranjaApoyo is not null ? MapearFranja(dto.FranjaApoyo) : null,
        };
    }

    private static FranjaMotor MapearFranja(FranjaMotorDto? f)
    {
        if (f is null) return new FranjaMotor { SeccionInsuficiente = true };
        // Insuficiente si el motor lo declaró explícitamente O si llegaron NaN (null tras normalizar).
        bool insuf = f.SeccionInsuficiente || f.AsRequerido is null || f.AsDiseno is null;
        return new FranjaMotor
        {
            AsRequerido       = f.AsRequerido ?? double.NaN,
            AsDiseno          = f.AsDiseno    ?? double.NaN,
            Disponer          = insuf ? "SECCIÓN INSUFICIENTE" : (f.Disponer ?? ""),
            Cumple            = !insuf && f.Cumple,
            SeccionInsuficiente = insuf,
        };
    }

    // ── DTOs internos para deserialización del JSON del motor ──────────────────
    // Los campos numéricos son nullable para distinguir null (era NaN) de 0.

    private sealed class ResultadoMotorDto
    {
        [JsonPropertyName("w_central")]    public double? WCentral  { get; set; }
        [JsonPropertyName("mx_max")]       public double? MxMax     { get; set; }
        [JsonPropertyName("my_max")]       public double? MyMax     { get; set; }
        [JsonPropertyName("m_apoyo_max")]  public double? MApoyoMax { get; set; }
        [JsonPropertyName("franja_x")]     public FranjaMotorDto? FranjaX   { get; set; }
        [JsonPropertyName("franja_y")]     public FranjaMotorDto? FranjaY   { get; set; }
        [JsonPropertyName("franja_apoyo")] public FranjaMotorDto? FranjaApoyo { get; set; }
    }

    private sealed class FranjaMotorDto
    {
        [JsonPropertyName("as_requerido")]        public double? AsRequerido { get; set; }
        [JsonPropertyName("as_minimo")]           public double? AsMinimo    { get; set; }
        [JsonPropertyName("as_diseno")]           public double? AsDiseno    { get; set; }
        [JsonPropertyName("seccion_insuficiente")] public bool   SeccionInsuficiente { get; set; }
        [JsonPropertyName("cumple")]              public bool    Cumple      { get; set; }
        [JsonPropertyName("disponer")]            public string? Disponer    { get; set; }
        [JsonPropertyName("numero_barra")]        public int     NumeroBarra { get; set; }
        [JsonPropertyName("espaciamiento")]       public double? Espaciamiento { get; set; }
        [JsonPropertyName("as_provista")]         public double? AsProvista  { get; set; }
        [JsonPropertyName("gobierna_minimo")]     public bool    GobiernaMinimo { get; set; }
    }

    /// <summary>
    /// Invoca el motor (<paramref name="comando"/>, p. ej. <c>motor-fea</c> o
    /// <c>python3 -m motor_fea.api.cli</c>) para diseñar la losa. Devuelve el
    /// resultado parseado, o lanza con un mensaje claro si el motor no está
    /// disponible (no rompe el flujo <c>Losas.exe</c>).
    /// </summary>
    public static async Task<ResultadoMotorLosa> DisenarLosaAsync(
        Losa losa, Sistema sistema, string comando = "motor-fea", int nx = MallaDefault, int ny = MallaDefault,
        string borde = "simple")
    {
        string json = ParametrosAJson(ConstruirParametros(losa, sistema, nx, ny, borde));

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

        // MSx/MSy por dirección. El contrato del motor expone un único escalar
        // m_apoyo_max (el FEM toma el máximo de |M| sobre TODO el contorno; ver
        // motor-fea/core/losa_fem.py — no hay momento de apoyo por dirección).
        // En vez de copiar el mismo valor en ambas direcciones (lo previo), se
        // reparte según la continuidad que el Tipo codifica:
        //   • MSx (acero de la franja X) ⇐ continuidad de los bordes E/W (perpendiculares a Lx).
        //   • MSy (acero de la franja Y) ⇐ continuidad de los bordes N/S (perpendiculares a Ly).
        // Una dirección sin borde continuo queda simplemente apoyada ⇒ MS = 0.
        double mApoyo = r.MApoyoMax * NM_a_TonM;
        var (contX, contY) = ContinuidadPorDireccion(losa.Tipo);
        losa.MSx = contX ? mApoyo : 0.0;
        losa.MSy = contY ? mApoyo : 0.0;
    }

    /// <summary>
    /// Determina, a partir del <paramref name="tipo"/> de losa, qué direcciones
    /// tienen continuidad (borde empotrado) y por tanto momento de apoyo:
    /// <list type="bullet">
    ///   <item><c>contX</c> — hay continuidad en algún borde Este/Oeste
    ///   (índices 1/3, perpendiculares a Lx): gobierna <c>MSx</c>.</item>
    ///   <item><c>contY</c> — hay continuidad en algún borde Norte/Sur
    ///   (índices 0/2, perpendiculares a Ly): gobierna <c>MSy</c>.</item>
    /// </list>
    /// Si el tipo no está en el catálogo, no se asume continuidad en ninguna
    /// dirección (ambos <see langword="false"/>; degradación segura).
    /// </summary>
    public static (bool contX, bool contY) ContinuidadPorDireccion(int tipo)
    {
        if (!TipoLosa.Catalogo.TryGetValue(TipoLosa.NormalizarCodigo(tipo), out var t))
            return (false, false);
        var b = t.Bordes;   // [N, E, S, W]
        bool contX = b[1] == BorderKind.Empotrado || b[3] == BorderKind.Empotrado;  // E/W ⟂ Lx
        bool contY = b[0] == BorderKind.Empotrado || b[2] == BorderKind.Empotrado;  // N/S ⟂ Ly
        return (contX, contY);
    }

    /// <summary>
    /// Calcula los momentos de TODAS las losas del sistema con el motor (alternativa
    /// aditiva a <c>Losas.exe</c>) y los aplica. No toca el flujo <c>Losas.exe</c>.
    /// <para>
    /// <b>Resilencia por lote:</b> si una losa individual falla (sección insuficiente,
    /// NaN en el resultado, o error del motor), se registra el error de diagnóstico y
    /// el lazo continúa con las demás losas — una losa no aborta el lote completo.
    /// </para>
    /// </summary>
    public static async Task CalcularSistemaConMotorAsync(
        Sistema sistema, string comando = "motor-fea", int nx = MallaDefault, int ny = MallaDefault)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        foreach (var losa in sistema.Losas)
        {
            ResultadoMotorLosa r;
            try
            {
                // Borde por Tipo: la continuidad que el caso Pieper-Martens codifica
                // (≥1 borde empotrado ⇒ "empotrado") en vez de asumir todo simple.
                r = await DisenarLosaAsync(losa, sistema, comando, nx, ny, BordeDesdeTipo(losa.Tipo));
            }
            catch (Exception ex)
            {
                // Losa insuficiente u otro error del motor: registrar y continuar.
                System.Diagnostics.Debug.WriteLine(
                    $"[MotorFEA] Losa {losa.Id} omitida en el lote: {ex.Message}");
                continue;
            }

            // Si el motor devolvió NaN (franja insuficiente) tampoco aplicamos momentos
            // non-finitos — la losa queda sin momentos del motor para ese ciclo.
            if (r.FranjaX.SeccionInsuficiente || r.FranjaY.SeccionInsuficiente)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MotorFEA] Losa {losa.Id}: sección insuficiente — momentos no aplicados.");
                continue;
            }

            AplicarMomentos(losa, r);
        }
    }

    /// <summary>
    /// Aplica un momento de continuidad/apoyo (<paramref name="muTonM"/>, ton·m/m) a un
    /// <see cref="BordeAdic"/>: setea <c>MuIJ</c> y diseña el acero de apoyo (D, AsReq,
    /// Disponer) con <see cref="AcerosLosaDesigner"/>, usando la geometría de
    /// <paramref name="losa"/> y los materiales del <paramref name="sistema"/>. Estos
    /// "aceros adicionales" hoy vienen del <c>.TXT</c> de <c>Losas.exe</c>; ésta es la
    /// ruta nativa (aproximación: panel empotrado, no la continuidad real de dos paneles).
    /// </summary>
    public static void AplicarMomentoBorde(
        BordeAdic borde, double muTonM, Losa losa, Sistema sistema, int numeroBarra = 4)
    {
        if (borde is null) throw new ArgumentNullException(nameof(borde));
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));

        double hCm = losa.Espesor * 100.0;
        double recCm = (losa.Rec > 0 ? losa.Rec : 0.025) * 100.0;        // m → cm
        double db = AcerosLosaDesigner.DiametroBarraCm(numeroBarra);
        double dCm = AcerosLosaDesigner.CantoUtil(hCm, recCm, db, capa: 1);  // apoyo = capa superior
        var d = AcerosLosaDesigner.DisenarFranja("apoyo", muTonM, hCm, dCm, sistema.Fc, sistema.Fy);

        borde.MuIJ = muTonM;
        borde.D = dCm;
        borde.AsReq = d.AsRequeridoCm2M;
        borde.Disponer = d.Disponer;
    }

    /// <summary>
    /// Calcula los "aceros adicionales" (bordes de apoyo/continuidad) con el motor:
    /// corre la primera losa con borde <b>empotrado</b> para obtener el momento de
    /// apoyo y lo aplica a todos los <c>BordesX</c>/<c>BordesY</c> existentes. Es una
    /// <b>aproximación</b> (momento representativo, panel empotrado — no la continuidad
    /// real de dos paneles) y <b>no</b> regenera la topología de bordes. Aditivo.
    /// </summary>
    public static async Task CalcularBordesConMotorAsync(
        Sistema sistema, string comando = "motor-fea", int nx = MallaDefault, int ny = MallaDefault)
    {
        if (sistema is null) throw new ArgumentNullException(nameof(sistema));
        if (sistema.Losas.Count == 0) return;
        if (sistema.BordesX.Count == 0 && sistema.BordesY.Count == 0) return;

        var losaRep = sistema.Losas[0];
        var r = await DisenarLosaAsync(losaRep, sistema, comando, nx, ny, borde: "empotrado");
        double muApoyoTonM = Math.Abs(r.MApoyoMax) * NM_a_TonM;

        foreach (var b in sistema.BordesX) AplicarMomentoBorde(b, muApoyoTonM, losaRep, sistema);
        foreach (var b in sistema.BordesY) AplicarMomentoBorde(b, muApoyoTonM, losaRep, sistema);
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
    /// <summary>
    /// <see langword="true"/> cuando el motor indicó que la sección es insuficiente
    /// (espesor demasiado pequeño) o cuando los valores numéricos llegaron como
    /// <c>NaN</c>/<c>Infinity</c>. El resto de los campos puede contener
    /// <see cref="double.NaN"/> en ese caso — no lanzará excepción.
    /// </summary>
    public bool SeccionInsuficiente { get; set; }
}
