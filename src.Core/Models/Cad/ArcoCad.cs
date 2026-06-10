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

    /// <summary>
    /// Bounding box REAL del arco (F2): extremos del barrido + los puntos
    /// cardinales (0°/90°/180°/270°) contenidos en él. Un arco parcial ya no
    /// infla el encuadre con el círculo circunscrito completo; el círculo
    /// completo sigue devolviendo el circunscrito. Convención DXF: el barrido
    /// va de <see cref="AnguloInicioGrados"/> a <see cref="AnguloFinGrados"/>
    /// en sentido antihorario.
    /// </summary>
    public (PuntoCad Min, PuntoCad Max) BoundingBox()
    {
        if (EsCirculoCompleto)
            return (new PuntoCad(Centro.X - Radio, Centro.Y - Radio),
                    new PuntoCad(Centro.X + Radio, Centro.Y + Radio));

        double inicio = AnguloInicioGrados;
        double barrido = ((AnguloFinGrados - AnguloInicioGrados) % 360.0 + 360.0) % 360.0;
        if (barrido < 1e-9) barrido = 360.0;   // degenerado: tratar como círculo

        PuntoCad En(double grados)
        {
            double rad = grados * Math.PI / 180.0;
            return new PuntoCad(Centro.X + Radio * Math.Cos(rad), Centro.Y + Radio * Math.Sin(rad));
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        void Acumular(PuntoCad p)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        Acumular(En(inicio));
        Acumular(En(inicio + barrido));
        for (double cardinal = 0; cardinal < 360.0; cardinal += 90.0)
        {
            double delta = ((cardinal - inicio) % 360.0 + 360.0) % 360.0;
            if (delta <= barrido) Acumular(En(cardinal));
        }

        return (new PuntoCad(minX, minY), new PuntoCad(maxX, maxY));
    }
}
