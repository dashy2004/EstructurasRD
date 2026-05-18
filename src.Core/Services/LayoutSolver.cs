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

        // ---- Modo híbrido (Fase 2): pre-posicionar las losas ANCLADAS ----
        // Una losa con PosX/PosY explícitos conserva esas coordenadas exactas
        // y no se recalcula topológicamente. El BFS las respeta porque saltea
        // cualquier vecino ya presente en 'placements'.
        bool hayAncladas = false;
        foreach (var l in sistema.Losas)
        {
            if (l.TienePosicionExplicita)
            {
                placements[l.Id] = new Placement
                {
                    Id = l.Id, Losa = l,
                    X = l.PosX!.Value, Y = l.PosY!.Value,
                };
                hayAncladas = true;
            }
        }

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

        // BFS: si hay losas ancladas, la cola arranca con todas ellas (sus
        // vecinos flotantes se infieren relativo a ellas). Si no hay ninguna,
        // arranca desde la losa de menor ID en (0,0) — comportamiento legacy.
        var queue = new Queue<int>();
        var sortedIds = byId.Keys.OrderBy(x => x).ToList();
        if (hayAncladas)
        {
            foreach (var id in placements.Keys.OrderBy(x => x)) queue.Enqueue(id);
        }
        else
        {
            var rootId = sortedIds[0];
            placements[rootId] = new Placement { Id = rootId, Losa = byId[rootId], X = 0, Y = 0 };
            queue.Enqueue(rootId);
        }

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

        // Normalizar para que min(x, y) = 0 — SÓLO en modo topológico puro.
        // Si hay losas ancladas, sus PosX/PosY son coordenadas absolutas del
        // lienzo CAD y no deben desplazarse: se preserva el sistema de
        // coordenadas tal cual.
        if (!hayAncladas)
        {
            double minX = placements.Values.Min(p => p.X);
            double minY = placements.Values.Min(p => p.Y);
            foreach (var p in placements.Values) { p.X -= minX; p.Y -= minY; }
        }

        result.Placements.AddRange(placements.Values.OrderBy(p => p.Id));
        result.MinX = placements.Values.Min(p => p.X);
        result.MinY = placements.Values.Min(p => p.Y);
        result.MaxX = placements.Values.Max(p => p.X + p.Width);
        result.MaxY = placements.Values.Max(p => p.Y + p.Height);
        return result;
    }
}
