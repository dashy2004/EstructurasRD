using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.IA;

/// <summary>
/// Implementación de <see cref="IAnalizadorEstructuralIA"/> contra una IA local
/// (Qwen con visión, p. ej. <c>qwen2.5vl</c>) servida por Ollama. Envía la imagen
/// del esquema a <c>POST /api/chat</c> y parsea la respuesta JSON a una
/// <see cref="PropuestaElementos"/> (losas + vigas). <b>Solo lectura</b>: lee la
/// imagen y devuelve datos; no modifica el proyecto ni el código.
/// </summary>
public sealed class QwenAnalizador : IAnalizadorEstructuralIA
{
    /// <summary>Prompt que fuerza una respuesta JSON con losas y vigas (foco actual).</summary>
    public const string Prompt =
        "Sos un asistente de ingeniería estructural. La imagen es un esquema/planta con " +
        "LOSAS (paños rectangulares) y VIGAS (líneas entre apoyos). Devolvé ÚNICAMENTE un " +
        "objeto JSON, sin texto adicional ni markdown. Ejemplo de UN paño 4x5 rodeado por sus 4 " +
        "vigas (2 horizontales con y constante + 2 verticales con x constante):\n" +
        "{\"losas\":[{\"x\":0.0,\"y\":0.0,\"lx\":4.0,\"ly\":5.0}]," +
        "\"vigas\":[{\"x1\":0.0,\"y1\":0.0,\"x2\":4.0,\"y2\":0.0}," +
        "{\"x1\":0.0,\"y1\":5.0,\"x2\":4.0,\"y2\":5.0}," +
        "{\"x1\":0.0,\"y1\":0.0,\"x2\":0.0,\"y2\":5.0}," +
        "{\"x1\":4.0,\"y1\":0.0,\"x2\":4.0,\"y2\":5.0}]}\n" +
        "SISTEMA DE COORDENADAS (crítico): metros; origen (0,0) en la esquina INFERIOR-IZQUIERDA " +
        "del dibujo; el eje X crece hacia la DERECHA y el eje Y crece hacia ARRIBA. La fila de " +
        "abajo en la imagen es y=0; las filas superiores tienen y MAYOR. TODAS las coordenadas " +
        "deben ser >= 0: NUNCA uses valores negativos (si te da negativo, invertí el eje Y).\n" +
        "LOSAS: para cada paño, (x,y) es su esquina inferior-izquierda, lx el ancho en X y ly el " +
        "alto en Y. Usá las cotas si están; si no, estimá dimensiones razonables.\n" +
        "VIGAS: trazá una viga sobre CADA línea de la retícula que bordea los paños, tanto las " +
        "HORIZONTALES (y constante) como las VERTICALES (x constante). Una viga entre dos ejes " +
        "consecutivos va de eje a eje. Incluí los bordes exteriores y los ejes interiores; no " +
        "omitas líneas. Cada viga es un segmento de (x1,y1) a (x2,y2).\n" +
        "No inventes elementos que no estén en la imagen.";

    private readonly QwenConfig _config;
    private readonly HttpClient _http;

    public QwenAnalizador(QwenConfig config, HttpClient? http = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.TimeoutSegundos)) };
    }

    /// <inheritdoc/>
    public async Task<PropuestaElementos> AnalizarAsync(string archivoPath, CancellationToken ct = default)
    {
        if (!File.Exists(archivoPath))
            throw new FileNotFoundException("Imagen no encontrada.", archivoPath);

        byte[] bytes = await File.ReadAllBytesAsync(archivoPath, ct);
        string b64 = Convert.ToBase64String(bytes);

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
                    ["content"] = Prompt,
                    ["images"] = new[] { b64 },
                },
            },
        };

        using var req = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{_config.Endpoint.TrimEnd('/')}/api/chat", req, ct);
        resp.EnsureSuccessStatusCode();

        string respText = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(respText);
        string contenido = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
        return ParsearPropuesta(contenido);
    }

    /// <summary>
    /// Parsea el JSON devuelto por el modelo a una <see cref="PropuestaElementos"/>.
    /// Tolerante: extrae el primer objeto <c>{...}</c> (por si el modelo agrega texto)
    /// y omite campos faltantes. Función pura — testeable sin red.
    /// </summary>
    public static PropuestaElementos ParsearPropuesta(string contenido)
    {
        var losas = new List<LosaPropuesta>();
        var vigas = new List<VigaPropuesta>();
        string? advertencia = null;

        try
        {
            using var doc = JsonDocument.Parse(ExtraerJson(contenido));
            var root = doc.RootElement;

            if (root.TryGetProperty("losas", out var ls) && ls.ValueKind == JsonValueKind.Array)
                foreach (var l in ls.EnumerateArray())
                    losas.Add(new LosaPropuesta(Num(l, "x"), Num(l, "y"), Num(l, "lx", 4.0), Num(l, "ly", 4.0)));

            if (root.TryGetProperty("vigas", out var vs) && vs.ValueKind == JsonValueKind.Array)
                foreach (var v in vs.EnumerateArray())
                    vigas.Add(new VigaPropuesta(Num(v, "x1"), Num(v, "y1"), Num(v, "x2"), Num(v, "y2")));
        }
        catch (Exception ex)
        {
            advertencia = "No se pudo interpretar la respuesta de la IA: " + ex.Message;
        }

        return new PropuestaElementos(losas, vigas,
            Array.Empty<ColumnaPropuesta>(), Array.Empty<EjePropuesto>(), advertencia);
    }

    private static double Num(JsonElement e, string nombre, double porDefecto = 0.0)
        => e.TryGetProperty(nombre, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDouble() : porDefecto;

    private static string ExtraerJson(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "{}";
        int a = s.IndexOf('{');
        int b = s.LastIndexOf('}');
        return a >= 0 && b > a ? s.Substring(a, b - a + 1) : "{}";
    }
}
