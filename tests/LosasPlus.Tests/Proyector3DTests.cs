using System;
using System.Numerics;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de la proyección 3D→2D por software (Fase I.3). Usa una cámara fija
/// construida a mano (mirando −Z desde (0,0,5), fov 90°, aspecto 1, viewport
/// 600×600) para que los píxeles esperados sean deterministas.
/// </summary>
public class Proyector3DTests
{
    private const float W = 600f, H = 600f;

    private static Matrix4x4 Mvp()
    {
        var view = Matrix4x4.CreateLookAt(new Vector3(0, 0, 5), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, 0.1f, 100f);
        return view * proj;
    }

    [Fact]
    public void El_objetivo_proyecta_al_centro()
    {
        Assert.True(Proyector3D.ProyectarPunto(Vector3.Zero, Mvp(), W, H, out var c));
        Assert.Equal(300f, c.X, 1);
        Assert.Equal(300f, c.Y, 1);
    }

    [Fact]
    public void X_positivo_va_a_la_derecha_e_Y_positivo_hacia_arriba()
    {
        var mvp = Mvp();
        Proyector3D.ProyectarPunto(new Vector3(1, 0, 0), mvp, W, H, out var px);
        Assert.Equal(360f, px.X, 1);  // ndc.x = 0.2 → 0.6·600
        Assert.Equal(300f, px.Y, 1);

        Proyector3D.ProyectarPunto(new Vector3(0, 1, 0), mvp, W, H, out var py);
        Assert.Equal(240f, py.Y, 1);  // Y de pantalla invertida
        Assert.Equal(300f, py.X, 1);
    }

    [Fact]
    public void Un_punto_detras_del_ojo_no_es_visible()
    {
        Assert.False(Proyector3D.ProyectarPunto(new Vector3(0, 0, 10), Mvp(), W, H, out _));
    }

    [Fact]
    public void Un_segmento_con_un_extremo_detras_se_recorta()
    {
        bool vis = Proyector3D.ProyectarSegmento(
            Vector3.Zero, new Vector3(0, 0, 10), Mvp(), W, H, out var sa, out _);
        Assert.True(vis);
        Assert.Equal(300f, sa.X, 1);
        Assert.Equal(300f, sa.Y, 1);
    }

    [Fact]
    public void Un_segmento_completamente_detras_no_es_visible()
    {
        Assert.False(Proyector3D.ProyectarSegmento(
            new Vector3(0, 0, 10), new Vector3(0, 0, 20), Mvp(), W, H, out _, out _));
    }
}
