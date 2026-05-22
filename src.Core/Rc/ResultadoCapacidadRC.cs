using System.Collections.Generic;

namespace LosasPlus.Rc;

/// <summary>
/// Clasificación de una sección de concreto reforzado a flexión según la
/// deformación neta del acero de tracción εt (ACI 318-19 §21.2).
/// </summary>
public enum ClasificacionSeccion
{
    /// <summary>Controlada por compresión: εt ≤ εty (φ = 0.65).</summary>
    CompresionControlada,

    /// <summary>Zona de transición: εty &lt; εt &lt; εty + 0.003.</summary>
    Transicion,

    /// <summary>Controlada por tracción: εt ≥ εty + 0.003 (φ = 0.90).</summary>
    TensionControlada,
}

/// <summary>
/// Resultado del cálculo de capacidad nominal a flexión de una
/// <see cref="SeccionRC"/> por el bloque equivalente de Whitney (ACI 318-19).
///
/// <para>
/// Unidades: <see cref="EjeNeutro"/>, <see cref="BloqueCompresion"/> y
/// <see cref="BrazoPalanca"/> en metros; <see cref="MomentoNominal"/> y
/// <see cref="MomentoDiseno"/> en kN·m; cuantías y deformaciones son
/// adimensionales.
/// </para>
/// </summary>
/// <param name="EjeNeutro">Profundidad del eje neutro c, en m.</param>
/// <param name="BloqueCompresion">Profundidad del bloque de Whitney a = β1·c, en m.</param>
/// <param name="BrazoPalanca">Brazo de palanca interno jd = d − a/2, en m.</param>
/// <param name="Beta1">Factor β1 del bloque equivalente (ACI 318-19 §22.2.2.4.3).</param>
/// <param name="Cuantia">Cuantía del acero de tracción ρ = As/(b·d).</param>
/// <param name="CuantiaMinima">Cuantía mínima ρ_min (ACI 318-19 §9.6.1.2).</param>
/// <param name="CuantiaMaxima">Cuantía máxima de referencia (informativa; el límite real es <see cref="CumpleLimiteDeformacion"/>).</param>
/// <param name="MomentoNominal">Momento nominal Mn, en kN·m.</param>
/// <param name="MomentoDiseno">Momento de diseño φMn, en kN·m.</param>
/// <param name="FactorPhi">Factor de reducción de resistencia φ.</param>
/// <param name="DeformacionNetaTraccion">Deformación neta del acero de tracción εt.</param>
/// <param name="AceroFluye">Indica si el acero de tracción alcanza la fluencia.</param>
/// <param name="Clasificacion">Clasificación de la sección según εt.</param>
/// <param name="CumpleCuantiaMinima">ρ ≥ ρ_min.</param>
/// <param name="CumpleLimiteDeformacion">εt ≥ 0.004 (límite de ductilidad, ACI 318-19 §9.3.3.1).</param>
/// <param name="Advertencias">Mensajes de diagnóstico (cuantía, ductilidad o entrada inválida).</param>
public sealed record ResultadoCapacidadRC(
    double EjeNeutro,
    double BloqueCompresion,
    double BrazoPalanca,
    double Beta1,
    double Cuantia,
    double CuantiaMinima,
    double CuantiaMaxima,
    double MomentoNominal,
    double MomentoDiseno,
    double FactorPhi,
    double DeformacionNetaTraccion,
    bool AceroFluye,
    ClasificacionSeccion Clasificacion,
    bool CumpleCuantiaMinima,
    bool CumpleLimiteDeformacion,
    IReadOnlyList<string> Advertencias)
{
    /// <summary>
    /// Construye un resultado de capacidad nula para una sección con datos de
    /// entrada inválidos (geometría, materiales o acero no positivos).
    /// </summary>
    public static ResultadoCapacidadRC Invalida(string motivo) => new(
        EjeNeutro: 0.0, BloqueCompresion: 0.0, BrazoPalanca: 0.0, Beta1: 0.0,
        Cuantia: 0.0, CuantiaMinima: 0.0, CuantiaMaxima: 0.0,
        MomentoNominal: 0.0, MomentoDiseno: 0.0, FactorPhi: 0.0,
        DeformacionNetaTraccion: 0.0, AceroFluye: false,
        Clasificacion: ClasificacionSeccion.CompresionControlada,
        CumpleCuantiaMinima: false, CumpleLimiteDeformacion: false,
        Advertencias: new List<string> { motivo });
}
