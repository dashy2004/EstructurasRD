using System;
using System.Collections.Generic;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Polilínea — traducción de las entidades DXF <c>LWPOLYLINE</c> y
/// <c>POLYLINE</c> (2D). Coordenadas en metros.
///
/// <para>
/// Una polilínea <see cref="Cerrada"/> de 4 vértices ortogonales es la
/// candidata natural a mapear a una <c>Losa</c> en la Fase 2 del plan CAD.
/// </para>
/// </summary>
public sealed class PolilineaCad : EntidadCad
{
    /// <summary>Vértices de la polilínea, en orden (m).</summary>
    public IReadOnlyList<PuntoCad> Vertices { get; init; } = Array.Empty<PuntoCad>();

    /// <summary>True si el último vértice se conecta con el primero (contorno cerrado).</summary>
    public bool Cerrada { get; init; }

    public override TipoEntidadCad Tipo => TipoEntidadCad.Polilinea;

    /// <summary>Cantidad de vértices.</summary>
    public int CantidadVertices => Vertices.Count;
}
