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

        double eps = ly / lx;
        // Con tolerancia: 2.6/1.3 da 2.0000000000000004 y debe seguir en la tabla.
        if (eps > EpsMaximo + ToleranciaEps) return EnUnaDireccion(losa.Tipo, q, lx, armaSegunX: true);
        if (eps < EpsMinimo - ToleranciaEps) return EnUnaDireccion(losa.Tipo, q, ly, armaSegunX: false);

        var f = _tabla.Factores(_tabla.SubtipoDeCodigoDL(losa.Tipo), eps);

        double mfx = q * lx * lx / f.Fx;
        double mfy = q * ly * ly / f.Fy;
        double msx = f.Sx is double sx ? q * lx * lx / Math.Abs(sx) : 0.0;
        double msy = f.Sy is double sy ? q * ly * ly / Math.Abs(sy) : 0.0;

        return new MomentosLosa(mfx, mfy, msx, msy);
    }

    /// <summary>Rango de ε = Ly/Lx que cubren las tablas; fuera de él la losa trabaja en una dirección.</summary>
    public const double EpsMinimo = 0.5, EpsMaximo = 2.0;
    private const double ToleranciaEps = 1e-9;

    // Divisor del momento de vano según cuántos extremos de la luz corta son
    // continuos: promedio entre la viga simplemente apoyada (1/8) y la viga con
    // su continuidad real (1/8, 9/128 o 1/24), igual que Losas.exe.
    private static readonly double[] DivisorVano = { 8.0, 10.24, 12.0 };
    // Divisor del momento en cada extremo continuo de la luz corta. La posición 0
    // (sin extremos continuos) no se usa: ahí el momento de apoyo es cero.
    private static readonly double[] DivisorApoyo = { 0.0, 8.0, 12.0 };
    private const double DivisorApoyoTransversal = 8.0;

    /// <summary>
    /// Losa en una dirección (ε fuera de 0.50–2.00): trabaja como franja sobre la
    /// luz corta. Regla deducida y verificada contra el listado de Losas.exe del
    /// sistema de demostración de 27 losas (tipos 10, 21, 22, 31 y 32).
    /// </summary>
    /// <param name="luzCorta">Luz sobre la que trabaja la franja, en metros.</param>
    /// <param name="armaSegunX">True si la luz corta es Lx (acero principal según X).</param>
    private MomentosLosa EnUnaDireccion(int tipo, double q, double luzCorta, bool armaSegunX)
    {
        var (ns, ew) = _tabla.BordesContinuos(tipo);
        int enLaLuz = armaSegunX ? ew : ns;          // extremos continuos de la franja
        int transversales = armaSegunX ? ns : ew;    // bordes continuos paralelos a la franja

        // Con continuidad en los dos sentidos a la vez no hay listado de control
        // para saber qué momento transversal usa Losas.exe: no se adivina.
        if (enLaLuz > 0 && transversales > 0)
            throw new NotSupportedException(
                $"Tipo {tipo} trabajando en una dirección (Ly/Lx fuera de 0.50–2.00) " +
                "con bordes continuos en ambos sentidos: caso sin verificar contra Losas.exe.");

        double ql2 = q * luzCorta * luzCorta;
        double vano = ql2 / DivisorVano[enLaLuz];
        double apoyo = enLaLuz > 0 ? ql2 / DivisorApoyo[enLaLuz] : 0.0;
        double transversal = transversales > 0 ? ql2 / DivisorApoyoTransversal : 0.0;

        return armaSegunX
            ? new MomentosLosa(vano, 0.0, apoyo, transversal)
            : new MomentosLosa(0.0, vano, transversal, apoyo);
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
