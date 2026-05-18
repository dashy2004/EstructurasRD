using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LosasPlus.Models.Cad;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Units;

namespace LosasPlus.Services;

/// <summary>
/// Implementación de <see cref="IPlanoImporter"/> para archivos <c>.DXF</c>
/// (Drawing Exchange Format) usando la librería open-source <b>netDxf</b>
/// (MIT). Es la <b>única</b> clase del proyecto que conoce netDxf — el patrón
/// Adapter aísla la dependencia.
///
/// <para>
/// Características:
/// </para>
/// <list type="bullet">
///   <item>Traduce LINE, LWPOLYLINE/POLYLINE, TEXT, MTEXT, CIRCLE y ARC a las
///         entidades puras de <c>src.Core</c> (<see cref="EntidadCad"/>).</item>
///   <item><b>Normaliza a metros</b> según la variable <c>$INSUNITS</c> del DXF
///         (mm, cm, pulgadas, pies, etc.).</item>
///   <item><b>Resiliente</b>: archivos inexistentes, vacíos o corruptos
///         producen excepciones controladas y claras — nunca un crash.</item>
/// </list>
/// </summary>
public sealed class DxfImportService : IPlanoImporter
{
    /// <inheritdoc/>
    public PlanoReferencia Importar(string rutaArchivo)
    {
        // ---- Resiliencia: validar la entrada antes de tocar netDxf ----
        if (string.IsNullOrWhiteSpace(rutaArchivo))
            throw new ArgumentException("La ruta del archivo DXF no puede estar vacía.", nameof(rutaArchivo));

        if (!File.Exists(rutaArchivo))
            throw new FileNotFoundException($"No se encontró el archivo DXF: {rutaArchivo}", rutaArchivo);

        var info = new FileInfo(rutaArchivo);
        if (info.Length == 0)
            throw new InvalidOperationException(
                $"El archivo DXF está vacío (0 bytes): {Path.GetFileName(rutaArchivo)}.");

        // ---- Carga con netDxf, atrapando cualquier fallo de parseo ----
        DxfDocument? doc;
        try
        {
            doc = DxfDocument.Load(rutaArchivo);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"El archivo DXF no se pudo leer (formato inválido o corrupto): " +
                $"{Path.GetFileName(rutaArchivo)}. Detalle: {ex.Message}", ex);
        }

        if (doc is null)
            throw new InvalidOperationException(
                $"El archivo no es un DXF válido o usa una versión no soportada: " +
                $"{Path.GetFileName(rutaArchivo)}.");

        // ---- Factor de normalización a metros según $INSUNITS ----
        var factor = FactorAMetros(doc.DrawingVariables.InsUnits);

        // ---- Traducción de entidades ----
        var entidades = new List<EntidadCad>();

        foreach (var l in doc.Entities.Lines)
        {
            entidades.Add(new LineaCad
            {
                Capa = NombreCapa(l.Layer),
                Inicio = new PuntoCad(l.StartPoint.X * factor, l.StartPoint.Y * factor),
                Fin = new PuntoCad(l.EndPoint.X * factor, l.EndPoint.Y * factor),
            });
        }

        foreach (var p in doc.Entities.Polylines2D)
        {
            var verts = p.Vertexes
                .Select(v => new PuntoCad(v.Position.X * factor, v.Position.Y * factor))
                .ToList();
            entidades.Add(new PolilineaCad
            {
                Capa = NombreCapa(p.Layer),
                Vertices = verts,
                Cerrada = p.IsClosed,
            });
        }

        foreach (var t in doc.Entities.Texts)
        {
            entidades.Add(new TextoCad
            {
                Capa = NombreCapa(t.Layer),
                Posicion = new PuntoCad(t.Position.X * factor, t.Position.Y * factor),
                Contenido = t.Value ?? "",
                Altura = t.Height * factor,
                RotacionGrados = t.Rotation,
            });
        }

        foreach (var m in doc.Entities.MTexts)
        {
            entidades.Add(new TextoCad
            {
                Capa = NombreCapa(m.Layer),
                Posicion = new PuntoCad(m.Position.X * factor, m.Position.Y * factor),
                Contenido = m.Value ?? "",
                Altura = m.Height * factor,
                RotacionGrados = m.Rotation,
            });
        }

        foreach (var c in doc.Entities.Circles)
        {
            // Un círculo se representa como un arco de 0° a 360°.
            entidades.Add(new ArcoCad
            {
                Capa = NombreCapa(c.Layer),
                Centro = new PuntoCad(c.Center.X * factor, c.Center.Y * factor),
                Radio = c.Radius * factor,
                AnguloInicioGrados = 0.0,
                AnguloFinGrados = 360.0,
            });
        }

        foreach (var a in doc.Entities.Arcs)
        {
            entidades.Add(new ArcoCad
            {
                Capa = NombreCapa(a.Layer),
                Centro = new PuntoCad(a.Center.X * factor, a.Center.Y * factor),
                Radio = a.Radius * factor,
                AnguloInicioGrados = a.StartAngle,
                AnguloFinGrados = a.EndAngle,
            });
        }

        // ---- Bounding box ----
        var (minX, minY, maxX, maxY) = CalcularBoundingBox(entidades);

        return new PlanoReferencia
        {
            NombreArchivo = Path.GetFileName(rutaArchivo),
            UnidadOriginal = doc.DrawingVariables.InsUnits.ToString(),
            Entidades = entidades,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
        };
    }

    /// <summary>
    /// Factor para convertir las coordenadas del DXF a metros, según la
    /// variable <c>$INSUNITS</c>. Para <see cref="DrawingUnits.Unitless"/> se
    /// asume que el plano ya está en metros (factor 1.0) — la mayoría de los
    /// planos estructurales dominicanos se dibujan en metros.
    /// </summary>
    private static double FactorAMetros(DrawingUnits unidades) => unidades switch
    {
        DrawingUnits.Millimeters => 0.001,
        DrawingUnits.Centimeters => 0.01,
        DrawingUnits.Meters      => 1.0,
        DrawingUnits.Kilometers  => 1000.0,
        DrawingUnits.Inches      => 0.0254,
        DrawingUnits.Feet        => 0.3048,
        DrawingUnits.Miles       => 1609.344,
        _                        => 1.0,   // Unitless u otra → asumir metros
    };

    /// <summary>Nombre de la capa de una entidad, con fallback a "0" si es nula.</summary>
    private static string NombreCapa(netDxf.Tables.Layer? capa) =>
        string.IsNullOrEmpty(capa?.Name) ? "0" : capa!.Name;

    /// <summary>Calcula el rectángulo envolvente de todas las entidades (m).</summary>
    private static (double minX, double minY, double maxX, double maxY)
        CalcularBoundingBox(IReadOnlyList<EntidadCad> entidades)
    {
        if (entidades.Count == 0) return (0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        void Acumular(PuntoCad p)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        foreach (var e in entidades)
        {
            switch (e)
            {
                case LineaCad l:
                    Acumular(l.Inicio);
                    Acumular(l.Fin);
                    break;
                case PolilineaCad p:
                    foreach (var v in p.Vertices) Acumular(v);
                    break;
                case TextoCad t:
                    Acumular(t.Posicion);
                    break;
                case ArcoCad a:
                    // Aproximación: el bounding box del círculo circunscrito.
                    Acumular(new PuntoCad(a.Centro.X - a.Radio, a.Centro.Y - a.Radio));
                    Acumular(new PuntoCad(a.Centro.X + a.Radio, a.Centro.Y + a.Radio));
                    break;
            }
        }

        // Si por algún motivo no se acumuló nada, devolver origen.
        if (minX == double.MaxValue) return (0, 0, 0, 0);
        return (minX, minY, maxX, maxY);
    }
}
