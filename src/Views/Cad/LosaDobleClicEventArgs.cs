using System;
using Avalonia;
using LosasPlus.Models;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Argumentos del evento <c>CadCanvasHost.LosaDobleClicada</c>: identifican la
/// losa doble-clickeada y dónde anclar el editor flotante in-canvas
/// (Iteración 2 del Epic v1.2).
/// </summary>
public sealed class LosaDobleClicEventArgs : EventArgs
{
    public LosaDobleClicEventArgs(Losa losa, Rect rectPantalla, double posX, double posY)
    {
        Losa = losa;
        RectPantalla = rectPantalla;
        PosX = posX;
        PosY = posY;
    }

    /// <summary>La losa del SSOT bajo el doble clic.</summary>
    public Losa Losa { get; }

    /// <summary>Rectángulo de la losa en coordenadas de pantalla del host (post-transform).</summary>
    public Rect RectPantalla { get; }

    /// <summary>Posición efectiva X actual de la losa (m) — se pasa intacta al comando.</summary>
    public double PosX { get; }

    /// <summary>Posición efectiva Y actual de la losa (m) — se pasa intacta al comando.</summary>
    public double PosY { get; }
}
