using LosasPlus.Models;
using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests del <see cref="SincronizadorPlanta"/>: hornear el layout de
/// <see cref="LayoutSolver"/> en <see cref="Losa.CoordenadaX"/>/<see cref="Losa.CoordenadaY"/>
/// para que el 2D y el 3D dejen de apilar las losas en el origen.
/// </summary>
public class SincronizadorPlantaTests
{
    private static Sistema DosLosasAdyacentesEnX()
    {
        var s = new Sistema();
        s.Losas.Add(new Losa { Id = 1, Lx = 4.0, Ly = 4.0 });   // CoordenadaX/Y = 0 (sin posicionar)
        s.Losas.Add(new Losa { Id = 2, Lx = 4.0, Ly = 4.0 });
        s.BordesX.Add(new BordeAdic { BI = 1, BJ = 2 });        // losa 2 a la derecha de la 1
        return s;
    }

    [Fact]
    public void RequiereSincronizacion_true_cuando_todas_en_origen()
    {
        Assert.True(SincronizadorPlanta.RequiereSincronizacion(DosLosasAdyacentesEnX()));
    }

    [Fact]
    public void Sincronizar_separa_las_losas_y_no_se_apilan()
    {
        var s = DosLosasAdyacentesEnX();
        bool cambio = SincronizadorPlanta.Sincronizar(s);

        Assert.True(cambio);
        var l1 = s.Losas[0];
        var l2 = s.Losas[1];
        // La losa 2 queda a la derecha de la 1 (a Lx de distancia) → no se solapan.
        Assert.Equal(0.0, l1.CoordenadaX, 6);
        Assert.Equal(4.0, l2.CoordenadaX, 6);
        Assert.NotEqual(
            (l1.CoordenadaX, l1.CoordenadaY),
            (l2.CoordenadaX, l2.CoordenadaY));
    }

    [Fact]
    public void Sincronizar_es_idempotente_y_no_pisa_lo_ya_posicionado()
    {
        var s = DosLosasAdyacentesEnX();
        SincronizadorPlanta.Sincronizar(s);
        // Tras posicionar, ya no requiere sincronización ni cambia.
        Assert.False(SincronizadorPlanta.RequiereSincronizacion(s));
        Assert.False(SincronizadorPlanta.Sincronizar(s));
    }

    [Fact]
    public void Sincronizar_respeta_posicion_manual_del_usuario()
    {
        var s = DosLosasAdyacentesEnX();
        s.Losas[0].CoordenadaX = 12.5;     // el usuario movió una losa a mano
        // No todas están en (0,0) → no se reposiciona automáticamente.
        Assert.False(SincronizadorPlanta.Sincronizar(s));
        Assert.Equal(12.5, s.Losas[0].CoordenadaX, 6);
    }

    [Fact]
    public void Sincronizar_sin_losas_o_una_sola_no_hace_nada()
    {
        Assert.False(SincronizadorPlanta.Sincronizar(new Sistema()));
        var una = new Sistema();
        una.Losas.Add(new Losa { Id = 1 });
        Assert.False(SincronizadorPlanta.RequiereSincronizacion(una));
    }
}
