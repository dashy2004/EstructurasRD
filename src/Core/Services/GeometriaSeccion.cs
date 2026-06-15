using System;

namespace LosasPlus.Services;

/// <summary>Propiedades de una sección rectangular para el modelo del motor (SI: m², m⁴).</summary>
public readonly record struct PropsSeccion(double Area, double InerciaY, double InerciaZ, double ConstanteTorsion);

public static class GeometriaSeccion
{
    /// <param name="b">Ancho (Base) en m.</param>
    /// <param name="h">Peralte en m.</param>
    public static PropsSeccion Rectangular(double b, double h)
    {
        double area = b * h;
        double iz = b * h * h * h / 12.0;   // eje fuerte (local z)
        double iy = h * b * b * b / 12.0;   // eje débil (local y)
        double largo = Math.Max(b, h);
        double corto = Math.Min(b, h);
        double r = corto / largo;
        double beta = (1.0 / 3.0) - 0.21 * r * (1.0 - Math.Pow(r, 4) / 12.0);
        double j = largo * corto * corto * corto * beta;  // torsión rectangular
        return new PropsSeccion(area, iy, iz, j);
    }
}
