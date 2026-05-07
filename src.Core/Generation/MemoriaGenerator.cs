using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LosasPlus.Models;

namespace LosasPlus.Generation;

/// <summary>
/// Generador de memorias de cálculo en formato <c>.docx</c>. Toma una plantilla
/// con placeholders <c>{{KEY}}</c> y produce un documento nuevo con los valores
/// del <see cref="Proyecto"/> sustituidos.
///
/// <para>
/// Algoritmo: para cada <see cref="Paragraph"/> del documento (incluyendo
/// headers y footers), se ejecutan dos pasadas:
/// </para>
/// <list type="number">
///   <item><b>Normalización de runs</b>: Word frecuentemente fragmenta
///         <c>{{KEY}}</c> entre múltiples <c>&lt;w:r&gt;</c> al guardar — por
///         cambios de formato invisibles, autocorrección, etc. Antes de
///         reemplazar, se mueven todos los caracteres del placeholder al primer
///         run de cada match, dejando los runs intermedios en blanco. Esto
///         garantiza que el placeholder esté contenido en un solo
///         <see cref="Text"/>.</item>
///   <item><b>Reemplazo literal</b>: con los placeholders ya enteros, se hace
///         un <c>String.Replace</c> ordinal por cada par
///         <c>{{KEY}} → valor</c>.</item>
/// </list>
///
/// <para>
/// El generador no modifica la plantilla original — copia primero a
/// <c>outputPath</c> y opera sobre la copia.
/// </para>
/// </summary>
public sealed class MemoriaGenerator
{
    /// <summary>
    /// Genera la memoria sustituyendo los placeholders en
    /// <paramref name="plantillaPath"/> y guardando el resultado en
    /// <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="proyecto">Datos del proyecto con todos los campos populados.</param>
    /// <param name="plantillaPath">Ruta a la plantilla <c>.docx</c> origen. NO se modifica.</param>
    /// <param name="outputPath">Ruta del <c>.docx</c> destino. Se sobrescribe si existe.</param>
    /// <param name="fechaCalculo">Fecha que se usa para <c>{{DD/MM/AAAA}}</c>. Default: <see cref="DateTime.Now"/>.</param>
    /// <returns>Reporte con el detalle de placeholders sustituidos.</returns>
    public ReporteGeneracion Generar(
        Proyecto proyecto,
        string plantillaPath,
        string outputPath,
        DateTime? fechaCalculo = null)
    {
        if (proyecto is null)        throw new ArgumentNullException(nameof(proyecto));
        if (plantillaPath is null)   throw new ArgumentNullException(nameof(plantillaPath));
        if (outputPath is null)      throw new ArgumentNullException(nameof(outputPath));
        if (!File.Exists(plantillaPath))
            throw new FileNotFoundException($"Plantilla no encontrada: {plantillaPath}", plantillaPath);

        // 1. Copiar plantilla → destino (NO se modifica la plantilla original).
        var dstDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
        File.Copy(plantillaPath, outputPath, overwrite: true);

        // 2. Construir tabla de reemplazos.
        var reemplazos = ConstruirReemplazos(proyecto, fechaCalculo ?? DateTime.Now);

        // 3. Aplicar a body, headers y footers.
        var reporte = new ReporteGeneracion(outputPath, plantillaPath);
        using (var doc = WordprocessingDocument.Open(outputPath, isEditable: true))
        {
            var main = doc.MainDocumentPart
                ?? throw new InvalidOperationException("Plantilla sin MainDocumentPart");

            // 3a. Render plurinivel: si la plantilla tiene los markers
            //     {{NIVEL_BLOQUE_INICIO}} y {{NIVEL_BLOQUE_FIN}}, clona el bloque
            //     una vez por sistema y sustituye los placeholders de nivel.
            //     Si no hay markers, el método es no-op (compat con plantillas
            //     simples que solo tienen los 17 placeholders de portada).
            int totalSust = 0;
            if (main.Document.Body is { } body0)
            {
                var (niveles, sustNivel) = RenderearNiveles(body0, proyecto.Sistemas);
                reporte.NivelesRenderizados = niveles;
                totalSust += sustNivel;
            }

            // 3b. Aplicar reemplazos de portada/descripcion. Estos corren DESPUES
            //     del render plurinivel para que los clones (ya replicados) tambien
            //     reciban substituciones globales en caso de tenerlas.
            if (main.Document.Body is { } body)
                totalSust += AplicarReemplazos(body, reemplazos);

            foreach (var hp in main.HeaderParts)
                if (hp.Header is { } header)
                    totalSust += AplicarReemplazos(header, reemplazos);

            foreach (var fp in main.FooterParts)
                if (fp.Footer is { } footer)
                    totalSust += AplicarReemplazos(footer, reemplazos);

            main.Document.Save();
            reporte.SustitucionesAplicadas = totalSust;

            // 4. Detectar placeholders huérfanos (definidos en la plantilla pero no
            //    cubiertos por la tabla de reemplazos).
            var conocidos = reemplazos.Keys.Concat(new[]
            {
                PlaceholderConstants.NivelBloqueInicio,
                PlaceholderConstants.NivelBloqueFin,
            }).Concat(PlaceholderConstants.TodosNivel);
            reporte.PlaceholdersNoSustituidos = DetectarPlaceholdersHuerfanos(main, conocidos);
        }

        return reporte;
    }

    // =====================================================================
    // RENDER PLURINIVEL
    // =====================================================================

    /// <summary>
    /// Si la plantilla contiene los markers <c>{{NIVEL_BLOQUE_INICIO}}</c> y
    /// <c>{{NIVEL_BLOQUE_FIN}}</c>, clona el contenido entre ellos una vez por
    /// cada <see cref="Sistema"/> de <paramref name="sistemas"/>, y sustituye
    /// los placeholders de nivel (<c>{{NIVEL_NOMBRE}}</c>, etc.) en cada clon.
    /// Después remueve los markers y el contenido original del bloque.
    ///
    /// <para>
    /// Devuelve la cantidad de niveles renderizados. Si los markers no están en
    /// la plantilla, devuelve 0 (no-op gracioso).
    /// </para>
    ///
    /// <para>
    /// <b>Convención de los markers</b>: cada uno debe vivir en su propio párrafo
    /// (texto plano del marker, sin más contenido). Esto permite remover el
    /// párrafo completo del marker sin tocar el contenido adyacente.
    /// </para>
    /// </summary>
    private static (int Niveles, int Sustituciones) RenderearNiveles(OpenXmlElement body, IList<Sistema> sistemas)
    {
        // 1. Encontrar parrafos marker (texto = marker, ignorando espacios).
        var (inicio, fin) = EncontrarMarkersNivel(body);
        if (inicio is null || fin is null) return (0, 0);

        // 2. Recolectar elementos entre los markers (siblings).
        var template = new List<OpenXmlElement>();
        var node = inicio.NextSibling();
        while (node is not null && node != fin)
        {
            template.Add(node);
            node = node.NextSibling();
        }

        // 3. Remover el contenido original del bloque (lo vamos a re-insertar
        //    como clones para mantener un loop simétrico).
        foreach (var el in template) el.Remove();

        // 4. Por cada sistema, clonar template, sustituir placeholders, e
        //    insertar antes del marker fin.
        int totalSust = 0;
        for (int i = 0; i < sistemas.Count; i++)
        {
            var sistema = sistemas[i];
            var dict = ConstruirReemplazosNivel(sistema, indiceUnoBased: i + 1);

            foreach (var t in template)
            {
                var clone = t.CloneNode(deep: true);
                fin.InsertBeforeSelf(clone);
                totalSust += AplicarReemplazos(clone, dict);
            }
        }

        // 5. Remover los markers (ya cumplieron su función).
        inicio.Remove();
        fin.Remove();

        return (sistemas.Count, totalSust);
    }

    /// <summary>
    /// Localiza los párrafos que contienen <c>{{NIVEL_BLOQUE_INICIO}}</c> y
    /// <c>{{NIVEL_BLOQUE_FIN}}</c>. Devuelve <c>(null, null)</c> si alguno
    /// no aparece. Ambos markers se buscan en el <see cref="Body"/>; cualquier
    /// otra ubicación (header, footer) se ignora — el render plurinivel solo
    /// aplica al cuerpo del documento.
    /// </summary>
    private static (Paragraph? inicio, Paragraph? fin) EncontrarMarkersNivel(OpenXmlElement body)
    {
        Paragraph? inicio = null, fin = null;
        foreach (var p in body.Descendants<Paragraph>())
        {
            var texto = string.Concat(p.Descendants<Text>().Select(t => t.Text ?? "")).Trim();
            if (inicio is null && texto.Contains(PlaceholderConstants.NivelBloqueInicio, StringComparison.Ordinal))
                inicio = p;
            else if (fin is null && texto.Contains(PlaceholderConstants.NivelBloqueFin, StringComparison.Ordinal))
                fin = p;
            if (inicio is not null && fin is not null) break;
        }
        return (inicio, fin);
    }

    /// <summary>
    /// Construye la tabla de reemplazos por-nivel a partir de un
    /// <see cref="Sistema"/>.
    /// </summary>
    private static Dictionary<string, string> ConstruirReemplazosNivel(Sistema s, int indiceUnoBased)
    {
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlaceholderConstants.NivelNombre]       = s.Nombre ?? "",
            [PlaceholderConstants.NivelNumero]       = indiceUnoBased.ToString(inv),
            [PlaceholderConstants.NivelUso]          = s.Uso.ToString(),
            [PlaceholderConstants.NivelCota]         = $"+{s.CotaMetros.ToString("0.00", inv)} m",
            [PlaceholderConstants.NivelNumeroLosas]  = s.Losas.Count.ToString(inv),
        };
    }

    /// <summary>
    /// Construye la tabla <c>{{KEY}} → valor</c> a partir de un <see cref="Proyecto"/>.
    /// Los valores numéricos se formatean con <see cref="CultureInfo.InvariantCulture"/>
    /// y dos decimales para que el <c>.docx</c> sea byte-estable independientemente
    /// del locale del Windows del ingeniero.
    /// </summary>
    private static Dictionary<string, string> ConstruirReemplazos(Proyecto p, DateTime fecha)
    {
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PlaceholderConstants.NombreProyecto]       = p.Nombre                  ?? "",
            [PlaceholderConstants.CiudadUbicacion]      = p.Ciudad                  ?? "",
            [PlaceholderConstants.MesAno]               = p.MesAno                  ?? "",
            [PlaceholderConstants.UbicacionCompleta]    = p.UbicacionCompleta       ?? "",
            [PlaceholderConstants.Uso]                  = p.Uso                     ?? "",
            [PlaceholderConstants.CantidadNiveles]      = p.CantidadNiveles.ToString(inv),
            [PlaceholderConstants.SistemaEstructural]   = p.SistemaEstructural      ?? "",
            [PlaceholderConstants.NombreIngeniero]      = p.Autor                   ?? "",
            [PlaceholderConstants.Codia]                = p.Codia                   ?? "",
            [PlaceholderConstants.TelFijo]              = p.TelefonoFijo            ?? "",
            [PlaceholderConstants.TelCelular]           = p.TelefonoCelular         ?? "",
            [PlaceholderConstants.NombreDisenadorArq]   = p.DisenadorArquitectonico ?? "",
            [PlaceholderConstants.TipoFundaciones]      = p.TipoFundaciones         ?? "",
            [PlaceholderConstants.EsfuerzoAdmisible]    = p.EsfuerzoAdmisible.ToString("0.00", inv),
            [PlaceholderConstants.ProfundidadDesplante] = p.ProfundidadDesplante.ToString("0.00", inv),
            [PlaceholderConstants.OtrosParametros]      = p.OtrosParametros         ?? "",
            [PlaceholderConstants.FechaDdMmAaaa]        = fecha.ToString("dd/MM/yyyy", inv),
        };
    }

    /// <summary>
    /// Aplica los reemplazos a todos los párrafos descendientes — incluyendo el
    /// propio <paramref name="root"/> si éste es un <see cref="Paragraph"/>.
    /// Devuelve la cantidad de sustituciones aplicadas.
    /// </summary>
    private static int AplicarReemplazos(OpenXmlElement root, Dictionary<string, string> reemplazos)
    {
        int total = 0;
        foreach (var paragraph in EnumerarParrafos(root))
        {
            NormalizarParrafo(paragraph);
            total += ReemplazarTextos(paragraph, reemplazos);
        }
        return total;
    }

    /// <summary>
    /// Yields self-or-descendants Paragraphs. Necesario porque
    /// <see cref="OpenXmlElement.Descendants{T}()"/> no incluye al propio
    /// elemento — y los clones del bloque NIVEL pueden ser Paragraphs raíz.
    /// </summary>
    private static IEnumerable<Paragraph> EnumerarParrafos(OpenXmlElement root)
    {
        if (root is Paragraph rootP) yield return rootP;
        foreach (var p in root.Descendants<Paragraph>()) yield return p;
    }

    /// <summary>
    /// Mueve los caracteres de cada placeholder fragmentado a su primer run, dejando
    /// los runs intermedios con texto vacío. Tras esta operación, cada
    /// <c>{{KEY}}</c> está garantizado de existir dentro de un solo <see cref="Text"/>.
    /// </summary>
    private static void NormalizarParrafo(Paragraph paragraph)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count <= 1) return;

        // Construir representación combinada y mapping char→run.
        (StringBuilder combinedSb, List<int> charToRun) BuildIndex()
        {
            var sb = new StringBuilder();
            var c2r = new List<int>();
            for (int i = 0; i < texts.Count; i++)
            {
                var s = texts[i].Text ?? "";
                sb.Append(s);
                for (int k = 0; k < s.Length; k++) c2r.Add(i);
            }
            return (sb, c2r);
        }

        var (combinedBuilder, charToRunMap) = BuildIndex();
        var combined = combinedBuilder.ToString();

        int searchFrom = 0;
        while (true)
        {
            int open  = combined.IndexOf("{{", searchFrom, StringComparison.Ordinal);
            if (open < 0) break;
            int close = combined.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0) break;
            int placeholderEnd = close + 2;

            int firstRun = charToRunMap[open];
            int lastRun  = charToRunMap[placeholderEnd - 1];

            if (firstRun == lastRun)
            {
                // Ya está contenido en un solo run — nada que normalizar.
                searchFrom = placeholderEnd;
                continue;
            }

            // Calcular offsets dentro de cada run.
            int firstRunStart = 0;
            for (int i = 0; i < firstRun; i++) firstRunStart += texts[i].Text?.Length ?? 0;
            int relOpen = open - firstRunStart;

            int lastRunStart = 0;
            for (int i = 0; i < lastRun; i++) lastRunStart += texts[i].Text?.Length ?? 0;
            int relClose = placeholderEnd - lastRunStart;

            string firstPrefix = (texts[firstRun].Text ?? "").Substring(0, relOpen);
            string lastSuffix  = (texts[lastRun].Text  ?? "").Substring(relClose);
            string placeholderText = combined.Substring(open, placeholderEnd - open);

            texts[firstRun].Text  = firstPrefix + placeholderText;
            texts[firstRun].Space = SpaceProcessingModeValues.Preserve;
            for (int i = firstRun + 1; i < lastRun; i++) texts[i].Text = "";
            texts[lastRun].Text   = lastSuffix;
            if (!string.IsNullOrEmpty(lastSuffix))
                texts[lastRun].Space = SpaceProcessingModeValues.Preserve;

            // Re-construir indice porque las longitudes cambiaron.
            (combinedBuilder, charToRunMap) = BuildIndex();
            combined = combinedBuilder.ToString();
            searchFrom = open + placeholderText.Length;
        }
    }

    /// <summary>
    /// Recorre cada <see cref="Text"/> del párrafo y aplica
    /// <see cref="string.Replace(string, string?)"/> ordinal para cada placeholder.
    /// Devuelve el número de sustituciones efectivas.
    /// </summary>
    private static int ReemplazarTextos(Paragraph paragraph, Dictionary<string, string> reemplazos)
    {
        int count = 0;
        foreach (var t in paragraph.Descendants<Text>())
        {
            var original = t.Text ?? "";
            var actual = original;
            foreach (var (placeholder, replacement) in reemplazos)
            {
                if (actual.Contains(placeholder, StringComparison.Ordinal))
                {
                    int found = (actual.Length - actual.Replace(placeholder, "", StringComparison.Ordinal).Length)
                                / placeholder.Length;
                    actual = actual.Replace(placeholder, replacement ?? "", StringComparison.Ordinal);
                    count += found;
                }
            }
            if (!ReferenceEquals(actual, original) && actual != original)
            {
                t.Text  = actual;
                t.Space = SpaceProcessingModeValues.Preserve;
            }
        }
        return count;
    }

    /// <summary>
    /// Recorre el documento y reporta cualquier <c>{{...}}</c> que haya quedado
    /// sin sustituir (placeholder en plantilla pero no en
    /// <paramref name="conocidos"/>).
    /// </summary>
    private static List<string> DetectarPlaceholdersHuerfanos(MainDocumentPart main, IEnumerable<string> conocidos)
    {
        var conocidosSet = new HashSet<string>(conocidos, StringComparer.Ordinal);
        var huerfanos = new HashSet<string>(StringComparer.Ordinal);

        void Scan(OpenXmlElement? root)
        {
            if (root is null) return;
            foreach (var p in root.Descendants<Paragraph>())
            {
                var combined = string.Concat(p.Descendants<Text>().Select(t => t.Text ?? ""));
                int from = 0;
                while (true)
                {
                    int open = combined.IndexOf("{{", from, StringComparison.Ordinal);
                    if (open < 0) break;
                    int close = combined.IndexOf("}}", open + 2, StringComparison.Ordinal);
                    if (close < 0) break;
                    var ph = combined.Substring(open, close + 2 - open);
                    if (!conocidosSet.Contains(ph)) huerfanos.Add(ph);
                    from = close + 2;
                }
            }
        }

        Scan(main.Document.Body);
        foreach (var hp in main.HeaderParts) Scan(hp.Header);
        foreach (var fp in main.FooterParts) Scan(fp.Footer);

        return huerfanos.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}

/// <summary>
/// Resumen del resultado de una corrida del <see cref="MemoriaGenerator"/>.
/// Útil para mostrar status en la pestaña Generar y para asserts en tests.
/// </summary>
public sealed class ReporteGeneracion
{
    public ReporteGeneracion(string outputPath, string plantillaPath)
    {
        OutputPath    = outputPath;
        PlantillaPath = plantillaPath;
    }

    public string OutputPath    { get; }
    public string PlantillaPath { get; }

    /// <summary>Cantidad total de placeholders sustituidos en el documento.</summary>
    public int SustitucionesAplicadas { get; set; }

    /// <summary>
    /// Cantidad de niveles renderizados (clones del bloque NIVEL). Si la
    /// plantilla no tiene markers <c>{{NIVEL_BLOQUE_*}}</c>, queda en 0 — el
    /// generador siguió funcionando solo con los placeholders de portada.
    /// </summary>
    public int NivelesRenderizados { get; set; }

    /// <summary>
    /// Placeholders <c>{{...}}</c> que aparecen en la plantilla pero no estaban
    /// en la tabla de reemplazos del generador. Idealmente vacío.
    /// </summary>
    public List<string> PlaceholdersNoSustituidos { get; set; } = new();

    /// <summary>True si la generación cubrió todos los placeholders de la plantilla.</summary>
    public bool Exito => PlaceholdersNoSustituidos.Count == 0;
}
