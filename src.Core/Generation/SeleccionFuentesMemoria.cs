using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace LosasPlus.Generation;

/// <summary>Selecciona los archivos que el lector armar_datos.py sabe interpretar.</summary>
public sealed class SeleccionFuentesMemoria
{
    private static readonly StringComparer Rutas = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly List<string> _archivos = new();
    public IReadOnlyList<string> Archivos => _archivos.AsReadOnly();

    public void Limpiar() => _archivos.Clear();

    public void Agregar(string ruta)
    {
        ruta = ArchivoLocal(ruta, Environment.CurrentDirectory);
        if (_archivos.Contains(ruta, Rutas)) return;
        string ext = Extension(ruta);
        if (ext is not (".txt" or ".dl" or ".xls" or ".xlsx" or ".xlsm" or ".fdb" or ".edb" or ".json"))
            throw new InvalidDataException("Elija un listado TXT, DL, libro de cargas, modelo ETABS o SAFE, o fuentes.json.");
        if (_archivos.Count != 0 && (ext == ".json" || Extension(_archivos[0]) == ".json"))
            throw new InvalidDataException("Use un manifiesto o una selección de archivos. Vacíe la selección para cambiar.");
        if ((ext == ".fdb" && _archivos.Any(a => Extension(a) == ".fdb")) ||
            (EsLibro(ext) && _archivos.Any(a => EsLibro(Extension(a)))))
            throw new InvalidDataException("Solo se admite una fundación SAFE y un libro de cargas por extracción.");
        _archivos.Add(ruta);
    }

    public static void ValidarDestinoDatos(string destino)
    {
        if (Extension(destino) != ".json" || destino.EndsWith(".lpx.json", StringComparison.OrdinalIgnoreCase)
            || new[] { "memoria.json", "fuentes.json" }.Contains(Path.GetFileName(destino), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Elija un archivo de datos JSON distinto del proyecto, fuentes.json y memoria.json.");
    }

    public JsonObject Preparar(string salida)
    {
        string destino = Path.GetFullPath(salida);
        ValidarDestinoDatos(destino);
        if (_archivos.Contains(destino, Rutas))
            throw new InvalidDataException("La salida sobrescribiría una fuente seleccionada.");

        if (_archivos.Count == 1 && Extension(_archivos[0]) == ".json")
        {
            var ruta = _archivos[0];
            var manifiesto = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject
                ?? throw new InvalidDataException("Se esperaba un manifiesto de fuentes.");
            Normalizar(manifiesto, Path.GetDirectoryName(ruta)!, destino);
            return manifiesto;
        }
        var resultado = new JsonObject();
        var niveles = new JsonArray();
        var bloques = new JsonArray();
        var dlPendientes = _archivos.Where(a => Extension(a) == ".dl").ToList();
        foreach (string ruta in _archivos)
        {
            switch (Extension(ruta))
            {
                case ".txt":
                    string nombre = Path.GetFileNameWithoutExtension(ruta);
                    var nivel = new JsonObject { ["nombre"] = nombre, ["txt"] = ruta };
                    string? dl = dlPendientes.FirstOrDefault(d => Rutas.Equals(Path.ChangeExtension(d, null), Path.ChangeExtension(ruta, null)));
                    if (dl != null) { nivel["dl"] = dl; dlPendientes.Remove(dl); }
                    niveles.Add(nivel);
                    break;
                case ".edb":
                    bloques.Add(new JsonObject { ["nombre"] = Path.GetFileNameWithoutExtension(ruta), ["etabs"] = ruta });
                    break;
                case ".fdb":
                    resultado["fundaciones"] = new JsonObject { ["safe"] = ruta };
                    break;
                case ".xls":
                case ".xlsx":
                case ".xlsm":
                    resultado["cargas"] = new JsonObject { ["libro"] = ruta };
                    break;
            }
        }
        if (dlPendientes.Count != 0)
            throw new InvalidDataException($"Seleccione el TXT correspondiente a {Path.GetFileName(dlPendientes[0])}. Para nombres diferentes, use fuentes.json.");
        resultado["niveles"] = niveles;
        resultado["bloques"] = bloques;
        Normalizar(resultado, Environment.CurrentDirectory, destino);
        return resultado;
    }

    private static void Normalizar(JsonObject man, string carpeta, string destino)
    {
        int fuentes = 0;
        string Resolver(JsonNode? nodo, params string[] extensiones)
        {
            string? ruta = nodo?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(ruta)) throw new InvalidDataException("Falta una ruta en el manifiesto.");
            ruta = ArchivoLocal(ruta, carpeta);
            if (!extensiones.Contains(Extension(ruta)))
                throw new InvalidDataException($"Tipo de archivo incompatible: {Path.GetFileName(ruta)}.");
            if (Rutas.Equals(ruta, destino)) throw new InvalidDataException("La salida sobrescribiría una fuente declarada.");
            fuentes++;
            return ruta;
        }

        foreach (var n in Filas(man, "niveles"))
        {
            n["txt"] = Resolver(n["txt"], ".txt");
            if (n["dl"] != null) n["dl"] = Resolver(n["dl"], ".dl");
        }
        foreach (var b in Filas(man, "bloques"))
        {
            b["etabs"] = Resolver(b["etabs"], ".edb");
            if (b["art38"] != null && b["espectro"] == null)
                throw new InvalidDataException("Un libro del Art. 38 requiere el libro de espectro del bloque.");
            foreach (var campo in new[] { "espectro", "art38" })
                if (b[campo] != null) b[campo] = Resolver(b[campo], ".xls", ".xlsx", ".xlsm");
        }
        if (man["fundaciones"] is JsonNode fund) fund["safe"] = Resolver(fund["safe"], ".fdb");
        if (man["cargas"] is JsonNode cargas) cargas["libro"] = Resolver(cargas["libro"], ".xls", ".xlsx", ".xlsm");
        if (man["bloques_json"] != null)
        {
            var bloques = man["bloques_json"]!.AsArray();
            for (int i = 0; i < bloques.Count; i++) bloques[i] = Resolver(bloques[i], ".json");
        }
        if (fuentes == 0) throw new InvalidDataException("El manifiesto debe declarar al menos una fuente compatible.");
    }

    private static IEnumerable<JsonObject> Filas(JsonObject man, string clave) =>
        man[clave]?.AsArray().Select(n => n as JsonObject ?? throw new InvalidDataException($"Entrada inválida en {clave}."))
        ?? Enumerable.Empty<JsonObject>();

    private static bool EsLibro(string extension) => extension is ".xls" or ".xlsx" or ".xlsm";
    private static string Extension(string ruta) => Path.GetExtension(ruta).ToLowerInvariant();
    private static string ArchivoLocal(string ruta, string carpeta)
    {
        if (ruta.Contains("://", StringComparison.Ordinal) || ruta.StartsWith(@"\\", StringComparison.Ordinal) || ruta.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidDataException("Seleccione archivos locales de este equipo.");
        string resultado = Path.GetFullPath(ruta, carpeta);
        if (!File.Exists(resultado)) throw new FileNotFoundException("No se encontró la fuente.", resultado);
        return resultado;
    }
}
