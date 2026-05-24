using LosasPlus.Models;
using LosasPlus.Topologia;

namespace LosasPlus.ViewModels.Mensajeria;

/// <summary>
/// Implementación default de <see cref="IRatioCalculatorService"/> —
/// delega al método público
/// <see cref="LosasPlus.Interop.SAF.RatioCalculator.CalcularRatioPorElemento"/>
/// del proyecto SAF interop. Sin estado, segura para uso concurrente.
/// </summary>
public sealed class DefaultRatioCalculator : IRatioCalculatorService
{
    public double CalcularRatio(
        Proyecto proyecto, DomainKey key,
        double fc = 28.0, double fy = 420.0)
        => LosasPlus.Interop.SAF.RatioCalculator
                   .CalcularRatioPorElemento(proyecto, key, fc, fy);
}
