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
///   <item>Traduce LINE, LWPOLYLINE/POLYLINE, TEXT, MTEXT, CIRCLE, ARC e
///         <b>INSERT</b> (bloques) a las entidades puras de <c>src.Core</c>
///         (<see cref="EntidadCad"/>).</item>
///   <item><b>Expande bloques recursivamente</b> (Parche v1.2.4): los INSERTs
///         de AutoCAD/Revit que envuelven símbolos arquitectónicos se
///         desreferencian aplicando su transformación afín (posición + escala
///         + rotación). Soporta hasta 8 niveles de bloques anidados.</item>
///   <item><b>Normaliza a metros</b> según la variable <c>$INSUNITS</c> del DXF
///         (mm, cm, pulgadas, pies, etc.).</item>
///   <item><b>Resiliente</b>: archivos inexistentes, vacíos o corruptos
///         producen excepciones controladas y claras — nunca un crash.</item>
/// </list>
/// </summary>
public sealed class DxfImportService : IPlanoImporter
{
    /// <summary>
    /// Profundidad máxima de anidamiento de bloques (INSERT dentro de INSERT).
    /// 8 niveles cubre virtualmente todos los planos reales y previene
    /// stack overflow ante DXFs hostiles con ciclos.
    /// </summary>
    private const int MaxProfundidadBloque = 8;

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

        // ---- Recolección de entidades (top-level + INSERTs anidados) ----
        // El dispatcher TraducirEntidad maneja cada tipo y, para INSERT,
        // recurre sobre las entidades del bloque referenciado aplicando la
        // composición de transformaciones afines.
        var entidades = new List<EntidadCad>();
        var ident = TransformacionAfin.Identidad;
        foreach (var e in doc.Entities.Lines)       TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.Polylines2D) TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.Texts)       TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.MTexts)      TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.Circles)     TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.Arcs)        TraducirEntidad(e, ident, factor, entidades, 0);
        foreach (var e in doc.Entities.Inserts)     TraducirEntidad(e, ident, factor, entidades, 0);

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
    /// Traduce una entidad netDxf a su equivalente <see cref="EntidadCad"/>
    /// aplicando la transformación afín acumulada (posición + escala + rotación
    /// heredada de los INSERTs externos). Para los INSERTs, recurre sobre el
    /// bloque referenciado componiendo la transformación local con la acumulada.
    /// </summary>
    /// <param name="ent">La entidad netDxf top-level o anidada en un bloque.</param>
    /// <param name="acumulada">Transformación heredada de los INSERTs exteriores.</param>
    /// <param name="factor">Factor de conversión a metros (según $INSUNITS).</param>
    /// <param name="salida">Lista destino donde se acumulan las entidades traducidas.</param>
    /// <param name="profundidad">Profundidad actual de anidamiento (corta en <see cref="MaxProfundidadBloque"/>).</param>
    private static void TraducirEntidad(
        EntityObject ent,
        TransformacionAfin acumulada,
        double factor,
        List<EntidadCad> salida,
        int profundidad)
    {
        switch (ent)
        {
            case Line l:
                salida.Add(new LineaCad
                {
                    Capa = NombreCapa(l.Layer),
                    Inicio = acumulada.Aplicar(l.StartPoint.X * factor, l.StartPoint.Y * factor),
                    Fin    = acumulada.Aplicar(l.EndPoint.X   * factor, l.EndPoint.Y   * factor),
                });
                break;

            case Polyline2D p:
                salida.Add(new PolilineaCad
                {
                    Capa = NombreCapa(p.Layer),
                    Vertices = p.Vertexes
                        .Select(v => acumulada.Aplicar(v.Position.X * factor, v.Position.Y * factor))
                        .ToList(),
                    Cerrada = p.IsClosed,
                });
                break;

            case Text t:
                salida.Add(new TextoCad
                {
                    Capa = NombreCapa(t.Layer),
                    Posicion = acumulada.Aplicar(t.Position.X * factor, t.Position.Y * factor),
                    Contenido = t.Value ?? "",
                    Altura = t.Height * factor * acumulada.EscalaLongitud(),
                    RotacionGrados = t.Rotation + acumulada.RotacionGrados(),
                });
                break;

            case MText m:
                salida.Add(new TextoCad
                {
                    Capa = NombreCapa(m.Layer),
                    Posicion = acumulada.Aplicar(m.Position.X * factor, m.Position.Y * factor),
                    Contenido = m.Value ?? "",
                    Altura = m.Height * factor * acumulada.EscalaLongitud(),
                    RotacionGrados = m.Rotation + acumulada.RotacionGrados(),
                });
                break;

            case Circle c:
                // Un círculo se representa como un arco de 0° a 360°.
                salida.Add(new ArcoCad
                {
                    Capa = NombreCapa(c.Layer),
                    Centro = acumulada.Aplicar(c.Center.X * factor, c.Center.Y * factor),
                    Radio = c.Radius * factor * acumulada.EscalaLongitud(),
                    AnguloInicioGrados = 0.0,
                    AnguloFinGrados = 360.0,
                });
                break;

            case Arc a:
                double rotNeta = acumulada.RotacionGrados();
                salida.Add(new ArcoCad
                {
                    Capa = NombreCapa(a.Layer),
                    Centro = acumulada.Aplicar(a.Center.X * factor, a.Center.Y * factor),
                    Radio = a.Radius * factor * acumulada.EscalaLongitud(),
                    AnguloInicioGrados = a.StartAngle + rotNeta,
                    AnguloFinGrados    = a.EndAngle   + rotNeta,
                });
                break;

            case Insert ins:
                // Expansión recursiva del bloque referenciado. La transformación
                // local del INSERT (posición, escala XY, rotación) se compone
                // con la acumulada del contexto exterior; los puntos del bloque
                // luego se mapean correctamente al espacio del documento.
                if (profundidad >= MaxProfundidadBloque) return;
                var local = TransformacionAfin.DesdeInsert(
                    ins.Position.X * factor, ins.Position.Y * factor,
                    ins.Scale.X, ins.Scale.Y, ins.Rotation);
                var compuesta = acumulada.Componer(local);
                foreach (var sub in ins.Block.Entities)
                    TraducirEntidad(sub, compuesta, factor, salida, profundidad + 1);
                break;

            // SPLINE, HATCH, DIMENSION, Polyline3D y otros tipos se ignoran
            // silenciosamente — fuera de scope del importador estructural.
        }
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

    /// <summary>
    /// Transformación afín 2D (matriz lineal 2×2 + traslación) usada para
    /// expandir INSERTs anidados (Parche v1.2.4). Cada INSERT contribuye
    /// una rotación + escala XY + traslación; la composición de niveles
    /// anidados produce una afín general que se aplica a cada punto del
    /// bloque interno para mapearlo al espacio del documento.
    /// </summary>
    private readonly struct TransformacionAfin
    {
        public readonly double M11, M12, M21, M22, Tx, Ty;

        public static readonly TransformacionAfin Identidad = new(1, 0, 0, 1, 0, 0);

        public TransformacionAfin(double m11, double m12, double m21, double m22, double tx, double ty)
        { M11 = m11; M12 = m12; M21 = m21; M22 = m22; Tx = tx; Ty = ty; }

        /// <summary>Construye una transformación desde los parámetros de un INSERT.</summary>
        /// <param name="posX">Punto de inserción X (en metros).</param>
        /// <param name="posY">Punto de inserción Y (en metros).</param>
        /// <param name="sx">Escala X del INSERT (puede ser negativa = espejo).</param>
        /// <param name="sy">Escala Y del INSERT.</param>
        /// <param name="rotacionGrados">Rotación del INSERT en grados.</param>
        public static TransformacionAfin DesdeInsert(double posX, double posY, double sx, double sy, double rotacionGrados)
        {
            double rad = rotacionGrados * Math.PI / 180.0;
            double c = Math.Cos(rad), s = Math.Sin(rad);
            return new TransformacionAfin(c * sx, -s * sy, s * sx, c * sy, posX, posY);
        }

        /// <summary>Aplica la transformación a un punto y devuelve el resultado.</summary>
        public PuntoCad Aplicar(double x, double y) =>
            new(M11 * x + M12 * y + Tx, M21 * x + M22 * y + Ty);

        /// <summary>
        /// Composición de transformaciones: el resultado equivale a aplicar
        /// primero <paramref name="otra"/> y luego ésta — útil cuando se entra
        /// en un INSERT anidado y la acumulada exterior se combina con la
        /// transformación local del INSERT actual.
        /// </summary>
        public TransformacionAfin Componer(TransformacionAfin otra) => new(
            M11 * otra.M11 + M12 * otra.M21,
            M11 * otra.M12 + M12 * otra.M22,
            M21 * otra.M11 + M22 * otra.M21,
            M21 * otra.M12 + M22 * otra.M22,
            M11 * otra.Tx + M12 * otra.Ty + Tx,
            M21 * otra.Tx + M22 * otra.Ty + Ty);

    /// <summary>
    /// Factor de escala efectivo para magnitudes 1D (radio de un arco,
    /// altura de un texto). Se computa como el promedio de las normas
    /// columna de la parte lineal — exacto para transformaciones de
    /// escala uniforme + rotación, aproximación razonable para escalas
    /// no uniformes anidadas.
    /// </summary>
        public double EscalaLongitud()
        {
            double sx = Math.Sqrt(M11 * M11 + M21 * M21);
            double sy = Math.Sqrt(M12 * M12 + M22 * M22);
            return (sx + sy) * 0.5;
        }

        /// <summary>
        /// Rotación neta de la transformación en grados. Útil para acumular
        /// los ángulos de start/end de un arco que viene rotado por INSERTs.
        /// </summary>
        public double RotacionGrados() => Math.Atan2(M21, M11) * 180.0 / Math.PI;
    }
}
