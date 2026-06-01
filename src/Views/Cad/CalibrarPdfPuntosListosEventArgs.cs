using System;
using System.Windows;

namespace LosasPlus.Views.Cad;

/// <summary>
/// Argumentos del evento <c>CadCanvasHost.CalibrarPdfPuntosListos</c>: el
/// host los emite cuando el usuario coloca el segundo punto de calibración
/// del PDF Underlay (Iteración 5 Epic v1.2).
///
/// <para>
/// El <c>CadView</c> escucha el evento, posiciona el editor flotante
/// <c>EditorCalibrarPdf</c> en <see cref="MidpointPantalla"/>, le da foco
/// al TextBox de distancia real y, al confirmar, arma un
/// <c>CalibracionPdfArgs</c> con <see cref="PivoteX"/>, <see cref="PivoteY"/>
/// y <see cref="DistanciaActual"/> + la distancia tipeada por el usuario.
/// </para>
/// </summary>
public sealed class CalibrarPdfPuntosListosEventArgs : EventArgs
{
    public CalibrarPdfPuntosListosEventArgs(
        double pivoteX,
        double pivoteY,
        double distanciaActual,
        Point midpointPantalla)
    {
        PivoteX = pivoteX;
        PivoteY = pivoteY;
        DistanciaActual = distanciaActual;
        MidpointPantalla = midpointPantalla;
    }

    /// <summary>Coordenada X de P₁ en metros del dominio del lienzo.</summary>
    public double PivoteX { get; }

    /// <summary>Coordenada Y de P₁ en metros del dominio del lienzo.</summary>
    public double PivoteY { get; }

    /// <summary>Distancia geométrica entre P₁ y P₂ en metros del dominio.</summary>
    public double DistanciaActual { get; }

    /// <summary>
    /// Punto medio entre P₁ y P₂ en coordenadas de pantalla del host
    /// (post-transform), donde se debe anclar el editor flotante.
    /// </summary>
    public Point MidpointPantalla { get; }
}
