using Avalonia;
using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Views;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del hit-test de asas de redimensionado de losa en Planta 2D (UI1.5):
/// 8 asas (4 esquinas + 4 medios de borde, orden de <see cref="AsaRedim"/>)
/// sobre el rectángulo [CoordenadaX, +Lx] × [CoordenadaY, +Ly], en metros.
/// El resize en sí lo hace <see cref="GeometriaEdicion.Redimensionar"/> (ya
/// testeado); aquí se fija qué asa toma el puntero.
/// </summary>
public class PlantaCanvasResizeTests
{
    // Losa [2..6] × [3..5]: SupIzq(2,3) · SupCentro(4,3) · SupDer(6,3) ·
    // CentroDer(6,4) · InfDer(6,5) · InfCentro(4,5) · InfIzq(2,5) · CentroIzq(2,4)
    private static Losa LosaBase() => new() { CoordenadaX = 2, CoordenadaY = 3, Lx = 4, Ly = 2 };

    [Fact]
    public void Esquina_superior_izquierda_dentro_de_tolerancia()
    {
        var asa = PlantaCanvas.AsaEnPunto(LosaBase(), new Point(2.05, 3.02), tolM: 0.1);
        Assert.Equal(AsaRedim.SupIzq, asa);
    }

    [Fact]
    public void Medio_del_borde_derecho()
    {
        var asa = PlantaCanvas.AsaEnPunto(LosaBase(), new Point(6.0, 4.0), tolM: 0.1);
        Assert.Equal(AsaRedim.CentroDer, asa);
    }

    [Fact]
    public void El_centro_de_la_losa_no_es_un_asa()
    {
        Assert.Null(PlantaCanvas.AsaEnPunto(LosaBase(), new Point(4.0, 4.0), tolM: 0.1));
    }

    [Fact]
    public void Fuera_de_tolerancia_no_hay_asa()
    {
        // (2.5, 3): a 0.5 m del SupIzq y 1.5 m del SupCentro — tol 0.1 ⇒ nada.
        Assert.Null(PlantaCanvas.AsaEnPunto(LosaBase(), new Point(2.5, 3.0), tolM: 0.1));
    }
}
