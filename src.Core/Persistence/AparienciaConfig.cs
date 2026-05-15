using System;
using System.IO;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Preferencias de apariencia de la app (tema, tipografía de datos, densidad, color acento).
/// Persiste en <c>%APPDATA%/MemoriaPlus/apariencia.json</c>. La UI lee estos
/// valores al arrancar y los aplica al ResourceDictionary global.
/// </summary>
public class AparienciaConfig
{
    /// <summary>"Claro" (default) o "Oscuro".</summary>
    public string Tema { get; set; } = "Claro";

    /// <summary>"JetBrains Mono" (default), "Iosevka", o "Consolas".</summary>
    public string TipografiaDatos { get; set; } = "JetBrains Mono";

    /// <summary>"Compacto" (24 px), "Medio" (28 px, default), "Cómodo" (32 px).</summary>
    public string Densidad { get; set; } = "Medio";

    /// <summary>
    /// Color de acento personalizado en formato hex <c>#RRGGBB</c> o <c>#AARRGGBB</c>.
    /// Vacío = usar el PrimaryBrush default del tema. Cuando está set, sobreescribe
    /// el PrimaryBrush (y AccentBrush) del ResourceDictionary global.
    /// </summary>
    public string ColorAcentoHex { get; set; } = "";

    /// <summary>Devuelve la altura de fila correspondiente a <see cref="Densidad"/>.</summary>
    public double RowHeight => Densidad switch
    {
        "Compacto" => 24,
        "Cómodo"   => 32,
        _          => 28,   // Medio default
    };

    /// <summary>True si el tema actual es oscuro.</summary>
    public bool EsTemaOscuro => string.Equals(Tema, "Oscuro", StringComparison.OrdinalIgnoreCase);

    /// <summary>True si hay un color de acento personalizado configurado.</summary>
    public bool TieneColorAcentoCustom =>
        !string.IsNullOrWhiteSpace(ColorAcentoHex) && ColorAcentoHex.StartsWith("#");
}

/// <summary>
/// Servicio de persistencia para <see cref="AparienciaConfig"/>. Misma estrategia
/// que <see cref="PerfilIngenieroService"/>: API estática + override de path
/// para tests + resiliente a archivos faltantes/corruptos.
/// </summary>
public static class AparienciaService
{
    public static string? PathOverride { get; set; }

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "apariencia.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    public static AparienciaConfig Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new AparienciaConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AparienciaConfig>(json, _opts) ?? new AparienciaConfig();
        }
        catch
        {
            return new AparienciaConfig();
        }
    }

    public static void Save(AparienciaConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, _opts));
    }

    public static void Reset()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }
}
