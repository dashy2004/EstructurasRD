using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LosasPlus.Persistence;

/// <summary>
/// Una plantilla de documento <c>.docx</c> registrada en la app. Se identifica
/// por un Id estable (Guid) — útil para marcar la predeterminada sin depender
/// del path (que puede mover el usuario).
/// </summary>
public sealed class PlantillaEntry
{
    /// <summary>Id estable. Asignado al crear la entrada; nunca cambia.</summary>
    public string Id { get; set; } = "";

    /// <summary>Nombre legible que ve el usuario (ej. "Memoria Estándar ACI 318-05").</summary>
    public string Nombre { get; set; } = "";

    /// <summary>Path absoluto al <c>.docx</c>. La app no copia el archivo; sólo lo referencia.</summary>
    public string Path { get; set; } = "";

    /// <summary>Última fecha en que el usuario importó / actualizó esta entrada.</summary>
    public DateTime Actualizado { get; set; }

    /// <summary>
    /// True si esta plantilla es la default usada al generar memorias. Sólo
    /// una entrada puede tener este flag en true; <see cref="PlantillaRegistry.MarcarComoPredeterminada"/>
    /// limpia las demás.
    /// </summary>
    public bool EsPredeterminada { get; set; }
}

/// <summary>
/// Registry persistente de plantillas <c>.docx</c> disponibles para generar
/// memorias. Misma filosofía que <see cref="ProyectoRegistry"/>: API estática,
/// archivo JSON en <c>%APPDATA%/MemoriaPlus/plantillas.json</c>, tolerante a
/// archivos corruptos o paths que ya no existen.
///
/// <para>
/// La plantilla bundleada con la app
/// (<c>Resources/templates/Memoria_Losas_PLANTILLA.docx</c>) se registra
/// automáticamente como predeterminada al primer arranque vía
/// <see cref="BootstrapIfNeeded"/> — no hace falta que el usuario "importe"
/// nada para que el flujo de Generar funcione.
/// </para>
/// </summary>
public static class PlantillaRegistry
{
    /// <summary>Para tests: override del path. <c>null</c> = usar default <c>%APPDATA%</c>.</summary>
    public static string? PathOverride { get; set; }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MemoriaPlus",
            "plantillas.json");

    private static string ResolvedPath => PathOverride ?? DefaultPath;

    /// <summary>
    /// Carga todas las plantillas registradas. Si el archivo no existe o está
    /// corrupto, devuelve lista vacía. Filtra entries cuyo <c>.docx</c> ya no
    /// existe en disco (para no listar plantillas rotas en la UI).
    /// </summary>
    public static List<PlantillaEntry> Load()
    {
        var path = ResolvedPath;
        if (!File.Exists(path)) return new List<PlantillaEntry>();

        try
        {
            var json = File.ReadAllText(path);
            var entries = JsonSerializer.Deserialize<List<PlantillaEntry>>(json, JsonConfigHelper.DefaultOptions) ?? new();
            entries.RemoveAll(e => string.IsNullOrEmpty(e.Path) || !File.Exists(e.Path));
            return entries;
        }
        catch
        {
            return new List<PlantillaEntry>();
        }
    }

    /// <summary>
    /// Agrega una plantilla nueva al registry. Si el path ya existe (match
    /// case-insensitive), actualiza nombre + timestamp en lugar de duplicar.
    /// La nueva entrada NO se marca como predeterminada automáticamente —
    /// usar <see cref="MarcarComoPredeterminada"/> si así se desea.
    /// </summary>
    public static PlantillaEntry AgregarDesdeArchivo(string filePath, string nombre)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath vacío", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("Plantilla no encontrada", filePath);

        var entries = Load();
        var normalized = Path.GetFullPath(filePath);
        var existing = entries.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.Path), normalized, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Nombre = string.IsNullOrWhiteSpace(nombre) ? existing.Nombre : nombre;
            existing.Actualizado = DateTime.UtcNow;
            Save(entries);
            return existing;
        }

        var entry = new PlantillaEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Nombre = string.IsNullOrWhiteSpace(nombre)
                ? Path.GetFileNameWithoutExtension(filePath)
                : nombre,
            Path = normalized,
            Actualizado = DateTime.UtcNow,
            EsPredeterminada = false,
        };
        entries.Add(entry);
        Save(entries);
        return entry;
    }

    /// <summary>
    /// Marca <paramref name="id"/> como predeterminada y limpia el flag en las
    /// demás entradas (garantiza unicidad). No-op si el id no existe.
    /// </summary>
    public static void MarcarComoPredeterminada(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var entries = Load();
        var target = entries.FirstOrDefault(e => e.Id == id);
        if (target is null) return;
        foreach (var e in entries) e.EsPredeterminada = false;
        target.EsPredeterminada = true;
        Save(entries);
    }

    /// <summary>
    /// Elimina la plantilla por id. Si era la predeterminada, no promueve
    /// automáticamente otra — la app caerá al bootstrap default al próximo
    /// arranque (o el usuario marcará una manualmente).
    /// </summary>
    public static void Quitar(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var entries = Load();
        var antes = entries.Count;
        entries.RemoveAll(e => e.Id == id);
        if (entries.Count != antes) Save(entries);
    }

    /// <summary>
    /// Devuelve la plantilla predeterminada o, si no hay flag set, la
    /// primera del registry. Null cuando el registry está vacío.
    /// </summary>
    public static PlantillaEntry? GetPredeterminada()
    {
        var entries = Load();
        return entries.FirstOrDefault(e => e.EsPredeterminada) ?? entries.FirstOrDefault();
    }

    /// <summary>
    /// Si el registry está vacío y existe la plantilla bundleada en
    /// <paramref name="plantillaBundledPath"/>, la registra como
    /// predeterminada con nombre "Memoria Estándar ACI 318-05". Idempotente:
    /// no hace nada si ya hay entradas o el archivo no existe.
    /// </summary>
    /// <returns>True si bootstrapió, false si no fue necesario.</returns>
    public static bool BootstrapIfNeeded(string plantillaBundledPath, string nombre = "Memoria Estándar ACI 318-05")
    {
        if (string.IsNullOrWhiteSpace(plantillaBundledPath)) return false;
        if (!File.Exists(plantillaBundledPath)) return false;
        var entries = Load();
        if (entries.Count > 0) return false;
        var entry = AgregarDesdeArchivo(plantillaBundledPath, nombre);
        MarcarComoPredeterminada(entry.Id);
        return true;
    }

    /// <summary>Vacía el registry — útil para tests.</summary>
    public static void Clear()
    {
        if (File.Exists(ResolvedPath))
        {
            try { File.Delete(ResolvedPath); } catch { /* ignorar */ }
        }
    }

    private static void Save(List<PlantillaEntry> entries)
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
            // Failure no es bloqueante.
        }
    }
}
