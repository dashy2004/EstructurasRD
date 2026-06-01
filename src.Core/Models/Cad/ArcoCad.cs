using System;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Arco de circunferencia — traducción de las entidades DXF <c>ARC</c> y
/// <c>CIRCLE</c>. Un círculo completo se representa como un arco de 0° a 360°
/// (<see cref="EsCirculoCompleto"/> = <c>true</c>). Coordenadas en metros.
///
/// <para>
/// En planos de arquitectura los arcos/círculos suelen marcar columnas o
/// detalles — se importan como geometría de referencia.
/// </para>
/// </summary>
public sealed class ArcoCad : EntidadCad
{
    /// <summary>Centro del arco (m).</summary>
    public PuntoCad Centro { get; init; }

    /// <summary>Radio del arco (m).</summary>
    public double Radio { get; init; }

    /// <summary>Ángulo inicial en grados.</summary>
    public double AnguloInicioGrados { get; init; }

    /// <summary>Ángulo final en grados.</summary>
    public double AnguloFinGrados { get; init; }

    public override TipoEntidadCad Tipo => TipoEntidadCad.Arco;

    /// <summary>
    /// True si el arco cubre los 360° completos — es decir, proviene de una
    /// entidad <c>CIRCLE</c> del DXF.
    /// </summary>
    public bool EsCirculoCompleto =>
        Math.Abs(Math.Abs(AnguloFinGrados - AnguloInicioGrados) - 360.0) < 1e-6;
}
