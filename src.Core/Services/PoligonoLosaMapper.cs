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

    /// <summary>
    /// Descompone un polígono cerrado <b>rectilíneo</b> (todos los lados
    /// paralelos a los ejes — p. ej. un ambiente en L o en T) en rectángulos
    /// ortogonales que cubren exactamente su interior (F2). Algoritmo de
    /// celdas: la rejilla definida por las X/Y distintas de los vértices se
    /// clasifica con <see cref="ContienePunto"/> (centro de celda) y las
    /// celdas se fusionan en bandas horizontales, extendiendo verticalmente
    /// los rectángulos con el mismo rango X. Un rectángulo simple produce su
    /// propio bbox (1 elemento). Devuelve <c>false</c> si el polígono no es
    /// cerrado, no es rectilíneo (±<paramref name="tolerancia"/>) o queda
    /// degenerado.
    /// </summary>
    public static bool TryDescomponerRectilineo(PolilineaCad poli,
                                                out List<RectanguloMapeado> rects,
                                                double tolerancia = 0.02)
    {
        rects = new List<RectanguloMapeado>();
        if (poli is null || !poli.Cerrada) return false;

        var pts = poli.Vertices.ToList();
        if (pts.Count >= 2 && CasiIgual(pts[0], pts[^1], tolerancia))
            pts.RemoveAt(pts.Count - 1);
        if (pts.Count < 4) return false;

        // Rectilíneo: cada lado horizontal XOR vertical.
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            bool horizontal = Math.Abs(a.Y - b.Y) <= tolerancia;
            bool vertical   = Math.Abs(a.X - b.X) <= tolerancia;
            if (horizontal == vertical) return false;
        }

        var xs = CoordenadasDistintas(pts.Select(p => p.X), tolerancia);
        var ys = CoordenadasDistintas(pts.Select(p => p.Y), tolerancia);
        if (xs.Count < 2 || ys.Count < 2) return false;

        var contorno = new PolilineaCad { Cerrada = true, Vertices = pts };

        // Rectángulos "abiertos" (en crecimiento vertical) de la banda anterior.
        var abiertos = new List<(double X0, double X1, double Y0)>();
        for (int j = 0; j < ys.Count - 1; j++)
        {
            double cy = (ys[j] + ys[j + 1]) / 2.0;

            // Corridas de celdas interiores contiguas en esta banda.
            var corridas = new List<(double X0, double X1)>();
            int inicio = -1;
            for (int i = 0; i < xs.Count - 1; i++)
            {
                bool dentro = ContienePunto(contorno, new PuntoCad((xs[i] + xs[i + 1]) / 2.0, cy));
                if (dentro && inicio < 0) inicio = i;
                if (inicio >= 0 && (!dentro || i == xs.Count - 2))
                {
                    int fin = dentro ? i : i - 1;
                    corridas.Add((xs[inicio], xs[fin + 1]));
                    inicio = -1;
                }
            }

            // Continuar los abiertos con el mismo rango X; abrir los nuevos.
            var siguientes = new List<(double X0, double X1, double Y0)>();
            foreach (var (x0, x1) in corridas)
            {
                int k = abiertos.FindIndex(a =>
                    Math.Abs(a.X0 - x0) <= tolerancia && Math.Abs(a.X1 - x1) <= tolerancia);
                if (k >= 0) { siguientes.Add(abiertos[k]); abiertos.RemoveAt(k); }
                else siguientes.Add((x0, x1, ys[j]));
            }
            // Los abiertos sin continuación se cierran en el borde de la banda.
            foreach (var a in abiertos)
                rects.Add(new RectanguloMapeado(a.X0, a.Y0, a.X1, ys[j], a.X1 - a.X0, ys[j] - a.Y0));
            abiertos = siguientes;
        }
        foreach (var a in abiertos)
            rects.Add(new RectanguloMapeado(a.X0, a.Y0, a.X1, ys[^1], a.X1 - a.X0, ys[^1] - a.Y0));

        rects.RemoveAll(r => r.Ancho <= tolerancia || r.Alto <= tolerancia);
        return rects.Count > 0;
    }

    /// <summary>Coordenadas ordenadas sin duplicados (±tol).</summary>
    private static List<double> CoordenadasDistintas(IEnumerable<double> valores, double tol)
    {
        var orden = valores.OrderBy(v => v).ToList();
        var unicos = new List<double>();
        foreach (var v in orden)
            if (unicos.Count == 0 || v - unicos[^1] > tol)
                unicos.Add(v);
        return unicos;
    }

    private static bool CasiIgual(PuntoCad a, PuntoCad b, double tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;
}
