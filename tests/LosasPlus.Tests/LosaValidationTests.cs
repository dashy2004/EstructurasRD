using System.ComponentModel;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de validación de <see cref="Losa"/> via IDataErrorInfo + warning de aspecto.
/// </summary>
public class LosaValidationTests
{
    private static IDataErrorInfo Dei(Losa l) => l;

    private static Losa Valida() => new Losa
    {
        Id = 1, Tipo = 10,
        Carga = 2.0, Espesor = 0.12, Lx = 4.0, Ly = 4.0, Rec = 0.02
    };

    // ---------- Casos válidos ----------

    [Fact]
    public void Losa_default_es_valida()
    {
        var l = Valida();
        Assert.True(l.EsValida);
        Assert.Empty(Dei(l).Error);
        foreach (var col in new[] { nameof(Losa.Lx), nameof(Losa.Ly), nameof(Losa.Espesor), nameof(Losa.Carga), nameof(Losa.Rec) })
            Assert.Empty(Dei(l)[col]);
    }

    // ---------- Errores duros ----------

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void Lx_no_positivo_dispara_error(double lx)
    {
        var l = Valida(); l.Lx = lx;
        Assert.False(l.EsValida);
        Assert.Contains("Lx", Dei(l)[nameof(Losa.Lx)]);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.5)]
    public void Ly_no_positivo_dispara_error(double ly)
    {
        var l = Valida(); l.Ly = ly;
        Assert.False(l.EsValida);
        Assert.Contains("Ly", Dei(l)[nameof(Losa.Ly)]);
    }

    [Fact]
    public void Espesor_no_positivo_dispara_error()
    {
        var l = Valida(); l.Espesor = 0;
        Assert.False(l.EsValida);
        Assert.Contains("Espesor", Dei(l)[nameof(Losa.Espesor)]);
    }

    [Fact]
    public void Carga_no_positiva_dispara_error()
    {
        var l = Valida(); l.Carga = -0.1;
        Assert.False(l.EsValida);
        Assert.Contains("Carga", Dei(l)[nameof(Losa.Carga)]);
    }

    [Fact]
    public void Rec_negativo_dispara_error()
    {
        var l = Valida(); l.Rec = -0.01;
        Assert.False(l.EsValida);
        Assert.Contains("negativo", Dei(l)[nameof(Losa.Rec)]);
    }

    [Fact]
    public void Rec_mayor_o_igual_a_Espesor_dispara_error()
    {
        var l = Valida(); l.Espesor = 0.10; l.Rec = 0.10;
        Assert.False(l.EsValida);
        Assert.Contains("menor que el espesor", Dei(l)[nameof(Losa.Rec)]);
    }

    [Fact]
    public void Multiples_errores_se_concatenan_en_Error()
    {
        var l = Valida(); l.Lx = -1; l.Ly = -2;
        var msg = Dei(l).Error;
        Assert.Contains("Lx", msg);
        Assert.Contains("Ly", msg);
    }

    // ---------- Warning de aspecto ----------

    [Theory]
    [InlineData(4.0, 4.0, false)]   // 1.00 → ok
    [InlineData(4.0, 2.0, false)]   // 0.50 → borde inferior, ok
    [InlineData(4.0, 8.0, false)]   // 2.00 → borde superior, ok
    [InlineData(4.0, 1.9, true)]    // 0.475 → fuera
    [InlineData(2.0, 5.0, true)]    // 2.50 → fuera
    [InlineData(1.0, 10.0, true)]   // 10.00 → muy fuera
    public void AspectoFueraDeRango_se_calcula_correctamente(double lx, double ly, bool fueraEsperado)
    {
        var l = Valida(); l.Lx = lx; l.Ly = ly;
        Assert.Equal(fueraEsperado, l.AspectoFueraDeRango);
    }

    [Fact]
    public void AspectoMensaje_es_vacio_cuando_aspecto_esta_en_rango()
    {
        var l = Valida();
        Assert.False(l.AspectoFueraDeRango);
        Assert.Empty(l.AspectoMensaje);
    }

    [Fact]
    public void AspectoMensaje_describe_el_warning_cuando_esta_fuera_de_rango()
    {
        var l = Valida(); l.Lx = 1; l.Ly = 5;
        Assert.True(l.AspectoFueraDeRango);
        Assert.Contains("Pieper-Martens", l.AspectoMensaje);
        Assert.Contains("0.5", l.AspectoMensaje);
        Assert.Contains("2.0", l.AspectoMensaje);
    }

    [Fact]
    public void Aspecto_fuera_de_rango_NO_marca_la_losa_como_invalida()
    {
        // El warning de aspecto es informativo, no bloquea validación.
        var l = Valida(); l.Lx = 1; l.Ly = 5;
        Assert.True(l.AspectoFueraDeRango);
        Assert.True(l.EsValida);  // sin errores duros
    }

    // ---------- Notificación de cambios ----------

    [Fact]
    public void Cambiar_Espesor_renotifica_a_Rec_para_revalidacion()
    {
        var l = Valida(); l.Espesor = 0.10; l.Rec = 0.05;  // ok
        var notificadas = new System.Collections.Generic.List<string>();
        l.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName ?? "");

        l.Espesor = 0.04;  // ahora Rec=0.05 >= Espesor=0.04 → Rec debe revalidarse

        Assert.Contains(nameof(Losa.Espesor), notificadas);
        Assert.Contains(nameof(Losa.Rec), notificadas);
        Assert.False(l.EsValida);  // confirma que el error se detecta tras el cambio
    }

    [Fact]
    public void Cambiar_Lx_renotifica_Aspecto_y_AspectoFueraDeRango()
    {
        var l = Valida();  // Lx=4, Ly=4, Aspecto=1, en rango
        var notificadas = new System.Collections.Generic.List<string>();
        l.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName ?? "");

        l.Lx = 1;  // Aspecto pasa a 4, fuera de rango

        Assert.Contains(nameof(Losa.Aspecto), notificadas);
        Assert.Contains(nameof(Losa.AspectoFueraDeRango), notificadas);
        Assert.Contains(nameof(Losa.AspectoMensaje), notificadas);
        Assert.True(l.AspectoFueraDeRango);
    }
}
