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
        var losa = new Losa { Id = 1 };
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
}
