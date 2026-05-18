using System;
using System.Collections.Generic;
using System.Linq;

namespace LosasPlus.Models.Cad;

/// <summary>
/// Plano de referencia importado de un archivo <c>.DXF</c> — el "calco" del
/// arquitecto sobre el que el ingeniero dibujará sus losas.
///
/// <para>
/// Es un agregado <b>puro de dominio</b>: una lista de <see cref="EntidadCad"/>
/// (líneas, polilíneas, textos, arcos) con coordenadas ya <b>normalizadas a
/// metros</b>, más metadata útil para encuadrar el plano en el lienzo.
/// No referencia ningún tipo de WPF.
/// </para>
/// </summary>
public sealed class PlanoReferencia
{
    /// <summary>Nombre del archivo .DXF de origen (sin ruta).</summary>
    public string NombreArchivo { get; init; } = "";

    /// <summary>
    /// Unidad declarada en el DXF de origen (<c>$INSUNITS</c>), ej.
    /// <c>"Millimeters"</c>. Las coordenadas de <see cref="Entidades"/> ya
    /// están convertidas a metros — este campo se conserva para trazabilidad.
    /// </summary>
    public string UnidadOriginal { get; init; } = "";

    /// <summary>Entidades geométricas del plano, con coordenadas en metros.</summary>
    public IReadOnlyList<EntidadCad> Entidades { get; init; } = Array.Empty<EntidadCad>();

    /// <summary>Cantidad total de entidades importadas.</summary>
    public int CantidadEntidades => Entidades.Count;

    /// <summary>Cantidad de entidades de un tipo concreto.</summary>
    public int Contar(TipoEntidadCad tipo) => Entidades.Count(e => e.Tipo == tipo);

    // ---- Bounding box (m) — útil para encuadrar el plano en el viewport ----

    /// <summary>Coordenada X mínima del plano (m). 0 si no hay entidades.</summary>
    public double MinX { get; init; }

    /// <summary>Coordenada Y mínima del plano (m). 0 si no hay entidades.</summary>
    public double MinY { get; init; }

    /// <summary>Coordenada X máxima del plano (m). 0 si no hay entidades.</summary>
    public double MaxX { get; init; }

    /// <summary>Coordenada Y máxima del plano (m). 0 si no hay entidades.</summary>
    public double MaxY { get; init; }

    /// <summary>Ancho del bounding box (m).</summary>
    public double Ancho => MaxX - MinX;

    /// <summary>Alto del bounding box (m).</summary>
    public double Alto => MaxY - MinY;

    /// <summary>True si el plano no contiene ninguna entidad geométrica.</summary>
    public bool EstaVacio => Entidades.Count == 0;
}
