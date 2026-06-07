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

    /// <summary>
    /// Versión actual del manifest multi-archivo.
    /// <list type="bullet">
    /// <item>v1: lista plana <c>sistemas</c> de <see cref="SistemaRef"/> — pierde
    /// todo nivel salvo el primero (bug pre-B2). Se sigue leyendo por
    /// compatibilidad hacia atrás.</item>
    /// <item>v2: árbol completo <c>edificios → niveles → sistemas</c> (B2). Cada
    /// sistema sigue persistiéndose en su propio <c>.DL</c>.</item>
    /// </list>
    /// </summary>
    public const int ManifestVersion = 2;

    public sealed class ProyectoManifest
    {
        [JsonPropertyName("nombre")]        public string Nombre        { get; set; } = "";
        [JsonPropertyName("autor")]         public string Autor         { get; set; } = "";
        [JsonPropertyName("codigo_obra")]   public string CodigoObra    { get; set; } = "";
        [JsonPropertyName("ubicacion")]     public string Ubicacion     { get; set; } = "";
        [JsonPropertyName("descripcion")]   public string Descripcion   { get; set; } = "";
        [JsonPropertyName("fecha_creacion")] public string FechaCreacion { get; set; } = "";
        [JsonPropertyName("version")]       public int Version          { get; set; } = ManifestVersion;

        /// <summary>
        /// Lista PLANA de sistemas — formato legado v1. Sólo se escribe vacía en
        /// v2 (el árbol vive en <see cref="Edificios"/>); al leer un manifest v1
        /// se interpreta como un único nivel. Es el discriminador de formato
        /// junto a <see cref="Version"/>.
        /// </summary>
        [JsonPropertyName("sistemas")]      public List<SistemaRef> Sistemas { get; set; } = new();

        /// <summary>
        /// Árbol completo del proyecto — formato v2 (B2). Cuando está presente,
        /// el loader lo prefiere sobre <see cref="Sistemas"/> y reconstruye TODOS
        /// los niveles. <c>null</c> en manifests v1.
        /// </summary>
        [JsonPropertyName("edificios")]     public List<EdificioRef>? Edificios { get; set; }
    }

    public sealed class EdificioRef
    {
        [JsonPropertyName("nombre")]  public string Nombre { get; set; } = "";
        [JsonPropertyName("niveles")] public List<NivelRef> Niveles { get; set; } = new();
    }

    public sealed class NivelRef
    {
        [JsonPropertyName("nombre")]   public string Nombre { get; set; } = "";
        [JsonPropertyName("cota")]     public double Cota   { get; set; }
        [JsonPropertyName("sistemas")] public List<SistemaRef> Sistemas { get; set; } = new();
    }

    public sealed class SistemaRef
    {
        [JsonPropertyName("nombre")] public string Nombre { get; set; } = "";
        [JsonPropertyName("archivo_dl")] public string ArchivoDL { get; set; } = "";
        [JsonPropertyName("notas")] public string Notas { get; set; } = "";
    }

    /// <summary>
    /// Enumera TODOS los sistemas del proyecto recorriendo el árbol completo
    /// <c>Edificios → Niveles → Sistemas</c> — no sólo la fachada
    /// <c>Proyecto.Sistemas</c> (= <c>Edificios[0].Niveles[0].Sistemas</c>).
    /// Punto único de verdad para guardado, exportación, validación y búsqueda
    /// multinivel.
    /// </summary>
    public static IEnumerable<Sistema> EnumerarSistemas(Proyecto p)
    {
        if (p is null) throw new ArgumentNullException(nameof(p));
        return p.Edificios
                .SelectMany(e => e.Niveles)
                .SelectMany(n => n.Sistemas);
    }

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Guarda el proyecto: manifest JSON v2 con el árbol completo
    /// <c>edificios → niveles → sistemas</c> + un archivo <c>.DL</c> por cada
    /// sistema de CADA nivel. Antes de B2 sólo persistía <c>p.Sistemas</c>
    /// (= <c>Niveles[0]</c>), perdiendo el resto de los niveles.
    /// </summary>
    public static void GuardarProyecto(Proyecto p, string carpetaDestino)
    {
        if (p is null) throw new ArgumentNullException(nameof(p));
        Directory.CreateDirectory(carpetaDestino);

        // Garantizar la estructura mínima para no escribir un manifest sin árbol.
        p.AsegurarEstructura();

        var manifest = new ProyectoManifest
        {
            Nombre = p.Nombre,
            Autor = p.Autor,
            CodigoObra = p.CodigoObra,
            Ubicacion = p.Ubicacion,
            Descripcion = p.Descripcion,
            FechaCreacion = p.FechaCreacion.ToString("yyyy-MM-ddTHH:mm:ss"),
            Version = ManifestVersion,
            Edificios = new List<EdificioRef>(),
        };

        // Slugs únicos a nivel de PROYECTO (no por nivel) para que dos sistemas
        // homónimos en niveles distintos no se pisen el mismo .DL.
        var slugUsados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ed in p.Edificios)
        {
            var edRef = new EdificioRef { Nombre = ed.Nombre };
            foreach (var nivel in ed.Niveles)
            {
                var nivRef = new NivelRef { Nombre = nivel.Nombre, Cota = nivel.Cota };
                foreach (var s in nivel.Sistemas)
                {
                    var slug = MakeSlug(s.Nombre);
                    var orig = slug;
                    int suffix = 2;
                    while (slugUsados.Contains(slug)) slug = $"{orig}_{suffix++}";
                    slugUsados.Add(slug);

                    var dlFile = $"sistema_{slug}.dl";
                    DLFileService.Save(s, Path.Combine(carpetaDestino, dlFile));

                    nivRef.Sistemas.Add(new SistemaRef { Nombre = s.Nombre, ArchivoDL = dlFile });
                }
                edRef.Niveles.Add(nivRef);
            }
            manifest.Edificios.Add(edRef);
        }

        // Manifest JSON
        var manifestPath = Path.Combine(carpetaDestino, ManifestFileName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, _json), Encoding.UTF8);

        p.Archivo = manifestPath;
    }

    /// <summary>
    /// Abre un proyecto a partir del manifest. Reconstruye el árbol completo
    /// <c>edificios → niveles → sistemas</c> (formato v2) o, para manifests v1
    /// (lista plana <c>sistemas</c>), los carga en un único nivel — la
    /// compatibilidad hacia atrás. Carga cada Sistema desde su <c>.DL</c>.
    /// </summary>
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

        Sistema LeerSistema(SistemaRef sref)
        {
            var dlPath = Path.Combine(carpeta, sref.ArchivoDL);
            if (!File.Exists(dlPath))
                throw new FileNotFoundException(
                    $"El proyecto declara el sistema '{sref.Nombre}' en '{sref.ArchivoDL}' pero el archivo no existe.",
                    dlPath);
            var s = DLFileService.Read(dlPath);
            // Si el manifest tiene un nombre distinto al del .DL, prevalece el del manifest.
            if (!string.IsNullOrWhiteSpace(sref.Nombre)) s.Nombre = sref.Nombre;
            return s;
        }

        // Discriminar el formato: v2 trae el árbol `edificios`; v1 sólo la lista
        // plana `sistemas`. Disparar al lector correcto evita corromper el
        // proyecto silenciosamente.
        if (manifest.Edificios is { Count: > 0 })
        {
            // Formato v2 — reconstruir TODOS los niveles.
            p.Edificios.Clear();
            foreach (var edRef in manifest.Edificios)
            {
                var ed = new Edificio { Nombre = edRef.Nombre };
                foreach (var nivRef in edRef.Niveles)
                {
                    var nivel = new Nivel { Nombre = nivRef.Nombre, Cota = nivRef.Cota };
                    foreach (var sref in nivRef.Sistemas)
                        nivel.Sistemas.Add(LeerSistema(sref));
                    ed.Niveles.Add(nivel);
                }
                p.Edificios.Add(ed);
            }
        }
        else
        {
            // Formato v1 (legado) — lista plana de sistemas en un único nivel.
            foreach (var sref in manifest.Sistemas)
                p.Sistemas.Add(LeerSistema(sref));
        }

        // Proyecto genuinamente vacío (manifest sin árbol y sin sistemas): dejarlo
        // con la estructura mínima editable, SIN fabricar un sistema demo que
        // enmascararía un parseo fallido.
        p.AsegurarEstructura();

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
