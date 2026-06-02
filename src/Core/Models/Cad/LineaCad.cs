using System;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Segmento de recta — traducción de la entidad DXF <c>LINE</c>.
/// Coordenadas en metros.
/// </summary>
public sealed class LineaCad : EntidadCad
{
    /// <summary>Punto inicial del segmento (m).</summary>
    public PuntoCad Inicio { get; init; }

    /// <summary>Punto final del segmento (m).</summary>
    public PuntoCad Fin { get; init; }

    public override TipoEntidadCad Tipo => TipoEntidadCad.Linea;

    /// <summary>Longitud euclidiana del segmento (m).</summary>
    public double Longitud
    {
        get
        {
            var dx = Fin.X - Inicio.X;
            var dy = Fin.Y - Inicio.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
