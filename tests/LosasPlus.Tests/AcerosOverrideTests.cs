using LosasPlus.Calculo;
using LosasPlus.ViewModels;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// "Editar el acero": el ingeniero fija barra+espaciamiento y el sistema recalcula
/// el acero provisto y si cumple, sin alterar el acero requerido.
/// </summary>
public class AcerosOverrideTests
{
    // Franja base: As requerido gobernado por el mínimo (3.60), h=20 → espMax=40.
    private static DisenoAceroFranja Base() =>
        AcerosLosaDesigner.DisenarFranja("X centro", 1.28, 20.0, 16.5, 0.210, 4.200);

    [Fact]
    public void Override_recalcula_barra_espaciamiento_y_disponer()
    {
        var ov = AcerosLosaDesigner.AplicarOverride(Base(), 4, 20.0, 40.0);

        Assert.Equal(4, ov.NumeroBarra);
        Assert.Equal(20.0, ov.EspaciamientoCm);
        Assert.Equal("#4 @ 20 cm", ov.Disponer);
        Assert.True(ov.AsProvistaCm2M > 0);
        Assert.True(ov.CumpleEspaciamiento);                 // #4@20 cubre 3.60 y 7.5≤20≤40
    }

    [Fact]
    public void Override_no_cumple_si_no_cubre_el_acero_requerido()
    {
        // #3 @ 40: 0.71·100/40 = 1.78 < 3.60 → no cumple.
        var ov = AcerosLosaDesigner.AplicarOverride(Base(), 3, 40.0, 40.0);
        Assert.False(ov.CumpleEspaciamiento);
    }

    [Fact]
    public void Override_conserva_el_acero_requerido()
    {
        var b = Base();
        var ov = AcerosLosaDesigner.AplicarOverride(b, 5, 18.0, 40.0);
        Assert.Equal(b.AsDisenoCm2M, ov.AsDisenoCm2M, 6);    // requerido intacto
        Assert.Equal(b.AsRequeridoCm2M, ov.AsRequeridoCm2M, 6);
    }

    [Fact]
    public void Fila_editable_marca_manual_y_notifica()
    {
        var fila = new DisenoAceroFila(1, 40, Base(), hCm: 20.0);
        Assert.False(fila.EsManual);

        string raised = "";
        fila.PropertyChanged += (_, e) => raised += e.PropertyName + ";";

        fila.AplicarOverride(5, 18.0);

        Assert.True(fila.EsManual);
        Assert.Equal(5, fila.NumeroBarra);
        Assert.Equal(18.0, fila.EspaciamientoCm);
        Assert.Contains("Disponer", raised);
        Assert.Contains("AsProvistaCm2M", raised);
    }
}
