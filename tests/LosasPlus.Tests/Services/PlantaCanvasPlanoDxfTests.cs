using Avalonia;
using LosasPlus.Models.Cad;
using LosasPlus.Views;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del mapeo mundo-DXF ↔ planta de <see cref="PlantaCanvas"/> (UI1.3).
/// El DXF guarda coordenadas mundo (Y ascendente); la planta es Y descendente.
/// La convención es la misma que <c>DxfEstructuraMapper.CrearLosaBatch</c> y el
/// <c>CadTransform</c> del lienzo CAD: <c>yPlanta = MaxY − yMundo</c> (+ ajuste
/// espacial Escala/Offset del plano). El bug que esto fija: el underlay DXF de
/// planta se dibujaba SIN el flip ⇒ plano espejado verticalmente vs la estructura.
/// </summary>
public class PlantaCanvasPlanoDxfTests
{
    private static PlanoReferencia Plano(double maxY = 10, double esc = 1, double ox = 0, double oy = 0)
        => new() { MaxY = maxY, Escala = esc, OffsetX = ox, OffsetY = oy };

    [Fact]
    public void PlanoAPlanta_invierte_Y_con_la_convencion_del_mapper()
    {
        // Punto mundo (2, 3) en un plano con MaxY=10 → planta (2, 10−3=7).
        var p = PlantaCanvas.PlanoAPlanta(Plano(maxY: 10), new PuntoCad(2, 3));
        Assert.Equal(2.0, p.X, 9);
        Assert.Equal(7.0, p.Y, 9);
    }

    [Fact]
    public void PlanoAPlanta_respeta_escala_y_offset_del_ajuste_espacial()
    {
        // (MaxY − y)·Escala + OffsetY; x·Escala + OffsetX.
        var p = PlantaCanvas.PlanoAPlanta(Plano(maxY: 10, esc: 2, ox: 1, oy: 3), new PuntoCad(2, 4));
        Assert.Equal(1 + 2 * 2.0, p.X, 9);        // 5
        Assert.Equal(3 + (10 - 4) * 2.0, p.Y, 9); // 15
    }

    [Fact]
    public void PlantaAPlano_es_el_inverso_exacto()
    {
        var plano = Plano(maxY: 12.5, esc: 1.75, ox: -2.0, oy: 4.25);
        var mundoOriginal = new PuntoCad(3.33, 8.21);

        var planta = PlantaCanvas.PlanoAPlanta(plano, mundoOriginal);
        var mundo = PlantaCanvas.PlantaAPlano(plano, new Point(planta.X, planta.Y));

        Assert.Equal(mundoOriginal.X, mundo.X, 9);
        Assert.Equal(mundoOriginal.Y, mundo.Y, 9);
    }

    [Fact]
    public void El_techo_del_plano_cae_arriba_en_planta()
    {
        // El punto de mayor Y mundo (el "techo" del plano) debe quedar con la
        // MENOR Y de planta — si esto falla, el plano se ve espejado (el bug).
        var plano = Plano(maxY: 10);
        var techo = PlantaCanvas.PlanoAPlanta(plano, new PuntoCad(0, 10));
        var piso  = PlantaCanvas.PlanoAPlanta(plano, new PuntoCad(0, 0));
        Assert.True(techo.Y < piso.Y);
    }
}
