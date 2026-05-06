using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LosasPlus.Services;

public sealed class Norma
{
    [JsonPropertyName("codigo")]   public string Codigo   { get; set; } = "";
    [JsonPropertyName("titulo")]   public string Titulo   { get; set; } = "";
    [JsonPropertyName("edicion")]  public string Edicion  { get; set; } = "";
    [JsonPropertyName("ambito")]   public string Ambito   { get; set; } = "";
    [JsonPropertyName("url")]      public string Url      { get; set; } = "";
    [JsonPropertyName("resumen")]  public string Resumen  { get; set; } = "";
    [JsonPropertyName("archivo")]  public string? Archivo { get; set; }
    [JsonPropertyName("secciones_relevantes")] public List<string> Secciones { get; set; } = new();
    [JsonPropertyName("aplicacion_losas")] public string? AplicacionLosas { get; set; }
}

public sealed class ReglamentoMeta
{
    [JsonPropertyName("carpeta_pdfs_default")] public string? CarpetaPdfsDefault { get; set; }
}

public static class ReglamentoService
{
    private static List<Norma>? _cache;
    private static ReglamentoMeta? _meta;

    public static IReadOnlyList<Norma> Load()
    {
        if (_cache != null) return _cache;
        var json = ReadResourceJson();
        (_cache, _meta) = ParseJson(json);
        return _cache;
    }

    public static ReglamentoMeta Meta
    {
        get
        {
            if (_meta is null) Load();
            return _meta ?? new ReglamentoMeta();
        }
    }

    /// <summary>
    /// Resuelve la carpeta donde están los PDFs de los reglamentos. Orden:
    /// 1) preferencia guardada del usuario (<c>%AppData%\LosasPlus\reglamentos_path.txt</c>)
    /// 2) carpeta_pdfs_default del JSON con <c>%USERPROFILE%</c>, <c>%APPDATA%</c>, etc. expandidas
    /// 3) cadena vacía (no resuelta — el botón "Abrir PDF" se deshabilita).
    /// </summary>
    public static string ResolveCarpetaPdfs()
    {
        var saved = LoadCarpetaPreference();
        if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved)) return saved;

        var fromMeta = Meta.CarpetaPdfsDefault;
        if (!string.IsNullOrWhiteSpace(fromMeta))
        {
            var expanded = Environment.ExpandEnvironmentVariables(fromMeta);
            if (Directory.Exists(expanded)) return expanded;
        }
        return saved ?? "";  // devuelve lo guardado (aunque no exista) para que el usuario lo edite
    }

    public static string? PdfPathFor(Norma n)
    {
        if (string.IsNullOrWhiteSpace(n.Archivo)) return null;
        var dir = ResolveCarpetaPdfs();
        if (string.IsNullOrEmpty(dir)) return null;
        var full = Path.Combine(dir, n.Archivo);
        return File.Exists(full) ? full : null;
    }

    public static string PrefsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LosasPlus");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "reglamentos_path.txt");
    }

    public static void SaveCarpetaPreference(string path)
    {
        try { File.WriteAllText(PrefsPath(), path ?? ""); }
        catch { /* sin persistencia, no es crítico */ }
    }

    public static string LoadCarpetaPreference()
    {
        try { return File.Exists(PrefsPath()) ? File.ReadAllText(PrefsPath()).Trim() : ""; }
        catch { return ""; }
    }

    private static string ReadResourceJson()
    {
        try
        {
            var uri = new System.Uri("pack://application:,,,/Resources/Reglamento.json");
            var sri = System.Windows.Application.GetResourceStream(uri);
            if (sri != null)
            {
                using var sr = new StreamReader(sri.Stream);
                return sr.ReadToEnd();
            }
        }
        catch { /* fallback abajo */ }

        var local = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
            "Resources", "Reglamento.json");
        return File.Exists(local) ? File.ReadAllText(local) : "{\"normas\":[]}";
    }

    private static (List<Norma>, ReglamentoMeta) ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var meta = new ReglamentoMeta();
        if (root.TryGetProperty("_meta", out var metaEl))
            meta = JsonSerializer.Deserialize<ReglamentoMeta>(metaEl.GetRawText()) ?? new ReglamentoMeta();
        if (!root.TryGetProperty("normas", out var arr)) return (new List<Norma>(), meta);
        var list = arr.EnumerateArray()
                      .Select(e => JsonSerializer.Deserialize<Norma>(e.GetRawText()) ?? new Norma())
                      .ToList();
        return (list, meta);
    }
}
