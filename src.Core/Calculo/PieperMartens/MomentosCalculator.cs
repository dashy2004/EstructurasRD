using System;
using LosasPlus.Models;

namespace LosasPlus.Calculo.PieperMartens;

/// <summary>
/// Momentos flectores de diseño de una losa (ton·m/m). El subíndice indica la
/// dirección de la armadura: <c>Mfx</c>/<c>Msx</c> arman según X, <c>Mfy</c>/
/// <c>Msy</c> según Y. Momentos de empotramiento (Ms) en valor positivo.
/// </summary>
public readonly record struct MomentosLosa(double Mfx, double Mfy, double Msx, double Msy);

/// <summary>
/// Calcula los momentos de una losa por el método de Pieper-Martens a partir de
/// los factores de <see cref="TablaPieperMartens"/> y las luces libres de la
/// losa. Cada dirección usa su propia luz: Mfx=q·Lx²/Fx, Mfy=q·Ly²/Fy, etc.
/// </summary>
public sealed class MomentosCalculator
{
    private readonly TablaPieperMartens _tabla;

    public MomentosCalculator(TablaPieperMartens tabla) => _tabla = tabla;

    /// <summary>Momentos (Mfx,Mfy,Msx,Msy) de la losa, en ton·m/m.</summary>
    public MomentosLosa Calcular(Losa losa)
    {
        double q = losa.Carga, lx = losa.Lx, ly = losa.Ly;
        if (lx <= 0 || ly <= 0)
            throw new ArgumentException("Las luces Lx y Ly deben ser positivas.", nameof(losa));

        // Tipos de voladizo (one-way): la losa vuela como ménsula y no sale de
        // las tablas two-way. El momento de empotramiento es q·L²/2 sobre la luz
        // del voladizo; los demás momentos son 0. Sólo el 71 está verificado
        // contra Losas.exe (RESTAURANTE 2); el 72 es su simétrico.
        if (EsVoladizo(losa.Tipo, out bool vuelaSegunY))
        {
            return vuelaSegunY
                ? new MomentosLosa(0, 0, 0, q * ly * ly / 2.0)
                : new MomentosLosa(0, 0, q * lx * lx / 2.0, 0);
        }

        var f = _tabla.Factores(_tabla.SubtipoDeCodigoDL(losa.Tipo), ly / lx);

        double mfx = q * lx * lx / f.Fx;
        double mfy = q * ly * ly / f.Fy;
        double msx = f.Sx is double sx ? q * lx * lx / Math.Abs(sx) : 0.0;
        double msy = f.Sy is double sy ? q * ly * ly / Math.Abs(sy) : 0.0;

        return new MomentosLosa(mfx, mfy, msx, msy);
    }

    /// <summary>
    /// Tipos de voladizo (franja en una dirección). <paramref name="vuelaSegunY"/>
    /// indica si la ménsula proyecta sobre Ly (momento Msy) o sobre Lx (Msx).
    /// La dirección del vuelo no lleva acero de vano (solo el de apoyo); la
    /// dirección soportada lleva acero de distribución.
    /// </summary>
    public static bool EsVoladizo(int tipo, out bool vuelaSegunY)
    {
        switch (tipo)
        {
            case 71: vuelaSegunY = true;  return true;   // vuela sobre Ly → Msy = q·Ly²/2
            case 72: vuelaSegunY = false; return true;   // vuela sobre Lx → Msx = q·Lx²/2
            default: vuelaSegunY = false; return false;
        }
    }
}
