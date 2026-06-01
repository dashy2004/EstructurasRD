using System.Numerics;
using LosasPlus.Render3D;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests del núcleo de geometría 3D (Fase I): cámara orbital y primitivas de
/// escena. Pura matemática con System.Numerics — sin GL ni UI.
/// </summary>
public class Render3DTests
{
    [Fact]
    public void Ejes_devuelve_tres_segmentos_desde_el_origen()
    {
        var ejes = PrimitivasEscena.Ejes(5f);
        Assert.Equal(3, ejes.Count);
        Assert.Equal(new Vector3(5, 0, 0), ejes[0].B);
        Assert.Equal(new Vector3(0, 5, 0), ejes[1].B);
        Assert.Equal(new Vector3(0, 0, 5), ejes[2].B);
        Assert.All(ejes, s => Assert.Equal(Vector3.Zero, s.A));
    }

    [Fact]
    public void RejillaSuelo_cuenta_lineas_y_rechaza_paso_no_positivo()
    {
        Assert.Equal(42, PrimitivasEscena.RejillaSuelo(10f, 1f).Count); // 21 + 21
        Assert.Empty(PrimitivasEscena.RejillaSuelo(10f, 0f));
        Assert.Empty(PrimitivasEscena.RejillaSuelo(0f, 1f));
    }

    [Fact]
    public void CajaAlambre_tiene_doce_aristas()
    {
        Assert.Equal(12, PrimitivasEscena.CajaAlambre(new Vector3(-1, -1, -1), new Vector3(1, 1, 1)).Count);
    }

    [Fact]
    public void Ojo_se_mantiene_a_la_distancia_del_objetivo()
    {
        var cam = new CamaraOrbital();
        Assert.Equal(cam.Distancia, Vector3.Distance(cam.PosicionOjo, cam.Objetivo), 3);
    }

    [Fact]
    public void Encuadrar_centra_el_objetivo_y_ajusta_la_distancia()
    {
        var cam = new CamaraOrbital();
        cam.Encuadrar(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));

        Assert.True(cam.Objetivo.Length() < 1e-3f);
        Assert.InRange(cam.Distancia, 4f, 5f);

        // La vista debe mapear el objetivo a (0,0,-distancia) en espacio de cámara.
        var vt = Vector3.Transform(cam.Objetivo, cam.MatrizVista);
        Assert.Equal(0f, vt.X, 2);
        Assert.Equal(0f, vt.Y, 2);
        Assert.Equal(-cam.Distancia, vt.Z, 2);
    }

    [Fact]
    public void Orbitar_acota_la_elevacion()
    {
        var cam = new CamaraOrbital();
        cam.Orbitar(0f, 1000f);
        Assert.Equal(CamaraOrbital.ElevacionMax, cam.ElevacionGrados, 3);
        cam.Orbitar(0f, -2000f);
        Assert.Equal(-CamaraOrbital.ElevacionMax, cam.ElevacionGrados, 3);
    }

    [Fact]
    public void AcercarAlejar_escala_y_acota_la_distancia()
    {
        var cam = new CamaraOrbital();
        float d0 = cam.Distancia;
        cam.AcercarAlejar(2f);
        Assert.Equal(d0 * 2f, cam.Distancia, 2);

        cam.AcercarAlejar(0f); // factor inválido → sin efecto
        Assert.Equal(d0 * 2f, cam.Distancia, 2);

        cam.AcercarAlejar(0.0001f); // acota al mínimo
        Assert.Equal(CamaraOrbital.DistanciaMin, cam.Distancia, 3);
    }

    [Fact]
    public void Proyeccion_es_perspectiva_y_tolera_aspecto_invalido()
    {
        var cam = new CamaraOrbital();
        var p = cam.MatrizProyeccion(0f); // aspecto ≤ 0 → tratado como 1
        Assert.Equal(0f, p.M44); // perspectiva: M44 = 0, M34 = -1
        Assert.Equal(-1f, p.M34, 3);
    }
}
