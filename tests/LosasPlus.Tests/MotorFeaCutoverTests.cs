using System;
using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de la ruta aditiva que reemplaza a Losas.exe: el motor calcula los
/// momentos y <see cref="MotorFeaService.AplicarMomentos"/> los puebla en la
/// <see cref="Losa"/> (convirtiendo N·m/m → ton·m/m) para que el pipeline de
/// Aceros los consuma igual que los del .TXT.
/// </summary>
public class MotorFeaCutoverTests
{
    [Fact]
    public void AplicarMomentos_puebla_la_losa_convirtiendo_a_ton_m()
    {
        // Tipo 60 = 4 bordes empotrados ⇒ continuidad en ambas direcciones,
        // así MSx y MSy reciben el momento de apoyo (validamos la conversión a ton·m).
        var losa = new Losa { Id = 1, Tipo = 60 };
        var r = new ResultadoMotorLosa
        {
            MxMax = 9806.65,      // N·m/m → 1.0 ton·m/m
            MyMax = 19613.30,     // → 2.0
            MApoyoMax = 4903.325, // → 0.5
        };

        MotorFeaService.AplicarMomentos(losa, r);

        Assert.Equal(1.0, losa.Mfx!.Value, 4);
        Assert.Equal(2.0, losa.Mfy!.Value, 4);
        Assert.Equal(0.5, losa.MSx!.Value, 4);
        Assert.Equal(0.5, losa.MSy!.Value, 4);
    }

    [Fact]
    public void AplicarMomentos_alimenta_el_pipeline_de_Aceros()
    {
        // Tras aplicar los momentos del motor, la losa "tiene momentos" y el
        // diseñador de aceros produce filas — igual que si vinieran del .TXT.
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        var losa = new Losa { Id = 1, Tipo = 33, Lx = 4.0, Ly = 5.0, Espesor = 0.20, Carga = 0.8 };
        s.Losas.Add(losa);
        MotorFeaService.AplicarMomentos(losa, new ResultadoMotorLosa { MxMax = 9806.65, MyMax = 7845.32, MApoyoMax = 14709.975 });

        var filas = AcerosLosaExporter.Filas(s);
        Assert.NotEmpty(filas);   // la losa entró al pipeline de Aceros con momentos del motor
    }

    [Fact]
    public void AplicarMomentoBorde_setea_MuIJ_y_disena_el_acero_de_apoyo()
    {
        var s = new Sistema { Fc = 0.210, Fy = 4.200 };
        var losa = new Losa { Id = 1, Espesor = 0.20, Rec = 0.025, Lx = 4, Ly = 5 };
        var borde = new BordeAdic { BI = 1, BJ = 2 };

        MotorFeaService.AplicarMomentoBorde(borde, 1.5, losa, s);

        Assert.Equal(1.5, borde.MuIJ!.Value, 4);   // momento de apoyo aplicado (ton·m/m)
        Assert.True(borde.D!.Value > 0);           // canto útil calculado
        Assert.True(borde.AsReq!.Value > 0);       // acero de apoyo diseñado
        Assert.Contains("@", borde.Disponer!);     // disposición "#n @ s cm"
    }

    // ── Tests de degradación NaN (fix crítico: seccion_insuficiente → no crash) ──

    /// <summary>
    /// Cuando el motor Python emite <c>NaN</c> literal en campos numéricos
    /// (losa con sección insuficiente), <see cref="MotorFeaService.ParsearResultado"/>
    /// NO debe lanzar excepción y la franja debe quedar marcada como insuficiente.
    /// </summary>
    [Fact]
    public void ParsearResultado_con_NaN_no_lanza_y_marca_franja_insuficiente()
    {
        // JSON real que emite el motor Python cuando la sección es insuficiente:
        // los campos numéricos de diseño llegan como bare NaN y seccion_insuficiente=true.
        const string jsonConNaN = """
        {
          "w_central": NaN,
          "mx_max": NaN,
          "my_max": NaN,
          "m_apoyo_max": 0.0,
          "franja_x": {
            "as_requerido": NaN,
            "as_minimo": NaN,
            "as_diseno": NaN,
            "seccion_insuficiente": true,
            "cumple": false,
            "disponer": "",
            "numero_barra": 0,
            "espaciamiento": NaN,
            "as_provista": NaN,
            "gobierna_minimo": false
          },
          "franja_y": {
            "as_requerido": NaN,
            "as_minimo": NaN,
            "as_diseno": NaN,
            "seccion_insuficiente": true,
            "cumple": false,
            "disponer": "",
            "numero_barra": 0,
            "espaciamiento": NaN,
            "as_provista": NaN,
            "gobierna_minimo": false
          }
        }
        """;

        // No debe lanzar JsonReaderException ni ninguna otra excepción.
        var resultado = MotorFeaService.ParsearResultado(jsonConNaN);

        // La franja X debe estar marcada como insuficiente.
        Assert.True(resultado.FranjaX.SeccionInsuficiente,
            "FranjaX.SeccionInsuficiente debe ser true cuando el motor emite NaN");

        // La franja Y también.
        Assert.True(resultado.FranjaY.SeccionInsuficiente,
            "FranjaY.SeccionInsuficiente debe ser true cuando el motor emite NaN");

        // No debe cumplir (la sección no sirve).
        Assert.False(resultado.FranjaX.Cumple);
        Assert.False(resultado.FranjaY.Cumple);

        // Los campos numéricos deben ser NaN (no un valor basura ni 0).
        Assert.True(double.IsNaN(resultado.WCentral),  "WCentral debe ser NaN");
        Assert.True(double.IsNaN(resultado.MxMax),     "MxMax debe ser NaN");
        Assert.True(double.IsNaN(resultado.FranjaX.AsRequerido), "FranjaX.AsRequerido debe ser NaN");

        // El mensaje de Disponer debe indicar sección insuficiente (no cadena vacía).
        Assert.Contains("INSUFICIENTE", resultado.FranjaX.Disponer,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica que un JSON completamente válido (sin NaN) sigue parseándose
    /// correctamente tras el fix — no hay regresión en el camino feliz.
    /// </summary>
    [Fact]
    public void ParsearResultado_json_valido_sin_NaN_sigue_funcionando()
    {
        const string jsonValido = """
        {
          "w_central": 0.00181, "mx_max": 10494.0, "my_max": 10494.0, "m_apoyo_max": 120.0,
          "franja_x": {"as_requerido": 300.0, "as_minimo": 360.0, "as_diseno": 360.0,
                       "seccion_insuficiente": false, "gobierna_minimo": true,
                       "numero_barra": 3, "espaciamiento": 190.0, "as_provista": 373.0,
                       "cumple": true, "disponer": "#3 @ 190 mm"},
          "franja_y": {"as_requerido": 300.0, "as_minimo": 360.0, "as_diseno": 360.0,
                       "seccion_insuficiente": false, "gobierna_minimo": true,
                       "numero_barra": 3, "espaciamiento": 190.0, "as_provista": 373.0,
                       "cumple": true, "disponer": "#3 @ 190 mm"}
        }
        """;

        var r = MotorFeaService.ParsearResultado(jsonValido);

        Assert.Equal(10494.0, r.MxMax, 3);
        Assert.Equal(360.0, r.FranjaX.AsDiseno, 3);
        Assert.Equal("#3 @ 190 mm", r.FranjaX.Disponer);
        Assert.True(r.FranjaX.Cumple);
        Assert.False(r.FranjaX.SeccionInsuficiente);
    }
}
