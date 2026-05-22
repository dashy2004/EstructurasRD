using System;
using System.Collections.Generic;

namespace LosasPlus.Rc;

/// <summary>
/// Verificación de demanda/capacidad a flexión en una estación (abscisa) de una
/// viga continua: el ratio D/C y las banderas normativas de esa sección
/// (Fase 4, Iteración 3).
/// </summary>
/// <param name="X">Abscisa de la estación a lo largo de la viga, en metros.</param>
/// <param name="MomentoUltimo">
/// Momento último gobernante Mu, en kN·m — con signo: positivo si gobierna el
/// momento de tracción inferior, negativo si gobierna el de tracción superior.
/// </param>
/// <param name="MomentoResistente">
/// Momento de diseño φMn de la sección para la dirección gobernante, en kN·m.
/// </param>
/// <param name="RatioDemandaCapacidad">Ratio D/C = |Mu| / φMn (adimensional).</param>
/// <param name="CumpleCuantiaMinima">ρ ≥ ρ_min en la sección gobernante.</param>
/// <param name="CumpleLimiteDeformacion">εt ≥ 0.004 en la sección gobernante.</param>
public readonly record struct PuntoVerificacionRc(
    double X,
    double MomentoUltimo,
    double MomentoResistente,
    double RatioDemandaCapacidad,
    bool CumpleCuantiaMinima,
    bool CumpleLimiteDeformacion);

/// <summary>
/// Resultado de la verificación demanda/capacidad de una viga completa
/// (Fase 4, Iteración 3): el perfil de ratios D/C a lo largo del eje
/// longitudinal de la viga y el veredicto global.
/// </summary>
/// <param name="Puntos">Verificación en cada estación de la envolvente, ordenada por abscisa.</param>
/// <param name="RatioMaximo">Ratio D/C máximo de toda la viga.</param>
/// <param name="Conforme"><c>true</c> si ninguna estación supera D/C = 1.0.</param>
public sealed record VerificacionViga(
    IReadOnlyList<PuntoVerificacionRc> Puntos,
    double RatioMaximo,
    bool Conforme)
{
    /// <summary>
    /// Verificación vacía — para una viga sin resolver, inestable o sin tramos.
    /// </summary>
    public static VerificacionViga Vacia { get; } =
        new(Array.Empty<PuntoVerificacionRc>(), 0.0, true);
}
