using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Ejecuta un proceso externo enviando <paramref name="stdin"/> y capturando stdout/stderr/exit.</summary>
public interface IProcesoRunner
{
    Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct);
}
