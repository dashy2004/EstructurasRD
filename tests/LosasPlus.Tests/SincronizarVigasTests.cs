using LosasPlus.Models;
using LosasPlus.Services;
using LosasPlus.Vigas;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="SincronizadorPlanta.SincronizarVigas"/>: distribuye las
/// vigas sin posicionar para que no arranquen apiladas en el origen (y se vean en
/// la Planta 2D / Vista 3D).
/// </summary>
public class SincronizarVigasTests
{
    [Fact]
    public void Distribuye_vigas_apiladas_en_el_origen()
    {
        var niv = new Nivel();
        niv.Vigas.Add(new Viga { Id = 1, OrigenX = 0, OrigenY = 0 });
        niv.Vigas.Add(new Viga { Id = 2, OrigenX = 0, OrigenY = 0 });
        niv.Vigas.Add(new Viga { Id = 3, OrigenX = 0, OrigenY = 0 });

        bool cambio = SincronizadorPlanta.SincronizarVigas(niv);

        Assert.True(cambio);
        Assert.NotEqual(niv.Vigas[0].OrigenY, niv.Vigas[2].OrigenY);   // ya no apiladas
    }

    [Fact]
    public void No_toca_vigas_ya_posicionadas()
    {
        var niv = new Nivel();
        niv.Vigas.Add(new Viga { Id = 1, OrigenX = 2, OrigenY = 3 });
        niv.Vigas.Add(new Viga { Id = 2, OrigenX = 0, OrigenY = 0 });

        Assert.False(SincronizadorPlanta.SincronizarVigas(niv));        // no todas en (0,0)
    }
}
