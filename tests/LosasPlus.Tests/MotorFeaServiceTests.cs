using System;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de la parte pura del puente B6 (<see cref="MotorFeaService"/>):
/// construcción de parámetros (ton·cm → SI) y parseo del JSON de resultados.
/// La invocación del proceso (Python) no se cubre en unit tests.
/// </summary>
public class MotorFeaServiceTests
{
    private static (Losa, Sistema) Caso()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };       // ton/cm²
        var l = new Losa { Id = 1, Lx = 5.0, Ly = 5.0, Espesor = 0.20, Carga = 2.0, Rec = 0.025 };
        return (l, s);
    }

    [Fact]
    public void ConstruirParametros_convierte_unidades_a_SI()
    {
        var (l, s) = Caso();
        var p = MotorFeaService.ConstruirParametros(l, s);

        Assert.Equal(5.0, (double)p["a"]);
        Assert.Equal(0.20, (double)p["t"]);
        // f'c: 0.21 ton/cm² · 98.0665 ≈ 20.59 MPa
        Assert.Equal(0.210 * 98.0665, (double)p["fc"], 3);
        Assert.Equal(4.200 * 98.0665, (double)p["fy"], 3);
        // q: 2 ton/m² · 9806.65 = 19613.3 N/m²
        Assert.Equal(2.0 * 9806.65, (double)p["q"], 3);
        // E = 4700·√f'c(MPa)·1e6 Pa
        double eEsperado = 4700.0 * Math.Sqrt(0.210 * 98.0665) * 1.0e6;
        Assert.Equal(eEsperado, (double)p["E"], 0);
        Assert.Equal(25.0, (double)p["recubrimiento"], 6);    // 0.025 m → 25 mm
    }

    [Fact]
    public void ParametrosAJson_es_json_valido_con_los_campos()
    {
        var (l, s) = Caso();
        string json = MotorFeaService.ParametrosAJson(MotorFeaService.ConstruirParametros(l, s));
        Assert.Contains("\"a\"", json);
        Assert.Contains("\"fc\"", json);
        Assert.Contains("\"borde\"", json);
    }

    [Fact]
    public void ParsearResultado_lee_momentos_y_franjas()
    {
        const string json = """
        {
          "w_central": 0.00181, "mx_max": 10494.0, "my_max": 10494.0, "m_apoyo_max": 120.0,
          "mu_x": 1.05e7, "mu_y": 1.05e7, "mu_apoyo": 1.2e5,
          "franja_x": {"as_requerido": 300.0, "as_minimo": 360.0, "as_diseno": 360.0,
                       "seccion_insuficiente": false, "gobierna_minimo": true,
                       "numero_barra": 3, "espaciamiento": 190.0, "as_provista": 373.0,
                       "cumple": true, "disponer": "#3 @ 190 mm"},
          "franja_y": {"as_requerido": 300.0, "as_minimo": 360.0, "as_diseno": 360.0,
                       "seccion_insuficiente": false, "gobierna_minimo": true,
                       "numero_barra": 3, "espaciamiento": 190.0, "as_provista": 373.0,
                       "cumple": true, "disponer": "#3 @ 190 mm"},
          "franja_apoyo": {"as_requerido": 10.0, "as_minimo": 360.0, "as_diseno": 360.0,
                       "seccion_insuficiente": false, "gobierna_minimo": true,
                       "numero_barra": 3, "espaciamiento": 190.0, "as_provista": 373.0,
                       "cumple": true, "disponer": "#3 @ 190 mm"}
        }
        """;
        var r = MotorFeaService.ParsearResultado(json);

        Assert.Equal(10494.0, r.MxMax, 3);
        Assert.Equal(120.0, r.MApoyoMax, 3);
        Assert.Equal(360.0, r.FranjaX.AsDiseno, 3);
        Assert.Equal("#3 @ 190 mm", r.FranjaX.Disponer);
        Assert.True(r.FranjaX.Cumple);
        Assert.NotNull(r.FranjaApoyo);
        Assert.Equal(360.0, r.FranjaApoyo!.AsDiseno, 3);
    }

    [Fact]
    public void ConstruirParametros_nulo_lanza()
    {
        var (_, s) = Caso();
        Assert.Throws<ArgumentNullException>(() => MotorFeaService.ConstruirParametros(null!, s));
    }
}
