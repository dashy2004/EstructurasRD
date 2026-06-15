using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

public class MotorFeaAdapterTests
{
    // JSON sintético para verificar las conversiones de unidades (valores controlados).
    private const string JsonMotor = """
    {
      "w_central": 0.00123,
      "mx_max": 5234.5, "my_max": 5210.3, "m_apoyo_max": 0.0,
      "mu_x": 5234500.0, "mu_y": 5210300.0, "mu_apoyo": 0.0,
      "franja_x": { "as_requerido": 485.5, "as_minimo": 400.0, "as_diseno": 485.5,
                    "numero_barra": 5, "espaciamiento": 150, "as_provista": 500.0,
                    "cumple": true, "disponer": "#5 @ 150" },
      "franja_y": { "as_requerido": 480.0, "as_minimo": 400.0, "as_diseno": 480.0,
                    "numero_barra": 5, "espaciamiento": 150, "as_provista": 500.0,
                    "cumple": true, "disponer": "#5 @ 150" },
      "franja_apoyo": { "as_requerido": 0.0, "as_minimo": 400.0, "as_diseno": 400.0,
                    "numero_barra": 4, "espaciamiento": 200, "as_provista": 400.0,
                    "cumple": true, "disponer": "#4 @ 200" }
    }
    """;

    // Salida REAL capturada del motor (losa 5x5, borde simple): numero_barra entero,
    // disponer con " mm", y campos extra (seccion_insuficiente, gobierna_minimo).
    private const string JsonReal = """
    {
      "w_central": 0.0016667799361366439,
      "mx_max": 10290.628877539646, "my_max": 10290.628877539733, "m_apoyo_max": 117.81644705404099,
      "mu_x": 10290628.877539646, "mu_y": 10290628.877539733, "mu_apoyo": 117816.447054041,
      "franja_x": { "as_requerido": 160.36, "as_minimo": 360.0, "as_diseno": 360.0,
                    "seccion_insuficiente": false, "gobierna_minimo": true, "numero_barra": 3,
                    "espaciamiento": 190.0, "as_provista": 373.68, "cumple": true, "disponer": "#3 @ 190 mm" },
      "franja_y": { "as_requerido": 160.36, "as_minimo": 360.0, "as_diseno": 360.0,
                    "seccion_insuficiente": false, "gobierna_minimo": true, "numero_barra": 3,
                    "espaciamiento": 190.0, "as_provista": 373.68, "cumple": true, "disponer": "#3 @ 190 mm" },
      "franja_apoyo": { "as_requerido": 1.82, "as_minimo": 360.0, "as_diseno": 360.0,
                    "seccion_insuficiente": false, "gobierna_minimo": true, "numero_barra": 3,
                    "espaciamiento": 190.0, "as_provista": 373.68, "cumple": true, "disponer": "#3 @ 190 mm" }
    }
    """;

    private static Losa DemoLosa() =>
        new Losa { Id = 7, Tipo = 1, Carga = 1.0, Espesor = 0.20, Lx = 5.0, Ly = 5.0, Rec = 0.025 };

    [Fact]
    public void Convierte_momentos_y_geometria()
    {
        var r = MotorFeaAdapter.Map(JsonMotor, DemoLosa());
        Assert.Equal(7, r.Id);
        Assert.Equal(1, r.Tipo);
        Assert.Equal(1.0, r.Carga!.Value, 6);
        Assert.Equal(0.20, r.H!.Value, 6);
        Assert.Equal(5.0, r.Lx!.Value, 6);
        Assert.Equal(0.534, r.Mfx!.Value, 3);
        Assert.Equal(0.531, r.Mfy!.Value, 3);
        Assert.Equal(0.0,   r.MSx!.Value, 3);
    }

    [Fact]
    public void Convierte_armado_X_y_Y()
    {
        var r = MotorFeaAdapter.Map(JsonMotor, DemoLosa());
        Assert.Equal(0.175, r.Dx!.Value, 3);
        Assert.Equal(0.534, r.Mux!.Value, 3);
        Assert.Equal(4.855, r.AsxReq!.Value, 3);
        Assert.Equal(5.0,   r.AsxProv!.Value, 3);
        Assert.Equal("#5 @ 150", r.DisponerX);
        Assert.Equal(4.80,  r.AsyReq!.Value, 3);
        Assert.Equal("#5 @ 150", r.DisponerY);
    }

    [Fact]
    public void Deserializa_la_salida_real_del_motor_sin_excepcion()
    {
        // Guarda contra desajustes de contrato (p.ej. numero_barra entero, no string).
        var r = MotorFeaAdapter.Map(JsonReal, DemoLosa());
        Assert.Equal("#3 @ 190 mm", r.DisponerX);
        Assert.True(r.Mfx!.Value > 0);              // 10290.6 N·m/m → ~1.049 ton·m/m
        Assert.Equal(0.012, r.MSx!.Value, 3);       // 117.8 / 9806.65
    }
}
