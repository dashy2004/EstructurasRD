using System.Threading;
using System.Threading.Tasks;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaLosasServiceTests
{
    private const string JsonOk = """
    { "w_central":0.001, "mx_max":5234.5, "my_max":5210.3, "m_apoyo_max":0.0,
      "mu_x":5234500.0, "mu_y":5210300.0, "mu_apoyo":0.0,
      "franja_x":{"as_requerido":485.5,"as_minimo":400,"as_diseno":485.5,"numero_barra":"#5","espaciamiento":150,"as_provista":500,"cumple":true,"disponer":"#5 @ 150"},
      "franja_y":{"as_requerido":480.0,"as_minimo":400,"as_diseno":480.0,"numero_barra":"#5","espaciamiento":150,"as_provista":500,"cumple":true,"disponer":"#5 @ 150"},
      "franja_apoyo":{"as_requerido":0,"as_minimo":400,"as_diseno":400,"numero_barra":"#4","espaciamiento":200,"as_provista":400,"cumple":true,"disponer":"#4 @ 200"} }
    """;

    private sealed class Runner : IProcesoRunner
    {
        public int Llamadas;
        public Task<ResultadoProceso> EjecutarAsync(string ejecutable, string argumentos, string stdin, CancellationToken ct)
        {
            Llamadas++;
            bool falla = stdin.Contains("\"a\":99") || stdin.Contains("\"a\": 99");
            return Task.FromResult(falla ? new ResultadoProceso(1, "", "borde invalido") : new ResultadoProceso(0, JsonOk, ""));
        }
    }

    private static Sistema SistemaCon(params Losa[] losas)
    {
        var s = new Sistema { Nombre = "Nivel 1", Fc = 0.210, Fy = 4.200 };
        foreach (var l in losas) s.Losas.Add(l);
        return s;
    }

    [Fact]
    public async Task Calcula_todas_las_losas_y_puebla_SalidaPerdomo()
    {
        var sis = SistemaCon(
            new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 },
            new Losa { Id = 2, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 4.0, Ly = 6.0, Rec = 0.025 });
        var svc = new MotorFeaLosasService(new MotorFeaClient(new Runner()));

        var (salida, fallidas) = await svc.CalcularAsync(sis, "simple", CancellationToken.None);

        Assert.Empty(fallidas);
        Assert.Equal(2, salida.Momentos.Count);
        Assert.Equal(2, salida.ArmadurasXCentro.Count);
        Assert.Equal(2, salida.ArmadurasYCentro.Count);
        Assert.Contains("motor", salida.ArchivoTxt);
    }

    [Fact]
    public async Task Una_losa_que_falla_se_reporta_y_no_corta_la_corrida()
    {
        var sis = SistemaCon(
            new Losa { Id = 1, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 },
            new Losa { Id = 2, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 99.0, Ly = 6.0, Rec = 0.025 });
        var svc = new MotorFeaLosasService(new MotorFeaClient(new Runner()));

        var (salida, fallidas) = await svc.CalcularAsync(sis, "simple", CancellationToken.None);

        Assert.Single(fallidas);
        Assert.Equal(2, fallidas[0]);
        Assert.Single(salida.Momentos);
    }
}
