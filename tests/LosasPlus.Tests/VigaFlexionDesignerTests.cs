using LosasPlus.Calculo;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del diseñador de acero a flexión de vigas (item E — armado real):
/// bloque de Whitney para As requerido, mínimo ACI 318-19 §9.6.1.1, conteo de
/// barras y el capstone <see cref="VigaFlexionDesigner.DisenarTramo"/>. Anclas
/// numéricas calculadas a mano (b=0.30, h=0.50, f'c=28, fy=420, barra #5).
/// </summary>
public class VigaFlexionDesignerTests
{
    private const double B = 0.30, H = 0.50, Fc = 28.0, Fy = 420.0;
    // d = h − rec − db(#5)/2 = 0.50 − 0.04 − 0.01588/2 = 0.45206 m
    private const double D = 0.45206;

    [Fact]
    public void AsRequerido_coincide_con_el_bloque_de_Whitney_calculado_a_mano()
    {
        // Mu = 100 kN·m, d = 0.45206 m → As ≈ 609.4 mm² (bloque de Whitney).
        double as_ = VigaFlexionDesigner.AsRequeridoMm2(100.0, B, D, Fc, Fy, out bool insuf);
        Assert.False(insuf);
        Assert.Equal(609.4, as_, 1);   // ±0.05 mm²
    }

    [Fact]
    public void AsRequerido_momento_cero_es_cero()
        => Assert.Equal(0.0, VigaFlexionDesigner.AsRequeridoMm2(0.0, B, D, Fc, Fy, out _), 9);

    [Fact]
    public void AsMinimo_usa_el_mayor_de_las_dos_expresiones_ACI_9_6_1_1()
    {
        // f'c=28: 0.25·√28/420 = 0.003150; 1.4/420 = 0.003333 → gobierna 1.4/fy.
        // As_min = 0.003333·300·452.06 ≈ 452.1 mm²
        double asMin = VigaFlexionDesigner.AsMinimoMm2(B, D, Fc, Fy);
        Assert.Equal(452.1, asMin, 0);
    }

    [Fact]
    public void NumeroBarras_redondea_hacia_arriba_y_respeta_el_minimo_de_dos()
    {
        // As=610, #5=199 mm² → ceil(3.07) = 4
        Assert.Equal(4, VigaFlexionDesigner.NumeroBarras(610.0, 5));
        // As pequeño → mínimo 2 barras por cara
        Assert.Equal(2, VigaFlexionDesigner.NumeroBarras(150.0, 5));
        Assert.Equal(2, VigaFlexionDesigner.NumeroBarras(0.0, 5));
    }

    [Fact]
    public void DisenarTramo_inferior_por_positivo_superior_por_negativo()
    {
        var d = VigaFlexionDesigner.DisenarTramo(120.0, -80.0, B, H, Fc, Fy);

        Assert.False(d.Inferior.SeccionInsuficiente);
        Assert.False(d.Superior.SeccionInsuficiente);
        // Más momento abajo (120) que arriba (80) → más (o igual) acero abajo.
        Assert.True(d.Inferior.AsMm2 >= d.Superior.AsMm2);
        Assert.True(d.Inferior.NumeroDeBarras >= d.Superior.NumeroDeBarras);
        // Cada cara cubre su As de diseño y tiene ≥ 2 barras.
        Assert.True(d.Inferior.NumeroDeBarras >= 2);
        Assert.True(d.Superior.NumeroDeBarras >= 2);
        Assert.True(d.Inferior.AsProvistaMm2 >= d.Inferior.AsMm2);
        Assert.True(d.Superior.AsProvistaMm2 >= d.Superior.AsMm2);
    }

    [Fact]
    public void DisenarTramo_momentos_cero_gobierna_el_minimo_con_dos_barras()
    {
        var d = VigaFlexionDesigner.DisenarTramo(0.0, 0.0, B, H, Fc, Fy);
        Assert.True(d.Inferior.GobiernaMinimo);
        Assert.True(d.Superior.GobiernaMinimo);
        // Sin momento, el As de diseño es exactamente el mínimo a flexión.
        Assert.Equal(d.Inferior.AsMinimoMm2, d.Inferior.AsMm2, 6);
        Assert.Equal(d.Superior.AsMinimoMm2, d.Superior.AsMm2, 6);
        // El As mínimo (≈452 mm²) ya exige ≥ 2 barras por cara (con #5 → 3).
        Assert.True(d.Inferior.NumeroDeBarras >= VigaFlexionDesigner.MinBarrasPorCara);
        Assert.True(d.Superior.NumeroDeBarras >= VigaFlexionDesigner.MinBarrasPorCara);
    }

    [Fact]
    public void DisenarCara_seccion_insuficiente_para_un_momento_enorme()
    {
        // Momento desproporcionado para una sección 0.30×0.50 → discriminante < 0.
        var cara = VigaFlexionDesigner.DisenarCara("Inferior", 2000.0, B, H, Fc, Fy,
                                                   VigaFlexionDesigner.RecubrimientoDefaultM, 5);
        Assert.True(cara.SeccionInsuficiente);
        Assert.Equal(0, cara.NumeroDeBarras);
    }
}
