using LosasPlus.Services;
using Xunit;

namespace LosasPlus.Tests.Services;

/// <summary>
/// Tests de <see cref="GeometriaEdicion"/> — la geometría de edición restringida
/// de la FASE A v1.1: redimensionado con anclaje de la esquina opuesta y
/// traslación. El resultado siempre es un rectángulo ortogonal.
/// </summary>
public class GeometriaEdicionTests
{
    [Fact]
    public void Redimensionar_asa_InfDer_ancla_la_esquina_superior_izquierda()
    {
        var original = new RectM(0, 0, 4, 3);
        var r = GeometriaEdicion.Redimensionar(original, AsaRedim.InfDer, new PuntoM(7, 5), ladoMin: 0.10);
        // PosX/PosY (esquina sup-izq) intactos; sólo crecen Lx/Ly.
        Assert.Equal(0, r.X, precision: 6);
        Assert.Equal(0, r.Y, precision: 6);
        Assert.Equal(7, r.Ancho, precision: 6);
        Assert.Equal(5, r.Alto, precision: 6);
    }

    [Fact]
    public void Redimensionar_asa_SupIzq_ancla_la_esquina_inferior_derecha()
    {
        var original = new RectM(2, 2, 4, 3);   // Der = 6, Inf = 5
        var r = GeometriaEdicion.Redimensionar(original, AsaRedim.SupIzq, new PuntoM(1, 1), ladoMin: 0.10);
        Assert.Equal(1, r.X, precision: 6);
        Assert.Equal(1, r.Y, precision: 6);
        // La esquina opuesta (inferior-derecha) NO se mueve.
        Assert.Equal(6, r.Der, precision: 6);
        Assert.Equal(5, r.Inf, precision: 6);
    }

    [Fact]
    public void Redimensionar_asa_de_borde_mueve_un_solo_eje()
    {
        var original = new RectM(0, 0, 4, 3);
        // CentroDer sólo mueve el borde derecho; la Y del destino se ignora.
        var r = GeometriaEdicion.Redimensionar(original, AsaRedim.CentroDer, new PuntoM(6, 999), ladoMin: 0.10);
        Assert.Equal(0, r.Y, precision: 6);
        Assert.Equal(3, r.Alto, precision: 6);
        Assert.Equal(6, r.Ancho, precision: 6);
    }

    [Fact]
    public void Redimensionar_respeta_el_lado_minimo_sin_flip()
    {
        var original = new RectM(0, 0, 4, 3);
        // Arrastrar la esquina inf-der muy por encima/izquierda de la sup-izq.
        var r = GeometriaEdicion.Redimensionar(original, AsaRedim.InfDer, new PuntoM(-10, -10), ladoMin: 0.10);
        // Sin "flip": el rect queda clampeado al lado mínimo, nunca negativo.
        Assert.Equal(0.10, r.Ancho, precision: 6);
        Assert.Equal(0.10, r.Alto, precision: 6);
        Assert.Equal(0, r.X, precision: 6);
        Assert.Equal(0, r.Y, precision: 6);
    }

    [Fact]
    public void Redimensionar_asa_SupDer_mueve_arriba_y_derecha()
    {
        var original = new RectM(0, 0, 4, 3);   // Inf = 3
        var r = GeometriaEdicion.Redimensionar(original, AsaRedim.SupDer, new PuntoM(6, 1), ladoMin: 0.10);
        Assert.Equal(0, r.X, precision: 6);     // izquierda anclada
        Assert.Equal(1, r.Y, precision: 6);     // superior movido
        Assert.Equal(6, r.Der, precision: 6);   // derecha movida
        Assert.Equal(3, r.Inf, precision: 6);   // inferior anclado
    }

    [Fact]
    public void Mover_traslada_y_conserva_dimensiones()
    {
        var r = GeometriaEdicion.Mover(new RectM(2, 3, 4, 5), dx: 10, dy: -1);
        Assert.Equal(12, r.X, precision: 6);
        Assert.Equal(2, r.Y, precision: 6);
        Assert.Equal(4, r.Ancho, precision: 6);
        Assert.Equal(5, r.Alto, precision: 6);
    }
}
