using System.Threading;
using System.Threading.Tasks;

namespace LosasPlus.Services;

/// <summary>Cliente del motor Python: envia los params de una losa por stdin y devuelve el JSON de salida.</summary>
public sealed class MotorFeaClient
{
    /// <summary>Comando por defecto (dev venv del motor, decision D2). Unico knob configurable de 5a.</summary>
    public const string EjecutablePorDefecto =
        "/home/gdc/Downloads/EstructurasRD-engine/motor-fea/.venv/bin/python";
    public const string ArgumentosPorDefecto = "-m motor_fea.api.cli --disenar-losa -";

    private readonly IProcesoRunner _runner;
    private readonly string _ejecutable;
    private readonly string _argumentos;

    public MotorFeaClient(IProcesoRunner runner, string? ejecutable = null, string? argumentos = null)
    {
        _runner = runner;
        _ejecutable = ejecutable ?? EjecutablePorDefecto;
        _argumentos = argumentos ?? ArgumentosPorDefecto;
    }

    public async Task<string> DisenarLosaAsync(string paramsJson, CancellationToken ct)
    {
        ResultadoProceso r;
        try
        {
            r = await _runner.EjecutarAsync(_ejecutable, _argumentos, paramsJson, ct);
        }
        catch (System.Exception ex)
        {
            throw new MotorFeaException($"No se pudo ejecutar el motor ('{_ejecutable}'): {ex.Message}");
        }
        if (r.ExitCode != 0)
            throw new MotorFeaException($"El motor termino con codigo {r.ExitCode}: {r.Stderr.Trim()}");
        if (string.IsNullOrWhiteSpace(r.Stdout))
            throw new MotorFeaException("El motor no produjo salida.");
        return r.Stdout;
    }
}
