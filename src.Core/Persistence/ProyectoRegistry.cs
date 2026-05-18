using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Registro persistente de proyectos abiertos recientemente. Se guarda en
/// <c>%APPDATA%/MemoriaPlus/recents.json</c> (Windows) — independiente del
/// proyecto activo, vive entre sesiones.
///
/// <para>
/// API estática y thread-safe (cada llamada relee/reescribe el archivo). Las
/// entradas se ordenan por <see cref="RecentEntry.UltimoAccesoUtc"/> descendente
/// y se limita a <see cref="MaxEntries"/> = 12 (los más recientes ganan).
/// </para>
/// </summary>
public static class ProyectoRegistry
{
    /// <summary>Cantidad máxima de entradas mantenidas en el registry.</summary>
    public const int MaxEntries = 12;

    /// <summary>Para tests: override del path. <c>null</c> = usar default <c>%APPDATA%</c>.</summary>
    public static string? PathOverride { get; set; }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "recents.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    /// <summary>Lee todas las entradas del registry. Devuelve lista vacía si el archivo no existe.</summary>
    public static List<RecentEntry> Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new List<RecentEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<RecentEntry>>(json, JsonConfigHelper.DefaultOptions) ?? new();
            // Filtra entries cuyo archivo ya no exista en disco.
            entries.RemoveAll(e => !File.Exists(e.Path));
            return entries.OrderByDescending(e => e.UltimoAccesoUtc).Take(MaxEntries).ToList();
        }
        catch
        {
            // Archivo corrupto — empezar limpio. No queremos crashear la app por
            // un registry malo.
            return new List<RecentEntry>();
        }
    }

    /// <summary>
    /// Agrega o actualiza una entrada del registry. Si el path ya existe,
    /// actualiza la metadata y el timestamp. Después poda a <see cref="MaxEntries"/>.
    /// </summary>
    public static void AddOrUpdate(string projectPath, string nombreProyecto, string ingeniero, string codia, int cantidadNiveles)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;

        var entries = Load();
        // Match por path normalizado (case-insensitive en Windows).
        var normalizedPath = Path.GetFullPath(projectPath);
        var existing = entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.NombreProyecto  = nombreProyecto;
            existing.Ingeniero       = ingeniero;
            existing.Codia           = codia;
            existing.CantidadNiveles = cantidadNiveles;
            existing.UltimoAccesoUtc = DateTime.UtcNow;
        }
        else
        {
            entries.Insert(0, new RecentEntry
            {
                Path            = normalizedPath,
                NombreProyecto  = nombreProyecto,
                Ingeniero       = ingeniero,
                Codia           = codia,
                CantidadNiveles = cantidadNiveles,
                UltimoAccesoUtc = DateTime.UtcNow,
            });
        }

        entries = entries.OrderByDescending(e => e.UltimoAccesoUtc).Take(MaxEntries).ToList();
        Save(entries);
    }

    /// <summary>Elimina una entrada por path. No falla si no existe.</summary>
    public static void Remove(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath)) return;
        var entries = Load();
        var normalizedPath = Path.GetFullPath(projectPath);
        entries.RemoveAll(e =>
            string.Equals(Path.GetFullPath(e.Path), normalizedPath, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }

    /// <summary>Vacía el registry completo.</summary>
    public static void Clear()
    {
        if (File.Exists(ResolvedPath))
        {
            try { File.Delete(ResolvedPath); } catch { /* ignorar */ }
        }
    }

    private static void Save(List<RecentEntry> entries)
    {
        var path = ResolvedPath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            var json = JsonSerializer.Serialize(entries, JsonConfigHelper.DefaultOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Failure to write el registry no es bloqueante; el usuario puede
            // seguir trabajando aunque se pierdan los "recents".
        }
    }
}

/// <summary>Una entrada en el registry de proyectos recientes.</summary>
public sealed class RecentEntry
{
    public string Path             { get; set; } = "";
    public string NombreProyecto   { get; set; } = "";
    public string Ingeniero        { get; set; } = "";
    public string Codia            { get; set; } = "";
    public int    CantidadNiveles  { get; set; }
    public DateTime UltimoAccesoUtc { get; set; }
}
