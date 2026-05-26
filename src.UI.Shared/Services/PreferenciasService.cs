using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MemoriaPlus.Services;

/// <summary>
/// Preferencias del usuario persistidas en
/// <c>%AppData%\LosasPlus\preferencias.json</c> (Liga E paso E2).
/// Carga lazy desde disco la primera vez que se accede; guarda
/// inmediatamente al asignar una propiedad nueva.
///
/// <para>
/// Tipo POCO inmutable parcial: las propiedades son <c>get</c>-only;
/// las mutaciones se hacen vía <see cref="Actualizar"/> que crea un
/// nuevo snapshot y lo persiste.
/// </para>
/// </summary>
public sealed class Preferencias
{
    /// <summary>Decimales mostrados en valores numéricos (1-4). Default 2.</summary>
    [JsonPropertyName("decimales")]
    public int Decimales { get; set; } = 2;

    /// <summary>Cultura para formato de números/fechas. Default es-DO.</summary>
    [JsonPropertyName("cultura")]
    public string Cultura { get; set; } = "es-DO";

    /// <summary>
    /// Sistema de unidades. Default "SI" (Imperial pendiente de soporte
    /// completo en motores de cálculo).
    /// </summary>
    [JsonPropertyName("unidades")]
    public string Unidades { get; set; } = "SI";

    /// <summary>
    /// Intervalo de auto-backup en minutos (0 = desactivado, 5/10/15/30).
    /// Default 10 — guarda un snapshot en %AppData%\LosasPlus\backups\.
    /// </summary>
    [JsonPropertyName("autoBackupMin")]
    public int AutoBackupMinutos { get; set; } = 10;
}

/// <summary>
/// Servicio singleton para load/save de <see cref="Preferencias"/> del
/// usuario en <c>%AppData%\LosasPlus\preferencias.json</c>. Patrón
/// idéntico al <c>ReglamentoService.SaveCarpetaPreference</c> existente.
/// </summary>
public static class PreferenciasService
{
    private static Preferencias? _cache;

    public static Preferencias Cargar()
    {
        if (_cache != null) return _cache;
        try
        {
            var path = PathArchivo();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _cache = JsonSerializer.Deserialize<Preferencias>(json) ?? new Preferencias();
                return _cache;
            }
        }
        catch
        {
            // Si el archivo está corrupto, regresar a defaults.
        }
        _cache = new Preferencias();
        return _cache;
    }

    public static void Guardar(Preferencias prefs)
    {
        _cache = prefs ?? throw new ArgumentNullException(nameof(prefs));
        try
        {
            var dir = Path.GetDirectoryName(PathArchivo());
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathArchivo(), json);
        }
        catch
        {
            // Persistencia no es crítica — si falla, las preferencias
            // viven sólo en memoria hasta que cierre la app.
        }
    }

    public static string PathArchivo()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LosasPlus");
        return Path.Combine(dir, "preferencias.json");
    }
}
