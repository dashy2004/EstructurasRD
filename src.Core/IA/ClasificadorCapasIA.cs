using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Services;

namespace LosasPlus.IA;

/// <summary>
/// Clasificador de capas DXF asistido por IA de <b>texto</b> (Qwen vía Ollama).
/// Es la segunda mitad de la clasificación <i>híbrida</i>: solo se invoca para las
/// capas que <see cref="ClasificadorCapas"/> no pudo reconocer por heurística.
///
/// <para>A diferencia de <see cref="QwenAnalizador"/> (visión), acá <b>no se manda
/// ninguna imagen</b>: se le pasa la lista de nombres de capa como texto y el
/// modelo devuelve un JSON {"NOMBRE_CAPA":"viga|columna|losa|eje|otro"}. Sigue
/// siendo solo lectura: clasifica nombres, no toca el código ni el plano.</para>
/// </summary>
public sealed class ClasificadorCapasIA
{
    /// <summary>Instrucción que fuerza un JSON capa→categoría, sin texto extra.</summary>
    public const string Instruccion =
        "Sos un asistente de ingeniería estructural. Te paso una lista de NOMBRES DE CAPA " +
        "(layers) de un plano DXF. Clasificá CADA capa en una sola de estas categorías: " +
        "\"viga\", \"columna\", \"losa\", \"eje\" u \"otro\". Devolvé ÚNICAMENTE un objeto JSON " +
        "(sin markdown ni texto adicional) que mapee cada nombre de capa EXACTO a su categoría, " +
        "por ejemplo: {\"V-25x50\":\"viga\",\"P1\":\"columna\",\"ENTREPISO\":\"losa\"}. " +
        "Usá \"otro\" si no es un elemento estructural (muros, cotas, textos, mobiliario).";

    private readonly QwenConfig _config;
    private readonly HttpClient _http;

    public ClasificadorCapasIA(QwenConfig config, HttpClient? http = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.TimeoutSegundos)) };
    }

    /// <summary>Construye el contenido del mensaje (instrucción + lista de capas).</summary>
    public static string ConstruirContenido(IEnumerable<string> capas)
    {
        var lista = string.Join("\n", (capas ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().Select(c => "- " + c));
        return Instruccion + "\n\nCapas:\n" + lista;
    }

    /// <summary>
    /// Clasifica las capas ambiguas con el modelo de texto. Devuelve un diccionario
    /// capa→categoría (solo las que el modelo devolvió). Resiliente: ante error de
    /// red o respuesta ilegible devuelve un diccionario vacío (la heurística manda).
    /// </summary>
    public async Task<IReadOnlyDictionary<string, CategoriaEstructural>> ClasificarAsync(
        IReadOnlyList<string> capas, CancellationToken ct = default)
    {
        if (capas is null || capas.Count == 0)
            return new Dictionary<string, CategoriaEstructural>();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = _config.Modelo,
            ["stream"] = false,
            ["format"] = "json",
            ["options"] = new Dictionary<string, object?> { ["temperature"] = _config.Temperatura },
            ["messages"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["role"] = "user",
                    ["content"] = ConstruirContenido(capas),
                },
            },
        };

        try
        {
            using var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{_config.Endpoint.TrimEnd('/')}/api/chat", req, ct);
            resp.EnsureSuccessStatusCode();

            string respText = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(respText);
            string contenido = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
            return ParsearRespuesta(contenido);
        }
        catch
        {
            return new Dictionary<string, CategoriaEstructural>();
        }
    }

    /// <summary>
    /// Parsea el JSON capa→categoría devuelto por el modelo. Tolerante: extrae el
    /// primer objeto <c>{...}</c> (por si agrega texto) y omite valores no
    /// reconocidos. Función pura — testeable sin red.
    /// </summary>
    public static IReadOnlyDictionary<string, CategoriaEstructural> ParsearRespuesta(string contenido)
    {
        var mapa = new Dictionary<string, CategoriaEstructural>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(ExtraerJson(contenido));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return mapa;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                mapa[prop.Name] = ParsearCategoria(prop.Value.GetString());
            }
        }
        catch
        {
            // respuesta ilegible → diccionario vacío (la heurística decide)
        }
        return mapa;
    }

    /// <summary>
    /// Combina la heurística con la IA: para cada capa usa la heurística; si ésta
    /// devuelve <see cref="CategoriaEstructural.Otro"/> y la IA tiene un veredicto,
    /// usa el de la IA. Devuelve la función capa→categoría lista para
    /// <see cref="DxfEstructuraMapper.Mapear"/>.
    /// </summary>
    public static Func<string, CategoriaEstructural> CombinarConHeuristica(
        IReadOnlyDictionary<string, CategoriaEstructural> ia)
    {
        ia ??= new Dictionary<string, CategoriaEstructural>();
        return capa =>
        {
            var h = ClasificadorCapas.Clasificar(capa);
            if (h != CategoriaEstructural.Otro) return h;
            return ia.TryGetValue(capa, out var c) ? c : CategoriaEstructural.Otro;
        };
    }

    private static CategoriaEstructural ParsearCategoria(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "viga"    => CategoriaEstructural.Viga,
        "columna" => CategoriaEstructural.Columna,
        "losa"    => CategoriaEstructural.Losa,
        "eje"     => CategoriaEstructural.Eje,
        _         => CategoriaEstructural.Otro,
    };

    private static string ExtraerJson(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "{}";
        int a = s.IndexOf('{');
        int b = s.LastIndexOf('}');
        return a >= 0 && b > a ? s.Substring(a, b - a + 1) : "{}";
    }
}
