using System;
using LosasPlus.Calculo;
using LosasPlus.Models;
using Xunit;

namespace LosasPlus.Tests.Calculo;

/// <summary>
/// Tests del diseñador de acero a flexión de losas (ACI 318-19). Los valores
/// esperados se calcularon a mano en ton·cm (ver AcerosLosaDesigner XML docs).
/// Materiales de referencia RD: f'c = 0.210 ton/cm², fy = 4.200 ton/cm².
/// </summary>
public class AcerosLosaDesignerTests
{
    private const double Fc = 0.210;
    private const double Fy = 4.200;

    [Fact]
    public void CantoUtil_capa_inferior_y_superior()
    {
        // h=20, rec=2.5, db(#4)=1.27
        Assert.Equal(16.865, AcerosLosaDesigner.CantoUtil(20, 2.5, 1.27, capa: 0), 3);
        Assert.Equal(15.595, AcerosLosaDesigner.CantoUtil(20, 2.5, 1.27, capa: 1), 3);
    }

    [Fact]
    public void AsRequerido_caso_base_coincide_con_calculo_manual()
    {
        double as_ = AcerosLosaDesigner.AsRequerido(1.0, 16.865, Fc, Fy, out bool insuf);
        Assert.False(insuf);
        Assert.Equal(1.586, as_, 2);   // cm²/m, tolerancia 0.01
    }

    [Fact]
    public void AsRequerido_momento_cero_es_cero()
        => Assert.Equal(0.0, AcerosLosaDesigner.AsRequerido(0.0, 16.865, Fc, Fy, out _));

    [Fact]
    public void AsRequerido_usa_valor_absoluto_del_momento()
    {
        double pos = AcerosLosaDesigner.AsRequerido(1.5, 16.865, Fc, Fy, out _);
        double neg = AcerosLosaDesigner.AsRequerido(-1.5, 16.865, Fc, Fy, out _);
        Assert.Equal(pos, neg, 6);
    }

    [Fact]
    public void AsRequerido_seccion_insuficiente_devuelve_NaN()
    {
        double as_ = AcerosLosaDesigner.AsRequerido(20.0, 5.0, Fc, Fy, out bool insuf);
        Assert.True(insuf);
        Assert.True(double.IsNaN(as_));
    }

    [Theory]
    [InlineData(2.80, 4.0)]    // fyKg=2800 ≤3500 → ρ=0.0020 → 0.0020·100·20
    [InlineData(4.20, 3.6)]    // Grado 420 → ρ=0.0018 → 0.0018·100·20
    public void AsMinimoTemperatura_segun_grado(double fy, double esperado)
        => Assert.Equal(esperado, AcerosLosaDesigner.AsMinimoTemperatura(20, fy), 4);

    [Fact]
    public void AsMinimoTemperatura_alto_grado_usa_formula_y_piso_0_0014()
    {
        // fyKg=5200 → ρ=max(0.0018·4200/5200, 0.0014)=0.00145385 → ·100·20
        Assert.Equal(2.9077, AcerosLosaDesigner.AsMinimoTemperatura(20, 5.20), 3);
    }

    [Theory]
    [InlineData(20, 40)]   // min(2·20, 45)
    [InlineData(25, 45)]   // min(2·25, 45)=45
    public void EspaciamientoMaximo_es_min_2h_45(double h, double esperado)
        => Assert.Equal(esperado, AcerosLosaDesigner.EspaciamientoMaximo(h));

    [Fact]
    public void SeleccionarArmadura_elige_barra_mas_pequena_factible()
    {
        var arm = AcerosLosaDesigner.SeleccionarArmadura(3.6, 40);
        Assert.Equal(3, arm.NumeroBarra);              // #3 alcanza
        Assert.Equal(17.5, arm.EspaciamientoCm, 3);    // floor(19.72/2.5)·2.5
        Assert.True(arm.AsProvistaCm2M >= 3.6);
        Assert.True(arm.Cumple);
    }

    [Fact]
    public void SeleccionarArmadura_capa_al_maximo_normativo()
    {
        // As muy pequeño → espaciamiento bruto enorme, se capa al máximo (40).
        var arm = AcerosLosaDesigner.SeleccionarArmadura(0.5, 40);
        Assert.Equal(40, arm.EspaciamientoCm, 3);
        Assert.True(arm.Cumple);
    }

    [Fact]
    public void DisenarFranja_minimo_gobierna_con_momento_bajo()
    {
        var f = AcerosLosaDesigner.DisenarFranja("X centro", 0.2, 20, 16.865, Fc, Fy);
        Assert.False(f.SeccionInsuficiente);
        Assert.Equal(3.6, f.AsMinimoCm2M, 4);
        Assert.True(f.AsRequeridoCm2M < f.AsMinimoCm2M);
        Assert.Equal(3.6, f.AsDisenoCm2M, 4);
        Assert.True(f.GobiernaMinimo);
        Assert.Contains("#", f.Disponer);
        Assert.Contains("cm", f.Disponer);
    }

    [Fact]
    public void DisenarFranja_seccion_insuficiente_propaga_flag()
    {
        var f = AcerosLosaDesigner.DisenarFranja("X apoyo", 20.0, 10, 5.0, Fc, Fy);
        Assert.True(f.SeccionInsuficiente);
        Assert.Equal("SECCIÓN INSUFICIENTE", f.Disponer);
        Assert.False(f.CumpleEspaciamiento);
    }

    [Fact]
    public void DisenarLosa_produce_cuatro_franjas_y_apoyo_pide_mas_que_centro()
    {
        var m = new MomentoLosa(
            LosaId: 7, Tipo: 33, Carga: 0.8, H: 0.20, Lx: 4.0, Ly: 5.0,
            Mfx: 1.0, Mfy: 0.8, NMSx: 1.5, NMSy: 1.2);

        var d = AcerosLosaDesigner.DisenarLosa(m, Fc, Fy);

        Assert.Equal(7, d.LosaId);
        Assert.Equal("X centro", d.XCentro.Franja);
        Assert.Equal("Y centro", d.YCentro.Franja);
        Assert.Equal("X apoyo", d.XApoyo.Franja);
        Assert.Equal("Y apoyo", d.YApoyo.Franja);
        // El momento negativo de apoyo (1.5) exige más acero que el de vano (1.0).
        Assert.True(d.XApoyo.AsRequeridoCm2M > d.XCentro.AsRequeridoCm2M);
    }
}
