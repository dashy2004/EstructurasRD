using System;
using System.IO;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Preferencias de cálculo/locale de la app (H4 — convergencia v0.8.1): cantidad de
/// decimales, cultura de formato numérico, sistema de unidades y el intervalo de
/// auto-backup. Persiste en <c>%APPDATA%/MemoriaPlus/preferencias.json</c>.
/// La aplicación en runtime (formateo global, disparo del backup) se cablea aparte;
/// este modelo + su servicio sólo persisten la elección del usuario.
/// </summary>
public class PreferenciasConfig
{
    /// <summary>Decimales para mostrar magnitudes (1–4; default 2).</summary>
    public int Decimales { get; set; } = 2;

    /// <summary>Cultura de formato: "es-DO" (default), "es-ES" o "en-US".</summary>
    public string Cultura { get; set; } = "es-DO";

    /// <summary>Sistema de unidades. "SI" (Sistema Internacional) por ahora.</summary>
    public string SistemaUnidades { get; set; } = "SI";

    /// <summary>Intervalo de auto-backup en minutos. 0 = desactivado (default).</summary>
    public int AutoBackupMinutos { get; set; }

    /// <summary>True si el auto-backup está activado (intervalo &gt; 0).</summary>
    public bool AutoBackupActivo => AutoBackupMinutos > 0;

    /// <summary>Decimales saneados al rango válido [1,4] para usar en formateo.</summary>
    public int DecimalesValidos => Math.Clamp(Decimales, 1, 4);
}

/// <summary>
/// Servicio de persistencia para <see cref="PreferenciasConfig"/>. Mismo patrón que
/// <see cref="AparienciaService"/>: API estática + override de path para tests +
/// resiliente a archivos faltantes/corruptos.
/// </summary>
public static class PreferenciasService
{
    public static string? PathOverride { get; set; }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "preferencias.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    public static PreferenciasConfig Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new PreferenciasConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PreferenciasConfig>(json, JsonConfigHelper.DefaultOptions) ?? new PreferenciasConfig();
        }
        catch
        {
            return new PreferenciasConfig();
        }
    }

    public static void Save(PreferenciasConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, JsonConfigHelper.DefaultOptions));
    }

    public static void Reset()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }
}
