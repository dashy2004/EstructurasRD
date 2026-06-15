using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaClientTests
{
    private sealed class FakeRunner : IProcesoRunner
    {
        public ResultadoProceso Resultado = new(0, "{}", "");
        public string? StdinRecibido;
        public Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
        {
            StdinRecibido = stdin;
            return Task.FromResult(Resultado);
        }
    }

    [Fact]
    public async Task Exito_devuelve_stdout_y_envia_params_por_stdin()
    {
        var fake = new FakeRunner { Resultado = new(0, "{\"mx_max\":1.0}", "") };
        var client = new MotorFeaClient(fake);
        var salida = await client.DisenarLosaAsync("{\"a\":5}", CancellationToken.None);
        Assert.Equal("{\"mx_max\":1.0}", salida);
        Assert.Equal("{\"a\":5}", fake.StdinRecibido);
    }

    [Fact]
    public async Task ExitCode_distinto_de_cero_lanza_MotorFeaException_con_stderr()
    {
        var fake = new FakeRunner { Resultado = new(1, "", "boom: parametros invalidos") };
        var client = new MotorFeaClient(fake);
        var ex = await Assert.ThrowsAsync<MotorFeaException>(
            () => client.DisenarLosaAsync("{}", CancellationToken.None));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task Stdout_vacio_con_exit_cero_lanza_MotorFeaException()
    {
        var fake = new FakeRunner { Resultado = new(0, "   ", "") };
        var client = new MotorFeaClient(fake);
        await Assert.ThrowsAsync<MotorFeaException>(
            () => client.DisenarLosaAsync("{}", CancellationToken.None));
    }
}
