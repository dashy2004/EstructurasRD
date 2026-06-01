using LosasPlus.Models;
using LosasPlus.Persistence;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la posición en planta de la viga (Fase J.14): longitud total,
/// extremo final según origen/ángulo, y round-trip de serialización (aditivo).
/// </summary>
public class VigaPlantaTests
{
    private static Viga VigaDe2Tramos(double ox, double oy, double ang)
    {
        var v = new Viga { Id = 1, Nombre = "V-1", OrigenX = ox, OrigenY = oy, AnguloGrados = ang };
        v.Tramos.Add(new TramoViga { Longitud = 5 });
        v.Tramos.Add(new TramoViga { Longitud = 5 });
        return v;
    }

    [Fact]
    public void Longitud_total_y_extremo_segun_origen_y_angulo()
    {
        var v = VigaDe2Tramos(1, 2, 0);
        Assert.Equal(10, v.LongitudTotal, 9);
        Assert.Equal(11, v.ExtremoX, 6);
        Assert.Equal(2, v.ExtremoY, 6);

        var v90 = VigaDe2Tramos(1, 2, 90);
        Assert.Equal(1, v90.ExtremoX, 6);
        Assert.Equal(12, v90.ExtremoY, 6);
    }

    [Fact]
    public void La_posicion_en_planta_sobrevive_al_round_trip()
    {
        var p = ProyectoFactory.NuevoProyectoSeedeado();
        p.AsegurarEstructura();
        p.Edificios[0].Niveles[0].Vigas.Add(VigaDe2Tramos(3, 4, 30));

        var json = ProyectoSerializer.ToJson(p);
        Assert.DoesNotContain("LongitudTotal", json);  // computada, JsonIgnore
        Assert.DoesNotContain("ExtremoX", json);

        var v = ProyectoSerializer.FromJson(json).Edificios[0].Niveles[0].Vigas[0];
        Assert.Equal(3, v.OrigenX, 9);
        Assert.Equal(4, v.OrigenY, 9);
        Assert.Equal(30, v.AnguloGrados, 9);
        Assert.Equal(10, v.LongitudTotal, 9); // recomputada desde los tramos
    }
}
