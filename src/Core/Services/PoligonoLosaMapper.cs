using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models.Cad;

namespace LosasPlus.Services;

/// <summary>
/// Rectángulo ortogonal detectado a partir de una <see cref="PolilineaCad"/>
/// cerrada. Coordenadas en metros, en el sistema del plano DXF.
/// </summary>
public readonly record struct RectanguloMapeado(
    double MinX, double MinY, double MaxX, double MaxY, double Ancho, double Alto);

/// <summary>
/// Lógica geométrica pura (sin WPF) para la <b>Fase 2</b> del PLAN_CAD_V1:
/// convertir un polígono del plano DXF en una losa.
///
/// <para>
/// Aplica una <b>restricción geométrica estricta</b>: sólo las polilíneas
/// cerradas que son rectángulos ortogonales se pueden mapear a una
/// <c>Losa</c> (que es siempre rectangular). Cualquier otro contorno se
/// rechaza — la UI muestra un aviso en vez de generar una losa inválida.
/// </para>
/// </summary>
public static class PoligonoLosaMapper
{
    /// <summary>
    /// Determina si <paramref name="poli"/> es un rectángulo ortogonal
    /// estricto y, de serlo, devuelve su bounding box en
    /// <paramref name="rect"/>.
    ///
    /// <para>Criterios (todos obligatorios):</para>
    /// <list type="bullet">
    ///   <item>La polilínea está <see cref="PolilineaCad.Cerrada"/>.</item>
    ///   <item>Tiene exactamente 4 vértices (se acepta un 5º si coincide con
    ///         el 1º — cierre explícito).</item>
    ///   <item>Cada uno de los 4 lados es estrictamente horizontal o
    ///         estrictamente vertical (ortogonalidad).</item>
    ///   <item>Ancho y alto mayores que <paramref name="tolerancia"/>.</item>
    /// </list>
    /// </summary>
    public static bool TryMapearRectangulo(PolilineaCad poli,
                                           out RectanguloMapeado rect,
                                           double tolerancia = 0.02)
    {
        rect = default;
        if (poli is null || !poli.Cerrada) return false;

        var pts = poli.Vertices.ToList();
        // Aceptar un 5º vértice si repite el 1º (cierre explícito en el DXF).
        if (pts.Count == 5 && CasiIgual(pts[0], pts[4], tolerancia))
            pts.RemoveAt(4);
        if (pts.Count != 4) return false;

        // Cada lado debe ser horizontal XOR vertical.
        for (int i = 0; i < 4; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % 4];
            bool horizontal = Math.Abs(a.Y - b.Y) <= tolerancia;
            bool vertical   = Math.Abs(a.X - b.X) <= tolerancia;
            // XOR: exactamente uno. (Ambos = lado degenerado de longitud 0.)
            if (horizontal == vertical) return false;
        }

        double minX = pts.Min(p => p.X);
        double minY = pts.Min(p => p.Y);
        double maxX = pts.Max(p => p.X);
        double maxY = pts.Max(p => p.Y);
        double ancho = maxX - minX;
        double alto  = maxY - minY;
        if (ancho <= tolerancia || alto <= tolerancia) return false;

        rect = new RectanguloMapeado(minX, minY, maxX, maxY, ancho, alto);
        return true;
    }

    /// <summary>
    /// Test punto-en-polígono (ray casting). Devuelve true si
    /// <paramref name="punto"/> cae dentro de la polilínea cerrada
    /// <paramref name="poli"/>. Usado por el hit-test del lienzo CAD.
    /// </summary>
    public static bool ContienePunto(PolilineaCad poli, PuntoCad punto)
    {
        if (poli is null) return false;
        var v = poli.Vertices;
        int n = v.Count;
        if (n < 3) return false;

        bool dentro = false;
        // Algoritmo clásico de cruce de rayos: cuenta cuántas aristas cruza
        // un rayo horizontal desde el punto hacia +X.
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = v[i].X, yi = v[i].Y;
            double xj = v[j].X, yj = v[j].Y;
            bool cruza = ((yi > punto.Y) != (yj > punto.Y)) &&
                         (punto.X < (xj - xi) * (punto.Y - yi) / (yj - yi) + xi);
            if (cruza) dentro = !dentro;
        }
        return dentro;
    }

    private static bool CasiIgual(PuntoCad a, PuntoCad b, double tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;
}
