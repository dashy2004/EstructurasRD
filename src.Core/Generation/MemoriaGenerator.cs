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
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

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
                // Recorre el árbol completo Edificios → Niveles → Sistemas vía
                // EnumerarSistemas — no sólo la fachada legacy proyecto.Sistemas
                // (= Niveles[0]), que omitía los sistemas de las plantas 2+ (B2c).
                var todosLosSistemas = Services.ProyectoService.EnumerarSistemas(proyecto).ToList();
                var (niveles, sustNivel) = RenderearNiveles(body0, todosLosSistemas);
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

            // 3c. Logo de la app en el encabezado (crea el header si la plantilla
            //     no lo trae). No-op si el recurso del logo no está embebido.
            InsertarLogoEnEncabezado(main);

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

            // Recolectamos los clones de este sistema para post-procesar tablas
            // de losas (necesitamos referencias estables a los Paragraphs ya
            // insertados en el documento).
            var clonesEsteSistema = new List<OpenXmlElement>();
            foreach (var t in template)
            {
                var clone = t.CloneNode(deep: true);
                fin.InsertBeforeSelf(clone);
                totalSust += AplicarReemplazos(clone, dict);
                clonesEsteSistema.Add(clone);
            }

            // Reemplazar placeholders de tabla por Tables reales (todos los
            // {{TABLA_*}} dentro del bloque NIVEL).
            foreach (var c in clonesEsteSistema)
            {
                RenderearTablaLosas(c, sistema);
                RenderearTablaSalidaPerdomo(c, sistema);
            }
        }

        // 5. Remover los markers (ya cumplieron su función).
        inicio.Remove();
        fin.Remove();

        return (sistemas.Count, totalSust);
    }

    /// <summary>
    /// Busca dentro de <paramref name="root"/> párrafos que contengan
    /// <c>{{TABLA_LOSAS}}</c> y los reemplaza por una <see cref="Table"/>
    /// generada con una fila por cada losa de <paramref name="sistema"/>.
    ///
    /// <para>
    /// Columnas: ID, Lx, Ly, Cond, h_usar, qd, ql, qu — los outputs computados
    /// del <c>CalculoEngine</c>. Si una losa todavía no fue recalculada
    /// (Qd / Ql / Qu en null), las celdas correspondientes muestran "—".
    /// </para>
    /// </summary>
    private static void RenderearTablaLosas(OpenXmlElement root, Sistema sistema)
    {
        // Localizar parrafos cuyo texto combinado contenga el placeholder.
        var candidatos = EnumerarParrafos(root)
            .Where(p => string.Concat(p.Descendants<Text>().Select(t => t.Text ?? ""))
                              .Contains(PlaceholderConstants.TablaLosas, StringComparison.Ordinal))
            .ToList();

        if (candidatos.Count == 0) return;

        foreach (var placeholder in candidatos)
        {
            var tabla = ConstruirTablaLosas(sistema);
            // Insertar la tabla DESPUES del placeholder y luego remover el placeholder.
            // (No se puede InsertBeforeSelf en un Paragraph hermano con Table directa
            // si el padre no permite mezclar; en Body sí — tablas son hermanas de
            // parrafos por definicion.)
            placeholder.Parent?.InsertAfter(tabla, placeholder);
            placeholder.Remove();
        }
    }

    /// <summary>Construye una <see cref="Table"/> OpenXml con header + 1 fila por losa.</summary>
    private static Table ConstruirTablaLosas(Sistema sistema)
    {
        var inv = CultureInfo.InvariantCulture;
        var tabla = NuevaTablaConBordes();

        tabla.AppendChild(FilaHeader(new[]
        {
            "ID", "Lx (m)", "Ly (m)", "Cond.", "h (m)",
            "qd (t/m²)", "ql (t/m²)", "qu (t/m²)"
        }));

        // Filas.
        foreach (var losa in sistema.Losas)
        {
            tabla.AppendChild(FilaDatos(new[]
            {
                losa.Id.ToString(inv),
                losa.Lx.ToString("0.00", inv),
                losa.Ly.ToString("0.00", inv),
                losa.Cond,
                losa.Espesor.ToString("0.000", inv),
                losa.Qd.HasValue ? losa.Qd.Value.ToString("0.000", inv) : "—",
                losa.Ql.HasValue ? losa.Ql.Value.ToString("0.000", inv) : "—",
                losa.Qu.HasValue ? losa.Qu.Value.ToString("0.000", inv) : "—",
            }));
        }

        return tabla;
    }

    // =====================================================================
    // RENDER DE TABLAS DE SALIDA F. PERDOMO
    //   {{TABLA_MOMENTOS}}, {{TABLA_ARMADURAS_X}}, {{TABLA_ARMADURAS_Y}},
    //   {{TABLA_APOYOS}} — leen de sistema.SalidaPerdomo.
    // =====================================================================

    /// <summary>
    /// Reemplaza los placeholders <c>{{TABLA_MOMENTOS}}</c>,
    /// <c>{{TABLA_ARMADURAS_X}}</c>, <c>{{TABLA_ARMADURAS_Y}}</c> y
    /// <c>{{TABLA_APOYOS}}</c> por tablas OpenXml generadas a partir de
    /// <see cref="Sistema.SalidaPerdomo"/>.
    ///
    /// <para>
    /// Si el sistema no tiene <c>SalidaPerdomo</c> asociada (el ingeniero aún
    /// no importó el <c>.txt</c>), cada placeholder se reemplaza por un
    /// párrafo informativo "(.txt no importado para este nivel)" — la memoria
    /// se genera de todos modos pero queda evidente la información faltante.
    /// </para>
    /// </summary>
    private static void RenderearTablaSalidaPerdomo(OpenXmlElement root, Sistema sistema)
    {
        var salida = sistema.SalidaPerdomo;

        ReemplazarPlaceholderConTabla(root, PlaceholderConstants.TablaMomentos,
            salida is null
                ? null
                : ConstruirTablaMomentos(salida));

        ReemplazarPlaceholderConTabla(root, PlaceholderConstants.TablaArmadurasX,
            salida is null
                ? null
                : ConstruirTablaArmaduras(salida.ArmadurasXCentro, "X"));

        ReemplazarPlaceholderConTabla(root, PlaceholderConstants.TablaArmadurasY,
            salida is null
                ? null
                : ConstruirTablaArmaduras(salida.ArmadurasYCentro, "Y"));

        ReemplazarPlaceholderConTabla(root, PlaceholderConstants.TablaApoyos,
            salida is null
                ? null
                : ConstruirTablaApoyos(salida));
    }

    /// <summary>
    /// Localiza los párrafos con <paramref name="placeholder"/> dentro de
    /// <paramref name="root"/>. Si <paramref name="tabla"/> no es null, los
    /// reemplaza por la tabla; si es null (no hay datos), los reemplaza por
    /// una nota informativa.
    /// </summary>
    private static void ReemplazarPlaceholderConTabla(
        OpenXmlElement root, string placeholder, Table? tabla)
    {
        var candidatos = EnumerarParrafos(root)
            .Where(p => string.Concat(p.Descendants<Text>().Select(t => t.Text ?? ""))
                              .Contains(placeholder, StringComparison.Ordinal))
            .ToList();

        foreach (var ph in candidatos)
        {
            if (tabla is not null)
            {
                ph.Parent?.InsertAfter(tabla.CloneNode(deep: true), ph);
            }
            else
            {
                ph.Parent?.InsertAfter(
                    new Paragraph(new Run(new Text("(.txt no importado para este nivel)"))),
                    ph);
            }
            ph.Remove();
        }
    }

    private static Table ConstruirTablaMomentos(SalidaPerdomo s)
    {
        var inv = CultureInfo.InvariantCulture;
        var tabla = NuevaTablaConBordes();

        tabla.AppendChild(FilaHeader(new[]
        {
            "LOSA", "TIPO", "CARGA (t/m²)", "H (cm)", "Lx (m)", "Ly (m)",
            "Mfx", "Mfy", "-Msx", "-Msy"
        }));

        foreach (var m in s.Momentos)
        {
            tabla.AppendChild(FilaDatos(new[]
            {
                m.LosaId.ToString(inv),
                m.Tipo.ToString(inv),
                m.Carga.ToString("0.000", inv),
                m.H.ToString("0.0", inv),
                m.Lx.ToString("0.000", inv),
                m.Ly.ToString("0.000", inv),
                m.Mfx.ToString("0.000", inv),
                m.Mfy.ToString("0.000", inv),
                m.NMSx.ToString("0.000", inv),
                m.NMSy.ToString("0.000", inv),
            }));
        }

        return tabla;
    }

    private static Table ConstruirTablaArmaduras(IList<ArmaduraLosa> armaduras, string direccion)
    {
        var inv = CultureInfo.InvariantCulture;
        var tabla = NuevaTablaConBordes();

        tabla.AppendChild(FilaHeader(new[]
        {
            "LOSA", $"d{direccion} (cm)", $"Mu{direccion} (t·m/m)",
            $"As{direccion} req (cm²/m)", "Disponer", $"As{direccion} prov (cm²/m)"
        }));

        foreach (var a in armaduras)
        {
            tabla.AppendChild(FilaDatos(new[]
            {
                a.LosaId.ToString(inv),
                a.D.ToString("0.0", inv),
                a.Mu.ToString("0.000", inv),
                a.As.ToString("0.000", inv),
                a.Disponer ?? "",
                a.AsReal.ToString("0.000", inv),
            }));
        }

        return tabla;
    }

    private static Table ConstruirTablaApoyos(SalidaPerdomo s)
    {
        var inv = CultureInfo.InvariantCulture;
        var tabla = NuevaTablaConBordes();

        tabla.AppendChild(FilaHeader(new[]
        {
            "Dir.", "I", "J", "MuI", "MuJ", "MuI-J", "d (cm)", "As", "AsI", "AsJ", "ΔAs", "Disponer"
        }));

        foreach (var a in s.ArmadurasXApoyos)
            tabla.AppendChild(FilaApoyo("X", a, inv));
        foreach (var a in s.ArmadurasYApoyos)
            tabla.AppendChild(FilaApoyo("Y", a, inv));

        return tabla;
    }

    private static TableRow FilaApoyo(string dir, ArmaduraApoyo a, CultureInfo inv) => FilaDatos(new[]
    {
        dir,
        a.BordeI.ToString(inv),
        a.BordeJ.ToString(inv),
        a.MuI.ToString("0.000", inv),
        a.MuJ.ToString("0.000", inv),
        a.MuIJ.ToString("0.000", inv),
        a.D.ToString("0.0", inv),
        a.As.ToString("0.000", inv),
        a.AsI.ToString("0.000", inv),
        a.AsJ.ToString("0.000", inv),
        a.DAs.ToString("0.000", inv),
        a.Disponer ?? "",
    });

    /// <summary>Crea una <see cref="Table"/> con bordes 1pt en todas las celdas.</summary>
    private static Table NuevaTablaConBordes()
    {
        var t = new Table();
        t.AppendChild(new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                new RightBorder  { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 }
            )
        ));
        return t;
    }

    /// <summary>Crea un <see cref="TableRow"/> con celdas de cabecera (texto en bold).</summary>
    private static TableRow FilaHeader(string[] textos)
    {
        var row = new TableRow();
        foreach (var t in textos)
        {
            var run = new Run(new Text(t));
            run.PrependChild(new RunProperties(new Bold()));
            row.AppendChild(new TableCell(new Paragraph(run)));
        }
        return row;
    }

    /// <summary>Crea un <see cref="TableRow"/> con celdas de datos (texto plano).</summary>
    private static TableRow FilaDatos(string[] textos)
    {
        var row = new TableRow();
        foreach (var t in textos)
        {
            row.AppendChild(new TableCell(new Paragraph(new Run(new Text(t)))));
        }
        return row;
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

    // =====================================================================
    // LOGO EN EL ENCABEZADO
    // =====================================================================

    /// <summary>Recurso embebido (PNG) del logo de la aplicación, en LosasPlus.Core.</summary>
    private const string RecursoLogo = "LosasPlus.Resources.EstructurasRD.png";

    /// <summary>Lado del logo en el encabezado (cuadrado): 1.8 cm en EMUs (1 cm = 360000).</summary>
    private const long LogoLadoEmu = 648000L;

    private static byte[]? CargarLogo()
    {
        using var s = typeof(MemoriaGenerator).Assembly.GetManifestResourceStream(RecursoLogo);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Inserta el logo de la aplicación en el encabezado del documento. Si la
    /// plantilla no trae encabezado (caso de la plantilla actual), crea uno y lo
    /// referencia en las propiedades de sección del cuerpo. Es no-op si el
    /// recurso del logo no está embebido.
    /// </summary>
    private static void InsertarLogoEnEncabezado(MainDocumentPart main)
    {
        var logo = CargarLogo();
        if (logo is null || main.Document.Body is not { } body) return;

        // 1. Header part + imagen embebida.
        var headerPart = main.AddNewPart<HeaderPart>();
        string headerRelId = main.GetIdOfPart(headerPart);

        var imagePart = headerPart.AddImagePart(ImagePartType.Png);
        using (var ms = new MemoryStream(logo)) imagePart.FeedData(ms);
        string imageRelId = headerPart.GetIdOfPart(imagePart);

        // 2. Encabezado con el logo alineado a la derecha.
        headerPart.Header = new Header(
            new Paragraph(
                new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new Run(CrearDrawingLogo(imageRelId))));
        headerPart.Header.Save();

        // 3. Referenciar el header en el sectPr del cuerpo (crearlo si falta).
        var sectPr = body.Elements<SectionProperties>().LastOrDefault();
        if (sectPr is null)
        {
            sectPr = new SectionProperties();
            body.Append(sectPr);
        }
        sectPr.RemoveAllChildren<HeaderReference>();
        sectPr.PrependChild(new HeaderReference { Type = HeaderFooterValues.Default, Id = headerRelId });
    }

    /// <summary>Construye el <see cref="Drawing"/> inline del logo referenciando la imagen embebida.</summary>
    private static Drawing CrearDrawingLogo(string imageRelId) => new(
        new DW.Inline(
            new DW.Extent { Cx = LogoLadoEmu, Cy = LogoLadoEmu },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = 1U, Name = "Logo EstructurasRD" },
            new DW.NonVisualGraphicFrameDrawingProperties(
                new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(
                new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = "EstructurasRD.png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = imageRelId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = LogoLadoEmu, Cy = LogoLadoEmu }),
                            new A.PresetGeometry(new A.AdjustValueList())
                            { Preset = A.ShapeTypeValues.Rectangle }))
                ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        });
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
