using System;
using LosasPlus.Models;

namespace LosasPlus.Services;

/// <summary>
/// Hash estable de la geometría/topología en planta que consume el
/// <see cref="LayoutSolver"/> — la clave de invalidación del caché de layout
/// del lienzo CAD (UI1.1: auditoría del caché). Observa exactamente los inputs
/// del solver: por losa (Id, Lx, Ly, CoordenadaX, CoordenadaY, Anclada) y los
/// bordes de adyacencia (BI, BJ) en X e Y. Así, mover una losa en Planta 2D
/// cambia el hash y el CAD jamás sirve un layout obsoleto. No incluye datos sin
/// efecto en la posición (carga, espesor, tipo…). O(losas+bordes) — recalcularlo
/// cada frame para decidir el cache-hit no cuesta nada.
/// </summary>
public static class TopologiaPlanta
{
    public static int Hash(Sistema sistema)
    {
        var h = new HashCode();
        h.Add(sistema.Losas.Count);
        foreach (var l in sistema.Losas)
        {
            h.Add(l.Id);
            h.Add(l.Lx);
            h.Add(l.Ly);
            h.Add(l.CoordenadaX);
            h.Add(l.CoordenadaY);
            h.Add(l.Anclada);
        }
        h.Add(sistema.BordesX.Count);
        foreach (var b in sistema.BordesX) { h.Add(b.BI); h.Add(b.BJ); }
        h.Add(sistema.BordesY.Count);
        foreach (var b in sistema.BordesY) { h.Add(b.BI); h.Add(b.BJ); }
        return h.ToHashCode();
    }
}
