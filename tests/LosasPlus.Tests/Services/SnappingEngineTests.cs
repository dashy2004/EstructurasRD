using System;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="SnappingEngine"/> — el motor matemático de snapping de
/// la Fase 3 del plan CAD: enganche a la grilla, enganche a bordes de losas
/// vecinas y resolución de colisiones lógicas (qué candidato gana).
/// </summary>
public class SnappingEngineTests
{
    // =================================================================
    // SnapAGrilla
    // =================================================================

    [Fact]
    public void SnapAGrilla_engancha_un_valor_cercano_a_la_linea()
    {
        var r = SnappingEngine.SnapAGrilla(2.1, paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAGrilla_no_engancha_un_valor_lejano()
    {
        // round(2.45) = 2 → distancia 0.45 > umbral 0.2.
        var r = SnappingEngine.SnapAGrilla(2.45, paso: 1.0, umbral: 0.2);
        Assert.False(r.Ajustado);
        Assert.Equal(2.45, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAGrilla_un_multiplo_exacto_se_mantiene()
    {
        var r = SnappingEngine.SnapAGrilla(3.0, paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(3.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAGrilla_funciona_con_coordenadas_negativas()
    {
        var r = SnappingEngine.SnapAGrilla(-2.1, paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(-2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAGrilla_el_borde_del_umbral_se_incluye()
    {
        // Distancia exacta 0.125 con umbral 0.125 (fracciones binarias exactas).
        var dentro = SnappingEngine.SnapAGrilla(0.125, paso: 1.0, umbral: 0.125);
        Assert.True(dentro.Ajustado);
        Assert.Equal(0.0, dentro.Valor, precision: 6);

        var fuera = SnappingEngine.SnapAGrilla(0.125, paso: 1.0, umbral: 0.124);
        Assert.False(fuera.Ajustado);
    }

    [Fact]
    public void SnapAGrilla_un_paso_no_positivo_deshabilita_el_snap()
    {
        Assert.False(SnappingEngine.SnapAGrilla(2.05, paso: 0.0).Ajustado);
        Assert.False(SnappingEngine.SnapAGrilla(2.05, paso: -1.0).Ajustado);
    }

    [Fact]
    public void SnapAGrilla_respeta_un_paso_distinto_de_un_metro()
    {
        // Grilla de 0.5 m: 2.55 engancha a 2.5.
        var r = SnappingEngine.SnapAGrilla(2.55, paso: 0.5, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.5, r.Valor, precision: 6);
    }

    // =================================================================
    // SnapAValores
    // =================================================================

    [Fact]
    public void SnapAValores_engancha_al_candidato_en_rango()
    {
        var r = SnappingEngine.SnapAValores(5.05, new[] { 5.0 }, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(5.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAValores_elige_el_candidato_mas_cercano()
    {
        // 1.95 (dist 0.05) gana sobre 2.10 (dist 0.10); 5.0 está fuera de rango.
        var r = SnappingEngine.SnapAValores(2.0, new[] { 1.95, 2.10, 5.0 }, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(1.95, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAValores_elige_el_mas_cercano_aunque_esten_a_ambos_lados()
    {
        var r = SnappingEngine.SnapAValores(2.0, new[] { 1.90, 2.05 }, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.05, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAValores_sin_candidatos_no_engancha()
    {
        var r = SnappingEngine.SnapAValores(2.0, Array.Empty<double>(), umbral: 0.2);
        Assert.False(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAValores_un_candidato_fuera_del_umbral_no_engancha()
    {
        var r = SnappingEngine.SnapAValores(2.0, new[] { 2.5 }, umbral: 0.2);
        Assert.False(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapAValores_funciona_con_coordenadas_negativas()
    {
        var r = SnappingEngine.SnapAValores(-3.05, new[] { -3.0 }, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(-3.0, r.Valor, precision: 6);
    }

    // =================================================================
    // SnapCombinado
    // =================================================================

    [Fact]
    public void SnapCombinado_gana_el_candidato_si_esta_mas_cerca_que_la_grilla()
    {
        // valor 2.05 — candidato 2.04 (dist 0.01) vence a la grilla 2.0 (dist 0.05).
        var r = SnappingEngine.SnapCombinado(2.05, new[] { 2.04 }, paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.04, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapCombinado_gana_la_grilla_si_esta_mas_cerca_que_el_candidato()
    {
        // valor 2.05 — grilla 2.0 (dist 0.05) vence al candidato 2.18 (dist 0.13).
        var r = SnappingEngine.SnapCombinado(2.05, new[] { 2.18 }, paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapCombinado_no_engancha_si_nada_esta_en_rango()
    {
        // 2.5 está a 0.5 de cualquier línea de grilla; el candidato 3.4 a 0.9.
        var r = SnappingEngine.SnapCombinado(2.5, new[] { 3.4 }, paso: 1.0, umbral: 0.2);
        Assert.False(r.Ajustado);
        Assert.Equal(2.5, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapCombinado_en_empate_gana_el_candidato()
    {
        // valor 2.5 — candidato 2.0 y grilla 3.0, ambos a distancia exacta 0.5.
        // El borde de losa "manda": debe ganar el candidato (2.0), no la grilla.
        var r = SnappingEngine.SnapCombinado(2.5, new[] { 2.0 }, paso: 1.0, umbral: 0.6);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    // =================================================================
    // SnapRango (movimiento rígido de un rango)
    // =================================================================

    [Fact]
    public void SnapRango_engancha_por_el_borde_inicial()
    {
        // [2.05, 5.05] — el inicio 2.05 engancha al candidato 2.0.
        var r = SnappingEngine.SnapRango(2.05, longitud: 3.0, new[] { 2.0 }, paso: 0.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapRango_engancha_por_el_borde_final()
    {
        // [1.9, 4.9] — el fin 4.9 engancha al candidato 5.0 → el inicio se corre a 2.0.
        var r = SnappingEngine.SnapRango(1.9, longitud: 3.0, new[] { 5.0 }, paso: 0.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapRango_si_ambos_bordes_enganchan_gana_el_de_menor_desplazamiento()
    {
        // [2.15, 5.15] — inicio→2.0 exige Δ 0.15; fin→5.05 exige Δ 0.10.
        // Gana el de menor magnitud: el inicio queda en 2.05.
        var r = SnappingEngine.SnapRango(2.15, longitud: 3.0, new[] { 2.0, 5.05 },
                                         paso: 0.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.05, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapRango_no_engancha_si_ningun_borde_esta_en_rango()
    {
        var r = SnappingEngine.SnapRango(2.37, longitud: 3.0, new[] { 10.0 },
                                         paso: 0.0, umbral: 0.2);
        Assert.False(r.Ajustado);
        Assert.Equal(2.37, r.Valor, precision: 6);
    }

    [Fact]
    public void SnapRango_engancha_a_la_grilla_sin_candidatos()
    {
        // [1.9, 4.9] sin candidatos: ambos bordes caen a 0.1 de la grilla → inicio 2.0.
        var r = SnappingEngine.SnapRango(1.9, longitud: 3.0, Array.Empty<double>(),
                                         paso: 1.0, umbral: 0.2);
        Assert.True(r.Ajustado);
        Assert.Equal(2.0, r.Valor, precision: 6);
    }
}
