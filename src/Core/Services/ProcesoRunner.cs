using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Impl real sobre <see cref="Process"/> con stdin/stdout/stderr redirigidos.</summary>
public sealed class ProcesoRunner : IProcesoRunner
{
    public async Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ejecutable,
            Arguments = argumentos,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = new Process { StartInfo = psi };
        p.Start();

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        await p.WaitForExitAsync(ct);

        return new ResultadoProceso(p.ExitCode, await stdoutTask, await stderrTask);
    }
}
