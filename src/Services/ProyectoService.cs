using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Persistencia de proyecto multi-archivo. Cada Sistema se guarda en su propio
/// <c>.DL</c> byte-compatible con <c>Losas.exe</c>. La carpeta del proyecto contiene:
/// <code>
/// MiProyecto/
///   proyecto.lpx.json     ← manifest con metadata y lista de archivos .DL
///   sistema_planta_baja.dl
///   sistema_techo.dl
/// </code>
/// </summary>
public static class ProyectoService
{
    public const string ManifestFileName = "proyecto.lpx.json";

    public sealed class ProyectoManifest
    {
        [JsonPropertyName("nombre")]        public string Nombre        { get; set; } = "";
        [JsonPropertyName("autor")]         public string Autor         { get; set; } = "";
        [JsonPropertyName("codigo_obra")]   public string CodigoObra    { get; set; } = "";
        [JsonPropertyName("ubicacion")]     public string Ubicacion     { get; set; } = "";
        [JsonPropertyName("descripcion")]   public string Descripcion   { get; set; } = "";
        [JsonPropertyName("fecha_creacion")] public string FechaCreacion { get; set; } = "";
        [JsonPropertyName("version")]       public int Version          { get; set; } = 1;
        [JsonPropertyName("sistemas")]      public List<SistemaRef> Sistemas { get; set; } = new();
    }

    public sealed class SistemaRef
    {
        [JsonPropertyName("nombre")] public string Nombre { get; set; } = "";
        [JsonPropertyName("archivo_dl")] public string ArchivoDL { get; set; } = "";
        [JsonPropertyName("notas")] public string Notas { get; set; } = "";
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Guarda el proyecto: manifest JSON + un archivo .DL por cada sistema.</summary>
    public static void GuardarProyecto(Proyecto p, string carpetaDestino)
    {
        Directory.CreateDirectory(carpetaDestino);
        var manifest = new ProyectoManifest
        {
            Nombre = p.Nombre,
            Autor = p.Autor,
            CodigoObra = p.CodigoObra,
            Ubicacion = p.Ubicacion,
            Descripcion = p.Descripcion,
            FechaCreacion = p.FechaCreacion.ToString("yyyy-MM-ddTHH:mm:ss"),
            Version = 1,
        };

        // Persistir cada sistema en su propio .DL con nombre derivado del nombre del sistema
        var slugUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in p.Sistemas)
        {
            var slug = MakeSlug(s.Nombre);
            // Evitar colisiones
            var orig = slug;
            int suffix = 2;
            while (slugUsados.Contains(slug)) slug = $"{orig}_{suffix++}";
            slugUsados.Add(slug);

            var dlFile = $"sistema_{slug}.dl";
            var dlPath = Path.Combine(carpetaDestino, dlFile);
            DLFileService.Save(s, dlPath);

            manifest.Sistemas.Add(new SistemaRef
            {
                Nombre = s.Nombre,
                ArchivoDL = dlFile,
            });
        }

        // Manifest JSON
        var manifestPath = Path.Combine(carpetaDestino, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _json), Encoding.UTF8);

        p.Archivo = manifestPath;
    }

    /// <summary>Abre un proyecto a partir del manifest. Carga cada Sistema desde su .DL.</summary>
    public static Proyecto AbrirProyecto(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Manifest del proyecto no encontrado", manifestPath);

        var carpeta = Path.GetDirectoryName(manifestPath) ?? "";
        var json = File.ReadAllText(manifestPath, Encoding.UTF8);
        var manifest = JsonSerializer.Deserialize<ProyectoManifest>(json, _json)
                       ?? throw new FormatException("Manifest del proyecto inválido (JSON corrupto).");

        var p = new Proyecto
        {
            Archivo = manifestPath,
            Nombre = manifest.Nombre,
            Autor = manifest.Autor,
            CodigoObra = manifest.CodigoObra,
            Ubicacion = manifest.Ubicacion,
            Descripcion = manifest.Descripcion,
            FechaCreacion = DateTime.TryParse(manifest.FechaCreacion, out var dt) ? dt : DateTime.Now,
        };

        foreach (var sref in manifest.Sistemas)
        {
            var dlPath = Path.Combine(carpeta, sref.ArchivoDL);
            if (!File.Exists(dlPath))
                throw new FileNotFoundException(
                    $"El proyecto declara el sistema '{sref.Nombre}' en '{sref.ArchivoDL}' pero el archivo no existe.",
                    dlPath);
            var s = DLFileService.Read(dlPath);
            // Si el manifest tiene un nombre distinto al del .DL, prevalece el del manifest
            if (!string.IsNullOrWhiteSpace(sref.Nombre)) s.Nombre = sref.Nombre;
            p.Sistemas.Add(s);
        }

        if (p.Sistemas.Count == 0 && manifest.Sistemas.Count == 0)
        {
            // Manifest vacío válido: crear un sistema demo para que el proyecto sea editable
            p.Sistemas.Add(new Sistema { Nombre = "Sistema 1" });
        }

        return p;
    }

    /// <summary>
    /// Crea un proyecto nuevo en una carpeta vacía (o existente) con un sistema inicial.
    /// </summary>
    public static Proyecto CrearProyecto(string carpetaDestino, string nombreProyecto, string autor = "")
    {
        var p = new Proyecto
        {
            Nombre = nombreProyecto,
            Autor = autor,
            FechaCreacion = DateTime.Now,
        };
        p.Sistemas.Add(new Sistema { Nombre = "Sistema 1" });
        GuardarProyecto(p, carpetaDestino);
        return p;
    }

    /// <summary>
    /// Importa un .DL legado (mono o multi-sistema) y lo convierte en un Proyecto en memoria.
    /// El usuario después decide guardar como proyecto multi-archivo.
    /// </summary>
    public static Proyecto AbrirDLLegacy(string dlPath)
    {
        var sistemas = DLFileService.ReadAll(dlPath);
        var p = new Proyecto
        {
            Archivo = dlPath,
            Nombre = Path.GetFileNameWithoutExtension(dlPath),
            FechaCreacion = File.GetCreationTime(dlPath),
        };
        foreach (var s in sistemas) p.Sistemas.Add(s);
        if (p.Sistemas.Count == 0) p.Sistemas.Add(new Sistema { Nombre = "Sistema 1" });
        return p;
    }

    /// <summary>Convierte un nombre arbitrario en un slug seguro para nombre de archivo.</summary>
    public static string MakeSlug(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return "sistema";
        var sb = new StringBuilder();
        foreach (var raw in nombre.Trim().ToLowerInvariant())
        {
            char c = raw switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ñ' => 'n',
                _ => raw,
            };
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '_' or '-' or '.') sb.Append('_');
            // resto se descarta
        }
        var slug = sb.ToString().Trim('_');
        // Colapsar dobles _
        while (slug.Contains("__")) slug = slug.Replace("__", "_");
        return string.IsNullOrEmpty(slug) ? "sistema" : slug;
    }
}
