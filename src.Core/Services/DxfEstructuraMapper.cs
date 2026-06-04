using System;
using System.Collections.Generic;
using LosasPlus.IA;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Mapeador <b>determinista</b> DXF → elementos estructurales. Toma las
/// <see cref="EntidadCad"/> ya parseadas por <see cref="DxfImportService"/>
/// (geometría exacta, en metros, con su capa) y produce una
/// <see cref="PropuestaElementos"/> (losas + vigas + columnas).
///
/// <para><b>Por qué determinista y no IA-visión.</b> Un DXF es geometría vectorial
/// exacta: las coordenadas son ciertas, no hay que "estimarlas" con un modelo de
/// visión. La clasificación se hace por la <b>capa</b> de cada entidad (vía
/// <paramref name="categoriaDeCapa"/>), que en la heurística pura es
/// <see cref="ClasificadorCapas.Clasificar"/> y en el modo híbrido puede venir
/// reforzada por la IA de texto para las capas ambiguas.</para>
///
/// <para><b>Reglas (columnas por capa, según decisión del usuario):</b></para>
/// <list type="bullet">
///   <item><b>Losa</b>: polilínea cerrada rectangular ortogonal en capa Losa/Otro
///         (reusa <see cref="PoligonoLosaMapper.TryMapearRectangulo"/>).</item>
///   <item><b>Columna</b>: círculo completo o rectángulo cerrado en capa Columna →
///         posición = centro; sección = diámetro (círculo) o ancho×alto (rect).</item>
///   <item><b>Viga</b>: <see cref="LineaCad"/> en capa Viga, o cada segmento de una
///         polilínea (abierta) en capa Viga.</item>
/// </list>
///
/// <para>Función pura — sin red ni I/O, totalmente testeable. No coloca elementos:
/// solo PROPONE geometría; el consumidor (ViewModel) crea los objetos en el nivel
/// y el ingeniero revisa. Los ejes se generan aparte (<c>GeneradorEjes</c>).</para>
/// </summary>
public static class DxfEstructuraMapper
{
    /// <summary>
    /// Mapea las entidades a una propuesta de losas/vigas/columnas.
    /// </summary>
    /// <param name="entidades">Entidades CAD ya importadas (en metros).</param>
    /// <param name="categoriaDeCapa">
    /// Clasificador capa→categoría. Por defecto la heurística pura
    /// <see cref="ClasificadorCapas.Clasificar"/>; en modo híbrido se le pasa un
    /// delegado que consulta primero el resultado de la IA de texto.
    /// </param>
    /// <param name="tolerancia">Tolerancia geométrica (m) para ortogonalidad/cierre.</param>
    public static PropuestaElementos Mapear(
        IReadOnlyList<EntidadCad> entidades,
        Func<string, CategoriaEstructural>? categoriaDeCapa = null,
        double tolerancia = 0.02)
    {
        categoriaDeCapa ??= ClasificadorCapas.Clasificar;

        var losas = new List<LosaPropuesta>();
        var vigas = new List<VigaPropuesta>();
        var columnas = new List<ColumnaPropuesta>();
        int contornosNoRect = 0;

        foreach (var e in entidades ?? Array.Empty<EntidadCad>())
        {
            var cat = categoriaDeCapa(e.Capa);
            switch (e)
            {
                // --- Círculo en capa Columna → columna (sección = diámetro) ---
                case ArcoCad arco when arco.EsCirculoCompleto && cat == CategoriaEstructural.Columna:
                    double d = 2.0 * arco.Radio;
                    columnas.Add(new ColumnaPropuesta(arco.Centro.X, arco.Centro.Y, d, d));
                    break;

                // --- Polilínea rectangular: columna (capa Col) o losa (capa Losa/Otro) ---
                case PolilineaCad poli when PoligonoLosaMapper.TryMapearRectangulo(poli, out var r, tolerancia):
                    if (cat == CategoriaEstructural.Columna)
                        columnas.Add(new ColumnaPropuesta(
                            r.MinX + (r.Ancho / 2.0), r.MinY + (r.Alto / 2.0), r.Ancho, r.Alto));
                    else if (cat is CategoriaEstructural.Losa or CategoriaEstructural.Otro)
                        losas.Add(new LosaPropuesta(r.MinX, r.MinY, r.Ancho, r.Alto));
                    // (rectángulo en capa Viga/Eje: se ignora — no es ninguno de ellos)
                    break;

                // --- Polilínea (abierta) en capa Viga → un segmento = una viga ---
                case PolilineaCad poli when cat == CategoriaEstructural.Viga:
                    var vs = poli.Vertices;
                    for (int i = 0; i + 1 < vs.Count; i++)
                        vigas.Add(new VigaPropuesta(vs[i].X, vs[i].Y, vs[i + 1].X, vs[i + 1].Y));
                    break;

                // --- Polilínea cerrada no rectangular y no-viga → contorno no mapeable ---
                case PolilineaCad poli when poli.Cerrada:
                    contornosNoRect++;
                    break;

                // --- Línea en capa Viga → viga ---
                case LineaCad linea when cat == CategoriaEstructural.Viga:
                    vigas.Add(new VigaPropuesta(
                        linea.Inicio.X, linea.Inicio.Y, linea.Fin.X, linea.Fin.Y));
                    break;
            }
        }

        string? adv = contornosNoRect > 0
            ? $"{contornosNoRect} contorno(s) cerrado(s) no rectangular(es) omitido(s) " +
              "(una Losa debe ser rectangular)."
            : null;

        return new PropuestaElementos(losas, vigas, columnas, Array.Empty<EjePropuesto>(), adv);
    }
}
