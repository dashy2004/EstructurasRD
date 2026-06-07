using Avalonia;
using LosasPlus.Models.Cad;
using LosasPlus.Services;
using LosasPlus.Views.Cad;
using Xunit;

namespace LosasPlus.Tests;

/// <summary>
/// Tests de <see cref="CadTransform"/> — el par mundo↔pantalla compartido por el
/// render del plano (<c>DibujarPlano</c>) y el hit-test (<c>HitTestPoligono</c>)
/// del lienzo CAD.
///
/// <para>
/// La regresión que cubren: antes el hit-test sólo deshacía <c>/Px</c> y el
/// flip-Y, sin invertir la <b>Escala</b> ni el <b>Offset</b> del panel «Ajuste
/// espacial». Con esc≠1 u offset≠0, un clic se mapeaba al punto-mundo
/// equivocado → la losa seleccionada no coincidía con la del cursor. Estos
/// tests fallarían contra ese mapeo no-reversible y pasan con el inverso exacto.
/// </para>
/// </summary>
public class CadTransformTests
{
    // Transform realista con esc≠1 y offset≠0 (lo que rompía el hit-test viejo),
    // además de un zoom/pan no triviales del viewport.
    private static CadTransform Tf() => new CadTransform(
        pxPorMetro: 50.0,
        escala: 1.7, offsetX: 3.5, offsetY: -2.25, maxY: 12.0,
        scaleX: 0.8, scaleY: 0.8, tx: 140.0, ty: -65.0);

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(4.2, 7.9)]
    [InlineData(-3.1, 11.4)]
    [InlineData(12.0, -5.0)]
    public void RoundTrip_PantallaAMundo_de_MundoAPantalla_recupera_el_punto(double x, double y)
    {
        var tf = Tf();
        var mundo = new PuntoCad(x, y);

        var pantalla = tf.MundoAPantalla(mundo);
        var vuelta = tf.PantallaAMundo(pantalla);

        Assert.Equal(mundo.X, vuelta.X, 9);
        Assert.Equal(mundo.Y, vuelta.Y, 9);
    }

    [Fact]
    public void RoundTrip_MundoAPantalla_de_PantallaAMundo_recupera_el_pixel()
    {
        var tf = Tf();
        var pantalla = new Point(321.5, 88.0);

        var mundo = tf.PantallaAMundo(pantalla);
        var vuelta = tf.MundoAPantalla(mundo);

        Assert.Equal(pantalla.X, vuelta.X, 9);
        Assert.Equal(pantalla.Y, vuelta.Y, 9);
    }

    [Fact]
    public void Punto_dentro_de_poligono_escalado_y_desplazado_da_dentro()
    {
        var tf = Tf();
        var poli = RectanguloCerrado(minX: 2, minY: 1, maxX: 8, maxY: 6);

        // Centro del rectángulo en mundo → pantalla → mundo (como un clic real):
        // tras el ida y vuelta debe seguir cayendo DENTRO del polígono.
        var centroMundo = new PuntoCad(5, 3.5);
        var pantalla = tf.MundoAPantalla(centroMundo);
        var reconstruido = tf.PantallaAMundo(pantalla);

        Assert.True(PoligonoLosaMapper.ContienePunto(poli, reconstruido));
    }

    [Fact]
    public void Punto_fuera_de_poligono_escalado_y_desplazado_da_fuera()
    {
        var tf = Tf();
        var poli = RectanguloCerrado(minX: 2, minY: 1, maxX: 8, maxY: 6);

        // Punto claramente fuera del rectángulo, mismo ida-y-vuelta.
        var fueraMundo = new PuntoCad(20, 20);
        var pantalla = tf.MundoAPantalla(fueraMundo);
        var reconstruido = tf.PantallaAMundo(pantalla);

        Assert.False(PoligonoLosaMapper.ContienePunto(poli, reconstruido));
    }

    [Fact]
    public void Hit_test_con_escala_unitaria_y_sin_offset_tambien_es_reversible()
    {
        // Caso degenerado (esc=1, offset=0) — el viejo mapeo lo acertaba; aquí
        // sólo confirmamos que la transformación general no lo rompe.
        var tf = new CadTransform(50.0, 1.0, 0.0, 0.0, 10.0, 1.0, 1.0, 0.0, 0.0);
        var mundo = new PuntoCad(3.3, 4.4);
        var vuelta = tf.PantallaAMundo(tf.MundoAPantalla(mundo));
        Assert.Equal(mundo.X, vuelta.X, 9);
        Assert.Equal(mundo.Y, vuelta.Y, 9);
    }

    /// <summary>Polilínea cerrada rectangular (4 vértices) en coordenadas mundo (m).</summary>
    private static PolilineaCad RectanguloCerrado(double minX, double minY, double maxX, double maxY) =>
        new PolilineaCad
        {
            Cerrada = true,
            Vertices = new[]
            {
                new PuntoCad(minX, minY),
                new PuntoCad(maxX, minY),
                new PuntoCad(maxX, maxY),
                new PuntoCad(minX, maxY),
            },
        };
}
