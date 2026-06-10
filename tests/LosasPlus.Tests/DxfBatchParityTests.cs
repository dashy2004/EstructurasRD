using LosasPlus.IA;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// F2 C1 — paridad batch↔interactivo: la losa creada por el pipeline batch
/// (<see cref="DxfEstructuraMapper.CrearLosaBatch"/>) queda ANCLADA
/// (PosX/PosY no nulos) y con la Y invertida a la convención del lienzo
/// (Y descendente), exactamente como el path interactivo
/// (CadEditorViewModel.MapearPoligono: posY = Plano.MaxY − rect.MaxY).
/// </summary>
public class DxfBatchParityTests
{
    [Fact]
    public void CrearLosaBatch_ancla_y_invierte_Y_como_el_path_interactivo()
    {
        // Rectángulo DXF: esquina inferior-izq (2,3), 5×4 m, en un plano con MaxY=20.
        var prop = new LosaPropuesta(XMetros: 2, YMetros: 3, LxM: 5, LyM: 4, Tipo: 40);

        var losa = DxfEstructuraMapper.CrearLosaBatch(prop, maxYPlano: 20, id: 7);

        // Fórmula interactiva: posY = MaxY − rect.MaxY = 20 − (3+4) = 13.
        Assert.Equal(7, losa.Id);
        Assert.Equal(2.0, losa.PosX!.Value, 9);
        Assert.Equal(13.0, losa.PosY!.Value, 9);
        Assert.True(losa.TienePosicionExplicita);          // anclada: LayoutSolver no la reubica
        Assert.Equal(2.0, losa.CoordenadaX, 9);
        Assert.Equal(13.0, losa.CoordenadaY, 9);           // misma convención Y-down que PosY
        Assert.Equal(5.0, losa.Lx, 9);
        Assert.Equal(4.0, losa.Ly, 9);
        Assert.Equal(40, losa.Tipo);                        // el tipo propuesto se propaga
    }

    [Fact]
    public void CrearLosaBatch_dimensiones_no_positivas_caen_al_default_4m()
    {
        var prop = new LosaPropuesta(XMetros: 0, YMetros: 0, LxM: 0, LyM: -1);
        var losa = DxfEstructuraMapper.CrearLosaBatch(prop, maxYPlano: 10, id: 1);
        Assert.Equal(4.0, losa.Lx, 9);
        Assert.Equal(4.0, losa.Ly, 9);
    }
}
