namespace LosasPlus.Zapatas;

/// <summary>
/// Resultado de la verificación estructural por Estado Límite Último (ELU)
/// de una <see cref="ZapataAislada"/> según ACI 318-19 (Fase 6, Iteración 3).
/// Consolida los ratios demanda/capacidad de las tres fallas críticas
/// (flexión en la cara del pedestal, cortante unidimensional a <c>d</c> y
/// punzonamiento biaxial a <c>d/2</c>) sobre todas las combinaciones de
/// tipo <c>Ultima</c> del proyecto.
/// </summary>
/// <param name="RatioFlexion">
/// D/C de flexión: máximo sobre las dos direcciones X/Y y todas las combos
/// Última, donde la capacidad es <c>φMn</c> con <c>A_s,min = 0.0018 · b · h</c>
/// (ACI 318-19 §13.3.3.1).
/// </param>
/// <param name="RatioCortante">
/// D/C de cortante unidimensional a una distancia <c>d</c> del pedestal,
/// con <c>φVc = 0.75 · 0.17 · √fc · b · d</c> (ACI 318-19 §22.5.5.1).
/// </param>
/// <param name="RatioPunzonamiento">
/// D/C de punzonamiento biaxial a <c>d/2</c> del pedestal, con
/// <c>φVc</c> mínimo de las tres ecuaciones de ACI 318-19 §22.6.5.2.
/// </param>
/// <param name="RatioEstructuralMaximo">Máximo de los tres ratios anteriores.</param>
/// <param name="EstructuraConforme">
/// <c>true</c> si <see cref="RatioEstructuralMaximo"/> ≤ 1.0.
/// </param>
/// <param name="NombreCombinacionCritica">
/// Nombre de la combinación Última que produjo el peor ratio.
/// </param>
/// <param name="MomentoUltimo">
/// Momento último por metro de ancho del peor caso, en kN·m/m.
/// </param>
/// <param name="CortanteUltimo">
/// Cortante último total del peor caso, en kN.
/// </param>
/// <param name="PunzonamientoUltimo">
/// Fuerza punzonante última del peor caso, en kN.
/// </param>
/// <param name="AceroMinimoRequerido">
/// Acero mínimo reglamentario por metro de ancho (<c>0.0018 · b · H</c>) en m²/m.
/// </param>
/// <param name="LadoPerimetroX">
/// Largo del perímetro crítico de punzonamiento en X (<c>b_col + d</c>), en m.
/// Permite a la vista dibujar el rectángulo concéntrico al pedestal.
/// </param>
/// <param name="LadoPerimetroY">
/// Largo del perímetro crítico de punzonamiento en Y (<c>l_col + d</c>), en m.
/// </param>
public sealed record ResultadoEstructuralZapata(
    double RatioFlexion,
    double RatioCortante,
    double RatioPunzonamiento,
    double RatioEstructuralMaximo,
    bool EstructuraConforme,
    string NombreCombinacionCritica,
    double MomentoUltimo,
    double CortanteUltimo,
    double PunzonamientoUltimo,
    double AceroMinimoRequerido,
    double LadoPerimetroX,
    double LadoPerimetroY)
{
    /// <summary>
    /// Resultado vacío — para una zapata con datos inválidos o un proyecto
    /// sin combinaciones <c>Ultima</c>.
    /// </summary>
    public static ResultadoEstructuralZapata Vacio { get; } = new(
        RatioFlexion: 0.0,
        RatioCortante: 0.0,
        RatioPunzonamiento: 0.0,
        RatioEstructuralMaximo: 0.0,
        EstructuraConforme: true,
        NombreCombinacionCritica: "",
        MomentoUltimo: 0.0,
        CortanteUltimo: 0.0,
        PunzonamientoUltimo: 0.0,
        AceroMinimoRequerido: 0.0,
        LadoPerimetroX: 0.0,
        LadoPerimetroY: 0.0);
}
