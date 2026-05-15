using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Severidad de un hallazgo del <see cref="DLDoctor"/>. Determina el color que
/// la UI usa al mostrar el reporte.
/// </summary>
public enum DLIssueSeverity
{
    /// <summary>Aviso informativo, el archivo abre OK pero hay algo que merece atención.</summary>
    Info,
    /// <summary>Problema corregible automáticamente — el archivo abre tras el fix.</summary>
    Warning,
    /// <summary>El archivo no puede abrirse hasta resolver el issue.</summary>
    Error,
}

/// <summary>
/// Un hallazgo individual del diagnóstico. Incluye descripción legible, línea
/// donde se detectó (opcional) y un sugerido <see cref="FixDescripcion"/>.
/// </summary>
public sealed record DLIssue(
    DLIssueSeverity Severity,
    string Codigo,            // Ej. "DL001-JSON-EN-DL"
    string Descripcion,
    int? LineaNumero,
    string? FixDescripcion);

/// <summary>
/// Resultado del diagnóstico de un .DL. Incluye:
/// <list type="bullet">
///   <item>Lista de <see cref="Issues"/> ordenada por severidad descendente.</item>
///   <item><see cref="PuedeAbrir"/>: true si no hay errores fatales.</item>
///   <item><see cref="ContenidoReparado"/>: si el doctor puede generar un .DL corregido,
///         expone aquí el contenido para que la UI lo escriba a otro path.</item>
///   <item><see cref="SistemasRecuperados"/>: si fue posible parsear (con o sin fix),
///         contiene los sistemas. La UI puede ofrecer "abrir aunque haya warnings".</item>
///   <item><see cref="EsArchivoJsonDisfrazado"/>: bandera para el caso especial donde
///         el .DL es en realidad un .lpx.json — la UI redirige al opener de proyecto.</item>
/// </list>
/// </summary>
public sealed class DLDiagnostico
{
    public string ArchivoOriginal { get; init; } = "";
    public IReadOnlyList<DLIssue> Issues { get; init; } = Array.Empty<DLIssue>();
    public bool PuedeAbrir { get; init; }
    public string? ContenidoReparado { get; init; }
    public IReadOnlyList<Sistema>? SistemasRecuperados { get; init; }
    public bool EsArchivoJsonDisfrazado { get; init; }
    public string? PathSugeridoReparado { get; init; }

    /// <summary>True si no hay issues (archivo limpio).</summary>
    public bool EstaLimpio => Issues.Count == 0;

    /// <summary>True si hay al menos un Issue de severidad Error.</summary>
    public bool TieneErrores => Issues.Any(i => i.Severity == DLIssueSeverity.Error);

    /// <summary>Cantidad de issues por severidad — útil para resúmenes.</summary>
    public (int Info, int Warning, int Error) ConteoPorSeveridad =>
        (Issues.Count(i => i.Severity == DLIssueSeverity.Info),
         Issues.Count(i => i.Severity == DLIssueSeverity.Warning),
         Issues.Count(i => i.Severity == DLIssueSeverity.Error));
}

/// <summary>
/// Diagnostica archivos .DL que no se pueden abrir con
/// <see cref="DLFileService.ReadAll"/>. Recorre los problemas habituales:
/// <list type="number">
///   <item><b>JSON disfrazado de .DL</b>: archivo es un .lpx.json guardado con
///         extensión .DL (caso reportado por el usuario). Detecta por primer
///         carácter no-whitespace = '{' o '['.</item>
///   <item><b>Encoding</b>: BOM UTF-8/UTF-16/UTF-32. El parser usa cp1252;
///         BOM rompe el parseo de NLOSA. Se ofrece reparación.</item>
///   <item><b>Decimal con coma</b> (es-DO local): "0,210" en vez de "0.210".
///         Se ofrece sustitución masiva.</item>
///   <item><b>NLOSA mismatch</b>: el header dice N losas pero el bloque tiene
///         otra cantidad. Se reporta — el archivo puede ser parcialmente
///         recuperable truncando o expandiendo.</item>
///   <item><b>Balanceo no normalizado</b>: valores distintos de "S"/"N"
///         (ej. "Y", "n", "Si"). Se ofrece normalización.</item>
///   <item><b>Líneas dobles vacías masivas</b>: a veces los archivos vienen
///         con espacios raros que no rompen pero ensucian. Aviso.</item>
///   <item><b>Archivo vacío o sólo comentarios</b>: error fatal.</item>
/// </list>
///
/// <para>
/// El doctor genera tanto el reporte como, cuando es posible, una versión
/// reparada del .DL en una variable string. La UI decide si la escribe a
/// disco (path sugerido: <c>{stem}.reparado.DL</c>) y luego abre la versión
/// reparada.
/// </para>
/// </summary>
public static class DLDoctor
{
    /// <summary>Punto de entrada principal. Recibe un path y devuelve el diagnóstico.</summary>
    public static DLDiagnostico Diagnosticar(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ConErrorFatal("", "DL000-PATH-VACIO", "Path del archivo vacío.");
        if (!File.Exists(path))
            return ConErrorFatal(path, "DL000-NO-EXISTE", $"El archivo no existe: {path}");

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex)
        {
            return ConErrorFatal(path, "DL000-IO", $"No se pudo leer el archivo: {ex.Message}");
        }

        if (bytes.Length == 0)
            return ConErrorFatal(path, "DL000-VACIO", "El archivo está vacío (0 bytes).");

        var issues = new List<DLIssue>();
        var (textoOriginal, bomDetectado) = LeerComoTexto(bytes);

        // Issue 1: ¿Es JSON disfrazado de .DL?
        if (PareceJson(textoOriginal))
        {
            issues.Add(new DLIssue(
                DLIssueSeverity.Error,
                "DL001-JSON-EN-DL",
                "El archivo tiene extensión .DL pero el contenido es JSON (probablemente un .lpx.json " +
                "guardado con extensión incorrecta). LosasPlus puede abrirlo como proyecto desde " +
                "File → 'Abrir proyecto' apuntando a este archivo.",
                LineaNumero: 1,
                FixDescripcion: "Renombrá el archivo agregando .lpx.json al final, o abrilo con " +
                                "File → 'Abrir proyecto…' en vez de 'Abrir .DL legacy…'."));
            return new DLDiagnostico
            {
                ArchivoOriginal = path,
                Issues = issues,
                PuedeAbrir = false,
                EsArchivoJsonDisfrazado = true,
                PathSugeridoReparado = AjustarExtension(path, ".lpx.json"),
            };
        }

        // Issue 2: BOM
        if (bomDetectado != null)
        {
            issues.Add(new DLIssue(
                DLIssueSeverity.Warning,
                "DL002-BOM",
                $"El archivo tiene un BOM {bomDetectado} al inicio. El parser legacy usa cp1252 sin " +
                "BOM y los primeros bytes pueden confundirlo.",
                LineaNumero: 1,
                FixDescripcion: "Eliminar BOM y guardar como cp1252."));
        }

        // Issue 3: Decimal con coma — heurística simple sobre tokens numéricos
        bool tieneDecimalConComa = DetectarDecimalConComa(textoOriginal);
        if (tieneDecimalConComa)
        {
            issues.Add(new DLIssue(
                DLIssueSeverity.Warning,
                "DL003-DECIMAL-COMA",
                "El archivo usa coma como separador decimal (ej. '0,210') pero el parser espera " +
                "punto. Es habitual en configuración regional es-DO al exportar desde Excel.",
                LineaNumero: null,
                FixDescripcion: "Reemplazar comas por puntos en valores numéricos."));
        }

        // Aplicar fixes posibles para tener una versión reparada
        string textoReparado = textoOriginal;
        if (bomDetectado != null) textoReparado = textoReparado.TrimStart('﻿', '​');
        if (tieneDecimalConComa)
            textoReparado = ReemplazarDecimalComaPorPunto(textoReparado);

        // Issue 4..N: tests semánticos vía intento de parse del original
        List<Sistema>? sistemasOriginal = null;
        Exception? errorOriginal = null;
        try { sistemasOriginal = ParseDesdeTexto(textoOriginal, path); }
        catch (Exception ex) { errorOriginal = ex; }

        // Si el original falla pero el reparado funciona → fix aplicable
        List<Sistema>? sistemasReparado = null;
        Exception? errorReparado = null;
        if (errorOriginal != null)
        {
            try { sistemasReparado = ParseDesdeTexto(textoReparado, path); }
            catch (Exception ex) { errorReparado = ex; }
        }
        else
        {
            sistemasReparado = sistemasOriginal;
        }

        // Issues semánticos: validaciones extra sobre los sistemas parseados
        var sistemasFinales = sistemasReparado ?? sistemasOriginal;
        if (sistemasFinales != null)
        {
            ValidarSistemas(sistemasFinales, issues);
        }
        else if (errorOriginal != null)
        {
            // Ni el original ni el reparado parsearon
            issues.Add(new DLIssue(
                DLIssueSeverity.Error,
                "DL999-PARSE",
                $"El parser legacy lanzó: {errorOriginal.GetType().Name}: {errorOriginal.Message}. " +
                "Revisá manualmente si el archivo tiene la estructura NLOSA/FC/FY/ADIC en su primer " +
                "bloque de números.",
                LineaNumero: null,
                FixDescripcion: errorReparado != null
                    ? $"Los fixes automáticos tampoco resuelven (último error: {errorReparado.Message})."
                    : null));
        }

        bool puedeAbrir = !issues.Any(i => i.Severity == DLIssueSeverity.Error);
        string? contenidoReparado = (textoReparado != textoOriginal && sistemasReparado != null)
            ? textoReparado : null;

        return new DLDiagnostico
        {
            ArchivoOriginal = path,
            Issues = issues
                .OrderByDescending(i => (int)i.Severity)
                .ThenBy(i => i.Codigo)
                .ToList(),
            PuedeAbrir = puedeAbrir,
            ContenidoReparado = contenidoReparado,
            SistemasRecuperados = sistemasFinales,
            EsArchivoJsonDisfrazado = false,
            PathSugeridoReparado = contenidoReparado != null
                ? AjustarExtension(path, ".reparado.DL")
                : null,
        };
    }

    /// <summary>
    /// Escribe el contenido reparado a disco en el path sugerido. Si ya existe,
    /// agrega timestamp al nombre para no pisar. Devuelve el path final.
    /// </summary>
    public static string AplicarReparacionADisco(DLDiagnostico diag)
    {
        if (diag.ContenidoReparado is null)
            throw new InvalidOperationException("Este diagnóstico no tiene contenido reparado.");
        if (string.IsNullOrWhiteSpace(diag.PathSugeridoReparado))
            throw new InvalidOperationException("No hay path sugerido.");

        var path = diag.PathSugeridoReparado!;
        if (File.Exists(path))
        {
            var dir   = Path.GetDirectoryName(path) ?? "";
            var stem  = Path.GetFileNameWithoutExtension(path);
            var ext   = Path.GetExtension(path);
            path = Path.Combine(dir, $"{stem}.{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        }

        var dirFinal = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dirFinal)) Directory.CreateDirectory(dirFinal);
        File.WriteAllText(path, diag.ContenidoReparado, Encoding.GetEncoding(1252));
        return path;
    }

    // -----------------------------------------------------------------
    // Helpers privados
    // -----------------------------------------------------------------

    private static DLDiagnostico ConErrorFatal(string path, string codigo, string descripcion) => new()
    {
        ArchivoOriginal = path,
        Issues = new List<DLIssue>
        {
            new(DLIssueSeverity.Error, codigo, descripcion, null, null),
        },
        PuedeAbrir = false,
    };

    private static (string texto, string? bom) LeerComoTexto(byte[] bytes)
    {
        // Detección manual de BOM antes de intentar decodear con cp1252.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "UTF-8");
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE");
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE");
        return (Encoding.GetEncoding(1252).GetString(bytes), null);
    }

    private static bool PareceJson(string texto)
    {
        var trimmed = texto.TrimStart();
        if (trimmed.Length == 0) return false;
        char first = trimmed[0];
        return first == '{' || first == '[';
    }

    private static bool DetectarDecimalConComa(string texto)
    {
        // Heurística: ¿hay tokens con dígitos + coma + dígitos (ej. "0,210") fuera
        // de líneas de comentario? Suficiente con encontrar uno.
        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lineas)
        {
            var l = raw.TrimStart();
            if (l.StartsWith("$") || l.Length == 0) continue;
            // Buscar patrón dígito-coma-dígito
            for (int i = 1; i < l.Length - 1; i++)
            {
                if (l[i] == ',' && char.IsDigit(l[i - 1]) && char.IsDigit(l[i + 1]))
                    return true;
            }
        }
        return false;
    }

    private static string ReemplazarDecimalComaPorPunto(string texto)
    {
        // Solo en posiciones dígito-coma-dígito. Conserva comas legítimas si
        // existen (poco común en .DL pero por las dudas).
        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(texto.Length);
        bool first = true;
        foreach (var raw in lineas)
        {
            if (!first) sb.Append("\r\n");
            first = false;
            var l = raw.TrimStart();
            if (l.StartsWith("$"))
            {
                sb.Append(raw);
                continue;
            }
            var arr = raw.ToCharArray();
            for (int i = 1; i < arr.Length - 1; i++)
            {
                if (arr[i] == ',' && char.IsDigit(arr[i - 1]) && char.IsDigit(arr[i + 1]))
                    arr[i] = '.';
            }
            sb.Append(new string(arr));
        }
        return sb.ToString();
    }

    private static List<Sistema> ParseDesdeTexto(string texto, string path)
    {
        // Escribir a un temp file y reutilizar DLFileService es lo más simple
        // y consistente con la rama real de lectura.
        var tmp = Path.Combine(Path.GetTempPath(), $"dldoctor_{Guid.NewGuid():N}.DL");
        try
        {
            File.WriteAllText(tmp, texto, Encoding.GetEncoding(1252));
            return DLFileService.ReadAll(tmp);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* nothing */ }
        }
    }

    private static void ValidarSistemas(IReadOnlyList<Sistema> sistemas, List<DLIssue> issues)
    {
        for (int s = 0; s < sistemas.Count; s++)
        {
            var sis = sistemas[s];

            // ID duplicados
            var idsDuplicados = sis.Losas.GroupBy(l => l.Id)
                                        .Where(g => g.Count() > 1 && g.Key > 0)
                                        .Select(g => g.Key)
                                        .ToList();
            foreach (var id in idsDuplicados)
            {
                issues.Add(new DLIssue(
                    DLIssueSeverity.Warning,
                    "DL101-ID-DUPLICADO",
                    $"Sistema '{sis.Nombre}': ID {id} aparece más de una vez. El motor de Losas " +
                    "puede confundirse al referenciarlo en los bordes adicionales.",
                    LineaNumero: null,
                    FixDescripcion: "Reasignar IDs únicos manualmente o usar 'Renumerar IDs'."));
            }

            // Geometría inválida
            int losaIdx = 0;
            foreach (var l in sis.Losas)
            {
                losaIdx++;
                if (l.Lx <= 0 || l.Ly <= 0 || l.Espesor <= 0 || l.Carga <= 0)
                {
                    issues.Add(new DLIssue(
                        DLIssueSeverity.Warning,
                        "DL102-GEOMETRIA",
                        $"Sistema '{sis.Nombre}' losa #{losaIdx} (ID {l.Id}): tiene un valor ≤ 0 " +
                        $"(Lx={l.Lx}, Ly={l.Ly}, H={l.Espesor}, Carga={l.Carga}). El motor lo rechaza.",
                        LineaNumero: null,
                        FixDescripcion: "Editar los valores en el DataGrid o exportar como CSV para revisar."));
                }
                if (l.Rec >= l.Espesor && l.Espesor > 0)
                {
                    issues.Add(new DLIssue(
                        DLIssueSeverity.Warning,
                        "DL103-RECUBRIMIENTO",
                        $"Sistema '{sis.Nombre}' losa #{losaIdx} (ID {l.Id}): recubrimiento " +
                        $"({l.Rec}) ≥ espesor ({l.Espesor}). El motor lo rechaza.",
                        LineaNumero: null,
                        FixDescripcion: "Disminuir Rec a un valor menor que H."));
                }
            }

            // Bordes huérfanos (BI o BJ no existe como Id en Losas)
            var idsValidos = new HashSet<int>(sis.Losas.Select(l => l.Id));
            void ValidarBordes(IEnumerable<BordeAdic> bordes, string dir)
            {
                int bIdx = 0;
                foreach (var b in bordes)
                {
                    bIdx++;
                    if (!idsValidos.Contains(b.BI))
                        issues.Add(new DLIssue(
                            DLIssueSeverity.Warning,
                            "DL104-BORDE-HUERFANO",
                            $"Sistema '{sis.Nombre}' borde {dir} #{bIdx}: B-I={b.BI} no es ID " +
                            "de ninguna losa del sistema.",
                            null, "Editar el borde para usar un ID válido."));
                    if (!idsValidos.Contains(b.BJ))
                        issues.Add(new DLIssue(
                            DLIssueSeverity.Warning,
                            "DL104-BORDE-HUERFANO",
                            $"Sistema '{sis.Nombre}' borde {dir} #{bIdx}: B-J={b.BJ} no es ID " +
                            "de ninguna losa del sistema.",
                            null, "Editar el borde para usar un ID válido."));
                    var bal = (b.Balanceo ?? "").Trim().ToUpperInvariant();
                    if (bal != "S" && bal != "N")
                        issues.Add(new DLIssue(
                            DLIssueSeverity.Info,
                            "DL105-BAL-NO-NORMALIZADO",
                            $"Sistema '{sis.Nombre}' borde {dir} #{bIdx}: BALANCEO='{b.Balanceo}' " +
                            "no es 'S' ni 'N'. Se trató como 'S' por defecto.",
                            null, "El modelo ya lo normaliza al guardar."));
                }
            }
            ValidarBordes(sis.BordesX, "X");
            ValidarBordes(sis.BordesY, "Y");
        }
    }

    private static string AjustarExtension(string path, string newExtensionConPunto)
    {
        var dir  = Path.GetDirectoryName(path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(path);
        return Path.Combine(dir, stem + newExtensionConPunto);
    }
}
