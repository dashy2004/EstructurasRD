using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using LosasPlus.Models;

namespace LosasPlus.Generation;

/// <summary>
/// Lleva un <see cref="Proyecto"/> al contrato del motor <c>memoria-diseno</c>
/// (repositorio CDCRD-Digital): <c>data.json</c> con los números de las losas y
/// un <c>memoria.json</c> inicial con portada, responsable y datos del proyecto.
/// Los valores van como texto, con las unidades y decimales del listado de
/// Losas.exe (momentos en ton·m/m, d y H en cm, luces en m).
/// </summary>
public static class MemoriaDisenoExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Relación de luces a partir de la cual la losa se rotula como armada en una dirección.</summary>
    private const double RelacionUnaDireccion = 2.0;

    /// <summary>Contenido de <c>data.json</c>: un nivel por cada sistema de cada nivel del proyecto.</summary>
    public static string ExportarDatos(Proyecto proyecto)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));

        var niveles = new JsonArray();
        var avisos = new JsonArray();
        foreach (var edificio in proyecto.Edificios)
        {
            foreach (var nivel in edificio.Niveles)
            {
                foreach (var sistema in nivel.Sistemas)
                {
                    string nombre = NombreDeNivel(nivel, sistema);
                    // Con varios edificios, el nombre del edificio distingue niveles homónimos.
                    if (proyecto.Edificios.Count > 1 && !string.IsNullOrWhiteSpace(edificio.Nombre))
                        nombre = $"{edificio.Nombre.Trim()} — {nombre}";
                    niveles.Add(Nivel(nombre, sistema));
                    foreach (string aviso in Avisos(nombre, sistema)) avisos.Add(JsonValue.Create(aviso));
                }
            }
        }

        var raiz = new JsonObject
        {
            ["referencia_pdf"] = false,
            ["paginas_total"] = 0,
            ["sobrecarga"] = new JsonArray(),
            ["niveles"] = niveles,
            ["bloques"] = new JsonArray(),
            ["avisos"] = avisos,
        };
        return raiz.ToJsonString(Opciones);
    }

    /// <summary>
    /// Contenido inicial de <c>memoria.json</c> con lo que el proyecto ya sabe. Los
    /// campos que el proyecto no tiene quedan vacíos o se omiten; el motor completa
    /// el resto con sus valores por defecto.
    /// </summary>
    public static string ConfiguracionInicial(Proyecto proyecto)
    {
        if (proyecto is null) throw new ArgumentNullException(nameof(proyecto));

        var filas = new JsonArray();
        void Fila(string etiqueta, string? valor)
        {
            if (!string.IsNullOrWhiteSpace(valor)) filas.Add(new JsonArray(etiqueta, valor.Trim()));
        }
        Fila("Proyecto", proyecto.Nombre);
        Fila("Ubicación", string.IsNullOrWhiteSpace(proyecto.UbicacionCompleta) ? proyecto.Ubicacion : proyecto.UbicacionCompleta);
        Fila("Uso", proyecto.Uso);
        Fila("Cantidad de niveles", proyecto.CantidadNiveles > 0 ? proyecto.CantidadNiveles.ToString(Inv) : null);
        Fila("Sistema estructural", proyecto.SistemaEstructural);
        Fila("Tipo de fundaciones", proyecto.TipoFundaciones);
        Fila("Parámetros de suelo", ParametrosDeSuelo(proyecto));

        // Los materiales salen de los sistemas de losas, que es con lo que se
        // calculó. Los del proyecto pueden ser valores por defecto sin revisar y
        // contradecir el cálculo.
        var sistemas = proyecto.Edificios.SelectMany(e => e.Niveles).SelectMany(n => n.Sistemas).ToList();
        var materiales = new JsonArray();
        string Valores(Func<Sistema, double> campo, string formato)
            => string.Join(" y ", sistemas.Select(campo).Where(v => v > 0).Select(v => Math.Round(v * 1000.0, 3))
                .Distinct().OrderBy(v => v).Select(v => v.ToString(formato, Inv)));
        string fc = Valores(s => s.Fc, "#,0.##"), fy = Valores(s => s.Fy, "#,0.##");
        if (fc.Length > 0) materiales.Add(new JsonArray("Concreto de losas", $"f'c = {fc} kg/cm²"));
        if (fy.Length > 0) materiales.Add(new JsonArray("Acero de refuerzo de losas", $"fy = {fy} kg/cm²"));

        var normas = new JsonArray();
        foreach (string n in proyecto.Normas.Where(n => !string.IsNullOrWhiteSpace(n))) normas.Add(JsonValue.Create(n.Trim()));

        var raiz = new JsonObject
        {
            ["portada"] = new JsonObject
            {
                ["titulo"] = string.IsNullOrWhiteSpace(proyecto.Nombre) ? "Nombre del proyecto" : proyecto.Nombre.Trim(),
                ["alcance"] = "Diseño de losas",
                ["lugar_fecha"] = Unir(" · ", proyecto.Ciudad, proyecto.MesAno),
            },
            ["responsable"] = new JsonObject
            {
                ["nombre"] = (proyecto.Autor ?? "").Trim(),
                ["credencial"] = string.IsNullOrWhiteSpace(proyecto.Codia) ? "" : "CODIA " + proyecto.Codia.Trim(),
                ["contacto"] = Unir(" · ", Prefijo("Tel. ", proyecto.TelefonoFijo), Prefijo("Cel. ", proyecto.TelefonoCelular)),
                ["diseno"] = (proyecto.DisenadorArquitectonico ?? "").Trim(),
            },
            ["control"] = new JsonObject
            {
                ["codigo"] = (proyecto.CodigoObra ?? "").Trim(),
                ["fecha"] = (proyecto.MesAno ?? "").Trim(),
                ["origen"] = "Proyecto de LosasPlus" + (string.IsNullOrEmpty(proyecto.Archivo) ? "" : ": " + Path.GetFileName(proyecto.Archivo)),
            },
            ["proyecto"] = filas,
            ["materiales"] = materiales,
            ["normas"] = normas,
        };
        return raiz.ToJsonString(Opciones);
    }

    // ---- data.json -------------------------------------------------------

    private static string NombreDeNivel(Nivel nivel, Sistema sistema)
    {
        bool unico = nivel.Sistemas.Count == 1;
        string n = (nivel.Nombre ?? "").Trim(), s = (sistema.Nombre ?? "").Trim();
        if (n.Length == 0) return s.Length == 0 ? "Sistema" : s;
        return unico || s.Length == 0 ? n : $"{n} — {s}";
    }

    private static JsonObject Nivel(string nombre, Sistema sistema)
    {
        var salida = sistema.SalidaPerdomo;
        var esp = new JsonObject();
        var carga = new JsonObject();
        foreach (var l in sistema.Losas)
        {
            string id = l.Id.ToString(Inv);
            esp[id] = new JsonObject
            {
                ["Lx"] = F(l.Lx, "0.00"),
                ["Ly"] = F(l.Ly, "0.00"),
                ["cond"] = Condicion(l),
                ["h"] = F(l.Espesor, "0.00"),
            };
            carga[id] = new JsonObject { ["qu"] = F(l.Carga, "0.00") };
        }

        var mom = new JsonObject();
        var ax = new JsonObject();
        var ay = new JsonObject();
        var apx = new JsonArray();
        var apy = new JsonArray();
        if (salida is not null)
        {
            foreach (var m in salida.Momentos)
            {
                mom[m.LosaId.ToString(Inv)] = new JsonObject
                {
                    ["tipo"] = m.Tipo.ToString(Inv),
                    ["q"] = F(m.Carga, "0.000"),
                    ["H"] = F(EnCentimetros(m.H), "0.0"),
                    ["Lx"] = F(m.Lx, "0.000"),
                    ["Ly"] = F(m.Ly, "0.000"),
                    ["Mfx"] = F(m.Mfx, "0.000"),
                    ["Mfy"] = F(m.Mfy, "0.000"),
                    ["Msx"] = F(m.NMSx, "0.000"),
                    ["Msy"] = F(m.NMSy, "0.000"),
                };
            }
            foreach (var a in salida.ArmadurasXCentro) ax[a.LosaId.ToString(Inv)] = Armadura(a);
            foreach (var a in salida.ArmadurasYCentro) ay[a.LosaId.ToString(Inv)] = Armadura(a);
            foreach (var a in salida.ArmadurasXApoyos) apx.Add(Apoyo(a));
            foreach (var a in salida.ArmadurasYApoyos) apy.Add(Apoyo(a));
        }

        var meta = new JsonObject
        {
            ["sistema"] = (sistema.Nombre ?? "").Trim(),
            ["fc"] = F(sistema.Fc * 1000.0, "0.##"),      // ton/cm² → kg/cm²
            ["fy"] = F(sistema.Fy, "0.##"),               // ton/cm²
        };
        if (salida is not null)
        {
            if (!string.IsNullOrWhiteSpace(salida.ArchivoTxt)) meta["archivo"] = Path.GetFileName(salida.ArchivoTxt);
            if (!string.IsNullOrWhiteSpace(salida.VersionMotor)) meta["motor"] = salida.VersionMotor.Trim();
        }

        return new JsonObject
        {
            ["nombre"] = nombre,
            ["fuente"] = "LosasPlus",
            ["meta"] = meta,
            ["esp"] = esp,
            ["carga"] = carga,
            ["mom"] = mom,
            ["ax"] = ax,
            ["ay"] = ay,
            ["apx"] = apx,
            ["apy"] = apy,
        };
    }

    private static IEnumerable<string> Avisos(string nombre, Sistema sistema)
    {
        var salida = sistema.SalidaPerdomo;
        if (salida is null || salida.Momentos.Count == 0)
        {
            if (sistema.Losas.Count > 0)
                yield return $"Losas, nivel {nombre}: sistema sin calcular; no hay momentos ni armaduras que mostrar.";
            yield break;
        }
        if (salida.LosasNoParseadas.Count > 0)
            yield return $"Losas, nivel {nombre}: no se calcularon las losas {string.Join(", ", salida.LosasNoParseadas)} " +
                         "(tipo o proporción sin verificar contra Losas.exe).";
    }

    private static JsonObject Armadura(ArmaduraLosa a) => new()
    {
        ["d"] = F(EnCentimetros(a.D), "0.00"),
        ["Mu"] = F(a.Mu, "0.000"),
        ["As"] = F(a.As, "0.000"),
        ["barra"] = string.IsNullOrWhiteSpace(a.Disponer) ? "—" : a.Disponer.Trim(),
        ["Asd"] = F(a.AsReal, "0.000"),
    };

    private static JsonObject Apoyo(ArmaduraApoyo a) => new()
    {
        ["i"] = a.BordeI,
        ["j"] = a.BordeJ,
        ["MuI"] = F(a.MuI, "0.000"),
        ["MuJ"] = F(a.MuJ, "0.000"),
        ["Mu"] = F(a.MuIJ, "0.000"),
        ["d"] = F(EnCentimetros(a.D), "0.0"),
        ["As"] = F(a.As, "0.000"),
        ["DAs"] = F(a.DAs, "0.000"),
        ["barra"] = string.IsNullOrWhiteSpace(a.Disponer) ? "—" : a.Disponer.Trim(),
    };

    /// <summary>"1D" si una luz supera el doble de la otra, "2D" si no; "—" si falta una luz.</summary>
    private static string Condicion(Losa l)
    {
        if (l.Lx <= 0 || l.Ly <= 0) return "—";
        double relacion = Math.Max(l.Lx, l.Ly) / Math.Min(l.Lx, l.Ly);
        return relacion > RelacionUnaDireccion ? "1D" : "2D";
    }

    /// <summary>
    /// Espesor o canto útil en centímetros. <see cref="SalidaPerdomo"/> llega con
    /// dos convenciones: el cálculo nativo guarda metros y la importación del
    /// .TXT guarda los centímetros que imprime Losas.exe. Se distinguen por
    /// magnitud: una losa nunca mide 3 m de espesor ni menos de 3 cm.
    /// </summary>
    private static double EnCentimetros(double valor) => valor < UmbralMetros ? valor * 100.0 : valor;

    private const double UmbralMetros = 3.0;

    // ---- utilidades ------------------------------------------------------

    private static string F(double valor, string formato) => valor.ToString(formato, Inv);

    private static string Unir(string separador, params string?[] partes)
        => string.Join(separador, partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    private static string? Prefijo(string prefijo, string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : prefijo + valor.Trim();

    private static string? ParametrosDeSuelo(Proyecto p)
    {
        var partes = new List<string?>();
        if (p.EsfuerzoAdmisible > 0) partes.Add($"Esfuerzo admisible {p.EsfuerzoAdmisible.ToString("0.00", Inv)} kg/cm²");
        if (p.ProfundidadDesplante > 0) partes.Add($"Profundidad de desplante {p.ProfundidadDesplante.ToString("0.00", Inv)} m");
        partes.Add(p.OtrosParametros);
        string texto = Unir(" · ", partes.ToArray());
        return texto.Length == 0 ? null : texto;
    }
}
