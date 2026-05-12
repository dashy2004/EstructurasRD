using System;
using System.IO;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Perfil del ingeniero — reutilizable entre proyectos. Persiste en
/// <c>%APPDATA%/MemoriaPlus/perfil.json</c>. Cuando el usuario crea un
/// "Nuevo proyecto", la app precarga los campos del proyecto con este
/// perfil para que no tenga que reescribir nombre, CODIA, teléfonos, etc.
/// en cada memoria.
/// </summary>
public class PerfilIngeniero
{
    // IDENTIDAD
    public string Nombre        { get; set; } = "";
    public string Codia         { get; set; } = "";
    public string Especialidad  { get; set; } = "Estructural";

    // CONTACTO
    public string TelefonoFijo    { get; set; } = "";
    public string TelefonoCelular { get; set; } = "";
    public string Email           { get; set; } = "";
    public string Ciudad          { get; set; } = "";

    // FIRMA Y SELLO (paths absolutos a las imágenes en disco)
    public string FirmaPath { get; set; } = "";
    public string SelloPath { get; set; } = "";

    // FORMACIÓN (opcional, colapsable en UI)
    public string Universidad       { get; set; } = "";
    public string AnoGraduacion     { get; set; } = "";
    public string PostGrado         { get; set; } = "";
}

/// <summary>
/// Servicio de persistencia para <see cref="PerfilIngeniero"/>. API estática
/// (un solo perfil por usuario de Windows). Resiliente: si el archivo no
/// existe o está corrupto, devuelve un perfil vacío en lugar de lanzar.
/// </summary>
public static class PerfilIngenieroService
{
    /// <summary>Para tests: override del path. <c>null</c> = default <c>%APPDATA%</c>.</summary>
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
            "perfil.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    /// <summary>
    /// Carga el perfil del disco. Devuelve un <see cref="PerfilIngeniero"/>
    /// vacío si el archivo no existe o está corrupto.
    /// </summary>
    public static PerfilIngeniero Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new PerfilIngeniero();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PerfilIngeniero>(json, _opts) ?? new PerfilIngeniero();
        }
        catch
        {
            return new PerfilIngeniero();
        }
    }

    /// <summary>Guarda el perfil en disco. Crea el directorio si no existe.</summary>
    public static void Save(PerfilIngeniero perfil)
    {
        if (perfil is null) throw new ArgumentNullException(nameof(perfil));
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(perfil, _opts);
        File.WriteAllText(path, json);
    }

    /// <summary>True si existe un perfil guardado en disco.</summary>
    public static bool Existe() => File.Exists(ResolvedPath);

    /// <summary>Borra el perfil guardado.</summary>
    public static void Clear()
    {
        if (File.Exists(ResolvedPath))
            try { File.Delete(ResolvedPath); } catch { }
    }
}
