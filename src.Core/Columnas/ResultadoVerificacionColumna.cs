namespace LosasPlus.Columnas;

/// <summary>
/// Resultado de la verificación radial de un punto de demanda <c>(Mu, Pu)</c>
/// contra la frontera de diseño <c>CurvaDiseno</c> de un
/// <see cref="DiagramaInteraccion2D"/> (Fase 5, Iteración 3).
///
/// <para>
/// El ratio se calcula como la distancia euclidiana del origen al punto de
/// demanda dividida por la distancia del origen al punto de intersección del
/// rayo radial con el polígono de diseño cerrado.
/// </para>
/// </summary>
/// <param name="RatioEficiencia">
/// Ratio Demanda/Capacidad radial. Es <c>0</c> si la demanda es nula, y
/// <see cref="double.PositiveInfinity"/> si la sección no aporta capacidad
/// de diseño en la dirección del rayo.
/// </param>
/// <param name="EsSeguro"><c>true</c> si <see cref="RatioEficiencia"/> ≤ 1.0.</param>
/// <param name="PuntoCapacidad">
/// Punto de intersección del rayo radial con la frontera <c>CurvaDiseno</c>
/// — la capacidad de diseño de la sección colineal con la demanda evaluada.
/// </param>
public sealed record ResultadoVerificacionColumna(
    double RatioEficiencia,
    bool EsSeguro,
    PuntoInteraccion PuntoCapacidad)
{
    /// <summary>Resultado para una demanda nula (Pu = Mu = 0).</summary>
    public static ResultadoVerificacionColumna Seguro { get; } =
        new(0.0, true, new PuntoInteraccion(0.0, 0.0));

    /// <summary>
    /// Resultado para una sección sin capacidad — diagrama vacío o demanda
    /// fuera de cualquier intersección admisible con la frontera. El ratio
    /// es <see cref="double.PositiveInfinity"/>.
    /// </summary>
    public static ResultadoVerificacionColumna SinCapacidad { get; } =
        new(double.PositiveInfinity, false, new PuntoInteraccion(0.0, 0.0));
}
