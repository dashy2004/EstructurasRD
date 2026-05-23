using System;
using System.Collections.Generic;

namespace LosasPlus.Zapatas;

/// <summary>
/// Perfil de presiones de contacto en las cuatro esquinas de la base de una
/// <see cref="ZapataAislada"/> bajo una combinación de carga de servicio
/// concreta (Fase 6, Iteración 1).
/// </summary>
/// <param name="NombreCombinacion">Etiqueta de la combinación de servicio evaluada.</param>
/// <param name="Q1">Presión en la esquina (+B/2, +L/2), en kN/m².</param>
/// <param name="Q2">Presión en la esquina (−B/2, +L/2), en kN/m².</param>
/// <param name="Q3">Presión en la esquina (−B/2, −L/2), en kN/m².</param>
/// <param name="Q4">Presión en la esquina (+B/2, −L/2), en kN/m².</param>
/// <param name="Maxima">Máxima de las cuatro presiones, en kN/m².</param>
/// <param name="Minima">Mínima de las cuatro presiones, en kN/m².</param>
/// <param name="ExcedePresionAdmisible">
/// <c>true</c> si <see cref="Maxima"/> supera la
/// <c>PropiedadesTerreno.PresionAdmisible</c>.
/// </param>
/// <param name="HayDespegue">
/// <c>true</c> si <see cref="Minima"/> es negativa — el suelo no puede
/// resistir tracción y la zapata pierde contacto en parte de la base.
/// </param>
/// <param name="PorcentajeAreaEnTraccion">
/// Fracción del área de la base que pierde contacto con el suelo (0..1).
/// Es 0 cuando no hay despegue; en caso contrario se calcula recortando el
/// rectángulo por la línea <c>q = 0</c>.
/// </param>
public sealed record PresionEsquinaPorCombinacion(
    string NombreCombinacion,
    double Q1,
    double Q2,
    double Q3,
    double Q4,
    double Maxima,
    double Minima,
    bool ExcedePresionAdmisible,
    bool HayDespegue,
    double PorcentajeAreaEnTraccion);

/// <summary>
/// Resultado completo del análisis de presiones de contacto de una
/// <see cref="ZapataAislada"/> sobre todas las combinaciones de servicio
/// del proyecto (Fase 6, Iteración 1).
/// </summary>
/// <param name="PorCombinacion">
/// Una entrada por cada combinación de servicio del proyecto, en el orden
/// en que aparecen en <c>CombinacionesProyecto.Combinaciones</c>.
/// </param>
/// <param name="PresionMaximaGlobal">
/// Mayor presión de contacto sobre todas las combinaciones, en kN/m².
/// </param>
/// <param name="PresionMinimaGlobal">
/// Menor presión de contacto sobre todas las combinaciones, en kN/m²
/// (puede ser negativa si alguna combinación produce despegue).
/// </param>
/// <param name="EsEstable">
/// <c>true</c> si ninguna combinación de servicio excede la
/// <c>PresionAdmisible</c>. El despegue (q_min &lt; 0) se reporta por
/// combinación pero no participa en esta bandera global.
/// </param>
public sealed record ResultadoPresionesZapata(
    IReadOnlyList<PresionEsquinaPorCombinacion> PorCombinacion,
    double PresionMaximaGlobal,
    double PresionMinimaGlobal,
    bool EsEstable)
{
    /// <summary>
    /// Resultado vacío — para una zapata con geometría/suelo inválidos o un
    /// proyecto sin combinaciones de servicio.
    /// </summary>
    public static ResultadoPresionesZapata Vacio { get; } = new(
        Array.Empty<PresionEsquinaPorCombinacion>(), 0.0, 0.0, true);
}
