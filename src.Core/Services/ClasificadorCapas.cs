using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LosasPlus.Services;

/// <summary>Categoría estructural a la que pertenece una capa (layer) del DXF.</summary>
public enum CategoriaEstructural
{
    /// <summary>No reconocida por la heurística — candidata a clasificación por IA.</summary>
    Otro = 0,
    Losa,
    Viga,
    Columna,
    Eje,
}

/// <summary>
/// Clasificador <b>heurístico</b> de capas DXF por nombre. Mapea el nombre de una
/// capa ("VIGAS", "COL", "LOSA-2", "EJES") a una <see cref="CategoriaEstructural"/>
/// por coincidencia de palabras clave, insensible a mayúsculas y acentos.
///
/// <para>Función <b>pura</b> (sin red, sin estado): es la primera mitad de la
/// clasificación <i>híbrida</i>. Las capas que caen en
/// <see cref="CategoriaEstructural.Otro"/> son las candidatas a resolverse con la
/// IA de texto (Qwen) cuando el plano no usa nombres claros.</para>
/// </summary>
public static class ClasificadorCapas
{
    // Palabra clave (ya normalizada: MAYÚSCULAS, sin acentos) -> categoría.
    // Se evalúan EN ORDEN: la primera coincidencia por substring gana. Por eso
    // "COLUMNA"/"COLUMN" van antes que "COL" (todas dan Columna, da igual cuál
    // matchee, pero el orden documenta la intención).
    private static readonly (string Clave, CategoriaEstructural Cat)[] Reglas =
    {
        ("VIGA",    CategoriaEstructural.Viga),
        ("BEAM",    CategoriaEstructural.Viga),
        ("COLUMNA", CategoriaEstructural.Columna),
        ("COLUMN",  CategoriaEstructural.Columna),
        ("PILAR",   CategoriaEstructural.Columna),
        ("COL",     CategoriaEstructural.Columna),
        ("LOSA",    CategoriaEstructural.Losa),
        ("SLAB",    CategoriaEstructural.Losa),
        ("PANO",    CategoriaEstructural.Losa),   // "PAÑO/PAÑOS" tras quitar acentos
        ("PLACA",   CategoriaEstructural.Losa),
        ("EJE",     CategoriaEstructural.Eje),
        ("GRID",    CategoriaEstructural.Eje),
        ("AXIS",    CategoriaEstructural.Eje),
    };

    /// <summary>Clasifica una capa por su nombre. Devuelve <see cref="CategoriaEstructural.Otro"/> si no reconoce ninguna palabra clave.</summary>
    public static CategoriaEstructural Clasificar(string? capa)
    {
        var k = Normalizar(capa);
        if (k.Length == 0) return CategoriaEstructural.Otro;
        foreach (var (clave, cat) in Reglas)
            if (k.Contains(clave, StringComparison.Ordinal))
                return cat;
        return CategoriaEstructural.Otro;
    }

    /// <summary>Clasifica un conjunto de nombres de capa (deduplicado).</summary>
    public static IReadOnlyDictionary<string, CategoriaEstructural> ClasificarTodas(IEnumerable<string> capas)
        => (capas ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToDictionary(c => c, Clasificar);

    /// <summary>Capas que la heurística NO pudo clasificar — candidatas a la IA de texto.</summary>
    public static IReadOnlyList<string> Ambiguas(IEnumerable<string> capas)
        => (capas ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .Where(c => Clasificar(c) == CategoriaEstructural.Otro)
            .ToList();

    /// <summary>MAYÚSCULAS, sin acentos, sin espacios al borde — para comparar por substring.</summary>
    private static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var formD = s.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
