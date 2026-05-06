using System;
using System.Collections.Generic;
using System.Linq;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Reconstruye la posición topológica 2D de cada losa a partir de las adyacencias
/// declaradas en <see cref="Sistema.BordesX"/> y <see cref="Sistema.BordesY"/>.
///
/// Convención conforme a Losas.hlp:
/// <list type="bullet">
///   <item><b>ADIC. SEGÚN X (B-I, B-J)</b>: las losas comparten un borde paralelo a Y;
///         B-J queda a la derecha de B-I (en el sentido positivo del eje X).</item>
///   <item><b>ADIC. SEGÚN Y (B-I, B-J)</b>: las losas comparten un borde paralelo a X;
///         B-J queda debajo de B-I (en el sentido positivo del eje Y, descendente en pantalla).</item>
/// </list>
///
/// Algoritmo: BFS desde la losa con menor ID. Cada vez que se descubre un vecino no
/// posicionado, se le asigna coordenada relativa basada en las dimensiones de la losa
/// origen (Lx para adyacencia X, Ly para adyacencia Y). Tras posicionar todo, se
/// normaliza para que la mínima x e y sean 0.
///
/// Si una losa queda desconectada (no aparece en ninguna adyacencia), se la coloca
/// en una "fila huérfana" debajo del cluster principal para que sea visible.
/// </summary>
public static class LayoutSolver
{
    public sealed class Placement
    {
        public required int Id { get; init; }
        public required Losa Losa { get; init; }
        /// <summary>Origen en metros (esquina superior-izquierda) en el sistema topológico.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public double Width => Losa.Lx > 0 ? Losa.Lx : 1.0;
        public double Height => Losa.Ly > 0 ? Losa.Ly : 1.0;
        /// <summary>True si la losa quedó desconectada del grafo principal.</summary>
        public bool Huerfana { get; set; }
    }

    public sealed class LayoutResult
    {
        public List<Placement> Placements { get; } = new();
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }

    public static LayoutResult Solve(Sistema sistema)
    {
        var result = new LayoutResult();
        if (sistema.Losas.Count == 0) return result;

        var byId = sistema.Losas.ToDictionary(l => l.Id);
        var placements = new Dictionary<int, Placement>();

        // Tabla de adyacencias: para cada losa, lista de (vecino, dirección, esVecino-J?)
        // direccion: 'X' (vecino horizontal), 'Y' (vecino vertical)
        // forward: true si el vecino tiene que ir a la derecha/abajo de this; false si va a la izquierda/arriba.
        var adj = new Dictionary<int, List<(int neighbor, char dir, bool forward)>>();
        foreach (var id in byId.Keys) adj[id] = new();

        foreach (var b in sistema.BordesX)
        {
            if (!byId.ContainsKey(b.BI) || !byId.ContainsKey(b.BJ)) continue;
            adj[b.BI].Add((b.BJ, 'X', forward: true));
            adj[b.BJ].Add((b.BI, 'X', forward: false));
        }
        foreach (var b in sistema.BordesY)
        {
            if (!byId.ContainsKey(b.BI) || !byId.ContainsKey(b.BJ)) continue;
            adj[b.BI].Add((b.BJ, 'Y', forward: true));
            adj[b.BJ].Add((b.BI, 'Y', forward: false));
        }

        // BFS desde la losa con menor ID
        var queue = new Queue<int>();
        var sortedIds = byId.Keys.OrderBy(x => x).ToList();
        var rootId = sortedIds[0];
        placements[rootId] = new Placement { Id = rootId, Losa = byId[rootId], X = 0, Y = 0 };
        queue.Enqueue(rootId);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var p = placements[cur];

            foreach (var (nb, dir, fwd) in adj[cur])
            {
                if (placements.ContainsKey(nb)) continue;  // ya posicionado, primer match gana
                var nbLosa = byId[nb];
                double nx, ny;
                if (dir == 'X')
                {
                    if (fwd) { nx = p.X + p.Width; ny = p.Y; }
                    else     { nx = p.X - nbLosa.Lx; ny = p.Y; }
                }
                else  // 'Y'
                {
                    if (fwd) { nx = p.X; ny = p.Y + p.Height; }
                    else     { nx = p.X; ny = p.Y - nbLosa.Ly; }
                }
                placements[nb] = new Placement { Id = nb, Losa = nbLosa, X = nx, Y = ny };
                queue.Enqueue(nb);
            }
        }

        // Losas desconectadas: BFS adicional desde cada huérfano, en filas separadas debajo del bbox principal
        if (placements.Count < byId.Count)
        {
            // Bbox del cluster conectado
            double maxYSoFar = placements.Values.Max(p => p.Y + p.Height);
            double padY = 1.0;  // 1 metro de separación visual

            foreach (var orphanId in sortedIds.Where(id => !placements.ContainsKey(id)))
            {
                placements[orphanId] = new Placement
                {
                    Id = orphanId, Losa = byId[orphanId],
                    X = 0, Y = maxYSoFar + padY,
                    Huerfana = true,
                };
                maxYSoFar = placements[orphanId].Y + placements[orphanId].Height;
                padY = 1.0;
            }
        }

        // Normalizar para que min(x, y) = 0
        double minX = placements.Values.Min(p => p.X);
        double minY = placements.Values.Min(p => p.Y);
        foreach (var p in placements.Values) { p.X -= minX; p.Y -= minY; }

        result.Placements.AddRange(placements.Values.OrderBy(p => p.Id));
        result.MinX = 0;
        result.MinY = 0;
        result.MaxX = placements.Values.Max(p => p.X + p.Width);
        result.MaxY = placements.Values.Max(p => p.Y + p.Height);
        return result;
    }
}
