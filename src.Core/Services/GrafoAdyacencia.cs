using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Topología de adyacencias entre losas. Dado un <see cref="Sistema"/> y la Id
/// de una losa raíz, devuelve los Ids de todas las losas conectadas a ella
/// (incluyéndola) por la unión de <c>BordesX</c> y <c>BordesY</c> — la
/// componente conexa en el grafo de adyacencia.
///
/// <para>
/// Lo usa la herramienta «Mover Conectadas» (Iteración 3, Epic v1.2) para
/// arrastrar el bloque rígido al mover una losa. Servicio puro de dominio, sin
/// dependencias de UI — testeable en aislamiento.
/// </para>
/// </summary>
public static class GrafoAdyacencia
{
    /// <summary>
    /// Recolecta por <b>BFS</b> los Ids de las losas alcanzables desde
    /// <paramref name="losaIdRaiz"/> a través de la unión no dirigida de
    /// <c>sistema.BordesX</c> + <c>sistema.BordesY</c>. Cada <see cref="BordeAdic"/>
    /// aporta la arista <c>BI ↔ BJ</c>.
    ///
    /// <para>Devuelve un set vacío si <paramref name="sistema"/> es <c>null</c> o
    /// si la raíz no existe en <c>sistema.Losas</c>. Bordes cuyos Ids no
    /// corresponden a una losa del sistema se ignoran. Maneja ciclos y aristas
    /// duplicadas sin colgarse.</para>
    /// </summary>
    public static IReadOnlySet<int> ComponenteConexa(Sistema? sistema, int losaIdRaiz)
    {
        var visitados = new HashSet<int>();
        if (sistema is null) return visitados;

        var existentes = new HashSet<int>(sistema.Losas.Select(l => l.Id));
        if (!existentes.Contains(losaIdRaiz)) return visitados;

        // Construir la adyacencia como diccionario id → vecinos.
        var ady = new Dictionary<int, HashSet<int>>();
        void Conectar(int a, int b)
        {
            if (a == b || !existentes.Contains(a) || !existentes.Contains(b)) return;
            if (!ady.TryGetValue(a, out var la)) ady[a] = la = new HashSet<int>();
            la.Add(b);
        }
        foreach (var be in sistema.BordesX) { Conectar(be.BI, be.BJ); Conectar(be.BJ, be.BI); }
        foreach (var be in sistema.BordesY) { Conectar(be.BI, be.BJ); Conectar(be.BJ, be.BI); }

        var cola = new Queue<int>();
        cola.Enqueue(losaIdRaiz);
        visitados.Add(losaIdRaiz);
        while (cola.Count > 0)
        {
            int actual = cola.Dequeue();
            if (!ady.TryGetValue(actual, out var vecinos)) continue;
            foreach (var v in vecinos)
                if (visitados.Add(v))
                    cola.Enqueue(v);
        }
        return visitados;
    }
}
