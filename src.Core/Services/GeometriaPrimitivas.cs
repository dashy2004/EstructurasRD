namespace LosasPlus.Services;

/// <summary>
/// Punto 2D inmutable, en metros — primitiva geométrica común de la FASE A del
/// editor avanzado (<c>PLAN_V1.1_DIBUJO_AVANZADO.md</c>). Sin dependencias de UI.
/// </summary>
/// <param name="X">Coordenada X, en metros.</param>
/// <param name="Y">Coordenada Y, en metros.</param>
public readonly record struct PuntoM(double X, double Y);

/// <summary>
/// Rectángulo ortogonal axis-aligned, inmutable, en metros (sistema de
/// coordenadas con Y descendente, igual que el lienzo CAD y que
/// <c>Losa.CoordenadaX/Y</c>). Es la moneda geométrica común de
/// <see cref="GeometriaEdicion"/>, <see cref="TopologiaAdyacencia"/> y
/// <see cref="MotorCotas"/>.
/// </summary>
/// <param name="X">X de la esquina superior-izquierda.</param>
/// <param name="Y">Y de la esquina superior-izquierda.</param>
/// <param name="Ancho">Ancho del rectángulo (≥ 0 en uso normal).</param>
/// <param name="Alto">Alto del rectángulo (≥ 0 en uso normal).</param>
public readonly record struct RectM(double X, double Y, double Ancho, double Alto)
{
    /// <summary>Borde izquierdo (= <see cref="X"/>).</summary>
    public double Izq => X;

    /// <summary>Borde superior (= <see cref="Y"/>).</summary>
    public double Sup => Y;

    /// <summary>Borde derecho (= <see cref="X"/> + <see cref="Ancho"/>).</summary>
    public double Der => X + Ancho;

    /// <summary>Borde inferior (= <see cref="Y"/> + <see cref="Alto"/>).</summary>
    public double Inf => Y + Alto;

    /// <summary>X del centro del rectángulo.</summary>
    public double CentroX => X + Ancho / 2.0;

    /// <summary>Y del centro del rectángulo.</summary>
    public double CentroY => Y + Alto / 2.0;
}
