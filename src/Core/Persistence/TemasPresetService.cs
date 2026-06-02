using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Un preset de apariencia con nombre — combinación nombrada de tema, tipografía,
/// densidad y color acento. El usuario puede guardar varias combinaciones (ej.
/// "Estructural oscuro", "Daylight oficina", "Cliente Cisneros") y alternar entre
/// ellas sin reconfigurar cada cosa.
/// </summary>
public class TemaPreset
{
    /// <summary>Nombre legible del preset (clave de búsqueda — case-insensitive).</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Snapshot de la configuración de apariencia al momento de guardar.</summary>
    public AparienciaConfig Apariencia { get; set; } = new();

    /// <summary>Fecha de creación / última actualización (UTC).</summary>
    public DateTime ModificadoUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persistencia de presets de apariencia en <c>%APPDATA%/LosasPlus/themes.json</c>
/// (por usuario, según decisión del usuario para LosasPlus). Lista de
/// <see cref="TemaPreset"/> con CRUD básico: LoadAll, SaveAll, AddOrUpdate, Remove.
///
/// <para>
/// El servicio es resiliente: si el archivo no existe o está corrupto, devuelve
/// lista vacía sin lanzar. Cualquier operación que muta la lista guarda el JSON
/// completo (no append) para mantener atomicidad simple.
/// </para>
/// </summary>
public static class TemasPresetService
{
    /// <summary>Override del path para tests aislados.</summary>
    public static string? PathOverride { get; set; }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LosasPlus",
            "themes.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    /// <summary>Carga la lista de presets desde disco. Devuelve [] si no existe o está corrupto.</summary>
    public static List<TemaPreset> LoadAll()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new List<TemaPreset>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<TemaPreset>>(json, JsonConfigHelper.DefaultOptions) ?? new List<TemaPreset>();
        }
        catch
        {
            return new List<TemaPreset>();
        }
    }

    /// <summary>Guarda la lista completa (sobreescribe). Crea el directorio si no existe.</summary>
    public static void SaveAll(List<TemaPreset> presets)
    {
        if (presets is null) throw new ArgumentNullException(nameof(presets));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(presets, JsonConfigHelper.DefaultOptions));
    }

    /// <summary>
    /// Agrega un preset o lo reemplaza si ya existe uno con el mismo nombre
    /// (case-insensitive). Actualiza <see cref="TemaPreset.ModificadoUtc"/>.
    /// </summary>
    public static void AddOrUpdate(string nombre, AparienciaConfig apariencia)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("Nombre del preset no puede estar vacío.", nameof(nombre));
        if (apariencia is null) throw new ArgumentNullException(nameof(apariencia));

        var presets = LoadAll();
        var existente = presets.FirstOrDefault(p =>
            string.Equals(p.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        if (existente is not null)
        {
            existente.Apariencia    = Clone(apariencia);
            existente.ModificadoUtc = DateTime.UtcNow;
        }
        else
        {
            presets.Add(new TemaPreset
            {
                Nombre        = nombre.Trim(),
                Apariencia    = Clone(apariencia),
                ModificadoUtc = DateTime.UtcNow,
            });
        }
        SaveAll(presets);
    }

    /// <summary>
    /// Elimina el preset con el nombre dado (case-insensitive). Si no existe,
    /// no lanza — devuelve false silenciosamente.
    /// </summary>
    public static bool Remove(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return false;
        var presets = LoadAll();
        var removed = presets.RemoveAll(p =>
            string.Equals(p.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) SaveAll(presets);
        return removed > 0;
    }

    /// <summary>Busca un preset por nombre (case-insensitive). Null si no existe.</summary>
    public static TemaPreset? Get(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        return LoadAll().FirstOrDefault(p =>
            string.Equals(p.Nombre, nombre, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Borra todos los presets (operación de mantenimiento, no expuesta en UI normal).</summary>
    public static void Reset()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }

    private static AparienciaConfig Clone(AparienciaConfig src) => new()
    {
        Tema            = src.Tema,
        TipografiaDatos = src.TipografiaDatos,
        Densidad        = src.Densidad,
        ColorAcentoHex  = src.ColorAcentoHex,
    };
}
