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
