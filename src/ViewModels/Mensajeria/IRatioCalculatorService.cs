using LosasPlus.Models;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels.Mensajeria;

/// <summary>
/// Servicio abstracto de cómputo de ratio Demanda/Capacidad por
/// elemento (Liga B paso B4). Existe para desacoplar al
/// <c>RatioComputacionCoordinator</c> del calculator concreto:
/// los tests inyectan mocks con timing controlado para validar el
/// comportamiento de cancelación, ráfagas y aislamiento de
/// excepciones sin depender del SAF interop assembly.
///
/// <para>
/// <b>Contrato</b>: el método NUNCA debe lanzar excepción. Ante
/// cualquier error (entidad inexistente, singularidad numérica,
/// geometría degenerada, tipo no soportado) debe retornar
/// <see cref="double.NaN"/>. El coordinator confía en esta semántica
/// para no envolver el cálculo en un segundo try/catch redundante.
/// </para>
/// </summary>
public interface IRatioCalculatorService
{
    /// <summary>
    /// Calcula sincrónicamente el ratio D/C de la entidad identificada
    /// por <paramref name="key"/>. Pure function (no muta el proyecto).
    /// Retorna <see cref="double.NaN"/> ante cualquier error.
    /// </summary>
    double CalcularRatio(
        Proyecto proyecto, DomainKey key,
        double fc = 28.0, double fy = 420.0);
}
