using Avalonia;
using Avalonia.Input;
using LosasPlus.Views;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del snap ortogonal del gesto de calibración de PDF en Planta 2D
/// (UI1.2): con Shift, la línea P₁→P₂ se fuerza al eje del delta dominante —
/// el mismo gesto que la calibración del lienzo CAD, pero en metros.
/// </summary>
public class PlantaCanvasCalibracionTests
{
    [Fact]
    public void Sin_Shift_el_segundo_punto_no_se_modifica()
    {
        var p2 = PlantaCanvas.AplicarOrtoCalibracion(new Point(1, 1), new Point(4, 3), KeyModifiers.None);
        Assert.Equal(new Point(4, 3), p2);
    }

    [Fact]
    public void Con_Shift_y_delta_X_dominante_la_linea_es_horizontal()
    {
        var p2 = PlantaCanvas.AplicarOrtoCalibracion(new Point(1, 1), new Point(6, 2.5), KeyModifiers.Shift);
        Assert.Equal(new Point(6, 1), p2);
    }

    [Fact]
    public void Con_Shift_y_delta_Y_dominante_la_linea_es_vertical()
    {
        var p2 = PlantaCanvas.AplicarOrtoCalibracion(new Point(1, 1), new Point(2.5, 7), KeyModifiers.Shift);
        Assert.Equal(new Point(1, 7), p2);
    }
}
