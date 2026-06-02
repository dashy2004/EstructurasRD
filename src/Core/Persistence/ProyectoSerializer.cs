using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using LosasPlus.Cargas;
using LosasPlus.Models;

namespace LosasPlus.Persistence;

/// <summary>
/// Serializa/deserializa un <see cref="Proyecto"/> a/desde JSON con extensión
/// <c>.lpx.json</c> ("LosasPlus Project") — el formato nativo de Memoria Plus
/// para persistir el trabajo del usuario.
///
/// <para>
/// El JSON va envuelto en un <see cref="ProyectoEnvelope"/> con metadata
/// (versión del formato, timestamps, versión de la app) para permitir
/// migraciones futuras del formato sin perder compatibilidad con archivos viejos.
/// </para>
///
/// <para>
/// Las propiedades computadas del modelo (Cond, Area, WPropio, Total, etc.)
/// llevan <see cref="JsonIgnoreAttribute"/> para evitar bloat. Solo se persisten
/// los inputs editables + outputs poblados por engines (HCalc, Qd, Qu, etc.)
/// que conviene preservar entre sesiones.
/// </para>
/// </summary>
public static class ProyectoSerializer
{
    /// <summary>
    /// Versión actual del formato de archivo. Incrementar con cada cambio breaking.
    /// v1 → v2: jerarquía <c>Edificio → Nivel</c> entre <c>Proyecto</c> y
    /// <c>Sistema</c>. v2 → v3: casos y combinaciones de carga. Los archivos
    /// anteriores se migran automáticamente al cargar.
    /// </summary>
    public const int FormatVersion = 3;

    /// <summary>Extensión canónica de archivos de proyecto Memoria Plus.</summary>
    public const string Extension = ".lpx.json";

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,  // preserva Ñ, acentos
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Crítico para nuestro modelo: las colecciones del dominio (Sistemas,
        // Losas, BordesX/Y, Cargas.Filas, Items, Normas, Momentos, etc.) son
        // get-only con inicializador `= new()`. El default de System.Text.Json
        // (Replace) las ignora al deserializar. Con Populate, las llena via
        // .Add() sobre la instancia existente.
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
    };

    /// <summary>
    /// Serializa <paramref name="proyecto"/> a string JSON (sin escribir a
    /// disco). Útil para snapshots en memoria — ver Undo/Redo en
    /// LosasPlus.App. Misma envelope/opciones que <see cref="Save"/>.
    /// </summary>
    public static string ToJson(Proyecto proyecto)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));
        var envelope = new ProyectoEnvelope
        {
            Version       = FormatVersion,
            CreadoUtc     = proyecto.FechaCreacion.ToUniversalTime(),
            ModificadoUtc = DateTime.UtcNow,
            AppVersion    = "LosasPlus 0.5.0",
            Proyecto      = proyecto,
        };
        return JsonSerializer.Serialize(envelope, _opts);
    }

    /// <summary>
    /// Deserializa un JSON producido por <see cref="ToJson"/> y devuelve el
    /// <see cref="Proyecto"/> contenido. Lanza
    /// <see cref="InvalidProyectoFileException"/> si el JSON es inválido.
    /// </summary>
    public static Proyecto FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidProyectoFileException("JSON vacío");
        var envelope = DeserializarEnvelope(json);
        if (envelope.Proyecto is null)
            throw new InvalidProyectoFileException("JSON sin proyecto válido");
        return envelope.Proyecto;
    }

    /// <summary>
    /// Deserializa un <see cref="ProyectoEnvelope"/> desde JSON aplicando, si
    /// hace falta, la migración de formato. Los archivos v1 (jerarquía plana
    /// <c>proyecto.sistemas</c>) se transforman a v2 (<c>proyecto.edificios</c>)
    /// sobre el JSON crudo, antes de materializar el modelo.
    /// </summary>
    private static ProyectoEnvelope DeserializarEnvelope(string json)
    {
        JsonNode? node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling     = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        if (node is not JsonObject root)
            throw new InvalidProyectoFileException("El archivo está vacío o malformado.");

        int version = root["version"] is JsonValue v && v.TryGetValue<int>(out var ver) ? ver : 0;
        if (version == 1)
            MigrarV1aV2(root);

        var envelope = root.Deserialize<ProyectoEnvelope>(_opts);
        if (envelope is null)
            throw new InvalidProyectoFileException("El archivo está vacío o malformado.");

        // Salvavidas retroactivo: los archivos anteriores a v3 no tienen casos
        // ni combinaciones de carga. Se les inyecta la semilla por defecto para
        // que ningún proyecto existente quede sin base de diseño (PROPUESTA Fase 2).
        if (version < 3 && envelope.Proyecto is not null)
            envelope.Proyecto.Combinaciones =
                CombinacionesProyecto.SemillaPorDefecto(NormaCombinaciones.Asce7_05);

        return envelope;
    }

    /// <summary>
    /// Migra el JSON de un proyecto del formato v1 al v2 <b>in situ</b>: el
    /// array plano <c>proyecto.sistemas</c> se envuelve en un único
    /// <c>Edificio</c> con un único <c>Nivel</c> que contiene todos los
    /// sistemas heredados.
    /// </summary>
    private static void MigrarV1aV2(JsonObject root)
    {
        root["version"] = 2;

        if (root["proyecto"] is not JsonObject proyecto)
            return;   // sin proyecto — el caller valida y reporta el error

        // DeepClone → nodo sin padre, re-ubicable bajo el nuevo nivel.
        JsonArray sistemas = (proyecto["sistemas"] as JsonArray)?.DeepClone().AsArray()
                             ?? new JsonArray();
        proyecto.Remove("sistemas");

        var nivel = new JsonObject
        {
            ["nombre"]   = "Nivel 1",
            ["cota"]     = 0,
            ["sistemas"] = sistemas,
        };
        var edificio = new JsonObject
        {
            ["nombre"]  = "Edificio 1",
            ["niveles"] = new JsonArray(nivel),
        };
        proyecto["edificios"] = new JsonArray(edificio);
    }

    /// <summary>
    /// Guarda <paramref name="proyecto"/> como JSON en <paramref name="path"/>.
    /// Si el directorio no existe, lo crea. Sobreescribe archivos existentes.
    /// </summary>
    public static void Save(Proyecto proyecto, string path)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path requerido", nameof(path));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var envelope = new ProyectoEnvelope
        {
            Version       = FormatVersion,
            CreadoUtc     = proyecto.FechaCreacion.ToUniversalTime(),
            ModificadoUtc = DateTime.UtcNow,
            AppVersion    = "MemoriaPlus 0.5.0",
            Proyecto      = proyecto,
        };

        var json = JsonSerializer.Serialize(envelope, _opts);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Lee un <c>.lpx.json</c> y devuelve el <see cref="Proyecto"/> contenido.
    /// Lanza <see cref="InvalidProyectoFileException"/> si el archivo es
    /// inválido o de versión incompatible.
    /// </summary>
    public static Proyecto Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path requerido", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archivo de proyecto no encontrado: {path}", path);

        var json = File.ReadAllText(path, Encoding.UTF8);
        try
        {
            var envelope = DeserializarEnvelope(json);
            if (envelope.Version > FormatVersion)
                throw new InvalidProyectoFileException(
                    $"Versión del archivo ({envelope.Version}) superior a la soportada ({FormatVersion}). " +
                    "Actualiza Memoria Plus a la última versión.");
            if (envelope.Proyecto is null)
                throw new InvalidProyectoFileException("El archivo no contiene un proyecto válido.");

            // Sellar el path en el modelo para que Guardar sobreescriba este archivo.
            envelope.Proyecto.Archivo = path;
            return envelope.Proyecto;
        }
        catch (JsonException ex)
        {
            throw new InvalidProyectoFileException("JSON inválido: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Lee solo la metadata sin deserializar todo el proyecto. Útil para mostrar
    /// detalles en una lista de proyectos recientes sin abrir cada archivo
    /// completo.
    /// </summary>
    public static ProyectoMetadata ReadMetadata(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Archivo no encontrado: {path}", path);

        try
        {
            // Streaming read del primer fragmento del archivo — más eficiente
            // que cargar 1 MB en memoria para sacar 4 strings.
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int GetInt(string key) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetInt32()
                    : 0;

            var proyectoEl = root.TryGetProperty("proyecto", out var p) ? p : default;

            string GetProyecto(string key) =>
                proyectoEl.ValueKind == JsonValueKind.Object
                && proyectoEl.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? ""
                    : "";

            // Conteo version-aware: v2 anida sistemas bajo edificios[].niveles[];
            // v1 los lista planos en proyecto.sistemas.
            int CountSistemas()
            {
                if (proyectoEl.ValueKind != JsonValueKind.Object) return 0;

                if (proyectoEl.TryGetProperty("edificios", out var eds)
                    && eds.ValueKind == JsonValueKind.Array)
                {
                    int total = 0;
                    foreach (var ed in eds.EnumerateArray())
                    {
                        if (ed.ValueKind == JsonValueKind.Object
                            && ed.TryGetProperty("niveles", out var nivs)
                            && nivs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var niv in nivs.EnumerateArray())
                            {
                                if (niv.ValueKind == JsonValueKind.Object
                                    && niv.TryGetProperty("sistemas", out var sis)
                                    && sis.ValueKind == JsonValueKind.Array)
                                    total += sis.GetArrayLength();
                            }
                        }
                    }
                    return total;
                }

                if (proyectoEl.TryGetProperty("sistemas", out var s)
                    && s.ValueKind == JsonValueKind.Array)
                    return s.GetArrayLength();

                return 0;
            }

            return new ProyectoMetadata
            {
                Path          = path,
                Version       = GetInt("version"),
                ModificadoUtc = root.TryGetProperty("modificadoUtc", out var m) && m.ValueKind == JsonValueKind.String
                                   ? DateTime.TryParse(m.GetString(), out var dt) ? dt : DateTime.MinValue
                                   : DateTime.MinValue,
                NombreProyecto = GetProyecto("nombre"),
                Ingeniero      = GetProyecto("autor"),
                Codia          = GetProyecto("codia"),
                CantidadNiveles = CountSistemas(),
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidProyectoFileException("No se pudo leer la metadata: " + ex.Message, ex);
        }
    }
}

/// <summary>
/// Wrapper externo del proyecto con metadata para versionar el formato.
/// </summary>
public class ProyectoEnvelope
{
    public int       Version       { get; set; }
    public DateTime  CreadoUtc     { get; set; }
    public DateTime  ModificadoUtc { get; set; }
    public string    AppVersion    { get; set; } = "";
    public Proyecto? Proyecto      { get; set; }
}

/// <summary>Metadata liviana de un proyecto sin cargar todo el contenido.</summary>
public sealed class ProyectoMetadata
{
    public string   Path             { get; set; } = "";
    public int      Version          { get; set; }
    public DateTime ModificadoUtc    { get; set; }
    public string   NombreProyecto   { get; set; } = "";
    public string   Ingeniero        { get; set; } = "";
    public string   Codia            { get; set; } = "";
    public int      CantidadNiveles  { get; set; }
}

/// <summary>Excepción específica al cargar archivos de proyecto inválidos o incompatibles.</summary>
public sealed class InvalidProyectoFileException : Exception
{
    public InvalidProyectoFileException(string message) : base(message) { }
    public InvalidProyectoFileException(string message, Exception inner) : base(message, inner) { }
}
