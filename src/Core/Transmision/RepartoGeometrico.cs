using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LosasPlus.Models;
using LosasPlus.Vigas;

namespace LosasPlus.Transmision;

/// <summary>
/// Asignación de la carga de un borde de losa a la viga que lo soporta.
/// </summary>
/// <param name="Viga">La viga sobre la que descansa el borde.</param>
/// <param name="CargaLineal">Carga lineal uniforme equivalente del borde (unidad de q · m, p. ej. t/m).</param>
/// <param name="FuerzaTotal">Fuerza total del borde (unidad de q · m²).</param>
public readonly record struct AsignacionViga(Viga Viga, double CargaLineal, double FuerzaTotal);

/// <summary>
/// Carga distribuida total que recibe una viga tras agregar los bordes de todas
/// las losas que se apoyan en ella en un nivel.
/// </summary>
/// <param name="Viga">La viga.</param>
/// <param name="CargaLineal">Suma de las cargas lineales equivalentes de los bordes que soporta (q · m).</param>
/// <param name="FuerzaTotal">Suma de las fuerzas totales de esos bordes (q · m²).</param>
public readonly record struct CargaVigaAgregada(Viga Viga, double CargaLineal, double FuerzaTotal);

/// <summary>
/// Carga axial (vertical) que una columna recibe de las vigas que apoyan en
/// ella tras el descenso geométrico viga→columna.
/// </summary>
/// <param name="Columna">La columna.</param>
/// <param name="CargaAxial">Suma de las medias-reacciones de las vigas cuyos extremos caen sobre ella (q · m²).</param>
public readonly record struct CargaColumnaAxial(Columna Columna, double CargaAxial);

/// <summary>
/// Reparto <b>geométrico</b> losa→viga (Fase J.15): usando las posiciones en
/// planta de losas (<see cref="Losa.CoordenadaX"/>/Y + Lx/Ly) y vigas
/// (<see cref="Viga.OrigenX"/>/Y + ángulo), asigna la carga tributaria de cada
/// uno de los cuatro bordes del paño a la viga <b>colineal y solapada</b> con
/// ese borde. Complementa el reparto no-geométrico de <see cref="RepartoCargaLosa"/>
/// (que sólo divide la carga en bordes corto/largo, sin saber a qué viga van).
///
/// <para>Tipo puro de dominio (System.Numerics) — multiplataforma y testeable.</para>
/// </summary>
public static class RepartoGeometrico
{
    /// <summary>Tolerancia de colinealidad y de solape, en metros.</summary>
    public const double Tolerancia = 0.05;

    /// <summary>
    /// Asigna los bordes del <paramref name="losa"/> a las <paramref name="vigas"/>
    /// que los soportan. Cada borde se asigna a la primera viga colineal y
    /// solapada; los bordes sin viga no producen asignación.
    /// </summary>
    public static List<AsignacionViga> AsignarLosaAVigas(Losa losa, IEnumerable<Viga> vigas)
    {
        var resultado = new List<AsignacionViga>();
        if (losa is null || vigas is null || losa.Lx <= 0 || losa.Ly <= 0 || losa.Carga <= 0)
            return resultado;

        var reparto = RepartoCargaLosa.Calcular(losa.Lx, losa.Ly, losa.Carga);
        // Bordes de longitud Lx usan la carga cuyo lado coincide; ídem Ly.
        var cargaLx = losa.Lx <= losa.Ly ? reparto.BordeCorto : reparto.BordeLargo;
        var cargaLy = losa.Ly <= losa.Lx ? reparto.BordeCorto : reparto.BordeLargo;

        float x = (float)losa.CoordenadaX, y = (float)losa.CoordenadaY;
        float lx = (float)losa.Lx, ly = (float)losa.Ly;
        var p00 = new Vector2(x, y);
        var p10 = new Vector2(x + lx, y);
        var p11 = new Vector2(x + lx, y + ly);
        var p01 = new Vector2(x, y + ly);

        var bordes = new (Vector2 A, Vector2 B, CargaBorde Carga)[]
        {
            (p00, p10, cargaLx), // inferior
            (p01, p11, cargaLx), // superior
            (p00, p01, cargaLy), // izquierdo
            (p10, p11, cargaLy), // derecho
        };

        var lista = vigas as IList<Viga> ?? new List<Viga>(vigas);
        foreach (var (a, b, carga) in bordes)
        {
            foreach (var viga in lista)
            {
                var va = new Vector2((float)viga.OrigenX, (float)viga.OrigenY);
                var vb = new Vector2((float)viga.ExtremoX, (float)viga.ExtremoY);
                if (SegmentoSoporta(a, b, va, vb))
                {
                    resultado.Add(new AsignacionViga(viga, carga.LineaUniformeEquivalente, carga.FuerzaTotal));
                    break; // un borde lo soporta la primera viga colineal y solapada
                }
            }
        }
        return resultado;
    }

    /// <summary>
    /// Asigna geométricamente las cargas de <b>todas las losas</b> de un
    /// <paramref name="nivel"/> a sus vigas y las <b>agrega por viga</b>: una
    /// viga compartida por dos paños adyacentes suma las cargas lineales de
    /// ambos bordes. El resultado es la carga distribuida total de cada viga,
    /// apta como entrada al motor de vigas. Sólo incluye vigas con carga.
    /// </summary>
    public static List<CargaVigaAgregada> AsignarNivel(Nivel nivel)
    {
        var acumulado = new Dictionary<Viga, (double W, double F)>();
        if (nivel is null) return new List<CargaVigaAgregada>();

        foreach (var sistema in nivel.Sistemas)
            foreach (var losa in sistema.Losas)
                foreach (var asign in AsignarLosaAVigas(losa, nivel.Vigas))
                {
                    acumulado.TryGetValue(asign.Viga, out var t);
                    acumulado[asign.Viga] = (t.W + asign.CargaLineal, t.F + asign.FuerzaTotal);
                }

        return acumulado
            .Select(kv => new CargaVigaAgregada(kv.Key, kv.Value.W, kv.Value.F))
            .ToList();
    }

    /// <summary>
    /// Aplica el reparto geométrico agregado (<see cref="AsignarNivel"/>) como
    /// cargas <see cref="TipoCargaElemento.Distribuida"/> sobre los tramos de
    /// cada viga del nivel, bajo el caso de carga <paramref name="codigoCaso"/>
    /// — dejando las vigas listas para resolverse con el motor de vigas.
    /// La carga lineal agregada se aplica uniforme sobre toda la viga (conserva
    /// la fuerza total: w · longitud = Σ fuerzas de borde).
    ///
    /// <para>
    /// <b>Idempotente</b>: antes de aplicar, retira de cada tramo las cargas
    /// distribuidas previas con el mismo <paramref name="codigoCaso"/>, así que
    /// re-ejecutar actualiza en vez de acumular. Devuelve el nº de vigas cargadas.
    /// </para>
    /// </summary>
    public static int AplicarCargasGeometricas(Nivel nivel, string codigoCaso)
    {
        if (nivel is null) return 0;

        var agregado = AsignarNivel(nivel);
        foreach (var carga in agregado)
            foreach (var tramo in carga.Viga.Tramos)
            {
                for (int i = tramo.Cargas.Count - 1; i >= 0; i--)
                    if (tramo.Cargas[i].Tipo == TipoCargaElemento.Distribuida &&
                        tramo.Cargas[i].CodigoCaso == codigoCaso)
                        tramo.Cargas.RemoveAt(i);

                tramo.Cargas.Add(new CargaElemento(
                    TipoCargaElemento.Distribuida, carga.CargaLineal, codigoCaso));
            }
        return agregado.Count;
    }

    /// <summary>Tolerancia (m) para considerar que el extremo de una viga apoya en una columna.</summary>
    public const double ToleranciaColumna = 0.30;

    /// <summary>
    /// Descenso geométrico <b>viga→columna</b> (Fase J): reparte la fuerza total
    /// de cada viga del <paramref name="nivel"/> (de <see cref="AsignarNivel"/>)
    /// a las columnas cercanas a sus extremos y la acumula por columna. Aproxima
    /// la viga como simplemente apoyada: media reacción a la columna más cercana
    /// a su origen y media a la del extremo (dentro de
    /// <paramref name="tolerancia"/> m). Un extremo sin columna cercana no
    /// asigna esa mitad (la viga descansaría en otra viga o muro). El reparto
    /// exacto por reacciones del motor de vigas queda como mejora futura.
    /// </summary>
    public static List<CargaColumnaAxial> AsignarVigasAColumnas(
        Nivel nivel, double tolerancia = ToleranciaColumna)
    {
        var acumulado = new Dictionary<Columna, double>();
        if (nivel is null || nivel.Columnas.Count == 0) return new List<CargaColumnaAxial>();

        foreach (var carga in AsignarNivel(nivel))
        {
            double mitad = carga.FuerzaTotal / 2.0;
            var colOrigen = ColumnaMasCercana(nivel.Columnas, carga.Viga.OrigenX, carga.Viga.OrigenY, tolerancia);
            var colExtremo = ColumnaMasCercana(nivel.Columnas, carga.Viga.ExtremoX, carga.Viga.ExtremoY, tolerancia);

            if (colOrigen is not null)
            {
                acumulado.TryGetValue(colOrigen, out var v);
                acumulado[colOrigen] = v + mitad;
            }
            if (colExtremo is not null)
            {
                acumulado.TryGetValue(colExtremo, out var v);
                acumulado[colExtremo] = v + mitad;
            }
        }

        return acumulado.Select(kv => new CargaColumnaAxial(kv.Key, kv.Value)).ToList();
    }

    /// <summary>Columna a ≤ <paramref name="tolerancia"/> m del punto (x,y), la más cercana, o null.</summary>
    private static Columna? ColumnaMasCercana(
        IEnumerable<Columna> columnas, double x, double y, double tolerancia)
    {
        Columna? mejor = null;
        double mejorDist = tolerancia;
        var p = new Vector2((float)x, (float)y);
        foreach (var columna in columnas)
        {
            float d = Vector2.Distance(p, new Vector2((float)columna.CoordenadaX, (float)columna.CoordenadaY));
            if (d <= mejorDist) { mejorDist = d; mejor = columna; }
        }
        return mejor;
    }

    /// <summary>
    /// True si el segmento de viga [<paramref name="va"/>, <paramref name="vb"/>]
    /// es colineal (dentro de <see cref="Tolerancia"/>) con el borde
    /// [<paramref name="e0"/>, <paramref name="e1"/>] y solapa su extensión.
    /// </summary>
    private static bool SegmentoSoporta(Vector2 e0, Vector2 e1, Vector2 va, Vector2 vb)
    {
        var d = e1 - e0;
        float len = d.Length();
        if (len < 1e-6f) return false;
        var u = d / len;
        float tol = (float)Tolerancia;

        // Ambos extremos de la viga deben estar sobre la recta del borde.
        if (DistanciaPerpendicular(e0, u, va) > tol || DistanciaPerpendicular(e0, u, vb) > tol)
            return false;

        // Proyecciones sobre la dirección del borde; ¿solapan [0, len]?
        float pa = Vector2.Dot(va - e0, u);
        float pb = Vector2.Dot(vb - e0, u);
        float lo = System.MathF.Min(pa, pb), hi = System.MathF.Max(pa, pb);
        float solape = System.MathF.Min(hi, len) - System.MathF.Max(lo, 0f);
        return solape > tol;
    }

    private static float DistanciaPerpendicular(Vector2 origen, Vector2 u, Vector2 p)
    {
        var w = p - origen;
        float proy = Vector2.Dot(w, u);
        return (w - proy * u).Length();
    }
}
