using System;
using LosasPlus.Models;

namespace LosasPlus.Transmision;

/// <summary>
/// Carga que un borde de losa transmite a su viga de apoyo, por el método de
/// áreas tributarias (líneas a 45° desde las esquinas del paño).
/// </summary>
/// <param name="Longitud">Longitud del borde, en m.</param>
/// <param name="FuerzaTotal">Fuerza total que recibe el borde = q · área tributaria (en la unidad de q · m²).</param>
/// <param name="LineaUniformeEquivalente">
/// Carga lineal uniforme que conserva la fuerza total (= <see cref="FuerzaTotal"/> / <see cref="Longitud"/>),
/// apta como entrada distribuida para el motor de vigas (en la unidad de q · m).
/// </param>
/// <param name="IntensidadPico">Intensidad máxima de la carga triangular/trapezoidal (= q · lado_corto / 2).</param>
public readonly record struct CargaBorde(
    double Longitud,
    double FuerzaTotal,
    double LineaUniformeEquivalente,
    double IntensidadPico);

/// <summary>
/// Reparto de la carga de un paño de losa a sus cuatro bordes. Los dos bordes
/// <see cref="BordeCorto"/> (de longitud <see cref="LadoCorto"/>) reciben carga
/// triangular; los dos <see cref="BordeLargo"/> (de longitud
/// <see cref="LadoLargo"/>) reciben carga trapezoidal.
/// </summary>
public sealed record RepartoLosa(
    double LadoCorto,
    double LadoLargo,
    CargaBorde BordeCorto,
    CargaBorde BordeLargo,
    double CargaTotal);

/// <summary>
/// Transmisión de cargas losa→viga (Fase J — «Liga G», bajada de cargas) por el
/// método clásico de <b>áreas tributarias</b>: se trazan líneas a 45° desde las
/// esquinas del paño rectangular, repartiendo el área (y por tanto la carga
/// superficial q) entre los cuatro bordes de apoyo. Los bordes cortos reciben
/// un triángulo; los largos, un trapecio.
///
/// <para>
/// Para un paño de lados a ≤ b: cada borde corto (longitud a) toma un triángulo
/// de área a²/4; cada borde largo (longitud b) toma un trapecio de área
/// (a/4)·(2b − a). La suma de las cuatro áreas es a·b (se verifica). Sirve para
/// paños en dos direcciones; cuando b/a ≥ 2 el reparto tiende al comportamiento
/// en una dirección (los bordes largos toman casi toda la carga).
/// </para>
///
/// <para>
/// Tipo <b>puro de dominio</b> — sin dependencias de UI/SharpDX, multiplataforma
/// y testeable. Las unidades de salida son las de <c>q</c> (p. ej. t/m²) por la
/// longitud correspondiente; la conversión a kN para el motor de vigas la hace
/// el consumidor.
/// </para>
/// </summary>
public static class RepartoCargaLosa
{
    /// <summary>
    /// Reparte la carga superficial <paramref name="q"/> de un paño
    /// <paramref name="lx"/> × <paramref name="ly"/> a sus bordes. Dimensiones o
    /// carga no positivas devuelven un reparto nulo.
    /// </summary>
    public static RepartoLosa Calcular(double lx, double ly, double q)
    {
        if (lx <= 0 || ly <= 0 || q <= 0)
            return new RepartoLosa(0, 0, default, default, 0);

        double a = Math.Min(lx, ly);   // lado corto
        double b = Math.Max(lx, ly);   // lado largo

        double pico = q * a / 2.0;      // intensidad máxima común a ambos tipos de borde

        // Borde corto (longitud a): triángulo de área a²/4.
        double fCorto = q * a * a / 4.0;
        var bordeCorto = new CargaBorde(a, fCorto, fCorto / a, pico);

        // Borde largo (longitud b): trapecio de área (a/4)·(2b − a).
        double fLargo = q * (a / 4.0) * (2.0 * b - a);
        var bordeLargo = new CargaBorde(b, fLargo, fLargo / b, pico);

        return new RepartoLosa(a, b, bordeCorto, bordeLargo, q * lx * ly);
    }

    /// <summary>Reparte la carga de una <see cref="Losa"/> (usa <c>Lx</c>, <c>Ly</c> y <c>Carga</c>).</summary>
    public static RepartoLosa Calcular(Losa losa)
    {
        if (losa is null) throw new ArgumentNullException(nameof(losa));
        return Calcular(losa.Lx, losa.Ly, losa.Carga);
    }
}
